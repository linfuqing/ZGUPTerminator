using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Scenes;
using UnityEngine;
using ZG;
using EntityHash128 = Unity.Entities.Hash128;

[CreateAfter(typeof(PrefabLoaderSystem)),
 UpdateInGroup(typeof(SceneSystemGroup)),
 UpdateAfter(typeof(PrefabLoaderSystem)),
 UpdateAfter(typeof(WeakAssetReferenceLoadingSystem)),
 UpdateAfter(typeof(SceneSectionStreamingSystem))]
public partial class SceneArchiveDependencySystem : SystemBase
{
    private struct PendingRelease
    {
        public FixedString128Bytes relativePath;
        public int frame;
    }

    private enum RequestPlanStatus : byte
    {
        Ready,
        Blocked,
        Materialize,
        MissingHeader
    }

    private struct RequestPlan
    {
        public EntityPrefabReference reference;
        public EntityHash128 guid;
        public int commandStart;
        public int entitySceneCount;
        public int archiveCount;
        public RequestPlanStatus status;
    }

    [BurstCompile]
    private struct BuildPlanJob : IJob
    {
        [ReadOnly]
        public BlobAssetReference<SceneArchiveDependencies.RuntimeBlob> dependencyBlob;

        [ReadOnly]
        public NativeArray<EntityPrefabReference> requests;

        [ReadOnly]
        public NativeParallelHashSet<EntityHash128>.ReadOnly readyGuids;

        [ReadOnly]
        public NativeParallelHashSet<EntityHash128>.ReadOnly blockedGuids;

        public NativeList<RequestPlan> plans;
        public NativeList<FixedString128Bytes> commands;

        public NativeParallelHashSet<int> entityVisited;
        public NativeList<int> entityQueue;
        public NativeParallelHashSet<int> archiveVisited;
        public NativeList<int> archiveQueue;

        public void Execute()
        {
            plans.Clear();
            commands.Clear();

            ref var graph = ref dependencyBlob.Value;
            foreach (var reference in requests)
            {
                var guid = reference.AssetGUID;
                var plan = new RequestPlan
                {
                    reference = reference,
                    guid = guid,
                    commandStart = commands.Length
                };

                if (blockedGuids.Contains(guid))
                {
                    plan.status = RequestPlanStatus.Blocked;
                    plans.Add(plan);
                    continue;
                }

                if (readyGuids.Contains(guid))
                {
                    plan.status = RequestPlanStatus.Ready;
                    plans.Add(plan);
                    continue;
                }

                var headerIndex = FindHeaderIndex(ref graph, guid);
                if (headerIndex < 0)
                {
                    plan.status = RequestPlanStatus.MissingHeader;
                    plans.Add(plan);
                    continue;
                }

                entityVisited.Clear();
                entityQueue.Clear();
                archiveVisited.Clear();
                archiveQueue.Clear();

                entityVisited.Add(headerIndex);
                entityQueue.Add(headerIndex);

                var entityReadIndex = 0;
                while (entityReadIndex < entityQueue.Length)
                {
                    var entityIndex = entityQueue[entityReadIndex++];
                    commands.Add(graph.entityScenePaths[entityIndex]);
                    plan.entitySceneCount++;

                    var archiveBegin = graph.entitySceneArchiveOffsets[entityIndex];
                    var archiveEnd = graph.entitySceneArchiveOffsets[entityIndex + 1];
                    for (int edgeIndex = archiveBegin; edgeIndex < archiveEnd; edgeIndex++)
                    {
                        var archiveIndex = graph.entitySceneArchiveIndices[edgeIndex];
                        if (archiveVisited.Add(archiveIndex))
                        {
                            archiveQueue.Add(archiveIndex);
                        }
                    }

                    var entityBegin = graph.entityLocalDepOffsets[entityIndex];
                    var entityEnd = graph.entityLocalDepOffsets[entityIndex + 1];
                    for (int edgeIndex = entityBegin; edgeIndex < entityEnd; edgeIndex++)
                    {
                        var dependencyIndex = graph.entityLocalDepIndices[edgeIndex];
                        if (entityVisited.Add(dependencyIndex))
                        {
                            entityQueue.Add(dependencyIndex);
                        }
                    }
                }

                var archiveReadIndex = 0;
                while (archiveReadIndex < archiveQueue.Length)
                {
                    var archiveIndex = archiveQueue[archiveReadIndex++];
                    commands.Add(graph.archivePaths[archiveIndex]);
                    plan.archiveCount++;

                    var begin = graph.archiveDepOffsets[archiveIndex];
                    var end = graph.archiveDepOffsets[archiveIndex + 1];
                    for (int edgeIndex = begin; edgeIndex < end; edgeIndex++)
                    {
                        var dependencyIndex = graph.archiveDepIndices[edgeIndex];
                        if (archiveVisited.Add(dependencyIndex))
                        {
                            archiveQueue.Add(dependencyIndex);
                        }
                    }
                }

                plan.status = RequestPlanStatus.Materialize;
                plans.Add(plan);
            }
        }

        private static int FindHeaderIndex(
            ref SceneArchiveDependencies.RuntimeBlob graph,
            EntityHash128 guid)
        {
            var low = 0;
            var high = graph.headers.Length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var comparison = graph.headers[middle].guid.CompareTo(guid);
                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else if (comparison > 0)
                {
                    high = middle - 1;
                }
                else
                {
                    return graph.headers[middle].entitySceneIndex;
                }
            }

            return -1;
        }
    }

    private int __generation;
    private string __sceneName;
    private Func<string, bool> __materialize;
    private Func<string, bool> __dematerialize;
    private BlobAssetReference<SceneArchiveDependencies.RuntimeBlob> __dependencyBlob;

    private NativeList<EntityPrefabReference> __activeRequests;
    private NativeList<EntityPrefabReference> __waitingRequests;
    private NativeList<RequestPlan> __plans;
    private NativeList<FixedString128Bytes> __commands;

    private NativeParallelHashSet<EntityHash128> __readyGuids;
    private NativeParallelHashSet<EntityHash128> __blockedGuids;
    private NativeParallelMultiHashMap<EntityHash128, FixedString128Bytes> __pathsByGuid;
    private NativeParallelHashMap<FixedString128Bytes, int> __pathReferenceCounts;
    private NativeList<PendingRelease> __pendingReleases;
    private NativeParallelHashSet<int> __entityVisited;
    private NativeList<int> __entityQueue;
    private NativeParallelHashSet<int> __archiveVisited;
    private NativeList<int> __archiveQueue;

    private bool __hasScheduledPlan;
    private bool __isDraining;
    private bool __isReadyForRelease;
    private bool __isReleaseRequested;
    private bool __isReleaseComplete;
    private int __releaseRequestFrame;
    private uint __prefabReleaseVersion;

    private EntityQuery __sceneQuery;
    private EntityQuery __pendingPrefabLoadQuery;
    private EntityQuery __prefabLoaderReferencesQuery;

    public bool isReadyForRelease =>
        !__dependencyBlob.IsCreated || (__isDraining && __isReadyForRelease);

    public bool isReleaseComplete =>
        !__dependencyBlob.IsCreated || (__isReleaseRequested && __isReleaseComplete);

    /// <summary>
    /// Stops forwarding new prefab requests and waits for every already-started
    /// Unity.Scenes header/section load to reach a stable state.
    /// </summary>
    public void BeginShutdown()
    {
        // Close the loading entrance immediately, but keep existing requester
        // entities alive until all observable Unity.Scenes IO is stable.
        PrefabLoaderSettings.PauseLoading();

        if (!__dependencyBlob.IsCreated)
        {
            __isDraining = true;
            __isReadyForRelease = true;
            __isReleaseRequested = false;
            __isReleaseComplete = false;
            return;
        }

        __isDraining = true;
        __isReadyForRelease = false;
        __isReleaseRequested = false;
        __isReleaseComplete = false;

        CancelPendingPlans();
        ClearExternalInProgressRequests();
    }

    /// <summary>
    /// Triggers the one-shot prefab release after the drain fence is ready.
    /// Completion is reported only after PrefabLoader/WeakAsset/section streaming
    /// have received another SceneSystemGroup update.
    /// </summary>
    public void NotifyReleaseAll()
    {
        if (!__isDraining)
        {
            BeginShutdown();
        }

        if (!__isReadyForRelease)
        {
            Debug.LogError(
                "[SceneArchiveDependencySystem] Release requested before the " +
                "Unity.Scenes drain fence became ready.");
            return;
        }

        var referencesEntity = __prefabLoaderReferencesQuery.GetSingletonEntity();
        var references =
            EntityManager.GetComponentData<PrefabLoaderReferences>(referencesEntity);
        __prefabReleaseVersion = references.releaseVersion;

        PrefabLoaderSettings.ReleaseAllRightNow();

        __isReleaseRequested = true;
        __isReleaseComplete = false;
        __releaseRequestFrame = UnityEngine.Time.frameCount;
    }

    /// <summary>
    /// Hands an activation-owned Blob graph and materializer to this system.
    /// The returned generation prevents an old activation from clearing newer state.
    /// </summary>
    public int Initialize(
        string sceneName,
        BlobAssetReference<SceneArchiveDependencies.RuntimeBlob> dependencyBlob,
        Func<string, bool> materialize,
        Func<string, bool> dematerialize)
    {
        if (!dependencyBlob.IsCreated ||
            dependencyBlob.Value.headers.Length == 0 ||
            materialize == null ||
            dematerialize == null)
        {
            Debug.LogError(
                $"[SceneArchiveDependencySystem] Invalid dependency graph for '{sceneName}'.");
            return 0;
        }

        ClearActivationState();

        ref var graph = ref dependencyBlob.Value;
        EnsureCapacity(ref __entityQueue, graph.entityScenePaths.Length);
        EnsureCapacity(ref __archiveQueue, graph.archivePaths.Length);
        EnsureCapacity(ref __plans, 1);
        EnsureCapacity(ref __commands, 16);
        EnsureCapacity(ref __readyGuids, graph.headers.Length);
        EnsureCapacity(ref __blockedGuids, graph.headers.Length);
        EnsureCapacity(ref __pathsByGuid, 16);
        EnsureCapacity(ref __pathReferenceCounts, 16);
        EnsureCapacity(ref __pendingReleases, 16);
        EnsureCapacity(ref __entityVisited, graph.entityScenePaths.Length);
        EnsureCapacity(ref __archiveVisited, graph.archivePaths.Length);

        unchecked
        {
            __generation++;
            if (__generation == 0)
            {
                __generation++;
            }
        }

        __sceneName = sceneName;
        __dependencyBlob = dependencyBlob;
        __materialize = materialize;
        __dematerialize = dematerialize;

        // ReleaseAllRightNow keeps prefab loading closed across activation disposal.
        // Reopen only after the next activation has a valid dependency graph and
        // its critical files have already been materialized.
        PrefabLoaderSettings.ResumeLoading();

        return __generation;
    }

    public void Uninitialize(int generation)
    {
        if (generation == 0 || generation != __generation)
        {
            return;
        }

        // The activation owns the Blob and disposes it immediately after this call.
        // Complete first so no scheduled job can retain a pointer into that Blob.
        ClearActivationState();
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __activeRequests = new NativeList<EntityPrefabReference>(1, Allocator.Persistent);
        __waitingRequests = new NativeList<EntityPrefabReference>(1, Allocator.Persistent);
        __plans = new NativeList<RequestPlan>(1, Allocator.Persistent);
        __commands = new NativeList<FixedString128Bytes>(16, Allocator.Persistent);
        __readyGuids = new NativeParallelHashSet<EntityHash128>(1, Allocator.Persistent);
        __blockedGuids = new NativeParallelHashSet<EntityHash128>(1, Allocator.Persistent);
        __pathsByGuid =
            new NativeParallelMultiHashMap<EntityHash128, FixedString128Bytes>(1, Allocator.Persistent);
        __pathReferenceCounts =
            new NativeParallelHashMap<FixedString128Bytes, int>(1, Allocator.Persistent);
        __pendingReleases = new NativeList<PendingRelease>(1, Allocator.Persistent);
        __entityVisited = new NativeParallelHashSet<int>(1, Allocator.Persistent);
        __entityQueue = new NativeList<int>(1, Allocator.Persistent);
        __archiveVisited = new NativeParallelHashSet<int>(1, Allocator.Persistent);
        __archiveQueue = new NativeList<int>(1, Allocator.Persistent);

        __sceneQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<SceneReference>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build(this);
        __pendingPrefabLoadQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<RequestEntityPrefabLoaded>()
            .WithNone<PrefabLoadResult>()
            // CompleteLoad also adds RequestEntityPrefabLoaded to the Prefab root.
            // Keep the default Prefab exclusion so only unresolved request entities
            // participate in the shutdown fence.
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build(this);
        __prefabLoaderReferencesQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<PrefabLoaderReferences>()
            .Build(this);

        RequireForUpdate<PrefabLoaderReferences>();
    }

    protected override void OnDestroy()
    {
        ClearActivationState();

        Dispose(ref __activeRequests);
        Dispose(ref __waitingRequests);
        Dispose(ref __plans);
        Dispose(ref __commands);
        Dispose(ref __readyGuids);
        Dispose(ref __blockedGuids);
        Dispose(ref __pathsByGuid);
        Dispose(ref __pathReferenceCounts);
        Dispose(ref __pendingReleases);
        Dispose(ref __entityVisited);
        Dispose(ref __entityQueue);
        Dispose(ref __archiveVisited);
        Dispose(ref __archiveQueue);

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!__dependencyBlob.IsCreated)
        {
            return;
        }

        var references = SystemAPI.GetSingletonRW<PrefabLoaderReferences>().ValueRW;

        ReleaseUnloaded(ref references.unloaded);

        if (__isDraining)
        {
            // PrefabLoaderSystem runs before this system. References produced by it
            // during shutdown must not survive into the next frame and start a new
            // WeakAsset header load.
            references.inProgress.Clear();
            CancelPendingPlans();

            __isReadyForRelease = !HasUnityScenesLoadInProgress();
            if (__isReleaseRequested)
            {
                __isReleaseComplete =
                    UnityEngine.Time.frameCount > __releaseRequestFrame &&
                    __isReadyForRelease &&
                    references.releaseVersion != __prefabReleaseVersion &&
                    references.isReleaseComplete;
            }

            // Keep all materialized files alive until GameSceneActivation.Dispose().
            // In particular, Unity.Scenes can retain an internal header cleanup job
            // after its scene entity has already disappeared.
            return;
        }

        ProcessPendingReleases();

        // Hold every new load request outside PrefabLoaderSystem until both planning
        // and managed materialization have completed.
        if (__hasScheduledPlan)
        {
            CaptureRequests(ref references.inProgress, ref __waitingRequests);
        }
        else
        {
            CaptureRequests(ref references.inProgress, ref __activeRequests);
        }

        references.inProgress.Clear();

        if (__hasScheduledPlan)
        {
            // SystemBase completes the previous update's Dependency before OnUpdate.
            __hasScheduledPlan = false;
            ApplyCompletedPlans(ref references);

            __activeRequests.Clear();
            __plans.Clear();
            __commands.Clear();

            if (!__waitingRequests.IsEmpty)
            {
                SwapRequestBuffers();
                SchedulePlan();
            }

            return;
        }

        if (!__activeRequests.IsEmpty)
        {
            SchedulePlan();
        }
    }

    private void SchedulePlan()
    {
        __plans.Clear();
        __commands.Clear();

        EnsureCapacity(ref __plans, __activeRequests.Length);
        EnsureCapacity(ref __commands, Math.Max(16, __activeRequests.Length * 4));

        var job = new BuildPlanJob
        {
            dependencyBlob = __dependencyBlob,
            requests = __activeRequests.AsArray(),
            readyGuids = __readyGuids.AsReadOnly(),
            blockedGuids = __blockedGuids.AsReadOnly(),
            plans = __plans,
            commands = __commands,
            entityVisited = __entityVisited,
            entityQueue = __entityQueue,
            archiveVisited = __archiveVisited,
            archiveQueue = __archiveQueue
        };

        // This system runs after PrefabLoaderSystem, giving the planning job almost a
        // full frame before its Dependency is consumed on the next update.
        Dependency = job.Schedule(Dependency);
        __hasScheduledPlan = true;
    }

    private void ApplyCompletedPlans(ref PrefabLoaderReferences references)
    {
        foreach (var plan in __plans)
        {
            switch (plan.status)
            {
                case RequestPlanStatus.Blocked:
                    continue;
                case RequestPlanStatus.Ready:
                    AddReadyReference(ref references, plan.reference);
                    continue;
                case RequestPlanStatus.MissingHeader:
                    Block(
                        plan.guid,
                        $"No entity header mapped for requested prefab GUID {plan.guid}.");
                    continue;
            }

            var commandCount = plan.entitySceneCount + plan.archiveCount;
            var isReady = true;
            FixedString128Bytes failedPath = default;
            for (int commandOffset = 0; commandOffset < commandCount; commandOffset++)
            {
                var relativePath = __commands[plan.commandStart + commandOffset];
                if (relativePath.IsEmpty || !__materialize(relativePath.ToString()))
                {
                    failedPath = relativePath;
                    isReady = false;
                    break;
                }
            }

            if (!isReady)
            {
                Block(
                    plan.guid,
                    $"Failed to materialize '{failedPath}' for prefab {plan.guid}.");
                continue;
            }

            __readyGuids.Add(plan.guid);
            RegisterMaterializedPaths(plan);
            AddReadyReference(ref references, plan.reference);

            Debug.Log(
                $"[SceneArchiveDependencySystem] Materialized prefab {plan.guid} on demand " +
                $"for '{__sceneName}' (entityScenes={plan.entitySceneCount}, " +
                $"archives={plan.archiveCount}).");
        }
    }

    private void RegisterMaterializedPaths(in RequestPlan plan)
    {
        var commandCount = plan.entitySceneCount + plan.archiveCount;
        EnsureCapacity(ref __pathsByGuid, __pathsByGuid.Count() + commandCount);
        EnsureCapacity(
            ref __pathReferenceCounts,
            __pathReferenceCounts.Count() + commandCount);

        for (int commandOffset = 0; commandOffset < commandCount; commandOffset++)
        {
            var relativePath = __commands[plan.commandStart + commandOffset];
            RemovePendingRelease(relativePath);

            if (__pathReferenceCounts.TryGetValue(relativePath, out var count))
            {
                __pathReferenceCounts[relativePath] = count + 1;
            }
            else
            {
                __pathReferenceCounts.Add(relativePath, 1);
            }

            __pathsByGuid.Add(plan.guid, relativePath);
        }
    }

    private void ReleaseUnloaded(ref NativeList<EntityPrefabReference> unloaded)
    {
        foreach (var reference in unloaded)
        {
            var guid = reference.AssetGUID;
            if (!__readyGuids.Remove(guid))
            {
                continue;
            }

            if (__pathsByGuid.TryGetFirstValue(guid, out var relativePath, out var iterator))
            {
                do
                {
                    if (!__pathReferenceCounts.TryGetValue(relativePath, out var count))
                    {
                        continue;
                    }

                    if (count > 1)
                    {
                        __pathReferenceCounts[relativePath] = count - 1;
                    }
                    else
                    {
                        __pathReferenceCounts.Remove(relativePath);
                        QueueRelease(relativePath);
                    }
                }
                while (__pathsByGuid.TryGetNextValue(out relativePath, ref iterator));
            }

            __pathsByGuid.Remove(guid);
        }

        // PrefabLoaderSystem publishes one-frame safe-unload events. Consume each
        // event exactly once instead of retaining it until the loader has new work.
        unloaded.Clear();
    }

    private void QueueRelease(FixedString128Bytes relativePath)
    {
        var releaseFrame = UnityEngine.Time.frameCount;
        for (int i = 0; i < __pendingReleases.Length; i++)
        {
            var pending = __pendingReleases[i];
            if (pending.relativePath != relativePath)
            {
                continue;
            }

            pending.frame = releaseFrame;
            __pendingReleases[i] = pending;
            return;
        }

        __pendingReleases.Add(new PendingRelease
        {
            relativePath = relativePath,
            frame = releaseFrame
        });
    }

    private void RemovePendingRelease(FixedString128Bytes relativePath)
    {
        for (int i = __pendingReleases.Length - 1; i >= 0; i--)
        {
            if (__pendingReleases[i].relativePath == relativePath)
            {
                __pendingReleases.RemoveAtSwapBack(i);
            }
        }
    }

    private void ProcessPendingReleases()
    {
        var frame = UnityEngine.Time.frameCount;
        for (int i = __pendingReleases.Length - 1; i >= 0; i--)
        {
            var pending = __pendingReleases[i];
            if (frame < pending.frame)
            {
                continue;
            }

            if (__pathReferenceCounts.ContainsKey(pending.relativePath) ||
                __dematerialize(pending.relativePath.ToString()))
            {
                __pendingReleases.RemoveAtSwapBack(i);
            }
            else
            {
                // The archive can still be unmounting asynchronously. Retry next frame.
                pending.frame = frame + 1;
                __pendingReleases[i] = pending;
            }
        }
    }

    private void Block(EntityHash128 guid, string message)
    {
        if (__blockedGuids.Add(guid))
        {
            Debug.LogError(
                $"[SceneArchiveDependencySystem] {message} " +
                $"The request is blocked for scene '{__sceneName}'.");
        }
    }

    private void ClearActivationState()
    {
        if (__hasScheduledPlan)
        {
            CompleteDependency();
            __hasScheduledPlan = false;
        }

        __sceneName = null;
        __materialize = null;
        __dematerialize = null;
        __dependencyBlob = default;
        __isDraining = false;
        __isReadyForRelease = false;
        __isReleaseRequested = false;
        __isReleaseComplete = false;
        __releaseRequestFrame = 0;
        __prefabReleaseVersion = 0;

        if (__activeRequests.IsCreated)
        {
            __activeRequests.Clear();
            __waitingRequests.Clear();
            __plans.Clear();
            __commands.Clear();
            __readyGuids.Clear();
            __blockedGuids.Clear();
            __pathsByGuid.Clear();
            __pathReferenceCounts.Clear();
            __pendingReleases.Clear();
            __entityVisited.Clear();
            __entityQueue.Clear();
            __archiveVisited.Clear();
            __archiveQueue.Clear();
        }
    }

    private void CancelPendingPlans()
    {
        if (__hasScheduledPlan)
        {
            CompleteDependency();
            __hasScheduledPlan = false;
        }

        if (__activeRequests.IsCreated)
        {
            __activeRequests.Clear();
            __waitingRequests.Clear();
            __plans.Clear();
            __commands.Clear();
        }
    }

    private void ClearExternalInProgressRequests()
    {
        if (__prefabLoaderReferencesQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        var entity = __prefabLoaderReferencesQuery.GetSingletonEntity();
        var references = EntityManager.GetComponentData<PrefabLoaderReferences>(entity);
        references.inProgress.Clear();
    }

    private bool HasUnityScenesLoadInProgress()
    {
        if (__hasScheduledPlan ||
            !__activeRequests.IsEmpty ||
            !__waitingRequests.IsEmpty ||
            !__pendingPrefabLoadQuery.IsEmptyIgnoreFilter)
        {
            return true;
        }

        using var sceneEntities = __sceneQuery.ToEntityArray(Allocator.Temp);
        foreach (var sceneEntity in sceneEntities)
        {
            if (!EntityManager.Exists(sceneEntity))
            {
                continue;
            }

            var hasRequest = EntityManager.HasComponent<RequestSceneLoaded>(sceneEntity);
            var hasResolvedSections =
                EntityManager.HasComponent<ResolvedSectionEntity>(sceneEntity);

            // This is the public representation of an in-flight entity header.
            if (hasRequest && !hasResolvedSections)
            {
                return true;
            }

            if (!hasResolvedSections)
            {
                continue;
            }

            var sections = EntityManager.GetBuffer<ResolvedSectionEntity>(
                sceneEntity,
                true);
            for (int sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
            {
                var sectionEntity = sections[sectionIndex].SectionEntity;
                if (!EntityManager.Exists(sectionEntity))
                {
                    continue;
                }

                switch (SceneSystem.GetSectionStreamingState(
                            World.Unmanaged,
                            sectionEntity))
                {
                    case SceneSystem.SectionStreamingState.LoadRequested:
                    case SceneSystem.SectionStreamingState.Loading:
                    case SceneSystem.SectionStreamingState.UnloadRequested:
                        return true;
                }
            }
        }

        return false;
    }

    private void SwapRequestBuffers()
    {
        /*var temporary = __activeRequests;
        __activeRequests = __waitingRequests;
        __waitingRequests = temporary;*/
        (__activeRequests, __waitingRequests) = (__waitingRequests, __activeRequests);
        __waitingRequests.Clear();
    }

    private static void CaptureRequests(
        ref NativeList<EntityPrefabReference> source,
        ref NativeList<EntityPrefabReference> destination)
    {
        EnsureCapacity(ref destination, destination.Length + source.Length);
        foreach (var reference in source)
        {
            if (destination.IndexOf(reference) == -1)
                destination.Add(reference);
        }
    }

    private static void AddReadyReference(
        ref PrefabLoaderReferences references,
        EntityPrefabReference reference)
    {
        if (references.inProgress.IndexOf(reference) == -1)
        {
            references.inProgress.Add(reference);
        }
    }

    private static void EnsureCapacity<T>(ref NativeList<T> list, int capacity)
        where T : unmanaged
    {
        if (list.Capacity < capacity)
        {
            list.Capacity = capacity;
        }
    }

    private static void EnsureCapacity<T>(ref NativeParallelHashSet<T> set, int capacity)
        where T : unmanaged, IEquatable<T>
    {
        capacity = Math.Max(1, capacity);
        if (set.Capacity < capacity)
        {
            set.Capacity = capacity;
        }
    }

    private static void EnsureCapacity<TKey, TValue>(
        ref NativeParallelMultiHashMap<TKey, TValue> map,
        int capacity)
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        capacity = Math.Max(1, capacity);
        if (map.Capacity < capacity)
        {
            map.Capacity = capacity;
        }
    }

    private static void EnsureCapacity<TKey, TValue>(
        ref NativeParallelHashMap<TKey, TValue> map,
        int capacity)
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        capacity = Math.Max(1, capacity);
        if (map.Capacity < capacity)
        {
            map.Capacity = capacity;
        }
    }

    private static void Dispose<T>(ref NativeList<T> list)
        where T : unmanaged
    {
        if (list.IsCreated)
        {
            list.Dispose();
        }
    }

    private static void Dispose<T>(ref NativeParallelHashSet<T> set)
        where T : unmanaged, IEquatable<T>
    {
        if (set.IsCreated)
        {
            set.Dispose();
        }
    }

    private static void Dispose<TKey, TValue>(
        ref NativeParallelMultiHashMap<TKey, TValue> map)
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        if (map.IsCreated)
        {
            map.Dispose();
        }
    }

    private static void Dispose<TKey, TValue>(
        ref NativeParallelHashMap<TKey, TValue> map)
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        if (map.IsCreated)
        {
            map.Dispose();
        }
    }
}
