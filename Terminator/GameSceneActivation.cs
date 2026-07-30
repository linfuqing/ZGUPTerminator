using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;
using ZG;

public class GameSceneActivation : IGameSceneActivation
{
    private int __maxEntityScenesPerTime;
    private int __maxArchivesPerTime;
    
    private List<string> __materializedPaths;

#if ENABLE_CONTENT_DELIVERY
    private int __dependencySystemGeneration;
    private SceneArchiveDependencySystem __dependencySystem;
    private BlobAssetReference<SceneArchiveDependencies.RuntimeBlob> __dependencyBlob;
    private HashSet<string> __criticalRelativePaths;
#endif

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
        isInitialized = false;
        initializedProgress = 0f;
        __materializedPaths = null;
#if ENABLE_CONTENT_DELIVERY
        __criticalRelativePaths = null;
#endif
 
#if ENABLE_CONTENT_DELIVERY
        while (ContentDeliveryGlobalState.CurrentContentUpdateState <
               ContentDeliveryGlobalState.ContentUpdateState.ContentReady)
            yield return null;

        while (AssetFileUtility.isPending)
            yield return null;
        
        yield return MaterializeForScene(AssetFileUtility.GetFileNameWithoutExtension(sceneName));
#else
        PrefabLoaderSettings.ResumeLoading();
        initializedProgress = 1f;
        isInitialized = true;
        yield break;
#endif
    }

    public void Dispose()
    {
#if ENABLE_CONTENT_DELIVERY
        if (__dependencySystem != null)
        {
            __dependencySystem.Uninitialize(__dependencySystemGeneration);
            __dependencySystem = null;
            __dependencySystemGeneration = 0;
        }

        if (__dependencyBlob.IsCreated)
        {
            __dependencyBlob.Dispose();
            __dependencyBlob = default;
        }

        Dematerialize();
        __criticalRelativePaths = null;
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
        // Remaining prefab dependencies are materialized by SceneArchiveDependencySystem on demand.
        var criticalArchives = new List<int>(32);
        var criticalEntityScenes = new List<int>(32);
        SceneArchiveDependencies.ExpandSceneRootsLocal(
            dependencyFile, entry, criticalArchives, criticalEntityScenes);

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

        __materializedPaths = new List<string>(criticalArchives.Count + criticalEntityScenes.Count);
        __criticalRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        var world = World.DefaultGameObjectInjectionWorld;
        var dependencySystem = world == null
            ? null
            : world.GetExistingSystemManaged<SceneArchiveDependencySystem>();
        if (dependencySystem == null)
        {
            Debug.LogError(
                "[GameSceneActivation] SceneArchiveDependencySystem is unavailable; " +
                "cannot enable on-demand prefab dependencies.");
            yield break;
        }

        BlobAssetReference<SceneArchiveDependencies.RuntimeBlob> dependencyBlob;
        try
        {
            dependencyBlob =
                SceneArchiveDependencies.CreateRuntimeBlob(dependencyFile, Allocator.Persistent);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError(
                $"[GameSceneActivation] Failed to build the runtime dependency graph for '{sceneName}'.");
            yield break;
        }

        int generation;
        try
        {
            generation = dependencySystem.Initialize(
                sceneName,
                dependencyBlob,
                relativePath => MaterializeRelativePath(relativePath, remap, assetManager),
                relativePath => DematerializeRelativePath(relativePath, remap));
        }
        catch
        {
            dependencyBlob.Dispose();
            throw;
        }

        if (generation == 0)
        {
            dependencyBlob.Dispose();
            Debug.LogError(
                $"[GameSceneActivation] Failed to initialize on-demand dependencies for '{sceneName}'.");
            yield break;
        }

        __dependencyBlob = dependencyBlob;
        __dependencySystem = dependencySystem;
        __dependencySystemGeneration = generation;
        initializedProgress = 1f;
        isInitialized = true;

        Debug.Log(
            $"[GameSceneActivation] Materialized {__materializedPaths.Count} critical files for '{sceneName}' " +
            $"(roots archives={archiveRootCount}, entityScenes={entityRootCount}; " +
            $"critical archives={criticalArchives.Count}, entityScenes={criticalEntityScenes.Count}); " +
            "remaining prefab dependencies will be materialized on demand.");
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
                    relativePath = relativePath.Replace('\\', '/');
                    __criticalRelativePaths.Add(relativePath);
                    MaterializeRelativePath(relativePath, remap, assetManager);
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
                    var relativePath = RuntimeContentManager.DefaultArchivePathFunc(archiveId);
                    __criticalRelativePaths.Add(relativePath);
                    MaterializeRelativePath(relativePath, remap, assetManager);
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

    bool MaterializeRelativePath(string relativePath, Func<string, string> remap, AssetManager assetManager)
    {
        var destPath = remap(relativePath);
        if (string.IsNullOrEmpty(destPath))
        {
            Debug.LogError($"[GameSceneActivation] Remap failed for {relativePath}");
            return false;
        }

        // Only track files we create so Dispose won't delete files still needed by another scene.
        if (File.Exists(destPath))
        {
            return true;
        }

        if (!TryResolveAssetSource(relativePath, assetManager, out var sourcePath, out var fileOffset))
        {
            Debug.LogError($"[GameSceneActivation] GetAssetPath failed for {relativePath}");
            return false;
        }

        if (fileOffset != 0)
        {
            Debug.LogError($"[GameSceneActivation] Non-zero fileOffset ({fileOffset}) for {relativePath}; cannot materialize.");
            return false;
        }

        try
        {
            CopyAssetToFile(sourcePath, destPath);
            
            __materializedPaths.Add(destPath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[GameSceneActivation] Failed to materialize {sourcePath} -> {destPath}");
            return false;
        }
    }

    bool DematerializeRelativePath(string relativePath, Func<string, string> remap)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return true;
        }

        relativePath = relativePath.Replace('\\', '/');
        if (__criticalRelativePaths != null &&
            __criticalRelativePaths.Contains(relativePath))
        {
            return true;
        }

        var destPath = remap(relativePath);
        if (string.IsNullOrEmpty(destPath))
        {
            Debug.LogError($"[GameSceneActivation] Remap failed while releasing {relativePath}");
            return false;
        }

        var materializedIndex = -1;
        if (__materializedPaths != null)
        {
            for (int i = 0; i < __materializedPaths.Count; i++)
            {
                if (string.Equals(
                        __materializedPaths[i],
                        destPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    materializedIndex = i;
                    break;
                }
            }
        }

        // Do not delete a file this activation did not create.
        if (materializedIndex < 0)
        {
            return true;
        }

        try
        {
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            __materializedPaths.RemoveAt(materializedIndex);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[GameSceneActivation] Failed to release {destPath}: {e.Message}");
            return false;
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

        var candidate = relativePath.ToLowerInvariant();
        if (assetManager.GetAssetPath(candidate, out _, out fileOffset, out sourcePath) &&
            !string.IsNullOrEmpty(sourcePath))
        {
            return true;
        }

        candidate = (relativePath + ".bytes").ToLowerInvariant();
        if (assetManager.GetAssetPath(candidate, out _, out fileOffset, out sourcePath) &&
            !string.IsNullOrEmpty(sourcePath))
        {
            return true;
        }

        sourcePath = null;
        fileOffset = 0;

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
