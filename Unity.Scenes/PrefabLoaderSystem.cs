using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Scenes;

public struct PrefabLoader
{
    public struct Writer
    {
        [ReadOnly]
        private WeakAssetReferenceLoadingData __weakAssetReferenceLoadingData;

        private NativeQueue<PrefabLoaderSingleton.Result> __results;

        internal Writer(ref PrefabLoader value)
        {
            __weakAssetReferenceLoadingData = value.__weakAssetReferenceLoadingData;
            __results = value.__group.GetSingleton<PrefabLoaderSingleton>().results;
        }

        public bool TryGetOrLoadPrefabRoot(in EntityPrefabReference entityPrefabReference, out Entity entity)
        {
            PrefabLoaderSingleton.Result result;
            result.entityPrefabReference = entityPrefabReference;
            if (__weakAssetReferenceLoadingData.LoadedPrefabs.TryGetValue(entityPrefabReference,
                    out var loadedPrefab))
            {
                entity = loadedPrefab.PrefabRoot;

                result.status = PrefabLoaderSingleton.Status.Loaded;
            } 
            else 
            {
                entity = Entity.Null;

                result.status = __weakAssetReferenceLoadingData.InProgressLoads.ContainsKey(entityPrefabReference)
                    ? PrefabLoaderSingleton.Status.InProgressLoad
                    : PrefabLoaderSingleton.Status.None;
            }
            
            __results.Enqueue(result);

            return entity != Entity.Null;
        }
    }
    
    public struct ParallelWriter
    {
        [ReadOnly]
        private WeakAssetReferenceLoadingData __weakAssetReferenceLoadingData;

        private NativeQueue<PrefabLoaderSingleton.Result>.ParallelWriter __results;

        internal ParallelWriter(ref PrefabLoader value)
        {
            __weakAssetReferenceLoadingData = value.__weakAssetReferenceLoadingData;
            __results = value.__group.GetSingleton<PrefabLoaderSingleton>().results.AsParallelWriter();
        }

        public bool TryGetOrLoadPrefabRoot(in EntityPrefabReference entityPrefabReference, out Entity entity)
        {
            UnityEngine.Assertions.Assert.AreNotEqual(default, entityPrefabReference);
            
            PrefabLoaderSingleton.Result result;
            result.entityPrefabReference = entityPrefabReference;
            if (__weakAssetReferenceLoadingData.LoadedPrefabs.TryGetValue(entityPrefabReference,
                    out var loadedPrefab))
            {
                entity = loadedPrefab.PrefabRoot;

                result.status = PrefabLoaderSingleton.Status.Loaded;
            } 
            else 
            {
                entity = Entity.Null;

                result.status = __weakAssetReferenceLoadingData.InProgressLoads.ContainsKey(entityPrefabReference)
                    ? PrefabLoaderSingleton.Status.InProgressLoad
                    : PrefabLoaderSingleton.Status.None;
            }
            
            __results.Enqueue(result);

            return entity != Entity.Null;
        }
    }
    
    [ReadOnly]
    private WeakAssetReferenceLoadingData __weakAssetReferenceLoadingData;
    
    private EntityQuery __group;

    public PrefabLoader(ref SystemState systemState)
    {
        var world = systemState.WorldUnmanaged;
        var entityManager = world.EntityManager;
        var systemHandle = world.GetExistingUnmanagedSystem<WeakAssetReferenceLoadingSystem>();
        __weakAssetReferenceLoadingData = 
            entityManager.GetComponentData<WeakAssetReferenceLoadingData>(systemHandle);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAllRW<PrefabLoaderSingleton>()
                .Build(ref systemState);
    }
    
    public PrefabLoader(SystemBase system)
    {
        var world = system.World;
        var entityManager = world.EntityManager;
        var systemHandle = world.GetExistingSystem<WeakAssetReferenceLoadingSystem>();
        __weakAssetReferenceLoadingData = 
            entityManager.GetComponentData<WeakAssetReferenceLoadingData>(systemHandle);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAllRW<PrefabLoaderSingleton>()
                .Build(system);
    }

    /*public void AddDependency(in JobHandle jobHandle)
    {
        __group.AddDependency(jobHandle);
    }*/

    public Writer AsWriter()
    {
        return new Writer(ref this);
    }
    
    public ParallelWriter AsParallelWriter()
    {
        return new ParallelWriter(ref this);
    }
}

internal struct PrefabLoaderSingleton : IComponentData
{
    public enum Status
    {
        None, 
        Loaded,
        InProgressLoad
    }
    
    public struct Result
    {
        public Status status;
        public EntityPrefabReference entityPrefabReference;
    }
    
    public NativeQueue<Result> results;
}

public static class PrefabLoaderSettings
{
    public const float SAVED_TIME_MIN = 10.0f;
    
    private class SavedTime
    {
        public static readonly SharedStatic<float> Value = SharedStatic<float>.GetOrCreate<SavedTime>();
    }

    public static float savedTime
    {
        get => math.max(SavedTime.Value.Data, SAVED_TIME_MIN);
        
        set => SavedTime.Value.Data = value;
    }
}

[BurstCompile, 
 CreateAfter(typeof(WeakAssetReferenceLoadingSystem)), 
 UpdateInGroup(typeof(SceneSystemGroup)), 
 UpdateBefore(typeof(WeakAssetReferenceLoadingSystem))]
public partial struct PrefabLoaderSystem : ISystem
{
    private struct Instance
    {
        public double time;
        public Entity entity;
    }

    private struct Temp
    {
        public EntityPrefabReference entityPrefabReference;
        public Entity entity;
    }
    
    [BurstCompile]
    private struct Collect : IJob
    {
        public float savedTime;
        public double time;
        public NativeQueue<PrefabLoaderSingleton.Result> results;
        public NativeParallelHashMap<EntityPrefabReference, Instance> instances;
        public NativeList<EntityPrefabReference> entityPrefabReferences;
        public NativeList<Entity> entities;

        public void Execute()
        {
            Temp temp;
            Instance instance;
            NativeList<Temp> temps = default;
            while (results.TryDequeue(out var result))
            {
                if (instances.TryGetValue(result.entityPrefabReference, out instance))
                {
                    temp.entityPrefabReference = result.entityPrefabReference;
                    temp.entity = instance.entity;

                    if(!temps.IsCreated)
                        temps = new NativeList<Temp>(results.Count, Allocator.Temp);

                    temps.Add(temp);

                    instances.Remove(result.entityPrefabReference);
                }
                else if (result.status == PrefabLoaderSingleton.Status.None &&
                         entityPrefabReferences.IndexOf(result.entityPrefabReference) == -1)
                    entityPrefabReferences.Add(result.entityPrefabReference);
            }

            int source = entityPrefabReferences.Length;
            foreach (var pair in instances)
            {
                instance = pair.Value;
                if (instance.time < time)
                {
                    entityPrefabReferences.Add(pair.Key);

                    entities.Add(instance.entity);

                    //continue;
                }

                //break;
            }

            int destination = entityPrefabReferences.Length;
            for (int i = source; i < destination; ++i)
                instances.Remove(entityPrefabReferences[i]);

            entityPrefabReferences.ResizeUninitialized(source);

            instance.time = time + savedTime;
            int numTemps = temps.IsCreated ? temps.Length : 0;
            if (numTemps > 0)
            {
                for (int i = 0; i < numTemps; ++i)
                {
                    temp = temps[i];

                    instance.entity = temp.entity;

                    instances.Add(temp.entityPrefabReference, instance);
                }

                temps.Dispose();
            }
        }
    }
    
    [BurstCompile]
    private struct Apply : IJobParallelFor
    {
        public double time;
        
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<EntityPrefabReference> entityPrefabReferences;
        
        [NativeDisableParallelForRestriction]
        public ComponentLookup<RequestEntityPrefabLoaded> requestEntityPrefabLoadeds;

        public NativeParallelHashMap<EntityPrefabReference, Instance>.ParallelWriter instances;
        
        public void Execute(int index)
        {
            Instance instance;
            instance.time = time;
            instance.entity = entityArray[index];
            
            RequestEntityPrefabLoaded requestEntityPrefabLoaded;
            requestEntityPrefabLoaded.Prefab = entityPrefabReferences[index];

            instances.TryAdd(requestEntityPrefabLoaded.Prefab, instance);
                
            requestEntityPrefabLoadeds[instance.entity] = requestEntityPrefabLoaded;
        }
    }
    
    private ComponentLookup<RequestEntityPrefabLoaded> __requestEntityPrefabLoadeds;

    private EntityArchetype __entityArchetype;
    private NativeParallelHashMap<EntityPrefabReference, Instance> __instances;
    private NativeQueue<PrefabLoaderSingleton.Result> __results;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __requestEntityPrefabLoadeds = state.GetComponentLookup<RequestEntityPrefabLoaded>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            builder
                .WithAllRW<WeakAssetReferenceLoadingData>()
                .Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            builder
                .WithAllRW<PrefabLoaderSingleton>()
                .Build(ref state);
        
        var entityManager = state.EntityManager;
        using (var componentTypes = new NativeList<ComponentType>(Allocator.Temp)
               {
                   ComponentType.ReadWrite<RequestEntityPrefabLoaded>()
               })
            __entityArchetype = entityManager.CreateArchetype(componentTypes);

        __instances = new NativeParallelHashMap<EntityPrefabReference, Instance>(1, Allocator.Persistent);
        __results = new NativeQueue<PrefabLoaderSingleton.Result>(Allocator.Persistent);

        PrefabLoaderSingleton singleton;
        singleton.results = __results;
        entityManager.CreateSingleton(singleton);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __instances.Dispose();
        __results.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();

        int count = __results.Count;
        //if (count < 1)
        //    return;
        
        var worldUpdateAllocator = state.WorldUpdateAllocator;
        var entityPrefabReferences = new NativeList<EntityPrefabReference>(count, worldUpdateAllocator);
        var entities = new NativeList<Entity>(count, worldUpdateAllocator);
        
        float savedTime = PrefabLoaderSettings.savedTime;
        double time = SystemAPI.Time.ElapsedTime;

        Collect collect;
        collect.savedTime = savedTime;
        collect.time = time;
        collect.results = __results;
        collect.instances = __instances;
        collect.entityPrefabReferences = entityPrefabReferences;
        collect.entities = entities;
        collect.RunByRef();

        var entityManager = state.EntityManager;
        if (!entities.IsEmpty)
            entityManager.DestroyEntity(entities.AsArray());

        if (!entityPrefabReferences.IsEmpty)
        {
            var entityArray =
                entityManager.CreateEntity(__entityArchetype, entityPrefabReferences.Length, worldUpdateAllocator);

            __requestEntityPrefabLoadeds.Update(ref state);

            __instances.Capacity = math.max(__instances.Capacity, __instances.Count() + entityArray.Length);

            Apply apply;
            apply.time = time + savedTime;
            apply.entityArray = entityArray;
            apply.entityPrefabReferences = entityPrefabReferences.AsArray();
            apply.requestEntityPrefabLoadeds = __requestEntityPrefabLoadeds;
            apply.instances = __instances.AsParallelWriter();

            state.Dependency = apply.ScheduleByRef(entityArray.Length, 1, state.Dependency);
        }
    }
}
