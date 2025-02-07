using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Scenes;

public struct PrefabLoader
{
    public struct ParallelWriter
    {
        [ReadOnly]
        private ComponentLookup<PrefabLoadResult> __prefabLoadResults;

        [ReadOnly]
        private WeakAssetReferenceLoadingData __weakAssetReferenceLoadingData;

        private NativeQueue<EntityPrefabReference>.ParallelWriter __entityPrefabReferences;

        internal ParallelWriter(ref PrefabLoader value)
        {
            __prefabLoadResults = value.__prefabLoadResults;
            __weakAssetReferenceLoadingData = value.__weakAssetReferenceLoadingData;
            __entityPrefabReferences = value.__group.GetSingleton<PrefabLoaderSingleton>().entityPrefabReferences.AsParallelWriter();
        }

        public bool GetOrLoadPrefabRoot(in EntityPrefabReference entityPrefabReference, out Entity entity)
        {
            entity = Entity.Null;
            if (__weakAssetReferenceLoadingData.LoadedPrefabs.TryGetValue(entityPrefabReference, out var loadedPrefab))
                entity = loadedPrefab.PrefabRoot;
            
            if (!__weakAssetReferenceLoadingData.InProgressLoads.ContainsKey(entityPrefabReference))
                __entityPrefabReferences.Enqueue(entityPrefabReference);

            return entity != Entity.Null;
        }
    }
    
    [ReadOnly]
    private ComponentLookup<PrefabLoadResult> __prefabLoadResults;

    [ReadOnly]
    private WeakAssetReferenceLoadingData __weakAssetReferenceLoadingData;
    
    private EntityQuery __group;

    public PrefabLoader(ref SystemState systemState)
    {
        __prefabLoadResults = systemState.GetComponentLookup<PrefabLoadResult>(true);

        var world = systemState.WorldUnmanaged;
        var entityManager = world.EntityManager;
        var systemHandle = world.GetExistingUnmanagedSystem<WeakAssetReferenceLoadingSystem>();
        __weakAssetReferenceLoadingData =
            entityManager.GetComponentData<WeakAssetReferenceLoadingData>(systemHandle);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<PrefabLoaderSingleton>()
                .Build(ref systemState);
    }

    public void Update(ref SystemState systemState)
    {
        __prefabLoadResults.Update(ref systemState);
    }

    public void AddDependency(in JobHandle jobHandle)
    {
        __group.AddDependency(jobHandle);
    }

    public ParallelWriter AsParallelWriter()
    {
        return new ParallelWriter(ref this);
    }
}

internal struct PrefabLoaderSingleton : IComponentData
{
    public NativeQueue<EntityPrefabReference> entityPrefabReferences;
}

[BurstCompile, 
 CreateAfter(typeof(WeakAssetReferenceLoadingSystem)), 
 UpdateInGroup(typeof(SceneSystemGroup)), 
 UpdateBefore(typeof(WeakAssetReferenceLoadingSystem))]
public partial struct PrefabLoaderSystem : ISystem
{
    [BurstCompile]
    private struct Apply : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<EntityPrefabReference> entityPrefabReferences;
        
        public ComponentLookup<RequestEntityPrefabLoaded> requestEntityPrefabLoadeds;

        public void Execute(int index)
        {
            RequestEntityPrefabLoaded requestEntityPrefabLoaded;
            requestEntityPrefabLoaded.Prefab = entityPrefabReferences[index];
            requestEntityPrefabLoadeds[entityArray[index]] = requestEntityPrefabLoaded;
        }
    }
    
    private ComponentLookup<RequestEntityPrefabLoaded> __requestEntityPrefabLoadeds;

    private EntityArchetype __entityArchetype;
    private NativeQueue<EntityPrefabReference> __entityPrefabReferences;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __requestEntityPrefabLoadeds = state.GetComponentLookup<RequestEntityPrefabLoaded>();
        
        var entityManager = state.EntityManager;
        using (var componentTypes = new NativeList<ComponentType>(Allocator.Temp)
               {
                   ComponentType.ReadWrite<RequestEntityPrefabLoaded>()
               })
            __entityArchetype = entityManager.CreateArchetype(componentTypes);
        
        __entityPrefabReferences = new NativeQueue<EntityPrefabReference>(Allocator.Persistent);

        PrefabLoaderSingleton singleton;
        singleton.entityPrefabReferences = __entityPrefabReferences;
        entityManager.CreateSingleton(singleton);
        
        state.RequireForUpdate<PrefabLoaderSingleton>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __entityPrefabReferences.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();

        int count = __entityPrefabReferences.Count;
        var worldUpdateAllocator = state.WorldUpdateAllocator;
        var entityPrefabReferences = new NativeList<EntityPrefabReference>(count, worldUpdateAllocator);

        while (__entityPrefabReferences.TryDequeue(out var entityPrefabReference))
        {
            if (entityPrefabReferences.Contains(entityPrefabReference))
                continue;

            entityPrefabReferences.Add(entityPrefabReference);
        }

        var entityManager = state.EntityManager;
        var entityArray =
            entityManager.CreateEntity(__entityArchetype, entityPrefabReferences.Length, worldUpdateAllocator);

        __requestEntityPrefabLoadeds.Update(ref state);
        
        Apply apply;
        apply.entityArray = entityArray;
        apply.entityPrefabReferences = entityPrefabReferences;
        apply.requestEntityPrefabLoadeds = __requestEntityPrefabLoadeds;

        state.Dependency = apply.ScheduleByRef(entityArray.Length, 1, state.Dependency);
    }
}
