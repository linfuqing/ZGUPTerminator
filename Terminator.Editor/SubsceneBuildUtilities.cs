using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Build;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

static class SubSceneBuildUtilities
{
    [MenuItem("SubSceneBuildUtilities/BuildContent")]
    //prepares the content files for publish.  The original files can be deleted or retained during this process by changing the last parameter of the PublishContent call.
    static void BuildContent()
    {
        var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish",
            Path.GetDirectoryName(Application.dataPath), "Builds");
        if (string.IsNullOrEmpty(buildFolder))
        {
            return;
        }

        var buildTarget = EditorUserBuildSettings.activeBuildTarget;

        var instance = DotsGlobalSettings.Instance;
        var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client ? instance.GetClientGUID() : instance.GetServerGUID();
        if (!playerGuid.IsValid)
        {
            throw new Exception("Invalid Player GUID");
        }

        if (!TryCollectParentScenes(out var subSceneGuids, out var parentScenes))
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
            Path.GetDirectoryName(Application.dataPath), "Builds");
        if (string.IsNullOrEmpty(buildFolder))
        {
            return;
        }

        if (!TryCollectParentScenes(out _, out var parentScenes))
        {
            return;
        }

        WriteSceneArchiveDependencies(buildFolder, parentScenes);
    }

    static bool TryCollectParentScenes(out HashSet<Hash128> subSceneGuids, out List<ParentSceneInfo> parentScenes)
    {
        subSceneGuids = new HashSet<Hash128>();
        parentScenes = new List<ParentSceneInfo>();

        foreach (var sceneGUID in Selection.assetGUIDs)
        {
            if (!GUID.TryParse(sceneGUID, out var guid))
            {
                continue;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                continue;
            }

            var sceneName = Path.GetFileNameWithoutExtension(assetPath);
            // String compare avoids Hash128 vs UnityEditor.GUID endian / format mismatches.
            var relatedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeGuidString(guid.ToString())
            };

            var ssGuids = EditorEntityScenes.GetSubScenes(guid);
            if (ssGuids != null)
            {
                foreach (var ss in ssGuids)
                {
                    relatedGuids.Add(NormalizeGuidString(ss.ToString()));
                    subSceneGuids.Add(ss);
                }
            }

            parentScenes.Add(new ParentSceneInfo
            {
                sceneName = sceneName,
                sceneGuid = NormalizeGuidString(guid.ToString()),
                relatedContentGuids = relatedGuids
            });

            Debug.Log(
                $"[SubSceneBuildUtilities] Parent '{sceneName}' related GUIDs ({relatedGuids.Count}): " +
                string.Join(", ", relatedGuids));
        }

        if (parentScenes.Count == 0)
        {
            Debug.LogError("[SubSceneBuildUtilities] No scenes selected. Select parent Unity scene assets first.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Maps each parent Selection scene to the union of Content archive deps for its SubScenes
    /// (and the parent GUID if present in catalog). Written next to archive_dependencies.bin.
    /// </summary>
    public static void WriteSceneArchiveDependencies(string buildFolder, List<ParentSceneInfo> parentScenes)
    {
        var catalogPath = ResolveCatalogPath(buildFolder);
        if (catalogPath == null)
        {
            Debug.LogError(
                $"[SubSceneBuildUtilities] Catalog not found under '{buildFolder}' " +
                $"(tried {RuntimeContentManager.RelativeCatalogPath} and .bytes).");
            return;
        }

        if (!TryLoadCatalogViaRuntimeApi(catalogPath, out var catalogBoxed, out var catalogType))
        {
            Debug.LogError($"[SubSceneBuildUtilities] Failed to load catalog via RuntimeContentCatalog: {catalogPath}");
            return;
        }

        try
        {
            var sceneToArchives = BuildSceneGuidToArchives(catalogBoxed, catalogType);
            Debug.Log($"[SubSceneBuildUtilities] Catalog scene GUIDs with archives: {sceneToArchives.Count}");

            var entries = new List<SceneArchiveDependencies.SceneEntry>(parentScenes.Count);
            foreach (var parent in parentScenes)
            {
                var archives = new HashSet<string>(StringComparer.Ordinal);
                foreach (var related in parent.relatedContentGuids)
                {
                    if (sceneToArchives.TryGetValue(related, out var list))
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            archives.Add(list[i]);
                        }
                    }
                }

                var archiveList = new List<string>(archives);
                archiveList.Sort(StringComparer.Ordinal);

                entries.Add(new SceneArchiveDependencies.SceneEntry
                {
                    sceneName = parent.sceneName,
                    sceneGuid = parent.sceneGuid,
                    archives = archiveList.ToArray()
                });

                if (archiveList.Count == 0)
                {
                    Debug.LogWarning(
                        $"[SubSceneBuildUtilities] '{parent.sceneName}' matched 0 archives. " +
                        $"Related={parent.relatedContentGuids.Count}. " +
                        "Check SubScenes exist and catalog was built from the same Selection.");
                }
                else
                {
                    Debug.Log($"[SubSceneBuildUtilities] {parent.sceneName}: {archiveList.Count} archives");
                }
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.sceneName, b.sceneName));

            var dependencyFile = new SceneArchiveDependencies.File
            {
                catalog = RuntimeContentManager.RelativeCatalogPath.Replace('\\', '/'),
                scenes = entries.ToArray()
            };

            var outDir = Path.GetDirectoryName(catalogPath) ?? buildFolder;
            // Prefer sidecar next to .bin; if catalog is .bytes keep same folder/name without forcing .bytes on json.
            var outPath = Path.Combine(outDir, SceneArchiveDependencies.FileName);
            System.IO.File.WriteAllText(outPath, JsonUtility.ToJson(dependencyFile, true));
            Debug.Log($"[SubSceneBuildUtilities] Wrote parent-scene archive dependencies ({dependencyFile.scenes.Length} scenes): {outPath}");
        }
        finally
        {
            if (catalogBoxed is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    static string ResolveCatalogPath(string buildFolder)
    {
        var catalogPath = Path.Combine(buildFolder, RuntimeContentManager.RelativeCatalogPath);
        if (File.Exists(catalogPath))
        {
            return catalogPath;
        }

        var bytesPath = catalogPath + ".bytes";
        if (File.Exists(bytesPath))
        {
            return bytesPath;
        }

        return null;
    }

    static string NormalizeGuidString(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return string.Empty;
        }

        return guid.Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>
    /// RuntimeContentCatalog is internal; use it via reflection (same as Unity.Scenes.Editor tooling).
    /// </summary>
    static bool TryLoadCatalogViaRuntimeApi(string catalogPath, out object catalogBoxed, out Type catalogType)
    {
        catalogBoxed = null;
        catalogType = typeof(RuntimeContentManager).Assembly.GetType("Unity.Entities.Content.RuntimeContentCatalog");
        if (catalogType == null)
        {
            Debug.LogError("[SubSceneBuildUtilities] Type RuntimeContentCatalog not found.");
            return false;
        }

        catalogBoxed = Activator.CreateInstance(catalogType);
        var loadMethod = catalogType.GetMethod(
            "LoadCatalogData",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(Func<string, string>), typeof(Func<string, string>) },
            null);
        if (loadMethod == null)
        {
            Debug.LogError("[SubSceneBuildUtilities] LoadCatalogData(string, Func, Func) not found.");
            return false;
        }

        Func<string, string> identity = s => s;
        var loaded = loadMethod.Invoke(catalogBoxed, new object[] { catalogPath, identity, identity });
        return loaded is bool ok && ok;
    }

    static Dictionary<string, List<string>> BuildSceneGuidToArchives(object catalogBoxed, Type catalogType)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var getSceneIds = catalogType.GetMethod("GetSceneIds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var tryGetSceneLocation = FindTryGetSceneLocation(catalogType);
        var tryGetFileLocation = FindTryGetFileLocation(catalogType);

        if (getSceneIds == null || tryGetSceneLocation == null || tryGetFileLocation == null)
        {
            Debug.LogError("[SubSceneBuildUtilities] Missing RuntimeContentCatalog query methods.");
            return result;
        }

        var sceneIdsObj = getSceneIds.Invoke(catalogBoxed, new object[] { Allocator.Temp });
        if (sceneIdsObj == null)
        {
            return result;
        }

        try
        {
            var sceneIds = (NativeArray<UntypedWeakReferenceId>)sceneIdsObj;
            var fileIdType = typeof(RuntimeContentManager).Assembly.GetType("Unity.Entities.Content.ContentFileId");
            var archiveIdType = typeof(RuntimeContentManager).Assembly.GetType("Unity.Entities.Content.ContentArchiveId");
            var depsType = typeof(UnsafeList<>).MakeGenericType(fileIdType);

            for (int i = 0; i < sceneIds.Length; i++)
            {
                var sceneId = sceneIds[i];
                var sceneGuid = NormalizeGuidString(sceneId.GlobalId.AssetGUID.ToString());
                if (string.IsNullOrEmpty(sceneGuid))
                {
                    continue;
                }

                var sceneArgs = new object[] { sceneId, null, null };
                var sceneOk = tryGetSceneLocation.Invoke(catalogBoxed, sceneArgs);
                if (!(sceneOk is bool sceneFound) || !sceneFound)
                {
                    continue;
                }

                var fileId = sceneArgs[1];
                var archiveIds = new HashSet<string>(StringComparer.Ordinal);
                CollectArchiveIdsReflect(catalogBoxed, tryGetFileLocation, fileId, fileIdType, archiveIdType, depsType, archiveIds, new HashSet<object>());

                if (!result.TryGetValue(sceneGuid, out var list))
                {
                    list = new List<string>();
                    result[sceneGuid] = list;
                }

                foreach (var archiveId in archiveIds)
                {
                    if (!list.Contains(archiveId))
                    {
                        list.Add(archiveId);
                    }
                }
            }
        }
        finally
        {
            if (sceneIdsObj is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        return result;
    }

    static MethodInfo FindTryGetSceneLocation(Type catalogType)
    {
        foreach (var method in catalogType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != "TryGetSceneLocation")
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 3 &&
                parameters[0].ParameterType == typeof(UntypedWeakReferenceId) &&
                parameters[2].ParameterType == typeof(string).MakeByRefType())
            {
                return method;
            }
        }

        return null;
    }

    static MethodInfo FindTryGetFileLocation(Type catalogType)
    {
        foreach (var method in catalogType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != "TryGetFileLocation")
            {
                continue;
            }

            var parameters = method.GetParameters();
            // TryGetFileLocation(ContentFileId, out string, out UnsafeList<ContentFileId>, out ContentArchiveId, out int)
            if (parameters.Length == 5 &&
                parameters[1].ParameterType == typeof(string).MakeByRefType() &&
                parameters[4].ParameterType == typeof(int).MakeByRefType())
            {
                return method;
            }
        }

        return null;
    }

    static void CollectArchiveIdsReflect(
        object catalogBoxed,
        MethodInfo tryGetFileLocation,
        object fileId,
        Type fileIdType,
        Type archiveIdType,
        Type depsType,
        HashSet<string> archives,
        HashSet<object> visitedFiles)
    {
        if (fileId == null)
        {
            return;
        }

        // ContentFileId is a struct; use string key for visit set.
        var fileKey = fileId.ToString();
        if (!visitedFiles.Add(fileKey))
        {
            return;
        }

        var args = new object[] { fileId, null, Activator.CreateInstance(depsType), Activator.CreateInstance(archiveIdType), 0 };
        var ok = tryGetFileLocation.Invoke(catalogBoxed, args);
        if (!(ok is bool found) || !found)
        {
            return;
        }

        var archiveId = args[3];
        if (archiveId != null)
        {
            var isValidProp = archiveIdType.GetProperty("IsValid");
            var isValid = isValidProp == null || (bool)isValidProp.GetValue(archiveId);
            if (isValid)
            {
                var valueField = archiveIdType.GetField("Value");
                var archiveIdStr = valueField != null
                    ? valueField.GetValue(archiveId)?.ToString()
                    : archiveId.ToString();
                if (!string.IsNullOrEmpty(archiveIdStr) && !archiveIdStr.Contains("000000000000000000"))
                {
                    archives.Add(archiveIdStr);
                }
            }
        }

        var deps = args[2];
        if (deps == null)
        {
            return;
        }

        int length;
        var lengthProperty = depsType.GetProperty("Length");
        if (lengthProperty != null)
        {
            length = (int)lengthProperty.GetValue(deps);
        }
        else
        {
            var lengthField = depsType.GetField("Length");
            if (lengthField == null)
            {
                return;
            }

            length = (int)lengthField.GetValue(deps);
        }

        var indexer = depsType.GetProperty("Item");
        if (indexer == null)
        {
            return;
        }

        for (int i = 0; i < length; i++)
        {
            var depFileId = indexer.GetValue(deps, new object[] { i });
            CollectArchiveIdsReflect(catalogBoxed, tryGetFileLocation, depFileId, fileIdType, archiveIdType, depsType, archives, visitedFiles);
        }
    }

    public struct ParentSceneInfo
    {
        public string sceneName;
        public string sceneGuid;
        public HashSet<string> relatedContentGuids;
    }
}
