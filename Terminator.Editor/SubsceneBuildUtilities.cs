using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Build;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

static class SubSceneBuildUtilities
{
    const string WeakAssetRefsExtension = ".weakassetrefs";
    const string EntitiesBinaryExtension = ".entities";

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
        var instance = DotsGlobalSettings.Instance;
        var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client
            ? instance.GetClientGUID()
            : instance.GetServerGUID();
        if (!playerGuid.IsValid)
        {
            throw new Exception("Invalid Player GUID");
        }

        if (!TryCollectParentScenes(out var subSceneGuids, out var parentScenes))
        {
            return;
        }

        RemoteContentCatalogBuildUtility.BuildContent(subSceneGuids, playerGuid, buildTarget, buildFolder);
        WriteSceneArchiveDependencies(buildFolder, parentScenes, playerGuid);
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

        var instance = DotsGlobalSettings.Instance;
        var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client
            ? instance.GetClientGUID()
            : instance.GetServerGUID();
        if (!playerGuid.IsValid)
        {
            throw new Exception("Invalid Player GUID");
        }

        if (!TryCollectParentScenes(out _, out var parentScenes))
        {
            return;
        }

        WriteSceneArchiveDependencies(buildFolder, parentScenes, playerGuid);
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
                sceneGuid = NormalizeGuidString(guid.ToString()),
                subSceneGuids = relatedSubScenes
            });

            Debug.Log(
                $"[SubSceneBuildUtilities] Parent '{sceneName}' SubScenes ({relatedSubScenes.Count}): " +
                string.Join(", ", relatedSubScenes));
        }

        if (parentScenes.Count == 0)
        {
            Debug.LogError("[SubSceneBuildUtilities] No scenes selected. Select parent Unity scene assets first.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parent Unity scene → ContentArchives needed for its SubScenes' weak refs (and section hybrid refs).
    /// Catalog Scenes are Weak SceneAssets, NOT Entity SubScene GUIDs — do not match Selection GUID to catalog.Scenes.
    /// </summary>
    public static void WriteSceneArchiveDependencies(string buildFolder, List<ParentSceneInfo> parentScenes, Hash128 playerGuid)
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
            BuildCatalogIndices(catalogBoxed, catalogType, out var byAssetGuid, out var byObjectId, out int objectCount, out int sceneCount, out int archiveCount);
            Debug.Log(
                $"[SubSceneBuildUtilities] Catalog loaded: objects={objectCount}, scenes={sceneCount}, " +
                $"archives={archiveCount}, guidKeys={byAssetGuid.Count}, objectKeys={byObjectId.Count}");

            var entries = new List<SceneArchiveDependencies.SceneEntry>(parentScenes.Count);
            foreach (var parent in parentScenes)
            {
                var archives = new HashSet<string>(StringComparer.Ordinal);
                foreach (var subSceneGuid in parent.subSceneGuids)
                {
                    CollectArchivesForSubScene(
                        subSceneGuid,
                        playerGuid,
                        catalogBoxed,
                        catalogType,
                        byAssetGuid,
                        byObjectId,
                        archives);
                }

                var archiveList = new List<string>(archives);
                archiveList.Sort(StringComparer.Ordinal);

                entries.Add(new SceneArchiveDependencies.SceneEntry
                {
                    sceneName = parent.sceneName,
                    sceneGuid = parent.sceneGuid,
                    archives = archiveList.ToArray()
                });

                Debug.Log($"[SubSceneBuildUtilities] {parent.sceneName}: {archiveList.Count} archives from {parent.subSceneGuids.Count} SubScenes");
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.sceneName, b.sceneName));

            var dependencyFile = new SceneArchiveDependencies.File
            {
                catalog = RuntimeContentManager.RelativeCatalogPath.Replace('\\', '/'),
                scenes = entries.ToArray()
            };

            var outDir = Path.GetDirectoryName(catalogPath) ?? buildFolder;
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

    static void CollectArchivesForSubScene(
        Hash128 subSceneGuid,
        Hash128 playerGuid,
        object catalogBoxed,
        Type catalogType,
        Dictionary<string, HashSet<string>> byAssetGuid,
        Dictionary<string, HashSet<string>> byObjectId,
        HashSet<string> archives)
    {
        if (!TryGetSubSceneArtifactPaths(subSceneGuid, playerGuid, out var artifactPaths) || artifactPaths == null)
        {
            Debug.LogWarning($"[SubSceneBuildUtilities] No artifacts for SubScene {subSceneGuid}");
            return;
        }

        // 1) Embedded weak refs from SubScene import artifact
        foreach (var path in artifactPaths)
        {
            if (path == null || !path.EndsWith(WeakAssetRefsExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!BlobAssetReference<BlobArray<UntypedWeakReferenceId>>.TryRead(path, 1, out var weakAssets))
            {
                Debug.LogWarning($"[SubSceneBuildUtilities] Failed to read weakassetrefs: {path}");
                continue;
            }

            try
            {
                for (int i = 0; i < weakAssets.Value.Length; i++)
                {
                    AddArchivesForWeakId(weakAssets.Value[i], byAssetGuid, byObjectId, archives);
                }
            }
            finally
            {
                weakAssets.Dispose();
            }
        }

        // 2) ReferencedUnityObjects section hybrid ids (catalog stores remapped SubSceneObjectReferences)
        var tryGetObjectLocation = FindTryGetObjectLocation(catalogType);
        var tryGetFileLocation = FindTryGetFileLocation(catalogType);
        foreach (var path in artifactPaths)
        {
            if (path == null || !path.EndsWith(EntitiesBinaryExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sectionIndex = TryParseSectionIndexFromEntitiesPath(path);
            if (sectionIndex < 0)
            {
                continue;
            }

            // CreateSceneSectionHash(sceneGuid, sectionIndex, default) — artifactHash invalid → keep sceneGUID
            var sectionId = new UntypedWeakReferenceId
            {
                GenerationType = WeakReferenceGenerationType.SubSceneObjectReferences,
                GlobalId = new RuntimeGlobalObjectId
                {
                    AssetGUID = subSceneGuid,
                    SceneObjectIdentifier0 = sectionIndex
                }
            };

            AddArchivesForWeakId(sectionId, byAssetGuid, byObjectId, archives);

            if (tryGetObjectLocation != null && tryGetFileLocation != null)
            {
                CollectArchivesForObjectId(catalogBoxed, tryGetObjectLocation, tryGetFileLocation, sectionId, archives);
            }
        }
    }

    static void AddArchivesForWeakId(
        UntypedWeakReferenceId id,
        Dictionary<string, HashSet<string>> byAssetGuid,
        Dictionary<string, HashSet<string>> byObjectId,
        HashSet<string> archives)
    {
        if (!id.IsValid)
        {
            return;
        }

        var idKey = id.ToString();
        if (byObjectId.TryGetValue(idKey, out var byId))
        {
            foreach (var a in byId)
            {
                archives.Add(a);
            }
        }

        var guidKey = NormalizeGuidString(id.GlobalId.AssetGUID.ToString());
        if (byAssetGuid.TryGetValue(guidKey, out var byGuid))
        {
            foreach (var a in byGuid)
            {
                archives.Add(a);
            }
        }
    }

    static void CollectArchivesForObjectId(
        object catalogBoxed,
        MethodInfo tryGetObjectLocation,
        MethodInfo tryGetFileLocation,
        UntypedWeakReferenceId objectId,
        HashSet<string> archives)
    {
        var fileIdType = typeof(RuntimeContentManager).Assembly.GetType("Unity.Entities.Content.ContentFileId");
        var archiveIdType = typeof(RuntimeContentManager).Assembly.GetType("Unity.Entities.Content.ContentArchiveId");
        if (fileIdType == null || archiveIdType == null)
        {
            return;
        }

        var args = new object[] { objectId, Activator.CreateInstance(fileIdType), 0L };
        var ok = tryGetObjectLocation.Invoke(catalogBoxed, args);
        if (!(ok is bool found) || !found)
        {
            return;
        }

        var fileId = args[1];
        CollectArchiveIdsReflect(catalogBoxed, tryGetFileLocation, fileId, archiveIdType, archives, new HashSet<string>());
    }

    static bool TryGetSubSceneArtifactPaths(Hash128 sceneGuid, Hash128 playerGuid, out string[] artifactPaths)
    {
        artifactPaths = null;
        try
        {
            var scenesEditorAssembly = typeof(EditorEntityScenes).Assembly;
            var swbcType = scenesEditorAssembly.GetType("Unity.Scenes.Editor.SceneWithBuildConfigurationGUIDs");
            var importerType = scenesEditorAssembly.GetType("Unity.Scenes.Editor.SubSceneImporter");
            if (swbcType == null || importerType == null)
            {
                Debug.LogError("[SubSceneBuildUtilities] SceneWithBuildConfigurationGUIDs / SubSceneImporter not found.");
                return false;
            }

            var ensure = swbcType.GetMethod(
                "EnsureExistsFor",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Hash128), typeof(Hash128), typeof(bool), typeof(bool).MakeByRefType() },
                null);
            if (ensure == null)
            {
                Debug.LogError("[SubSceneBuildUtilities] EnsureExistsFor not found.");
                return false;
            }

            var ensureArgs = new object[] { sceneGuid, playerGuid, false, false };
            var buildConfigGuidObj = ensure.Invoke(null, ensureArgs);
            if (ensureArgs[3] is bool needsRefresh && needsRefresh)
            {
                AssetDatabase.Refresh();
                ensureArgs[3] = false;
                buildConfigGuidObj = ensure.Invoke(null, ensureArgs);
            }

            GUID buildConfigGuid;
            if (buildConfigGuidObj is GUID g)
            {
                buildConfigGuid = g;
            }
            else if (buildConfigGuidObj is Hash128 h)
            {
                buildConfigGuid = new GUID(h.ToString());
            }
            else
            {
                Debug.LogError($"[SubSceneBuildUtilities] Unexpected EnsureExistsFor return: {buildConfigGuidObj?.GetType()}");
                return false;
            }

            var artifactKey = new UnityEditor.Experimental.ArtifactKey(buildConfigGuid, importerType);
            var artifactId = UnityEditor.Experimental.AssetDatabaseExperimental.ProduceArtifact(artifactKey);
            if (!UnityEditor.Experimental.AssetDatabaseExperimental.GetArtifactPaths(artifactId, out artifactPaths) ||
                artifactPaths == null ||
                artifactPaths.Length == 0)
            {
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    static int TryParseSectionIndexFromEntitiesPath(string path)
    {
        // Artifact/build path: "{sceneGuid}.{sectionIndex}.entities"
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        var lastDot = name.LastIndexOf('.');
        if (lastDot < 0 || lastDot >= name.Length - 1)
        {
            return 0;
        }

        return int.TryParse(name.Substring(lastDot + 1), out var sectionIndex) ? sectionIndex : 0;
    }

    static string ResolveCatalogPath(string buildFolder)
    {
        var catalogPath = Path.Combine(buildFolder, RuntimeContentManager.RelativeCatalogPath);
        if (File.Exists(catalogPath))
        {
            return catalogPath;
        }

        var bytesPath = catalogPath + ".bytes";
        return File.Exists(bytesPath) ? bytesPath : null;
    }

    static string NormalizeGuidString(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return string.Empty;
        }

        return guid.Replace("-", string.Empty).ToLowerInvariant();
    }

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
        // Value-type catalog must be reassigned after Invoke so Native container fields persist on the box.
        var loaded = loadMethod.Invoke(catalogBoxed, new object[] { catalogPath, identity, identity });
        return loaded is bool ok && ok;
    }

    static void BuildCatalogIndices(
        object catalogBoxed,
        Type catalogType,
        out Dictionary<string, HashSet<string>> byAssetGuid,
        out Dictionary<string, HashSet<string>> byObjectId,
        out int objectCount,
        out int sceneCount,
        out int archiveCount)
    {
        byAssetGuid = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        byObjectId = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        objectCount = 0;
        sceneCount = 0;
        archiveCount = 0;

        var tryGetObjectLocation = FindTryGetObjectLocation(catalogType);
        var tryGetSceneLocation = FindTryGetSceneLocation(catalogType);
        var tryGetFileLocation = FindTryGetFileLocation(catalogType);
        var getObjectIds = catalogType.GetMethod("GetObjectIds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var getSceneIds = catalogType.GetMethod("GetSceneIds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var getArchiveIds = catalogType.GetMethod("GetArchiveIds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var archiveIdType = typeof(RuntimeContentManager).Assembly.GetType("Unity.Entities.Content.ContentArchiveId");

        if (tryGetFileLocation == null || archiveIdType == null)
        {
            Debug.LogError("[SubSceneBuildUtilities] Catalog query methods incomplete.");
            return;
        }

        AllocatorManager.AllocatorHandle alloc = Allocator.Temp;

        if (getArchiveIds != null)
        {
            var archiveIdsObj = getArchiveIds.Invoke(catalogBoxed, new object[] { alloc });
            if (archiveIdsObj != null)
            {
                try
                {
                    var lengthProp = archiveIdsObj.GetType().GetProperty("Length");
                    archiveCount = lengthProp != null ? (int)lengthProp.GetValue(archiveIdsObj) : 0;
                }
                finally
                {
                    (archiveIdsObj as IDisposable)?.Dispose();
                }
            }
        }

        if (getObjectIds != null && tryGetObjectLocation != null)
        {
            var objectIdsObj = getObjectIds.Invoke(catalogBoxed, new object[] { alloc });
            if (objectIdsObj is NativeArray<UntypedWeakReferenceId> objectIds)
            {
                try
                {
                    objectCount = objectIds.Length;
                    for (int i = 0; i < objectIds.Length; i++)
                    {
                        IndexObjectOrScene(catalogBoxed, tryGetObjectLocation, tryGetFileLocation, archiveIdType, objectIds[i], byAssetGuid, byObjectId);
                    }
                }
                finally
                {
                    objectIds.Dispose();
                }
            }
        }

        if (getSceneIds != null && tryGetSceneLocation != null)
        {
            var sceneIdsObj = getSceneIds.Invoke(catalogBoxed, new object[] { alloc });
            if (sceneIdsObj is NativeArray<UntypedWeakReferenceId> sceneIds)
            {
                try
                {
                    sceneCount = sceneIds.Length;
                    for (int i = 0; i < sceneIds.Length; i++)
                    {
                        IndexScene(catalogBoxed, tryGetSceneLocation, tryGetFileLocation, archiveIdType, sceneIds[i], byAssetGuid, byObjectId);
                    }
                }
                finally
                {
                    sceneIds.Dispose();
                }
            }
        }
    }

    static void IndexObjectOrScene(
        object catalogBoxed,
        MethodInfo tryGetObjectLocation,
        MethodInfo tryGetFileLocation,
        Type archiveIdType,
        UntypedWeakReferenceId objectId,
        Dictionary<string, HashSet<string>> byAssetGuid,
        Dictionary<string, HashSet<string>> byObjectId)
    {
        var fileIdType = typeof(RuntimeContentManager).Assembly.GetType("Unity.Entities.Content.ContentFileId");
        var args = new object[] { objectId, Activator.CreateInstance(fileIdType), 0L };
        var ok = tryGetObjectLocation.Invoke(catalogBoxed, args);
        if (!(ok is bool found) || !found)
        {
            return;
        }

        var archives = new HashSet<string>(StringComparer.Ordinal);
        CollectArchiveIdsReflect(catalogBoxed, tryGetFileLocation, args[1], archiveIdType, archives, new HashSet<string>());
        StoreIndex(objectId, archives, byAssetGuid, byObjectId);
    }

    static void IndexScene(
        object catalogBoxed,
        MethodInfo tryGetSceneLocation,
        MethodInfo tryGetFileLocation,
        Type archiveIdType,
        UntypedWeakReferenceId sceneId,
        Dictionary<string, HashSet<string>> byAssetGuid,
        Dictionary<string, HashSet<string>> byObjectId)
    {
        var fileIdType = typeof(RuntimeContentManager).Assembly.GetType("Unity.Entities.Content.ContentFileId");
        var args = new object[] { sceneId, Activator.CreateInstance(fileIdType), null };
        var ok = tryGetSceneLocation.Invoke(catalogBoxed, args);
        if (!(ok is bool found) || !found)
        {
            return;
        }

        var archives = new HashSet<string>(StringComparer.Ordinal);
        CollectArchiveIdsReflect(catalogBoxed, tryGetFileLocation, args[1], archiveIdType, archives, new HashSet<string>());
        StoreIndex(sceneId, archives, byAssetGuid, byObjectId);
    }

    static void StoreIndex(
        UntypedWeakReferenceId id,
        HashSet<string> archives,
        Dictionary<string, HashSet<string>> byAssetGuid,
        Dictionary<string, HashSet<string>> byObjectId)
    {
        if (archives.Count == 0)
        {
            return;
        }

        var idKey = id.ToString();
        if (!byObjectId.TryGetValue(idKey, out var idSet))
        {
            idSet = new HashSet<string>(StringComparer.Ordinal);
            byObjectId[idKey] = idSet;
        }

        var guidKey = NormalizeGuidString(id.GlobalId.AssetGUID.ToString());
        if (!byAssetGuid.TryGetValue(guidKey, out var guidSet))
        {
            guidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byAssetGuid[guidKey] = guidSet;
        }

        foreach (var a in archives)
        {
            idSet.Add(a);
            guidSet.Add(a);
        }
    }

    static MethodInfo FindTryGetObjectLocation(Type catalogType)
    {
        foreach (var method in catalogType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != "TryGetObjectLocation")
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 3 && parameters[0].ParameterType == typeof(UntypedWeakReferenceId))
            {
                return method;
            }
        }

        return null;
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
        Type archiveIdType,
        HashSet<string> archives,
        HashSet<string> visitedFiles)
    {
        if (fileId == null)
        {
            return;
        }

        var fileKey = fileId.ToString();
        if (!visitedFiles.Add(fileKey))
        {
            return;
        }

        var fileIdType = fileId.GetType();
        var depsType = typeof(UnsafeList<>).MakeGenericType(fileIdType);
        var args = new object[]
        {
            fileId,
            null,
            Activator.CreateInstance(depsType),
            Activator.CreateInstance(archiveIdType),
            0
        };
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
            CollectArchiveIdsReflect(catalogBoxed, tryGetFileLocation, depFileId, archiveIdType, archives, visitedFiles);
        }
    }

    public struct ParentSceneInfo
    {
        public string sceneName;
        public string sceneGuid;
        public HashSet<Hash128> subSceneGuids;
    }
}
