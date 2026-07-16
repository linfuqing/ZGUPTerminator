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
/// → ContentArchives needed for its Entity SubScenes.
///
/// Source of truth is archive_dependencies.txt (verbose catalog). Content catalog has
/// almost no Scene: entries; SubScenes with hybrid companions appear as
/// Object: {subSceneGuid}:{sectionIndex}. SubScenes with ObjectReferenceCount==0 need
/// EntityScenes/*.entities for load, but may list empty ContentArchives.
/// Soft WeakObjectReferences are collected from the same weakassetrefs artifact BuildContent uses,
/// then expanded via catalog Object → archive Dependency closures.
/// </summary>
static class SubSceneBuildUtilities
{
    /// <summary>Content delivery placeholder from BuildResultsCatalogDataSource.GetArchiveIds().Append(default).</summary>
    const string EmptyArchiveId = "00000000000000000000000000000000";

    static readonly Regex ArchiveLine = new Regex(@"^\s*Archive:\s*([0-9a-fA-F]+)\s*$", RegexOptions.Compiled);
    static readonly Regex ObjectLine = new Regex(@"^\s*Object:\s*([0-9a-fA-F]+):(\d+)\s*$", RegexOptions.Compiled);
    static readonly Regex DependencyLine = new Regex(@"^\s*Dependency:\s*([0-9a-fA-F]+)\s*$", RegexOptions.Compiled);

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
    /// One-click: all scenes under Assets/Scenes/Levels → write into Assets/Content.
    /// </summary>
    [MenuItem("SubSceneBuildUtilities/BuildSceneArchiveDependencies (Levels→Content)")]
    static void BuildSceneArchiveDependenciesLevelsToContent()
    {
        const string levelsFolder = "Assets/Scenes/Levels";
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

        var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { levelsFolder });
        if (sceneGuids == null || sceneGuids.Length == 0)
        {
            Debug.LogError($"[SubSceneBuildUtilities] No scenes under {levelsFolder}");
            return;
        }

        if (!TryCollectParentScenesFromGuids(sceneGuids, out _, out var parentScenes))
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

    public static void WriteSceneArchiveDependencies(string buildFolder, List<ParentSceneInfo> parentScenes)
    {
        var verboseCatalogPath = ResolveVerboseCatalogPath(buildFolder);
        if (verboseCatalogPath == null)
        {
            Debug.LogError(
                $"[SubSceneBuildUtilities] archive_dependencies.txt not found under '{buildFolder}/ContentArchives'. " +
                "Run BuildContent first (verbose catalog is written next to the .bin).");
            return;
        }

        var objectGuidToArchives = BuildObjectGuidToArchivesFromVerboseCatalog(verboseCatalogPath);
        Debug.Log(
            $"[SubSceneBuildUtilities] Parsed verbose catalog: {objectGuidToArchives.Count} object GUIDs → archives " +
            $"({verboseCatalogPath})");

        var entries = new List<SceneArchiveDependencies.SceneEntry>(parentScenes.Count);
        foreach (var parent in parentScenes)
        {
            var archives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entityScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var subSceneGuid in parent.subSceneGuids)
            {
                var key = NormalizeGuid(subSceneGuid.ToString());
                var objectRefCount = TryReadObjectReferenceCount(buildFolder, key);

                // Hybrid ReferencedUnityObjects → Object:{subSceneGuid}:{section} in Content catalog.
                if (objectGuidToArchives.TryGetValue(key, out var set) && set.Count > 0)
                {
                    foreach (var archiveId in set)
                    {
                        archives.Add(archiveId);
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
                    // Legitimate: entity payload is under EntityScenes/, not ContentArchives.
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

                // Soft UntypedWeakReferenceIds: same weakassetrefs artifact BuildContent feeds into ContentArchives.
                var weakGuids = CollectWeakAssetObjectGuids(subSceneGuid, parent.sceneName, out var weakStatus);
                var weakMatched = 0;
                foreach (var weakGuid in weakGuids)
                {
                    if (!objectGuidToArchives.TryGetValue(weakGuid, out var weakSet) || weakSet.Count == 0)
                    {
                        continue;
                    }

                    weakMatched++;
                    foreach (var archiveId in weakSet)
                    {
                        archives.Add(archiveId);
                    }
                }

                if (weakStatus == WeakAssetCollectStatus.Ok && weakGuids.Count > 0 && weakMatched == 0)
                {
                    Debug.LogError(
                        $"[SubSceneBuildUtilities] '{parent.sceneName}' SubScene {key}: weakassetrefs has {weakGuids.Count} GUIDs " +
                        "but none map to Objects in archive_dependencies.txt — Soft refs were not in this BuildContent set.");
                }
                else if (weakStatus == WeakAssetCollectStatus.Ok)
                {
                    Debug.Log(
                        $"[SubSceneBuildUtilities] '{parent.sceneName}' SubScene {key}: weakassetrefs {weakGuids.Count} GUIDs → " +
                        $"{weakMatched} catalog Objects (Soft WeakObjectReference archives).");
                }

                foreach (var relativePath in CollectEntitySceneRelativePaths(buildFolder, key))
                {
                    entityScenes.Add(relativePath);
                }
            }

            var archiveList = archives.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray();
            var entitySceneList = entityScenes.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray();
            entries.Add(new SceneArchiveDependencies.SceneEntry
            {
                sceneName = parent.sceneName,
                sceneGuid = parent.sceneGuid,
                archives = archiveList,
                entityScenes = entitySceneList
            });

            Debug.Log(
                $"[SubSceneBuildUtilities] {parent.sceneName}: {archiveList.Length} archives, " +
                $"{entitySceneList.Length} EntityScenes from {parent.subSceneGuids.Count} SubScenes");
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.sceneName, b.sceneName));

        var dependencyFile = new SceneArchiveDependencies.File
        {
            catalog = RuntimeContentManager.RelativeCatalogPath.Replace('\\', '/'),
            scenes = entries.ToArray()
        };

        var outDir = Path.Combine(buildFolder, "ContentArchives");
        if (!Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        var outPath = Path.Combine(outDir, SceneArchiveDependencies.FileName);
        File.WriteAllText(outPath, JsonUtility.ToJson(dependencyFile, true));
        AssetDatabase.Refresh();
        Debug.Log($"[SubSceneBuildUtilities] Wrote {dependencyFile.scenes.Length} scenes → {outPath}");
    }

    /// <summary>
    /// Parse archive_dependencies.txt into object AssetGUID → expanded archive set
    /// (own archive + recursive Dependency closures).
    /// </summary>
    public static Dictionary<string, HashSet<string>> BuildObjectGuidToArchivesFromVerboseCatalog(string verboseCatalogPath)
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

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks.Values)
        {
            if (block.ObjectGuids.Count == 0)
            {
                continue;
            }

            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ExpandArchiveClosure(block.ArchiveId, blocks, expanded);

            foreach (var objectGuid in block.ObjectGuids)
            {
                if (!result.TryGetValue(objectGuid, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[objectGuid] = set;
                }

                foreach (var archiveId in expanded)
                {
                    set.Add(archiveId);
                }
            }
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
    /// Disk may store *.entityheader.bytes / *.0.entities.bytes as TextAssets.
    /// </summary>
    static IEnumerable<string> CollectEntitySceneRelativePaths(string buildFolder, string subSceneGuid)
    {
        var entityScenesDir = Path.Combine(buildFolder, "EntityScenes");
        if (!Directory.Exists(entityScenesDir) || string.IsNullOrEmpty(subSceneGuid))
        {
            yield break;
        }

        foreach (var path in Directory.GetFiles(entityScenesDir, $"{subSceneGuid}.*"))
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name) || name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // TextAsset packaging may append .bytes — strip for PathRemap / Entities relative paths.
            if (name.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".bytes".Length);
            }

            if (!name.EndsWith(".entityheader", StringComparison.OrdinalIgnoreCase) &&
                name.IndexOf(".entities", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            yield return $"EntityScenes/{name}".Replace('\\', '/');
        }
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
        Failed
    }

    /// <summary>
    /// Soft references from the same SubSceneImporter weakassetrefs artifact that BuildContent consumes.
    /// Uses ProduceArtifact (player build config) so Soft Object archives are included in the JSON.
    /// </summary>
    static List<string> CollectWeakAssetObjectGuids(Hash128 subSceneGuid, string parentSceneName, out WeakAssetCollectStatus status)
    {
        var result = new List<string>();
        status = WeakAssetCollectStatus.Failed;

        try
        {
            var playerGuid = GetPlayerGuid();

            // SceneWithBuildConfigurationGUIDs / SubSceneImporter are internal — Type.GetType(assemblyQualified)
            // is unreliable; resolve from already-loaded assemblies instead.
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
                    "(Unity.Scenes / Unity.Scenes.Editor assemblies). Soft WeakObjectReference archives will be missing.");
                return result;
            }

            var ensure = swbcType.GetMethod(
                "EnsureExistsFor",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (ensure == null)
            {
                Debug.LogError("[SubSceneBuildUtilities] SceneWithBuildConfigurationGUIDs.EnsureExistsFor not found.");
                return result;
            }

            var ensureArgs = new object[] { subSceneGuid, playerGuid, false, false };
            var sceneBuildConfigGuidObj = ensure.Invoke(null, ensureArgs);
            if (ensureArgs[3] is bool mustRefresh && mustRefresh)
            {
                AssetDatabase.Refresh();
            }

            // ArtifactKey(GUID, Type) — Hash128 from Entities maps via ToString().
            var sceneBuildConfigHash = (Hash128)sceneBuildConfigGuidObj;
            var unityGuid = new GUID(NormalizeGuid(sceneBuildConfigHash.ToString()));

            var artifactKey = new UnityEditor.Experimental.ArtifactKey(unityGuid, subSceneImporterType);
            var artifactId = UnityEditor.Experimental.AssetDatabaseExperimental.ProduceArtifact(artifactKey);
            if (!UnityEditor.Experimental.AssetDatabaseExperimental.GetArtifactPaths(artifactId, out var paths) ||
                paths == null ||
                paths.Length == 0)
            {
                Debug.LogError(
                    $"[SubSceneBuildUtilities] '{parentSceneName}' SubScene {NormalizeGuid(subSceneGuid.ToString())}: " +
                    "ProduceArtifact returned no paths — cannot collect Soft WeakObjectReference deps.");
                return result;
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
                // No soft refs written for this SubScene.
                status = WeakAssetCollectStatus.Ok;
                return result;
            }

            if (!File.Exists(weakPath))
            {
                Debug.LogError(
                    $"[SubSceneBuildUtilities] '{parentSceneName}' weakassetrefs path missing: {weakPath}");
                return result;
            }

            if (!BlobAssetReference<BlobArray<UntypedWeakReferenceId>>.TryRead(weakPath, 1, out var weakAssets))
            {
                Debug.LogError(
                    $"[SubSceneBuildUtilities] '{parentSceneName}' failed to read weakassetrefs: {weakPath}");
                return result;
            }

            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < weakAssets.Value.Length; i++)
                {
                    var id = weakAssets.Value[i];
                    if (!id.IsValid)
                    {
                        continue;
                    }

                    var guid = NormalizeGuid(id.GlobalId.AssetGUID.ToString());
                    if (string.IsNullOrEmpty(guid) || IsEmptyArchiveId(guid) || !seen.Add(guid))
                    {
                        continue;
                    }

                    result.Add(guid);
                }
            }
            finally
            {
                weakAssets.Dispose();
            }

            status = WeakAssetCollectStatus.Ok;
        }
        catch (Exception e)
        {
            status = WeakAssetCollectStatus.Failed;
            Debug.LogError(
                $"[SubSceneBuildUtilities] CollectWeakAssetObjectGuids('{parentSceneName}', {subSceneGuid}) failed: {e}");
        }

        return result;
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
