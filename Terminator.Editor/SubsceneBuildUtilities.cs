using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Entities;
using Unity.Entities.Build;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

/// <summary>
/// Builds scene_archive_dependencies.json: parent Unity scene (Selection / Levels)
/// → ContentArchives / EntityScenes needed for its Entity SubScenes.
///
/// Shared pools + aligned dependency lists; each scene stores root indices only.
/// Runtime expands via CSR archiveDep* / entitySceneDep* / entitySceneArchive*.
/// Soft WeakObjectReferences and nested EntityScene/EntityPrefab (e.g. Player.prefab) become
/// graph edges (not duplicated into every scene's root arrays).
/// </summary>
static class SubSceneBuildUtilities
{
    /// <summary>Content delivery placeholder from BuildResultsCatalogDataSource.GetArchiveIds().Append(default).</summary>
    const string EmptyArchiveId = "00000000000000000000000000000000";

    /// <summary>EditorUtility progress bar title — report this if the tool appears stuck.</summary>
    const string ProgressBarTitle = "Scene Archive Dependencies";

    /// <summary>
    /// Soft UnityObject → owning archives only at root (depth 0) and header-backed nested (depth ≤1).
    /// EntityScene/EntityPrefab BFS has no depth cap — enqueue whenever header exists and not visited.
    /// </summary>
    const int MaxSoftUnityObjectDepth = 1;

    // Overall progress ranges for WriteSceneArchiveDependencies stages.
    const float ProgressParseCatalog = 0.02f;
    const float ProgressScenesStart = 0.05f;
    const float ProgressScenesEnd = 0.82f;
    const float ProgressBuildPools = 0.88f;
    const float ProgressBuildCsr = 0.92f;
    const float ProgressWriteJson = 0.96f;
    const float ProgressRefresh = 0.99f;
    const float ProgressDone = 1f;

    static readonly Regex ArchiveLine = new Regex(@"^\s*Archive:\s*([0-9a-fA-F]+)\s*$", RegexOptions.Compiled);
    static readonly Regex ObjectLine = new Regex(@"^\s*Object:\s*([0-9a-fA-F]+):(\d+)\s*$", RegexOptions.Compiled);
    static readonly Regex DependencyLine = new Regex(@"^\s*Dependency:\s*([0-9a-fA-F]+)\s*$", RegexOptions.Compiled);

    /// <returns>true if the user clicked Cancel (caller must abort; progress bar cleared in finally).</returns>
    static bool ReportProgress(string info, float progress)
    {
        if (EditorUtility.DisplayCancelableProgressBar(ProgressBarTitle, info, Mathf.Clamp01(progress)))
        {
            Debug.LogWarning($"[SubSceneBuildUtilities] Cancelled by user at: {info}");
            return true;
        }

        return false;
    }

    [MenuItem("SubSceneBuildUtilities/BuildContent")]
    static void BuildContent()
    {
        var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish",
            Path.GetDirectoryName(Application.dataPath), "Builds");
        if (string.IsNullOrEmpty(buildFolder))
        {
            return;
        }

        var buildTarget = EditorUserBuildSettings.activeBuildTarget;
        var playerGuid = GetPlayerGuid();
        if (!TryCollectParentScenesFromSelection(out var subSceneGuids, out var parentScenes))
        {
            return;
        }

        RemoteContentCatalogBuildUtility.BuildContent(subSceneGuids, playerGuid, buildTarget, buildFolder);
        WriteSceneArchiveDependencies(buildFolder, parentScenes);
    }

    [MenuItem("SubSceneBuildUtilities/BuildSceneArchiveDependencies")]
    static void BuildSceneArchiveDependencies()
    {
        var buildFolder = EditorUtility.OpenFolderPanel("Select Content Folder",
            Path.GetDirectoryName(Application.dataPath), "Content");
        if (string.IsNullOrEmpty(buildFolder))
        {
            return;
        }

        if (!TryCollectParentScenesFromSelection(out _, out var parentScenes))
        {
            return;
        }

        WriteSceneArchiveDependencies(buildFolder, parentScenes);
    }

    /// <summary>
    /// One-click: production Levels scenes → write into Assets/Content (full weakassetrefs).
    /// Skips Assets/Scenes/Levels/测试/ and scene names starting with Test_.
    /// </summary>
    [MenuItem("SubSceneBuildUtilities/BuildSceneArchiveDependencies (Levels to Content)")]
    static void BuildSceneArchiveDependenciesLevelsToContent()
    {
        const string levelsFolder = "Assets/Scenes/Levels";
        const string levelsTestFolder = levelsFolder + "/测试";
        var contentFolder = Path.Combine(Application.dataPath, "Content");
        if (!AssetDatabase.IsValidFolder(levelsFolder))
        {
            Debug.LogError($"[SubSceneBuildUtilities] Missing folder: {levelsFolder}");
            return;
        }

        if (!Directory.Exists(contentFolder))
        {
            Debug.LogError($"[SubSceneBuildUtilities] Missing folder: {contentFolder}");
            return;
        }

        var foundGuids = AssetDatabase.FindAssets("t:Scene", new[] { levelsFolder });
        if (foundGuids == null || foundGuids.Length == 0)
        {
            Debug.LogError($"[SubSceneBuildUtilities] No scenes under {levelsFolder}");
            return;
        }

        var filteredGuids = new List<string>(foundGuids.Length);
        var skipped = 0;
        foreach (var sceneGuid in foundGuids)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(sceneGuid);
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                Debug.Log(
                    $"[SubSceneBuildUtilities] Levels→Content skip (not a .unity scene): guid={sceneGuid} path='{assetPath}'");
                continue;
            }

            var normalized = assetPath.Replace('\\', '/');
            if (!normalized.StartsWith(levelsFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                Debug.Log(
                    $"[SubSceneBuildUtilities] Levels→Content skip (outside {levelsFolder}/): '{normalized}'");
                continue;
            }

            if (normalized.StartsWith(levelsTestFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                Debug.Log(
                    $"[SubSceneBuildUtilities] Levels→Content skip (under {levelsTestFolder}/): '{normalized}'");
                continue;
            }

            var sceneName = Path.GetFileNameWithoutExtension(normalized);
            if (sceneName.StartsWith("Test_", StringComparison.Ordinal))
            {
                skipped++;
                Debug.Log(
                    $"[SubSceneBuildUtilities] Levels→Content skip (Test_ prefix): '{normalized}'");
                continue;
            }

            filteredGuids.Add(sceneGuid);
        }

        if (filteredGuids.Count == 0)
        {
            Debug.LogError(
                $"[SubSceneBuildUtilities] No Levels scenes after filter " +
                $"(found={foundGuids.Length}, skipped={skipped}).");
            return;
        }

        Debug.Log(
            $"[SubSceneBuildUtilities] Levels→Content: keep={filteredGuids.Count} skip={skipped} " +
            $"(FindAssets={foundGuids.Length} under {levelsFolder})");

        if (!TryCollectParentScenesFromGuids(filteredGuids.ToArray(), out _, out var parentScenes))
        {
            return;
        }

        WriteSceneArchiveDependencies(contentFolder, parentScenes);
    }

    static Hash128 GetPlayerGuid()
    {
        var instance = DotsGlobalSettings.Instance;
        var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client
            ? instance.GetClientGUID()
            : instance.GetServerGUID();
        if (!playerGuid.IsValid)
        {
            throw new Exception("Invalid Player GUID");
        }

        return playerGuid;
    }

    static bool TryCollectParentScenesFromSelection(out HashSet<Hash128> subSceneGuids, out List<ParentSceneInfo> parentScenes)
    {
        return TryCollectParentScenesFromGuids(Selection.assetGUIDs, out subSceneGuids, out parentScenes);
    }

    static bool TryCollectParentScenesFromGuids(string[] sceneGUIDs, out HashSet<Hash128> subSceneGuids, out List<ParentSceneInfo> parentScenes)
    {
        subSceneGuids = new HashSet<Hash128>();
        parentScenes = new List<ParentSceneInfo>();

        if (sceneGUIDs == null || sceneGUIDs.Length == 0)
        {
            Debug.LogError("[SubSceneBuildUtilities] No scenes selected / provided.");
            return false;
        }

        foreach (var sceneGUID in sceneGUIDs)
        {
            if (!GUID.TryParse(sceneGUID, out var guid))
            {
                continue;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sceneName = Path.GetFileNameWithoutExtension(assetPath);
            var relatedSubScenes = new HashSet<Hash128>();
            var ssGuids = EditorEntityScenes.GetSubScenes(guid);
            if (ssGuids != null)
            {
                foreach (var ss in ssGuids)
                {
                    relatedSubScenes.Add(ss);
                    subSceneGuids.Add(ss);
                }
            }

            parentScenes.Add(new ParentSceneInfo
            {
                sceneName = sceneName,
                sceneGuid = NormalizeGuid(guid.ToString()),
                subSceneGuids = relatedSubScenes
            });

            Debug.Log(
                $"[SubSceneBuildUtilities] Parent '{sceneName}' SubScenes ({relatedSubScenes.Count}): " +
                string.Join(", ", relatedSubScenes.Select(g => NormalizeGuid(g.ToString()))));
        }

        if (parentScenes.Count == 0)
        {
            Debug.LogError("[SubSceneBuildUtilities] No Unity scene assets collected.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Full graph: catalog Object: + EntityScenes headers + weakassetrefs ProduceArtifact
    /// (Soft UnityObject / nested EntityPrefab). Cancelable progress; LookupArtifact + Soft depth cap apply.
    /// </summary>
    public static void WriteSceneArchiveDependencies(string buildFolder, List<ParentSceneInfo> parentScenes)
    {
        try
        {
            WriteSceneArchiveDependenciesInternal(buildFolder, parentScenes);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static void WriteSceneArchiveDependenciesInternal(
        string buildFolder,
        List<ParentSceneInfo> parentScenes)
    {
        var verboseCatalogPath = ResolveVerboseCatalogPath(buildFolder);
        if (verboseCatalogPath == null)
        {
            Debug.LogError(
                $"[SubSceneBuildUtilities] archive_dependencies.txt not found under '{buildFolder}/ContentArchives'. " +
                "Run BuildContent first (verbose catalog is written next to the .bin).");
            return;
        }

        if (ReportProgress($"Parse catalog: {Path.GetFileName(verboseCatalogPath)}", ProgressParseCatalog))
        {
            return;
        }

        var catalog = ParseVerboseCatalog(verboseCatalogPath);
        Debug.Log(
            $"[SubSceneBuildUtilities] Parsed verbose catalog: {catalog.objectGuidToOwningArchives.Count} object GUIDs → owning archives, " +
            $"{catalog.blocks.Count} archive blocks ({verboseCatalogPath})");

        // Global EntityScenes graph edges (header → sections / nested headers) and header → owning archive roots.
        var entitySceneEdges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var entitySceneArchiveRoots = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var ensuredEntityLocalEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // ProduceArtifact is expensive — share weakassetrefs results across all parent scenes / SubScenes.
        var weakAssetRefsCache = new WeakAssetRefsProduceCache();
        // Entity* BFS expansion memo: one GUID → direct children + Soft GUIDs; reuse across Levels / SubScenes.
        var entityExpansionMemo = new EntityWeakExpansionMemo();

        var sceneRoots = new List<(ParentSceneInfo parent, HashSet<string> archiveRoots, HashSet<string> entityHeaderRoots)>(
            parentScenes.Count);

        var parentTotal = parentScenes.Count;
        for (int parentIndex = 0; parentIndex < parentTotal; parentIndex++)
        {
            var parent = parentScenes[parentIndex];
            var archiveRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entityHeaderRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var subScenes = parent.subSceneGuids.ToList();
            var subTotal = Math.Max(1, subScenes.Count);
            var parentProgressStart = Mathf.Lerp(
                ProgressScenesStart, ProgressScenesEnd, (float)parentIndex / parentTotal);
            var parentProgressEnd = Mathf.Lerp(
                ProgressScenesStart, ProgressScenesEnd, (float)(parentIndex + 1) / parentTotal);

            if (ReportProgress(
                    $"Parent scene [{parentIndex + 1}/{parentTotal}]: {parent.sceneName} ({subScenes.Count} SubScenes)",
                    parentProgressStart))
            {
                return;
            }

            for (int subIndex = 0; subIndex < subScenes.Count; subIndex++)
            {
                var subSceneGuid = subScenes[subIndex];
                var key = NormalizeGuid(subSceneGuid.ToString());
                var subProgressStart = Mathf.Lerp(
                    parentProgressStart, parentProgressEnd, (float)subIndex / subTotal);
                var subProgressEnd = Mathf.Lerp(
                    parentProgressStart, parentProgressEnd, (float)(subIndex + 1) / subTotal);

                if (ReportProgress(
                        $"SubScene [{subIndex + 1}/{subScenes.Count}] in '{parent.sceneName}': {key}",
                        subProgressStart))
                {
                    return;
                }

                var objectRefCount = TryReadObjectReferenceCount(buildFolder, key);
                var headerPath = ToEntityHeaderRelativePath(key);

                if (catalog.objectGuidToOwningArchives.TryGetValue(key, out var owning) && owning.Count > 0)
                {
                    foreach (var archiveId in owning)
                    {
                        archiveRoots.Add(archiveId);
                    }
                }
                else if (objectRefCount > 0)
                {
                    Debug.LogError(
                        $"[SubSceneBuildUtilities] '{parent.sceneName}' SubScene {key} has ObjectReferenceCount={objectRefCount} " +
                        "but no Object: entry in archive_dependencies.txt — catalog / BuildContent mismatch.");
                }
                else if (objectRefCount == 0)
                {
                    Debug.Log(
                        $"[SubSceneBuildUtilities] '{parent.sceneName}' SubScene {key}: ObjectReferenceCount=0 " +
                        "(no ContentArchives hybrid object; EntityScenes still required at runtime).");
                }
                else
                {
                    Debug.LogWarning(
                        $"[SubSceneBuildUtilities] '{parent.sceneName}' SubScene {key}: no EntityScenes header in '{buildFolder}' " +
                        "and no Object: in catalog — cannot classify empty archives.");
                }

                if (EntitySceneHeaderExists(buildFolder, key))
                {
                    entityHeaderRoots.Add(headerPath);
                    EnsureEntitySceneLocalEdgesOnce(
                        buildFolder, key, headerPath, entitySceneEdges, ensuredEntityLocalEdges);
                }

                CollectWeakAssetDependencyGraph(
                    subSceneGuid,
                    parent.sceneName,
                    buildFolder,
                    catalog.objectGuidToOwningArchives,
                    archiveRoots,
                    entitySceneEdges,
                    entitySceneArchiveRoots,
                    ensuredEntityLocalEdges,
                    weakAssetRefsCache,
                    entityExpansionMemo,
                    subProgressStart,
                    subProgressEnd,
                    out var weakStatus,
                    out var weakObjectCount,
                    out var weakEntitySceneCount,
                    out var weakMatchedArchives);

                if (weakStatus == WeakAssetCollectStatus.Cancelled)
                {
                    return;
                }

                if (weakStatus == WeakAssetCollectStatus.Ok &&
                    weakObjectCount > 0 &&
                    weakMatchedArchives == 0)
                {
                    Debug.LogError(
                        $"[SubSceneBuildUtilities] '{parent.sceneName}' SubScene {key}: weakassetrefs has {weakObjectCount} UnityObject GUIDs " +
                        "but none map to Objects in archive_dependencies.txt — Soft refs were not in this BuildContent set.");
                }
                else if (weakStatus == WeakAssetCollectStatus.Ok)
                {
                    Debug.Log(
                        $"[SubSceneBuildUtilities] '{parent.sceneName}' SubScene {key}: weakassetrefs → " +
                        $"{weakMatchedArchives} catalog Objects, {weakEntitySceneCount} nested EntityScene/Prefab GUIDs.");
                }
            }

            sceneRoots.Add((parent, archiveRoots, entityHeaderRoots));
            Debug.Log(
                $"[SubSceneBuildUtilities] {parent.sceneName}: {archiveRoots.Count} archive roots, " +
                $"{entityHeaderRoots.Count} EntityScene header roots from {parent.subSceneGuids.Count} SubScenes");
        }

        Debug.Log(
            $"[SubSceneBuildUtilities] weakassetrefs ProduceArtifact cache: " +
            $"uniqueGuids={weakAssetRefsCache.UniqueGuidCount} " +
            $"hits={weakAssetRefsCache.Hits} misses={weakAssetRefsCache.Misses} " +
            $"lookupHits={weakAssetRefsCache.LookupHits} produceCalls={weakAssetRefsCache.ProduceCalls}");
        Debug.Log(
            $"[SubSceneBuildUtilities] Entity* expansion memo: " +
            $"uniqueGuids={entityExpansionMemo.UniqueGuidCount} " +
            $"hits={entityExpansionMemo.Hits} misses={entityExpansionMemo.Misses}");

        // Pool = closure of all scene roots through global edges / archive Dependency lists.
        if (ReportProgress("Build pools: expand archive / EntityScene closures", ProgressBuildPools))
        {
            return;
        }
        var archivePoolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entityScenePoolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, archiveRoots, entityHeaderRoots) in sceneRoots)
        {
            foreach (var archiveId in archiveRoots)
            {
                ExpandArchiveIdsInto(archiveId, catalog.blocks, archivePoolSet);
            }

            foreach (var header in entityHeaderRoots)
            {
                ExpandEntityScenePathsInto(header, entitySceneEdges, entityScenePoolSet);
            }
        }

        foreach (var kv in entitySceneArchiveRoots)
        {
            ExpandEntityScenePathsInto(kv.Key, entitySceneEdges, entityScenePoolSet);
            foreach (var archiveId in kv.Value)
            {
                ExpandArchiveIdsInto(archiveId, catalog.blocks, archivePoolSet);
            }
        }

        var archivePool = BuildPoolFromSet(archivePoolSet);
        var entityScenePool = BuildPoolFromSet(entityScenePoolSet);

        if (ReportProgress(
                $"Build CSR: archives={archivePool.values.Length}, entityScenes={entityScenePool.values.Length}",
                ProgressBuildCsr))
        {
            return;
        }

        var archiveDependencies = BuildAlignedArchiveDependencies(archivePool, catalog.blocks);
        var entitySceneDependencies = BuildAlignedEntitySceneDependencies(entityScenePool, entitySceneEdges);
        var entitySceneArchives = BuildAlignedEntitySceneArchives(
            entityScenePool,
            entitySceneArchiveRoots,
            archivePool.map);

        SceneArchiveDependencies.BuildCsr(archiveDependencies, out var archiveDepOffsets, out var archiveDepIndices);
        SceneArchiveDependencies.BuildCsr(entitySceneDependencies, out var entitySceneDepOffsets, out var entitySceneDepIndices);
        SceneArchiveDependencies.BuildCsr(entitySceneArchives, out var entitySceneArchiveOffsets, out var entitySceneArchiveIndices);

        var entries = new List<SceneArchiveDependencies.SceneEntry>(sceneRoots.Count);
        foreach (var (parent, archiveRoots, entityHeaderRoots) in sceneRoots)
        {
            entries.Add(new SceneArchiveDependencies.SceneEntry
            {
                sceneName = parent.sceneName,
                sceneGuid = parent.sceneGuid,
                archiveIndices = ToSortedIndices(archiveRoots, archivePool.map),
                entitySceneIndices = ToSortedIndices(entityHeaderRoots, entityScenePool.map)
            });
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.sceneName, b.sceneName));

        var dependencyFile = new SceneArchiveDependencies.File
        {
            catalog = RuntimeContentManager.RelativeCatalogPath.Replace('\\', '/'),
            archives = archivePool.values,
            archiveDepOffsets = archiveDepOffsets,
            archiveDepIndices = archiveDepIndices,
            entityScenes = entityScenePool.values,
            entitySceneDepOffsets = entitySceneDepOffsets,
            entitySceneDepIndices = entitySceneDepIndices,
            entitySceneArchiveOffsets = entitySceneArchiveOffsets,
            entitySceneArchiveIndices = entitySceneArchiveIndices,
            scenes = entries.ToArray()
        };

        var outDir = Path.Combine(buildFolder, "ContentArchives");
        if (!Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        var outPath = Path.Combine(outDir, SceneArchiveDependencies.DiskFileName);
        if (ReportProgress($"Write JSON: {SceneArchiveDependencies.DiskFileName}", ProgressWriteJson))
        {
            return;
        }

        File.WriteAllText(outPath, FormatSceneArchiveDependenciesJson(dependencyFile));
        if (ReportProgress("AssetDatabase.Refresh", ProgressRefresh))
        {
            return;
        }

        AssetDatabase.Refresh();
        if (ReportProgress("Done", ProgressDone))
        {
            return;
        }

        Debug.Log(
            $"[SubSceneBuildUtilities] Wrote {dependencyFile.scenes.Length} scenes → {outPath} " +
            $"(shared archives={archivePool.values.Length}, entityScenes={entityScenePool.values.Length}, " +
            $"weakProduce cache hits={weakAssetRefsCache.Hits} misses={weakAssetRefsCache.Misses} | " +
            $"expandMemo hits={entityExpansionMemo.Hits} misses={entityExpansionMemo.Misses})");
    }

    /// <summary>
    /// Compact readable JSON: shared pools, CSR deps, per-scene root indices.
    /// </summary>
    static string FormatSceneArchiveDependenciesJson(SceneArchiveDependencies.File file)
    {
        var sb = new System.Text.StringBuilder(64 * 1024);
        sb.Append("{\n");
        sb.Append("    \"catalog\": ").Append(JsonString(file.catalog)).Append(",\n");
        AppendStringArray(sb, "archives", file.archives);
        sb.Append(",\n");
        AppendIntArray(sb, "archiveDepOffsets", file.archiveDepOffsets);
        sb.Append(",\n");
        AppendIntArray(sb, "archiveDepIndices", file.archiveDepIndices);
        sb.Append(",\n");
        AppendStringArray(sb, "entityScenes", file.entityScenes);
        sb.Append(",\n");
        AppendIntArray(sb, "entitySceneDepOffsets", file.entitySceneDepOffsets);
        sb.Append(",\n");
        AppendIntArray(sb, "entitySceneDepIndices", file.entitySceneDepIndices);
        sb.Append(",\n");
        AppendIntArray(sb, "entitySceneArchiveOffsets", file.entitySceneArchiveOffsets);
        sb.Append(",\n");
        AppendIntArray(sb, "entitySceneArchiveIndices", file.entitySceneArchiveIndices);
        sb.Append(",\n");
        sb.Append("    \"scenes\": [\n");
        var scenes = file.scenes ?? Array.Empty<SceneArchiveDependencies.SceneEntry>();
        for (int i = 0; i < scenes.Length; i++)
        {
            var scene = scenes[i];
            sb.Append("        {\n");
            sb.Append("            \"sceneName\": ").Append(JsonString(scene.sceneName)).Append(",\n");
            sb.Append("            \"sceneGuid\": ").Append(JsonString(scene.sceneGuid)).Append(",\n");
            sb.Append("            \"archiveIndices\": [ ").Append(JoinInts(scene.archiveIndices)).Append(" ],\n");
            sb.Append("            \"entitySceneIndices\": [ ").Append(JoinInts(scene.entitySceneIndices)).Append(" ]\n");
            sb.Append("        }");
            if (i < scenes.Length - 1)
            {
                sb.Append(',');
            }

            sb.Append('\n');
        }

        sb.Append("    ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    static void AppendStringArray(System.Text.StringBuilder sb, string name, string[] values)
    {
        sb.Append("    \"").Append(name).Append("\": [\n");
        values ??= Array.Empty<string>();
        for (int i = 0; i < values.Length; i++)
        {
            sb.Append("        ").Append(JsonString(values[i]));
            if (i < values.Length - 1)
            {
                sb.Append(',');
            }

            sb.Append('\n');
        }

        sb.Append("    ]");
    }

    static void AppendIntArray(System.Text.StringBuilder sb, string name, int[] values)
    {
        sb.Append("    \"").Append(name).Append("\": [ ").Append(JoinInts(values)).Append(" ]");
    }

    static string JoinInts(int[] values)
    {
        if (values == null || values.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", values);
    }

    static string JsonString(string value)
    {
        return EscapeJson(value);
    }

    static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var sb = new System.Text.StringBuilder(value.Length + 8);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    static (string[] values, Dictionary<string, int> map) BuildPoolFromSet(HashSet<string> set)
    {
        var values = set.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();
        var map = new Dictionary<string, int>(values.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Length; i++)
        {
            map[values[i]] = i;
        }

        return (values, map);
    }

    static int[] ToSortedIndices(HashSet<string> values, Dictionary<string, int> poolMap)
    {
        var indices = new List<int>(values.Count);
        foreach (var value in values)
        {
            if (poolMap.TryGetValue(value, out var index))
            {
                indices.Add(index);
            }
        }

        indices.Sort();
        return indices.ToArray();
    }

    static int[][] BuildAlignedArchiveDependencies(
        (string[] values, Dictionary<string, int> map) archivePool,
        Dictionary<string, ArchiveBlock> blocks)
    {
        var result = new int[archivePool.values.Length][];
        for (int i = 0; i < archivePool.values.Length; i++)
        {
            var deps = new List<int>();
            if (blocks.TryGetValue(archivePool.values[i], out var block))
            {
                foreach (var depId in block.Dependencies)
                {
                    if (IsEmptyArchiveId(depId))
                    {
                        continue;
                    }

                    if (archivePool.map.TryGetValue(depId, out var depIndex))
                    {
                        deps.Add(depIndex);
                    }
                }
            }

            deps.Sort();
            result[i] = deps.ToArray();
        }

        return result;
    }

    static int[][] BuildAlignedEntitySceneDependencies(
        (string[] values, Dictionary<string, int> map) entityPool,
        Dictionary<string, HashSet<string>> edges)
    {
        var result = new int[entityPool.values.Length][];
        for (int i = 0; i < entityPool.values.Length; i++)
        {
            var deps = new List<int>();
            if (edges.TryGetValue(entityPool.values[i], out var set))
            {
                foreach (var path in set)
                {
                    if (entityPool.map.TryGetValue(path, out var depIndex))
                    {
                        deps.Add(depIndex);
                    }
                }
            }

            deps.Sort();
            result[i] = deps.ToArray();
        }

        return result;
    }

    static int[][] BuildAlignedEntitySceneArchives(
        (string[] values, Dictionary<string, int> map) entityPool,
        Dictionary<string, HashSet<string>> entitySceneArchiveRoots,
        Dictionary<string, int> archiveMap)
    {
        var result = new int[entityPool.values.Length][];
        for (int i = 0; i < entityPool.values.Length; i++)
        {
            var deps = new List<int>();
            if (entitySceneArchiveRoots.TryGetValue(entityPool.values[i], out var set))
            {
                foreach (var archiveId in set)
                {
                    if (archiveMap.TryGetValue(archiveId, out var archiveIndex))
                    {
                        deps.Add(archiveIndex);
                    }
                }
            }

            deps.Sort();
            result[i] = deps.ToArray();
        }

        return result;
    }

    static void AddEntityEdge(
        Dictionary<string, HashSet<string>> edges,
        string fromPath,
        string toPath)
    {
        if (string.IsNullOrEmpty(fromPath) ||
            string.IsNullOrEmpty(toPath) ||
            string.Equals(fromPath, toPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!edges.TryGetValue(fromPath, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            edges[fromPath] = set;
        }

        set.Add(toPath);
    }

    static void AddEntityArchiveRoot(
        Dictionary<string, HashSet<string>> entitySceneArchiveRoots,
        string headerPath,
        string archiveId)
    {
        if (string.IsNullOrEmpty(headerPath) || string.IsNullOrEmpty(archiveId) || IsEmptyArchiveId(archiveId))
        {
            return;
        }

        if (!entitySceneArchiveRoots.TryGetValue(headerPath, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            entitySceneArchiveRoots[headerPath] = set;
        }

        set.Add(archiveId);
    }

    static void EnsureEntitySceneLocalEdges(
        string buildFolder,
        string entityGuid,
        string headerPath,
        Dictionary<string, HashSet<string>> edges)
    {
        foreach (var relativePath in CollectEntitySceneRelativePaths(buildFolder, entityGuid))
        {
            if (string.Equals(relativePath, headerPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddEntityEdge(edges, headerPath, relativePath);
        }
    }

    /// <summary>
    /// One-shot local edges per entity GUID (avoid repeated Directory scans on large EntityScenes folders).
    /// </summary>
    static void EnsureEntitySceneLocalEdgesOnce(
        string buildFolder,
        string entityGuid,
        string headerPath,
        Dictionary<string, HashSet<string>> edges,
        HashSet<string> ensuredGuids)
    {
        if (string.IsNullOrEmpty(entityGuid) || !ensuredGuids.Add(entityGuid))
        {
            return;
        }

        EnsureEntitySceneLocalEdges(buildFolder, entityGuid, headerPath, edges);
    }

    static void ExpandArchiveIdsInto(
        string archiveId,
        Dictionary<string, ArchiveBlock> blocks,
        HashSet<string> result)
    {
        ExpandArchiveClosure(archiveId, blocks, result);
    }

    static void ExpandEntityScenePathsInto(
        string rootPath,
        Dictionary<string, HashSet<string>> edges,
        HashSet<string> result)
    {
        if (string.IsNullOrEmpty(rootPath))
        {
            return;
        }

        var queue = new Queue<string>();
        if (!result.Add(rootPath))
        {
            return;
        }

        queue.Enqueue(rootPath);
        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            if (!edges.TryGetValue(path, out var deps))
            {
                continue;
            }

            foreach (var dep in deps)
            {
                if (string.IsNullOrEmpty(dep) || !result.Add(dep))
                {
                    continue;
                }

                queue.Enqueue(dep);
            }
        }
    }

    static string ToEntityHeaderRelativePath(string entityGuid)
    {
        return $"EntityScenes/{entityGuid}.entityheader";
    }

    static bool EntitySceneHeaderExists(string buildFolder, string entityGuid)
    {
        var entityScenesDir = Path.Combine(buildFolder, "EntityScenes");
        if (!Directory.Exists(entityScenesDir) || string.IsNullOrEmpty(entityGuid))
        {
            return false;
        }

        return EntitySceneFileExists(entityScenesDir, $"{entityGuid}.entityheader");
    }

    class VerboseCatalog
    {
        public Dictionary<string, ArchiveBlock> blocks;
        public Dictionary<string, HashSet<string>> objectGuidToOwningArchives;
    }

    /// <summary>
    /// Parse archive_dependencies.txt: owning archives only (no Dependency closure),
    /// plus raw ArchiveBlock graph for direct edges.
    /// </summary>
    static VerboseCatalog ParseVerboseCatalog(string verboseCatalogPath)
    {
        var blocks = new Dictionary<string, ArchiveBlock>(StringComparer.OrdinalIgnoreCase);
        ArchiveBlock current = null;

        foreach (var rawLine in File.ReadLines(verboseCatalogPath))
        {
            var archiveMatch = ArchiveLine.Match(rawLine);
            if (archiveMatch.Success)
            {
                var id = NormalizeGuid(archiveMatch.Groups[1].Value);
                current = new ArchiveBlock { ArchiveId = id };
                blocks[id] = current;
                continue;
            }

            if (current == null)
            {
                continue;
            }

            var objectMatch = ObjectLine.Match(rawLine);
            if (objectMatch.Success)
            {
                current.ObjectGuids.Add(NormalizeGuid(objectMatch.Groups[1].Value));
                continue;
            }

            var depMatch = DependencyLine.Match(rawLine);
            if (depMatch.Success)
            {
                current.Dependencies.Add(NormalizeGuid(depMatch.Groups[1].Value));
            }
        }

        var objectGuidToOwning = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks.Values)
        {
            if (IsEmptyArchiveId(block.ArchiveId))
            {
                continue;
            }

            foreach (var objectGuid in block.ObjectGuids)
            {
                if (!objectGuidToOwning.TryGetValue(objectGuid, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    objectGuidToOwning[objectGuid] = set;
                }

                set.Add(block.ArchiveId);
            }
        }

        return new VerboseCatalog
        {
            blocks = blocks,
            objectGuidToOwningArchives = objectGuidToOwning
        };
    }

    /// <summary>
    /// Legacy helper: object GUID → expanded archive closure (for callers that still need it).
    /// </summary>
    public static Dictionary<string, HashSet<string>> BuildObjectGuidToArchivesFromVerboseCatalog(string verboseCatalogPath)
    {
        var catalog = ParseVerboseCatalog(verboseCatalogPath);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in catalog.objectGuidToOwningArchives)
        {
            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var owning in kv.Value)
            {
                ExpandArchiveClosure(owning, catalog.blocks, expanded);
            }

            result[kv.Key] = expanded;
        }

        return result;
    }

    static void ExpandArchiveClosure(
        string archiveId,
        Dictionary<string, ArchiveBlock> blocks,
        HashSet<string> result)
    {
        if (string.IsNullOrEmpty(archiveId) || IsEmptyArchiveId(archiveId) || !result.Add(archiveId))
        {
            return;
        }

        if (!blocks.TryGetValue(archiveId, out var block))
        {
            return;
        }

        foreach (var dep in block.Dependencies)
        {
            ExpandArchiveClosure(dep, blocks, result);
        }
    }

    static bool IsEmptyArchiveId(string archiveId)
    {
        return string.Equals(archiveId, EmptyArchiveId, StringComparison.OrdinalIgnoreCase);
    }

    static string ResolveVerboseCatalogPath(string buildFolder)
    {
        var candidates = new[]
        {
            Path.Combine(buildFolder, "ContentArchives", "archive_dependencies.txt"),
            Path.Combine(buildFolder, RuntimeContentManager.RelativeCatalogPath.Replace(".bin", ".txt")),
            Path.Combine(buildFolder, "ContentArchives", "archive_dependencies.bin.txt"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// EntityScenes relative paths as PathRemapFunc receives them (no .bytes suffix).
    /// Probe known names with File.Exists — do NOT Directory.GetFiles on the whole EntityScenes
    /// folder (thousands of files; repeated wildcard scans look like a hang).
    /// </summary>
    static IEnumerable<string> CollectEntitySceneRelativePaths(string buildFolder, string subSceneGuid)
    {
        var entityScenesDir = Path.Combine(buildFolder, "EntityScenes");
        if (!Directory.Exists(entityScenesDir) || string.IsNullOrEmpty(subSceneGuid))
        {
            yield break;
        }

        if (EntitySceneFileExists(entityScenesDir, $"{subSceneGuid}.entityheader"))
        {
            yield return $"EntityScenes/{subSceneGuid}.entityheader";
        }

        // Sections are contiguous from 0 in practice; stop at first missing index.
        const int maxSections = 64;
        for (int section = 0; section < maxSections; section++)
        {
            var sectionName = $"{subSceneGuid}.{section}.entities";
            if (!EntitySceneFileExists(entityScenesDir, sectionName))
            {
                yield break;
            }

            yield return $"EntityScenes/{sectionName}";
        }
    }

    static bool EntitySceneFileExists(string entityScenesDir, string fileName)
    {
        return File.Exists(Path.Combine(entityScenesDir, fileName)) ||
               File.Exists(Path.Combine(entityScenesDir, fileName + ".bytes"));
    }

    /// <summary>
    /// Read SceneSectionData.ObjectReferenceCount from EntityScenes/{guid}.entityheader(.bytes).
    /// Returns -1 if header missing / cannot parse.
    /// </summary>
    static int TryReadObjectReferenceCount(string buildFolder, string subSceneGuid)
    {
        var entityScenesDir = Path.Combine(buildFolder, "EntityScenes");
        if (!Directory.Exists(entityScenesDir))
        {
            return -1;
        }

        var candidates = new[]
        {
            Path.Combine(entityScenesDir, $"{subSceneGuid}.entityheader.bytes"),
            Path.Combine(entityScenesDir, $"{subSceneGuid}.entityheader"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var data = File.ReadAllBytes(path);
                var needle = GuidStringToHash128Bytes(subSceneGuid);
                var index = IndexOfBytes(data, needle);
                if (index < 0)
                {
                    return -1;
                }

                // SceneSectionData: SceneGUID@0, SubSectionIndex@16, FileSize@20, ObjectReferenceCount@24
                if (index + 28 > data.Length)
                {
                    return -1;
                }

                return BitConverter.ToInt32(data, index + 24);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SubSceneBuildUtilities] Failed reading ObjectReferenceCount from {path}: {e.Message}");
                return -1;
            }
        }

        return -1;
    }

    enum WeakAssetCollectStatus
    {
        Ok,
        Skipped,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Caches ProduceArtifact → weakassetrefs by scene GUID across parent scenes / SubScenes.
    /// </summary>
    sealed class WeakAssetRefsProduceCache
    {
        public int Hits;
        public int Misses;
        public int LookupHits;
        public int ProduceCalls;

        readonly Dictionary<string, Entry> _byGuid =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public int UniqueGuidCount => _byGuid.Count;

        public struct Entry
        {
            public List<UntypedWeakReferenceId> WeakIds;
            public WeakAssetCollectStatus Status;
            public bool Success;
        }

        public bool TryGet(string sceneGuid, out Entry entry)
        {
            return _byGuid.TryGetValue(sceneGuid, out entry);
        }

        public void Set(
            string sceneGuid,
            List<UntypedWeakReferenceId> weakIds,
            WeakAssetCollectStatus status,
            bool success)
        {
            _byGuid[sceneGuid] = new Entry
            {
                WeakIds = weakIds ?? new List<UntypedWeakReferenceId>(),
                Status = status,
                Success = success
            };
        }
    }

    /// <summary>
    /// Global memo of EntityScene/EntityPrefab weakassetrefs expansion:
    /// GUID → direct Entity* children + Soft UnityObject GUIDs.
    /// After the first full scan, later BFS visits reuse edges and must not ProduceArtifact /
    /// re-walk the nested closure (avoids O(scenes × closure) when Player etc. appear in many Levels).
    /// Soft application still respects MaxSoftUnityObjectDepth at the reuse site.
    /// </summary>
    sealed class EntityWeakExpansionMemo
    {
        public int Hits;
        public int Misses;

        public struct EntityChild
        {
            public string Guid;
            public Hash128 Hash;
            public bool HeaderExists;
        }

        public struct Entry
        {
            public List<EntityChild> EntityChildren;
            public List<string> SoftUnityObjectGuids;
        }

        readonly Dictionary<string, Entry> _byGuid =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public int UniqueGuidCount => _byGuid.Count;

        public bool TryGet(string sceneGuid, out Entry entry)
        {
            return _byGuid.TryGetValue(sceneGuid, out entry);
        }

        public void Set(string sceneGuid, List<EntityChild> entityChildren, List<string> softUnityObjectGuids)
        {
            _byGuid[sceneGuid] = new Entry
            {
                EntityChildren = entityChildren ?? new List<EntityChild>(),
                SoftUnityObjectGuids = softUnityObjectGuids ?? new List<string>()
            };
        }
    }

    /// <summary>
    /// Walk weakassetrefs for a SubScene and nested EntityScene/EntityPrefab that have
    /// EntityScenes/{guid}.* on disk:
    /// - UnityObject at depth ≤ MaxSoftUnityObjectDepth → scene archive roots (depth 0) or entitySceneArchives
    /// - EntityScene/EntityPrefab with header → graph edge; BFS while header exists (no depth cap; visited prevents cycles)
    /// - EntityWeakExpansionMemo: reuse direct edges for GUIDs already expanded in another scene walk
    /// </summary>
    static void CollectWeakAssetDependencyGraph(
        Hash128 rootSceneGuid,
        string parentSceneName,
        string buildFolder,
        Dictionary<string, HashSet<string>> objectGuidToOwningArchives,
        HashSet<string> sceneArchiveRoots,
        Dictionary<string, HashSet<string>> entitySceneEdges,
        Dictionary<string, HashSet<string>> entitySceneArchiveRoots,
        HashSet<string> ensuredEntityLocalEdges,
        WeakAssetRefsProduceCache produceCache,
        EntityWeakExpansionMemo expansionMemo,
        float progressMin,
        float progressMax,
        out WeakAssetCollectStatus status,
        out int unityObjectGuidCount,
        out int entitySceneGuidCount,
        out int matchedArchiveObjectCount)
    {
        status = WeakAssetCollectStatus.Failed;
        unityObjectGuidCount = 0;
        entitySceneGuidCount = 0;
        matchedArchiveObjectCount = 0;

        if (!TryResolveEntitiesEditorType(
                "Unity.Scenes",
                "Unity.Scenes.SceneWithBuildConfigurationGUIDs",
                out var swbcType) ||
            !TryResolveEntitiesEditorType(
                "Unity.Scenes.Editor",
                "Unity.Scenes.Editor.SubSceneImporter",
                out var subSceneImporterType))
        {
            Debug.LogError(
                "[SubSceneBuildUtilities] SceneWithBuildConfigurationGUIDs / SubSceneImporter not found " +
                "(Unity.Scenes / Unity.Scenes.Editor assemblies). Soft WeakObjectReference / EntityPrefab deps will be missing.");
            return;
        }

        var ensure = swbcType.GetMethod(
            "EnsureExistsFor",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (ensure == null)
        {
            Debug.LogError("[SubSceneBuildUtilities] SceneWithBuildConfigurationGUIDs.EnsureExistsFor not found.");
            return;
        }

        Hash128 playerGuid;
        try
        {
            playerGuid = GetPlayerGuid();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SubSceneBuildUtilities] GetPlayerGuid failed: {e.Message}");
            return;
        }

        var pendingEntityScenes = new Queue<(Hash128 guid, int depth)>();
        var visitedEntityScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUnityObjectGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootKey = NormalizeGuid(rootSceneGuid.ToString());

        pendingEntityScenes.Enqueue((rootSceneGuid, 0));
        visitedEntityScenes.Add(rootKey);

        try
        {
            while (pendingEntityScenes.Count > 0)
            {
                var (sceneGuid, depth) = pendingEntityScenes.Dequeue();
                var currentKey = NormalizeGuid(sceneGuid.ToString());
                var currentHeader = ToEntityHeaderRelativePath(currentKey);
                var isRootSubScene = depth == 0;

                var bfsProgress = Mathf.Lerp(
                    progressMin,
                    progressMax,
                    visitedEntityScenes.Count / (float)(visitedEntityScenes.Count + pendingEntityScenes.Count));
                if (ReportProgress(
                        $"weakassetrefs BFS '{parentSceneName}': guid={currentKey} | depth={depth} | " +
                        $"queue={pendingEntityScenes.Count} | visited={visitedEntityScenes.Count} | " +
                        $"produceCache hits={produceCache.Hits} misses={produceCache.Misses} | " +
                        $"expandMemo hits={expansionMemo.Hits} misses={expansionMemo.Misses}",
                        bfsProgress))
                {
                    status = WeakAssetCollectStatus.Cancelled;
                    return;
                }

                if (EntitySceneHeaderExists(buildFolder, currentKey))
                {
                    EnsureEntitySceneLocalEdgesOnce(
                        buildFolder, currentKey, currentHeader, entitySceneEdges, ensuredEntityLocalEdges);
                }

                // Global expansion memo: reuse direct Entity* + Soft edges; do not re-scan / re-enqueue closure.
                if (expansionMemo.TryGet(currentKey, out var memoEntry))
                {
                    expansionMemo.Hits++;
                    ApplyEntityWeakExpansionMemoEntry(
                        memoEntry,
                        depth,
                        isRootSubScene,
                        currentHeader,
                        objectGuidToOwningArchives,
                        sceneArchiveRoots,
                        entitySceneEdges,
                        entitySceneArchiveRoots,
                        seenUnityObjectGuids,
                        ref unityObjectGuidCount,
                        ref matchedArchiveObjectCount);
                    continue;
                }

                expansionMemo.Misses++;

                if (!TryReadWeakAssetRefs(
                        sceneGuid,
                        playerGuid,
                        ensure,
                        subSceneImporterType,
                        parentSceneName,
                        produceCache,
                        out var weakIds,
                        out var readStatus))
                {
                    if (readStatus == WeakAssetCollectStatus.Failed)
                    {
                        status = WeakAssetCollectStatus.Failed;
                        return;
                    }

                    expansionMemo.Set(
                        currentKey,
                        new List<EntityWeakExpansionMemo.EntityChild>(),
                        new List<string>());
                    continue;
                }

                var memoEntityChildren = new List<EntityWeakExpansionMemo.EntityChild>();
                var memoSoftGuids = new List<string>();

                for (int i = 0; i < weakIds.Count; i++)
                {
                    var id = weakIds[i];
                    if (!id.IsValid)
                    {
                        continue;
                    }

                    var guid = NormalizeGuid(id.GlobalId.AssetGUID.ToString());
                    if (string.IsNullOrEmpty(guid) || IsEmptyArchiveId(guid))
                    {
                        continue;
                    }

                    if (id.GenerationType == WeakReferenceGenerationType.EntityScene ||
                        id.GenerationType == WeakReferenceGenerationType.EntityPrefab)
                    {
                        var nestedHeader = ToEntityHeaderRelativePath(guid);
                        var nestedHeaderExists = EntitySceneHeaderExists(buildFolder, guid);
                        memoEntityChildren.Add(new EntityWeakExpansionMemo.EntityChild
                        {
                            Guid = guid,
                            Hash = id.GlobalId.AssetGUID,
                            HeaderExists = nestedHeaderExists
                        });

                        if (nestedHeaderExists)
                        {
                            AddEntityEdge(entitySceneEdges, currentHeader, nestedHeader);
                        }

                        if (objectGuidToOwningArchives.TryGetValue(guid, out var nestedOwning))
                        {
                            foreach (var archiveId in nestedOwning)
                            {
                                AddEntityArchiveRoot(entitySceneArchiveRoots, nestedHeader, archiveId);
                            }
                        }

                        if (!visitedEntityScenes.Add(guid))
                        {
                            continue;
                        }

                        entitySceneGuidCount++;
                        if (nestedHeaderExists)
                        {
                            EnsureEntitySceneLocalEdgesOnce(
                                buildFolder, guid, nestedHeader, entitySceneEdges, ensuredEntityLocalEdges);
                        }

                        // Header-backed Entity* only — no depth cap (Soft UnityObject uses MaxSoftUnityObjectDepth).
                        if (nestedHeaderExists)
                        {
                            pendingEntityScenes.Enqueue((id.GlobalId.AssetGUID, depth + 1));
                        }

                        continue;
                    }

                    // Record Soft GUIDs for memo even when depth skips application (reuse may be shallower).
                    memoSoftGuids.Add(guid);

                    // Soft UnityObject only on root SubScene and header-backed nested (depth ≤ max).
                    if (depth > MaxSoftUnityObjectDepth)
                    {
                        continue;
                    }

                    if (!seenUnityObjectGuids.Add(guid))
                    {
                        continue;
                    }

                    unityObjectGuidCount++;
                    if (!objectGuidToOwningArchives.TryGetValue(guid, out var weakSet) || weakSet.Count == 0)
                    {
                        continue;
                    }

                    matchedArchiveObjectCount++;
                    foreach (var archiveId in weakSet)
                    {
                        if (isRootSubScene)
                        {
                            sceneArchiveRoots.Add(archiveId);
                        }
                        else
                        {
                            AddEntityArchiveRoot(entitySceneArchiveRoots, currentHeader, archiveId);
                        }
                    }
                }

                expansionMemo.Set(currentKey, memoEntityChildren, memoSoftGuids);
            }

            status = WeakAssetCollectStatus.Ok;
        }
        catch (Exception e)
        {
            status = WeakAssetCollectStatus.Failed;
            Debug.LogError(
                $"[SubSceneBuildUtilities] CollectWeakAssetDependencyGraph('{parentSceneName}', {rootSceneGuid}) failed: {e}");
        }
    }

    /// <summary>
    /// Apply a previously expanded Entity* node's direct edges without re-reading weakassetrefs.
    /// Does not enqueue Entity* children — their subgraph edges already live in the global edge tables.
    /// Soft UnityObject archives follow MaxSoftUnityObjectDepth at this visit's depth.
    /// </summary>
    static void ApplyEntityWeakExpansionMemoEntry(
        EntityWeakExpansionMemo.Entry memoEntry,
        int depth,
        bool isRootSubScene,
        string currentHeader,
        Dictionary<string, HashSet<string>> objectGuidToOwningArchives,
        HashSet<string> sceneArchiveRoots,
        Dictionary<string, HashSet<string>> entitySceneEdges,
        Dictionary<string, HashSet<string>> entitySceneArchiveRoots,
        HashSet<string> seenUnityObjectGuids,
        ref int unityObjectGuidCount,
        ref int matchedArchiveObjectCount)
    {
        var entityChildren = memoEntry.EntityChildren;
        if (entityChildren != null)
        {
            for (int i = 0; i < entityChildren.Count; i++)
            {
                var child = entityChildren[i];
                if (string.IsNullOrEmpty(child.Guid))
                {
                    continue;
                }

                var nestedHeader = ToEntityHeaderRelativePath(child.Guid);
                if (child.HeaderExists)
                {
                    AddEntityEdge(entitySceneEdges, currentHeader, nestedHeader);
                }

                if (objectGuidToOwningArchives.TryGetValue(child.Guid, out var nestedOwning))
                {
                    foreach (var archiveId in nestedOwning)
                    {
                        AddEntityArchiveRoot(entitySceneArchiveRoots, nestedHeader, archiveId);
                    }
                }
            }
        }

        if (depth > MaxSoftUnityObjectDepth)
        {
            return;
        }

        var softGuids = memoEntry.SoftUnityObjectGuids;
        if (softGuids == null)
        {
            return;
        }

        for (int i = 0; i < softGuids.Count; i++)
        {
            var guid = softGuids[i];
            if (string.IsNullOrEmpty(guid) || !seenUnityObjectGuids.Add(guid))
            {
                continue;
            }

            unityObjectGuidCount++;
            if (!objectGuidToOwningArchives.TryGetValue(guid, out var weakSet) || weakSet.Count == 0)
            {
                continue;
            }

            matchedArchiveObjectCount++;
            foreach (var archiveId in weakSet)
            {
                if (isRootSubScene)
                {
                    sceneArchiveRoots.Add(archiveId);
                }
                else
                {
                    AddEntityArchiveRoot(entitySceneArchiveRoots, currentHeader, archiveId);
                }
            }
        }
    }

    static bool TryReadWeakAssetRefs(
        Hash128 sceneGuid,
        Hash128 playerGuid,
        MethodInfo ensureExistsFor,
        Type subSceneImporterType,
        string parentSceneName,
        WeakAssetRefsProduceCache produceCache,
        out List<UntypedWeakReferenceId> weakIds,
        out WeakAssetCollectStatus status)
    {
        weakIds = new List<UntypedWeakReferenceId>();
        status = WeakAssetCollectStatus.Failed;

        var cacheKey = NormalizeGuid(sceneGuid.ToString());
        if (produceCache != null && produceCache.TryGet(cacheKey, out var cached))
        {
            produceCache.Hits++;
            weakIds = cached.WeakIds;
            status = cached.Status;
            return cached.Success;
        }

        if (produceCache != null)
        {
            produceCache.Misses++;
        }

        var ensureArgs = new object[] { sceneGuid, playerGuid, false, false };
        var sceneBuildConfigGuidObj = ensureExistsFor.Invoke(null, ensureArgs);
        if (ensureArgs[3] is bool mustRefresh && mustRefresh)
        {
            AssetDatabase.Refresh();
        }

        var sceneBuildConfigHash = (Hash128)sceneBuildConfigGuidObj;
        var unityGuid = new GUID(NormalizeGuid(sceneBuildConfigHash.ToString()));
        var artifactKey = new UnityEditor.Experimental.ArtifactKey(unityGuid, subSceneImporterType);

        // Prefer existing ArtifactDB entry — avoid SubSceneImporter work when already produced.
        var artifactId = UnityEditor.Experimental.AssetDatabaseExperimental.LookupArtifact(artifactKey);
        var usedLookup = UnityEditor.Experimental.AssetDatabaseExperimental.GetArtifactPaths(artifactId, out var paths) &&
                         paths != null &&
                         paths.Length > 0;
        if (usedLookup)
        {
            if (produceCache != null)
            {
                produceCache.LookupHits++;
            }
        }
        else
        {
            if (produceCache != null)
            {
                produceCache.ProduceCalls++;
            }

            artifactId = UnityEditor.Experimental.AssetDatabaseExperimental.ProduceArtifact(artifactKey);
            if (!UnityEditor.Experimental.AssetDatabaseExperimental.GetArtifactPaths(artifactId, out paths) ||
                paths == null ||
                paths.Length == 0)
            {
                Debug.LogError(
                    $"[SubSceneBuildUtilities] '{parentSceneName}' scene {cacheKey}: " +
                    "ProduceArtifact returned no paths — cannot collect weakassetrefs.");
                produceCache?.Set(cacheKey, weakIds, status, false);
                return false;
            }
        }

        string weakPath = null;
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] != null &&
                paths[i].EndsWith("weakassetrefs", StringComparison.OrdinalIgnoreCase))
            {
                weakPath = paths[i];
                break;
            }
        }

        if (string.IsNullOrEmpty(weakPath))
        {
            // No soft / nested entity refs for this scene.
            status = WeakAssetCollectStatus.Ok;
            produceCache?.Set(cacheKey, weakIds, status, true);
            return true;
        }

        if (!File.Exists(weakPath))
        {
            Debug.LogError(
                $"[SubSceneBuildUtilities] '{parentSceneName}' weakassetrefs path missing: {weakPath}");
            produceCache?.Set(cacheKey, weakIds, status, false);
            return false;
        }

        if (!BlobAssetReference<BlobArray<UntypedWeakReferenceId>>.TryRead(weakPath, 1, out var weakAssets))
        {
            Debug.LogError(
                $"[SubSceneBuildUtilities] '{parentSceneName}' failed to read weakassetrefs: {weakPath}");
            produceCache?.Set(cacheKey, weakIds, status, false);
            return false;
        }

        try
        {
            for (int i = 0; i < weakAssets.Value.Length; i++)
            {
                weakIds.Add(weakAssets.Value[i]);
            }
        }
        finally
        {
            weakAssets.Dispose();
        }

        status = WeakAssetCollectStatus.Ok;
        produceCache?.Set(cacheKey, weakIds, status, true);
        return true;
    }

    static bool TryResolveEntitiesEditorType(string assemblyName, string fullTypeName, out Type type)
    {
        type = Type.GetType($"{fullTypeName}, {assemblyName}");
        if (type != null)
        {
            return true;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            var asm = assemblies[i];
            if (asm == null)
            {
                continue;
            }

            var name = asm.GetName().Name;
            if (!string.Equals(name, assemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            type = asm.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
            if (type != null)
            {
                return true;
            }
        }

        return false;
    }

    static byte[] GuidStringToHash128Bytes(string guid)
    {
        // Matches Unity.Entities.Hash128(string, guidFormatted: true) uint4 layout.
        var bytes = new byte[16];
        for (int i = 0; i < 4; i++)
        {
            uint cur = 0;
            for (int j = 0; j < 8; j++)
            {
                var c = guid[i * 8 + j];
                int h = c <= '9' ? c - '0' : (c <= 'F' ? c - 'A' + 10 : c - 'a' + 10);
                cur |= (uint)(h << (j * 4));
            }

            BitConverter.GetBytes(cur).CopyTo(bytes, i * 4);
        }

        return bytes;
    }

    static int IndexOfBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    static string NormalizeGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return string.Empty;
        }

        return guid.Replace("-", string.Empty).ToLowerInvariant();
    }

    class ArchiveBlock
    {
        public string ArchiveId;
        public readonly HashSet<string> ObjectGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Dependencies = new List<string>();
    }

    public struct ParentSceneInfo
    {
        public string sceneName;
        public string sceneGuid;
        public HashSet<Hash128> subSceneGuids;
    }
}
