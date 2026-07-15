using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Entities.Content;
using UnityEngine;
using ZG;

public class GameSceneActivation : IGameSceneActivation
{
    const int CopyBufferSize = 64 * 1024;

    List<string> __materializedPaths;

    /*public static void CreateDirectory(string path)
    {
        string folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || Directory.Exists(folder))
            return;

        CreateDirectory(folder);

        Directory.CreateDirectory(folder);
    }*/

    public IEnumerator Init(string sceneName)
    {
        __materializedPaths = null;

#if ENABLE_CONTENT_DELIVERY
        while (ContentDeliveryGlobalState.CurrentContentUpdateState <
               ContentDeliveryGlobalState.ContentUpdateState.ContentReady)
            yield return null;

        MaterializeArchivesForScene(AssetFileUtility.GetFileNameWithoutExtension(sceneName));
#else
        yield break;
#endif
    }

    public void Dispose()
    {
#if ENABLE_CONTENT_DELIVERY
        DematerializeArchives();
#endif
        __materializedPaths = null;
    }

#if ENABLE_CONTENT_DELIVERY
    void MaterializeArchivesForScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[GameSceneActivation] Init sceneName is empty; skip archive materialization.");
            return;
        }

        if (!TryLoadDependencyFile(out var dependencyFile) || dependencyFile.scenes == null)
        {
            Debug.LogError(
                $"[GameSceneActivation] Missing {SceneArchiveDependencies.RelativePath}; cannot materialize archives for '{sceneName}'.");
            return;
        }

        SceneArchiveDependencies.SceneEntry entry = null;
        for (int i = 0; i < dependencyFile.scenes.Length; i++)
        {
            var candidate = dependencyFile.scenes[i];
            if (candidate != null &&
                string.Equals(candidate.sceneName, sceneName, StringComparison.OrdinalIgnoreCase))
            {
                entry = candidate;
                break;
            }
        }

        if (entry == null || entry.archives == null || entry.archives.Length == 0)
        {
            Debug.LogWarning(
                $"[GameSceneActivation] No archive list for scene '{sceneName}' in {SceneArchiveDependencies.FileName}.");
            return;
        }

        var remap = ContentDeliveryGlobalState.PathRemapFunc;
        if (remap == null)
        {
            Debug.LogError("[GameSceneActivation] PathRemapFunc is null.");
            return;
        }

        var assetManager = GameMain.sceneArchiveAssetManager;
        if (assetManager == null)
        {
            Debug.LogError("[GameSceneActivation] SceneArchiveAssetManager is null.");
            return;
        }

        __materializedPaths = new List<string>(entry.archives.Length);

        for (int i = 0; i < entry.archives.Length; i++)
        {
            var archiveId = entry.archives[i];
            if (string.IsNullOrEmpty(archiveId))
                continue;

            var relativePath = RuntimeContentManager.DefaultArchivePathFunc(archiveId);
            var destPath = remap(relativePath);
            if (string.IsNullOrEmpty(destPath))
            {
                Debug.LogError($"[GameSceneActivation] Remap failed for {relativePath}");
                
                continue;
            }

            // Only track files we create so Dispose won't delete archives still needed by another scene.
            if (File.Exists(destPath))
                continue;

            var assetName = relativePath.ToLowerInvariant();
            if (!assetManager.GetAssetPath(assetName, out _, out ulong fileOffset, out string sourcePath) ||
                string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogError($"[GameSceneActivation] GetAssetPath failed for {assetName}");
                continue;
            }

            if (fileOffset != 0)
            {
                Debug.LogError($"[GameSceneActivation] Non-zero fileOffset ({fileOffset}) for {assetName}; cannot Mount.");
                continue;
            }

            try
            {
                CopyAssetToFile(sourcePath, destPath);
                __materializedPaths.Add(destPath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[GameSceneActivation] Failed to materialize {sourcePath} -> {destPath}");
            }
        }

        Debug.Log(
            $"[GameSceneActivation] Materialized {__materializedPaths.Count}/{entry.archives.Length} archives for '{sceneName}'.");
    }

    void DematerializeArchives()
    {
        if (__materializedPaths == null)
        {
            return;
        }

        for (int i = 0; i < __materializedPaths.Count; i++)
        {
            var path = __materializedPaths[i];
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSceneActivation] Failed to delete {path}: {e.Message}");
            }
        }
    }

    static bool TryLoadDependencyFile(out SceneArchiveDependencies.File dependencyFile)
    {
        dependencyFile = null;

        var remap = ContentDeliveryGlobalState.PathRemapFunc;
        var relativePath = SceneArchiveDependencies.RelativePath;
        if (remap != null)
            // Ensure content folder is LoadFrom'd via existing PathRemap side effects.
            remap(relativePath);

        var assetManager = GameMain.sceneArchiveAssetManager;
        if (assetManager == null)
            return false;

        var assetName = relativePath.ToLowerInvariant();
        if (!assetManager.GetAssetPath(assetName, out _, out _, out string sourcePath) ||
            string.IsNullOrEmpty(sourcePath))
            return false;

        var bytes = AssetFileUtility.ReadAllBytes(sourcePath);
        if (bytes == null || bytes.Length == 0)
            return false;

        dependencyFile = JsonUtility.FromJson<SceneArchiveDependencies.File>(Encoding.UTF8.GetString(bytes));
        return dependencyFile != null;
    }

    static void CopyAssetToFile(string sourcePath, string destPath)
    {
        var folder = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        using (var src = AssetFileUtility.Open(sourcePath, FileMode.Open, FileAccess.Read))
        using (var dst = File.Open(destPath, FileMode.Create, FileAccess.Write))
        {
            var buffer = new byte[CopyBufferSize];
            int read;
            while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                dst.Write(buffer, 0, read);
            }
        }
    }
#endif
}
