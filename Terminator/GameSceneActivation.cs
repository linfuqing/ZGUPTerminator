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
    private int __maxEntityScenesPerTime;
    private int __maxArchivesPerTime;
    
    private List<string> __materializedPaths;

    public bool isInitialized
    {
        get; 
        private set;
    }

    public float initializedProgress
    {
        get;

        private set;
    }

    public GameSceneActivation(int maxEntityScenesPerTime = 64, int maxArchivesPerTime = 32)
    {
        __maxEntityScenesPerTime = maxEntityScenesPerTime;
        __maxArchivesPerTime = maxArchivesPerTime;
    }

    public IEnumerator Init(string sceneName)
    {
        __materializedPaths = null;
 
#if ENABLE_CONTENT_DELIVERY
        while (ContentDeliveryGlobalState.CurrentContentUpdateState <
               ContentDeliveryGlobalState.ContentUpdateState.ContentReady)
            yield return null;

        while (AssetFileUtility.isPending)
            yield return null;
        
        yield return MaterializeForScene(AssetFileUtility.GetFileNameWithoutExtension(sceneName));
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
    IEnumerator MaterializeForScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[GameSceneActivation] Init sceneName is empty; skip materialization.");
            yield break;
        }

        if (!TryLoadDependencyFile(out var dependencyFile) || dependencyFile.scenes == null)
        {
            Debug.LogError(
                $"[GameSceneActivation] Missing {SceneArchiveDependencies.RelativePath}; cannot materialize for '{sceneName}'.");
            yield break;
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
            yield break;
        }

        var archiveRootCount = entry.archiveIndices != null ? entry.archiveIndices.Length : 0;
        var entityRootCount = entry.entitySceneIndices != null ? entry.entitySceneIndices.Length : 0;
        if (archiveRootCount == 0 && entityRootCount == 0)
        {
            Debug.LogWarning(
                $"[GameSceneActivation] Empty archives and EntityScenes for '{sceneName}' in {SceneArchiveDependencies.FileName}.");
            yield break;
        }

        var archivePool = dependencyFile.archives;
        var entityScenePool = dependencyFile.entityScenes;
        if ((archiveRootCount > 0 && (archivePool == null || archivePool.Length == 0)) ||
            (entityRootCount > 0 && (entityScenePool == null || entityScenePool.Length == 0)))
        {
            Debug.LogError(
                $"[GameSceneActivation] Scene '{sceneName}' has indices but shared pools are missing in {SceneArchiveDependencies.FileName}.");
            yield break;
        }

        // Critical: SubScene root headers + same-GUID .entities + their archives (enter scene safely).
        // Deferred: full Entity* closure (Player hub etc.) — overlaps scene load / IGameSceneLoader.
        var criticalArchives = new List<int>(32);
        var criticalEntityScenes = new List<int>(32);
        SceneArchiveDependencies.ExpandSceneRootsLocal(
            dependencyFile, entry, criticalArchives, criticalEntityScenes);

        var fullArchives = new List<int>(64);
        var fullEntityScenes = new List<int>(64);
        SceneArchiveDependencies.ExpandSceneRoots(
            dependencyFile, entry, fullArchives, fullEntityScenes);

        var remap = ContentDeliveryGlobalState.PathRemapFunc;
        if (remap == null)
        {
            Debug.LogError("[GameSceneActivation] PathRemapFunc is null.");
            yield break;
        }

        var assetManager = GameMain.sceneArchiveAssetManager;
        if (assetManager == null)
        {
            Debug.LogError("[GameSceneActivation] SceneArchiveAssetManager is null.");
            yield break;
        }

        __materializedPaths = new List<string>(fullArchives.Count + fullEntityScenes.Count);

        var criticalSteps = Math.Max(1, criticalEntityScenes.Count + criticalArchives.Count);
        var criticalDone = 0;

        yield return MaterializeEntitySceneIndices(
            sceneName,
            criticalEntityScenes,
            entityScenePool,
            remap,
            assetManager,
            () =>
            {
                criticalDone++;
                initializedProgress = criticalDone * 0.95f / criticalSteps;
            });

        if (__materializedPaths == null)
        {
            yield break;
        }

        yield return MaterializeArchiveIndices(
            sceneName,
            criticalArchives,
            archivePool,
            remap,
            assetManager,
            () =>
            {
                criticalDone++;
                initializedProgress = criticalDone * 0.95f / criticalSteps;
            });

        if (__materializedPaths == null)
        {
            yield break;
        }

        PrefabLoaderSettings.isPaused = true;
        initializedProgress = 1f;
        isInitialized = true;

        yield return null;

        var deferredEntityScenes = SubtractSortedIndices(fullEntityScenes, criticalEntityScenes);
        var deferredArchives = SubtractSortedIndices(fullArchives, criticalArchives);

        yield return MaterializeEntitySceneIndices(
            sceneName,
            deferredEntityScenes,
            entityScenePool,
            remap,
            assetManager,
            null);

        if (__materializedPaths == null)
        {
            PrefabLoaderSettings.isPaused = false;
            yield break;
        }

        yield return MaterializeArchiveIndices(
            sceneName,
            deferredArchives,
            archivePool,
            remap,
            assetManager,
            null);

        PrefabLoaderSettings.isPaused = false;

        Debug.Log(
            $"[GameSceneActivation] Materialized {__materializedPaths.Count} files for '{sceneName}' " +
            $"(roots archives={archiveRootCount}, entityScenes={entityRootCount}; " +
            $"critical archives={criticalArchives.Count}, entityScenes={criticalEntityScenes.Count}; " +
            $"deferred archives={deferredArchives.Count}, entityScenes={deferredEntityScenes.Count}; " +
            $"full archives={fullArchives.Count}, entityScenes={fullEntityScenes.Count}).");
    }

    IEnumerator MaterializeEntitySceneIndices(
        string sceneName,
        List<int> indices,
        string[] entityScenePool,
        Func<string, string> remap,
        AssetManager assetManager,
        Action onEach)
    {
        if (indices == null || indices.Count == 0)
        {
            yield break;
        }

        for (int i = 0; i < indices.Count; i++)
        {
            var poolIndex = indices[i];
            if (poolIndex < 0 || poolIndex >= entityScenePool.Length)
            {
                Debug.LogError(
                    $"[GameSceneActivation] expanded entityScene index {poolIndex} out of range " +
                    $"(pool={entityScenePool.Length}) for '{sceneName}'.");
            }
            else
            {
                var relativePath = entityScenePool[poolIndex];
                if (!string.IsNullOrEmpty(relativePath))
                {
                    MaterializeRelativePath(relativePath.Replace('\\', '/'), remap, assetManager);
                }
            }

            if (onEach != null)
            {
                onEach();
            }

            if ((i % __maxEntityScenesPerTime) == 0)
            {
                yield return null;

                if (__materializedPaths == null)
                {
                    yield break;
                }
            }
        }
    }

    IEnumerator MaterializeArchiveIndices(
        string sceneName,
        List<int> indices,
        string[] archivePool,
        Func<string, string> remap,
        AssetManager assetManager,
        Action onEach)
    {
        if (indices == null || indices.Count == 0)
        {
            yield break;
        }

        for (int i = 0; i < indices.Count; i++)
        {
            var poolIndex = indices[i];
            if (poolIndex < 0 || poolIndex >= archivePool.Length)
            {
                Debug.LogError(
                    $"[GameSceneActivation] expanded archive index {poolIndex} out of range " +
                    $"(pool={archivePool.Length}) for '{sceneName}'.");
            }
            else
            {
                var archiveId = archivePool[poolIndex];
                if (!string.IsNullOrEmpty(archiveId))
                {
                    MaterializeRelativePath(
                        RuntimeContentManager.DefaultArchivePathFunc(archiveId),
                        remap,
                        assetManager);
                }
            }

            if (onEach != null)
            {
                onEach();
            }

            if ((i % __maxArchivesPerTime) == 0)
            {
                yield return null;

                if (__materializedPaths == null)
                {
                    yield break;
                }
            }
        }
    }

    /// <summary>Both inputs must be sorted ascending (ExpandSceneRoots sorts).</summary>
    static List<int> SubtractSortedIndices(List<int> fullSorted, List<int> subtractSorted)
    {
        var result = new List<int>(Math.Max(0, fullSorted.Count - subtractSorted.Count));
        int i = 0;
        int j = 0;
        while (i < fullSorted.Count)
        {
            if (j >= subtractSorted.Count)
            {
                result.Add(fullSorted[i]);
                i++;
                continue;
            }

            var a = fullSorted[i];
            var b = subtractSorted[j];
            if (a == b)
            {
                i++;
                j++;
            }
            else if (a < b)
            {
                result.Add(a);
                i++;
            }
            else
            {
                j++;
            }
        }

        return result;
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
            return;

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
        
        var json = Encoding.UTF8.GetString(bytes);
        
        //Debug.Log(json);

        dependencyFile = JsonUtility.FromJson<SceneArchiveDependencies.File>(json);
        return dependencyFile != null;
    }

    static void CopyAssetToFile(string sourcePath, string destPath)
    {
        var folder = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllBytes(destPath, AssetFileUtility.ReadAllBytes(sourcePath));
    }
#endif
}
