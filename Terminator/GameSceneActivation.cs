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

#if ENABLE_CONTENT_DELIVERY
    private int __dependencySystemGeneration;
    private SceneArchiveDependencySystem __dependencySystem;
    private BlobAssetReference<SceneArchiveDependencies.RuntimeBlob> __dependencyBlob;
    // Single bookkeeper for every materialized file of this activation: critical
    // set (held as references for the activation lifetime) and on-demand prefab
    // dependencies (refcounted by SceneArchiveDependencySystem) share it.
    private SceneArchiveContentProvisioner __provisioner;
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

#if ENABLE_CONTENT_DELIVERY
        __provisioner = null;


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

        // Deletes every tracked file (criticals, ready prefab dependencies and any
        // deferred-release backlog) without a budget. Callers guarantee the drain
        // fence (BeginShutdown/NotifyReleaseAll) completed before Dispose.
        if (__provisioner != null)
        {
            __provisioner.Flush();
            __provisioner.Dispose();
            __provisioner = null;
        }
#endif
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

        var remap = ContentDeliveryGlobalState.PathRemapFuncWithFileCheck;
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

        __provisioner = new SceneArchiveContentProvisioner(
            relativePath => MaterializeRelativePath(relativePath, remap, assetManager),
            relativePath => DematerializeRelativePath(relativePath, remap, assetManager));

        var criticalSteps = Math.Max(1, criticalEntityScenes.Count + criticalArchives.Count);
        var criticalDone = 0;

        yield return MaterializeEntitySceneIndices(
            sceneName,
            criticalEntityScenes,
            entityScenePool,
            () =>
            {
                criticalDone++;
                initializedProgress = criticalDone * 0.95f / criticalSteps;
            });

        if (__provisioner == null)
        {
            yield break;
        }

        yield return MaterializeArchiveIndices(
            sceneName,
            criticalArchives,
            archivePool,
            () =>
            {
                criticalDone++;
                initializedProgress = criticalDone * 0.95f / criticalSteps;
            });

        if (__provisioner == null)
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
                __provisioner);
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
            $"[GameSceneActivation] Materialized {__provisioner.trackedCount} critical files for '{sceneName}' " +
            $"(roots archives={archiveRootCount}, entityScenes={entityRootCount}; " +
            $"critical archives={criticalArchives.Count}, entityScenes={criticalEntityScenes.Count}); " +
            "remaining prefab dependencies will be materialized on demand.");
    }

    IEnumerator MaterializeEntitySceneIndices(
        string sceneName,
        List<int> indices,
        string[] entityScenePool,
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
                    // The activation keeps this reference until Dispose, so critical
                    // files can never be deleted (or re-copied) by on-demand plans.
                    __provisioner.Acquire(relativePath);
                }
            }

            if (onEach != null)
            {
                onEach();
            }

            if ((i % __maxEntityScenesPerTime) == 0)
            {
                yield return null;

                if (__provisioner == null)
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
                    // The activation keeps this reference until Dispose, so critical
                    // files can never be deleted (or re-copied) by on-demand plans.
                    __provisioner.Acquire(
                        RuntimeContentManager.DefaultArchivePathFunc(archiveId));
                }
            }

            if (onEach != null)
            {
                onEach();
            }

            if ((i % __maxArchivesPerTime) == 0)
            {
                yield return null;

                if (__provisioner == null)
                {
                    yield break;
                }
            }
        }
    }

    // Raw copy primitive for the provisioner: resolve + replace-write, no tracking.
    // Exactly-once semantics are guaranteed by SceneArchiveContentProvisioner;
    // AssetFileUtility.Materialize is a destructive refresh (see IAssetFileManager).
    bool MaterializeRelativePath(string relativePath, Func<string, bool, string> remap, AssetManager assetManager)
    {
        var destPath = remap(relativePath, false);
        if (string.IsNullOrEmpty(destPath))
        {
            Debug.LogError($"[GameSceneActivation] Remap failed for {relativePath}");
            return false;
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

        // In-place platform: the download store already serves this file at the
        // remapped location (source == dest); zero IO needed.
        if (PathsEqual(sourcePath, destPath))
        {
            return true;
        }

        try
        {
            CopyAssetToFile(sourcePath, destPath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[GameSceneActivation] Failed to materialize {sourcePath} -> {destPath}");
            return false;
        }
    }

    // Raw delete primitive for the provisioner. Returns false to request a retry
    // (e.g. the archive is still unmounting asynchronously).
    bool DematerializeRelativePath(string relativePath, Func<string, bool, string> remap, AssetManager assetManager)
    {
        var destPath = remap(relativePath, false);
        if (string.IsNullOrEmpty(destPath))
        {
            Debug.LogError($"[GameSceneActivation] Remap failed while releasing {relativePath}");
            return false;
        }

        // In-place platform guard: when the resolved source IS the destination
        // (download store shares the remap root), the file is canonical downloaded
        // content, not a tmp copy — never delete it.
        if (assetManager != null &&
            TryResolveAssetSource(relativePath, assetManager, out var sourcePath, out _) &&
            PathsEqual(sourcePath, destPath))
        {
            return true;
        }

        try
        {
            AssetFileUtility.Dematerialize(destPath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[GameSceneActivation] Failed to release {destPath}: {e.Message}");
            return false;
        }
    }

    static bool PathsEqual(string a, string b)
    {
        return !string.IsNullOrEmpty(a) &&
               !string.IsNullOrEmpty(b) &&
               string.Equals(
                   a.Replace('\\', '/'),
                   b.Replace('\\', '/'),
                   StringComparison.OrdinalIgnoreCase);
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
            Directory.CreateDirectory(folder);

        AssetFileUtility.Materialize(sourcePath, destPath);
        //File.WriteAllBytes(destPath, AssetFileUtility.ReadAllBytes(sourcePath));
    }
#endif
}
