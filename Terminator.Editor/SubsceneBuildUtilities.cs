using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
    [MenuItem("SubSceneBuildUtilities/BuildContent")]
    //prepares the content files for publish.  The original files can be deleted or retained during this process by changing the last parameter of the PublishContent call.
    static void BuildContent()
    {
        var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish",
            Path.GetDirectoryName(Application.dataPath), "Builds");
        if (string.IsNullOrEmpty(buildFolder))
            return;

        var buildTarget = EditorUserBuildSettings.activeBuildTarget;

        var instance = DotsGlobalSettings.Instance;
        var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client ? instance.GetClientGUID() : instance.GetServerGUID();
        if (!playerGuid.IsValid)
        {
            throw new Exception("Invalid Player GUID");
        }

        var subSceneGuids = new HashSet<Hash128>();
        var parentScenes = new List<ParentSceneInfo>();

        foreach (var sceneGUID in Selection.assetGUIDs)
        {
            if (!GUID.TryParse(sceneGUID, out var guid))
                continue;

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
                continue;

            var sceneName = Path.GetFileNameWithoutExtension(assetPath);
            var relatedGuids = new HashSet<Hash128> { new Hash128(guid.ToString()) };

            var ssGuids = EditorEntityScenes.GetSubScenes(guid);
            foreach (var ss in ssGuids)
            {
                relatedGuids.Add(ss);
                subSceneGuids.Add(ss);
            }

            parentScenes.Add(new ParentSceneInfo
            {
                sceneName = sceneName,
                sceneGuid = guid.ToString(),
                relatedContentGuids = relatedGuids
            });
        }

        if (parentScenes.Count == 0)
        {
            Debug.LogError("[SubSceneBuildUtilities] No scenes selected. Select parent Unity scene assets first.");
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
            return;
        
        var subSceneGuids = new HashSet<Hash128>();
        var parentScenes = new List<ParentSceneInfo>();

        foreach (var sceneGUID in Selection.assetGUIDs)
        {
            if (!GUID.TryParse(sceneGUID, out var guid))
                continue;

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
                continue;

            var sceneName = Path.GetFileNameWithoutExtension(assetPath);
            var relatedGuids = new HashSet<Hash128> { new Hash128(guid.ToString()) };

            var ssGuids = EditorEntityScenes.GetSubScenes(guid);
            foreach (var ss in ssGuids)
            {
                relatedGuids.Add(ss);
                subSceneGuids.Add(ss);
            }

            parentScenes.Add(new ParentSceneInfo
            {
                sceneName = sceneName,
                sceneGuid = guid.ToString(),
                relatedContentGuids = relatedGuids
            });
        }

        if (parentScenes.Count == 0)
        {
            Debug.LogError("[SubSceneBuildUtilities] No scenes selected. Select parent Unity scene assets first.");
            return;
        }

        WriteSceneArchiveDependencies(buildFolder, parentScenes);
    }

    /// <summary>
    /// Maps each parent Selection scene to the union of Content archive deps for its SubScenes
    /// (and the parent GUID if present in catalog). Written next to archive_dependencies.bin.
    /// </summary>
    public static void WriteSceneArchiveDependencies(string buildFolder, List<ParentSceneInfo> parentScenes)
    {
        var catalogPath = Path.Combine(buildFolder, RuntimeContentManager.RelativeCatalogPath);
        if (!File.Exists(catalogPath))
        {
            Debug.LogError($"[SubSceneBuildUtilities] Catalog not found: {catalogPath}");
            return;
        }

        if (!BlobAssetReference<CatalogDataMirror>.TryRead(catalogPath, 1, out var catalogBlob))
        {
            Debug.LogError($"[SubSceneBuildUtilities] Failed to read catalog: {catalogPath}");
            return;
        }

        try
        {
            ref var catalog = ref catalogBlob.Value;
            var entries = new List<SceneArchiveDependencies.SceneEntry>(parentScenes.Count);

            foreach (var parent in parentScenes)
            {
                var archives = new HashSet<string>();
                for (int sceneIndex = 0; sceneIndex < catalog.Scenes.Length; sceneIndex++)
                {
                    var contentScene = catalog.Scenes[sceneIndex];
                    if (!parent.relatedContentGuids.Contains(contentScene.SceneId.GlobalId.AssetGUID))
                    {
                        continue;
                    }

                    var visitedFiles = new HashSet<int>();
                    CollectArchiveIds(ref catalog, contentScene.FileIndex, archives, visitedFiles);
                }

                var archiveList = new List<string>(archives);
                archiveList.Sort(StringComparer.Ordinal);

                entries.Add(new SceneArchiveDependencies.SceneEntry
                {
                    sceneName = parent.sceneName,
                    sceneGuid = parent.sceneGuid,
                    archives = archiveList.ToArray()
                });

                Debug.Log(
                    $"[SubSceneBuildUtilities] {parent.sceneName}: {archiveList.Count} archives " +
                    $"({parent.relatedContentGuids.Count} related content GUIDs)");
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
            catalogBlob.Dispose();
        }
    }

    static void CollectArchiveIds(
        ref CatalogDataMirror catalog,
        int fileIndex,
        HashSet<string> archives,
        HashSet<int> visitedFiles)
    {
        if (fileIndex < 0 || fileIndex >= catalog.Files.Length)
        {
            return;
        }

        if (!visitedFiles.Add(fileIndex))
        {
            return;
        }

        var file = catalog.Files[fileIndex];
        if (file.ArchiveIndex >= 0 && file.ArchiveIndex < catalog.Archives.Length)
        {
            var archiveId = catalog.Archives[file.ArchiveIndex].ArchiveId.Value;
            if (archiveId.IsValid)
            {
                var archiveIdStr = archiveId.ToString();
                if (!archiveIdStr.Contains("000000000000000000"))
                {
                    archives.Add(archiveIdStr);
                }
            }
        }

        if (file.DependencyIndex < 0 || file.DependencyIndex >= catalog.Dependencies.Length)
        {
            return;
        }

        ref var deps = ref catalog.Dependencies[file.DependencyIndex];
        for (int i = 0; i < deps.Length; i++)
        {
            CollectArchiveIds(ref catalog, deps[i], archives, visitedFiles);
        }
    }

    public struct ParentSceneInfo
    {
        public string sceneName;
        public string sceneGuid;
        public HashSet<Hash128> relatedContentGuids;
    }

    // Layout must match Unity.Entities.Content.RuntimeContentCatalogData (blob version 1).
    [StructLayout(LayoutKind.Sequential)]
    struct CatalogDataMirror
    {
        public BlobArray<ArchiveLocationMirror> Archives;
        public BlobArray<FileLocationMirror> Files;
        public BlobArray<ObjectLocationMirror> Objects;
        public BlobArray<SceneLocationMirror> Scenes;
        public BlobArray<BlobArray<int>> Dependencies;
        public BlobArray<BlobLocationMirror> Blobs;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ArchiveIdMirror
    {
        public Hash128 Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FileIdMirror
    {
        public Hash128 Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ArchiveLocationMirror
    {
        public ArchiveIdMirror ArchiveId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FileLocationMirror
    {
        public FileIdMirror FileId;
        public int ArchiveIndex;
        public int DependencyIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ObjectLocationMirror
    {
        public UntypedWeakReferenceId ObjectId;
        public int FileIndex;
        public long LocalIdentifierInFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SceneLocationMirror
    {
        public UntypedWeakReferenceId SceneId;
        public int FileIndex;
        public FixedString128Bytes SceneName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BlobLocationMirror
    {
        public UntypedWeakReferenceId ObjectId;
        public int FileIndex;
        public long Offset;
        public long Length;
    }
}
