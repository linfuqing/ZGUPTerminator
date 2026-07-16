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

    public IEnumerator Init(string sceneName)
    {
        __materializedPaths = null;

#if ENABLE_CONTENT_DELIVERY
        while (ContentDeliveryGlobalState.CurrentContentUpdateState <
               ContentDeliveryGlobalState.ContentUpdateState.ContentReady)
            yield return null;

        while (AssetFileUtility.isPending)
            yield return null;
        
        MaterializeForScene(AssetFileUtility.GetFileNameWithoutExtension(sceneName));
#else
        yield break;
#endif
    }

    public void Dispose()
    {
#if ENABLE_CONTENT_DELIVERY
        Dematerialize();
#endif
        __materializedPaths = null;
    }

#if ENABLE_CONTENT_DELIVERY
    void MaterializeForScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[GameSceneActivation] Init sceneName is empty; skip materialization.");
            return;
        }

        if (!TryLoadDependencyFile(out var dependencyFile) || dependencyFile.scenes == null)
        {
            Debug.LogError(
                $"[GameSceneActivation] Missing {SceneArchiveDependencies.RelativePath}; cannot materialize for '{sceneName}'.");
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

        if (entry == null)
        {
            Debug.LogWarning(
                $"[GameSceneActivation] No dependency entry for scene '{sceneName}' in {SceneArchiveDependencies.FileName}.");
            return;
        }

        var archiveCount = entry.archives != null ? entry.archives.Length : 0;
        var entitySceneCount = entry.entityScenes != null ? entry.entityScenes.Length : 0;
        if (archiveCount == 0 && entitySceneCount == 0)
        {
            Debug.LogWarning(
                $"[GameSceneActivation] Empty archives and EntityScenes for '{sceneName}' in {SceneArchiveDependencies.FileName}.");
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

        __materializedPaths = new List<string>(archiveCount + entitySceneCount);

        for (int i = 0; i < archiveCount; i++)
        {
            var archiveId = entry.archives[i];
            if (string.IsNullOrEmpty(archiveId))
                continue;

            MaterializeRelativePath(RuntimeContentManager.DefaultArchivePathFunc(archiveId), remap, assetManager);
        }

        for (int i = 0; i < entitySceneCount; i++)
        {
            var relativePath = entry.entityScenes[i];
            if (string.IsNullOrEmpty(relativePath))
                continue;

            MaterializeRelativePath(relativePath.Replace('\\', '/'), remap, assetManager);
        }

        Debug.Log(
            $"[GameSceneActivation] Materialized {__materializedPaths.Count} files for '{sceneName}' " +
            $"(archives={archiveCount}, entityScenes={entitySceneCount}).");
    }

    void MaterializeRelativePath(string relativePath, Func<string, string> remap, AssetManager assetManager)
    {
        var destPath = remap(relativePath);
        if (string.IsNullOrEmpty(destPath))
        {
            Debug.LogError($"[GameSceneActivation] Remap failed for {relativePath}");
            return;
        }

        // Only track files we create so Dispose won't delete files still needed by another scene.
        if (File.Exists(destPath))
        {
            return;
        }

        if (!TryResolveAssetSource(relativePath, assetManager, out var sourcePath, out var fileOffset))
        {
            Debug.LogError($"[GameSceneActivation] GetAssetPath failed for {relativePath}");
            return;
        }

        if (fileOffset != 0)
        {
            Debug.LogError($"[GameSceneActivation] Non-zero fileOffset ({fileOffset}) for {relativePath}; cannot materialize.");
            return;
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

    static bool TryResolveAssetSource(
        string relativePath,
        AssetManager assetManager,
        out string sourcePath,
        out ulong fileOffset)
    {
        sourcePath = null;
        fileOffset = 0;

        var candidates = new[]
        {
            relativePath.ToLowerInvariant(),
            (relativePath + ".bytes").ToLowerInvariant(),
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (assetManager.GetAssetPath(candidates[i], out _, out fileOffset, out sourcePath) &&
                !string.IsNullOrEmpty(sourcePath))
            {
                return true;
            }
        }

        return false;
    }

    void Dematerialize()
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

        var assetManager = GameMain.sceneArchiveAssetManager;
        if (assetManager == null)
            return false;

        var relativePath = SceneArchiveDependencies.RelativePathWithoutExtension;
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
        {
            Directory.CreateDirectory(folder);
        }

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
