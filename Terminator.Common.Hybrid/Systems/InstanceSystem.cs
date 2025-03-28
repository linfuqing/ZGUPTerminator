using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Transforms;

public struct InstanceSingleton : IComponentData
{
    private NativeList<int> __idsToDisable;
    private NativeList<CopyMatrixToTransformInstanceID> __idsToDestroy;

    private NativeParallelMultiHashMap<FixedString128Bytes, Entity> __entities;
    
    private NativeParallelMultiHashMap<int, EntityPrefabReference> __entityPrefabReferences;

    private NativeParallelMultiHashMap<EntityPrefabReference, RigidTransform> __loaders;
    
    internal InstanceSingleton(
        NativeList<int> idsToDisable, 
        NativeList<CopyMatrixToTransformInstanceID> idsToDestroy, 
        NativeParallelMultiHashMap<FixedString128Bytes, Entity> entities, 
        NativeParallelMultiHashMap<int, EntityPrefabReference> entityPrefabReferences, 
        NativeParallelMultiHashMap<EntityPrefabReference, RigidTransform> loaders)
    {
        __idsToDisable = idsToDisable;
        __idsToDestroy = idsToDestroy;
        __entities = entities;
        __entityPrefabReferences = entityPrefabReferences;
        __loaders = loaders;
    }
    
    public void Update(
        SystemBase system//, 
        //in ComponentLookup<LocalToWorld> localToWorlds, 
        //ref ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs
        )
    {
        if (!__idsToDisable.IsEmpty)
        {
            foreach (var id in __idsToDisable)
            {
                __entityPrefabReferences.Remove(id);

                InstanceManager.Destroy(id, false);
            }

            __idsToDisable.Clear();
        }

        if (!__idsToDestroy.IsEmpty)
        {
            UnityEngine.Transform transform;
            EntityPrefabReference entityPrefabReference;
            NativeParallelMultiHashMapIterator<int> iterator;
            foreach (var id in __idsToDestroy)
            {
                if (__entityPrefabReferences.TryGetFirstValue(id.value, out entityPrefabReference,
                        out iterator))
                {
                    transform = UnityEngine.Resources.InstanceIDToObject(id.value) as UnityEngine.Transform;
                    if (transform != null)
                    {
                        do
                        {
                            __loaders.Add(entityPrefabReference,
                                math.RigidTransform(transform.rotation, transform.position));
                        } while (__entityPrefabReferences.TryGetNextValue(out entityPrefabReference, ref iterator));
                    }

                    __entityPrefabReferences.Remove(id.value);
                }

                InstanceManager.Destroy(id.value, id.isSendMessageOnDestroy);
            }

            __idsToDestroy.Clear();
        }

        if (!__entities.IsEmpty)
        {
            using (var names = __entities.GetKeyArray(Allocator.Temp))
            using (var entities = new NativeList<Entity>(Allocator.Temp))
            {
                int count = names.Unique();
                FixedString128Bytes name;
                for (int i = 0; i < count; ++i)
                {
                    name = names[i];

                    entities.Clear();
                    foreach (var entity in __entities.GetValuesForKey(name))
                        entities.Add(entity);

                    InstanceManager.Instantiate(
                        name.ToString(),
                        system,
                        entities.AsArray()//,
                        //localToWorlds,
                        //ref instanceIDs
                        );
                }
            }

            __entities.Clear();
        }
    }
}

[BurstCompile, 
 CreateAfter(typeof(PrefabLoaderSystem)), 
 CreateAfter(typeof(CopyMatrixToTransformSystem)), 
 UpdateInGroup(typeof(InitializationSystemGroup))/*,
 UpdateAfter(typeof(MessageSystem))*/]
public partial struct InstanceSystemUnmanaged : ISystem
{
    private struct Save
    {
        [ReadOnly]
        public NativeArray<CopyMatrixToTransformInstanceID> ids;
        
        [ReadOnly]
        public BufferAccessor<InstancePrefab> prefabs;

        public NativeParallelMultiHashMap<int, EntityPrefabReference> entityPrefabReferences;

        public void Execute(int index)
        {
            int id = ids[index].value;
            var prefabs = this.prefabs[index];
            foreach (var prefab in prefabs)
                entityPrefabReferences.Add(id, prefab.reference);
        }
    }

    [BurstCompile]
    private struct SaveEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<CopyMatrixToTransformInstanceID> idType;
        
        [ReadOnly]
        public BufferTypeHandle<InstancePrefab> prefabType;

        public NativeParallelMultiHashMap<int, EntityPrefabReference> entityPrefabReferences;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Save save;
            save.ids = chunk.GetNativeArray(ref idType);
            save.prefabs = chunk.GetBufferAccessor(ref prefabType);
            save.entityPrefabReferences = entityPrefabReferences;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                save.Execute(i);
        }
    }
    
    private struct Collect
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Instance> instances;

        public NativeParallelMultiHashMap<FixedString128Bytes, Entity> entities;

        public void Execute(int index)
        {
            entities.Add(instances[index].name, entityArray[index]);
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Instance> instanceType;

        public NativeParallelMultiHashMap<FixedString128Bytes, Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.entities = entities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    [BurstCompile]
    private struct CollectIDs : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<CopyMatrixToTransformInstanceID> idType;

        public NativeList<CopyMatrixToTransformInstanceID> ids;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var ids = chunk.GetNativeArray(ref idType);

            if (useEnabledMask)
            {
                var iterator = new ChunkEntityEnumerator(true, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    this.ids.Add(ids[i]);
            }
            else
                this.ids.AddRange(ids);
        }
    }
    
    
    [BurstCompile]
    private struct Restore : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<CopyMatrixToTransformInstanceID> idType;

        public BufferTypeHandle<InstancePrefab> prefabType;

        public NativeList<int> ids;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var ids = chunk.GetNativeArray(ref idType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                this.ids.Add(ids[i].value);
            
            if(chunk.Has(ref prefabType))
                chunk.SetComponentEnabledForAll(ref prefabType, true);
        }
    }

    private EntityTypeHandle __entityType;

    private BufferTypeHandle<InstancePrefab> __prefabType;

    private ComponentTypeHandle<CopyMatrixToTransformInstanceID> __idType;
        
    private ComponentTypeHandle<Instance> __instanceType;
    private ComponentLookup<LocalTransform> __localTransforms;

    private EntityQuery __groupToEnable;
    private EntityQuery __groupToDisable;
    private EntityQuery __groupToDestroy;
    private EntityQuery __groupToCreate;
    private NativeList<int> __idsToDisable;
    private NativeList<CopyMatrixToTransformInstanceID> __idsToDestroy;
    private NativeParallelMultiHashMap<FixedString128Bytes, Entity> __entities;
    private NativeParallelMultiHashMap<int, EntityPrefabReference> __entityPrefabReferences;
    private NativeParallelMultiHashMap<EntityPrefabReference, RigidTransform> __loaders;
    private PrefabLoader __prefabLoader;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __prefabType = state.GetBufferTypeHandle<InstancePrefab>();
        __idType = state.GetComponentTypeHandle<CopyMatrixToTransformInstanceID>(true);
        __instanceType = state.GetComponentTypeHandle<Instance>(true);
        __localTransforms = state.GetComponentLookup<LocalTransform>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToEnable = builder
                .WithAll<CopyMatrixToTransformInstanceID, InstancePrefab>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDisable = builder
                .WithAll<Disabled, CopyMatrixToTransformInstanceID>()
                .WithPresent<Instance>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDestroy = builder
                .WithAll<CopyMatrixToTransformInstanceID>()
                .WithAbsent<Instance>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCreate = builder
                .WithAll<LocalTransform>()
                .WithAllRW<Instance>()
                //.WithNone<CopyMatrixToTransformInstanceID>()
                .Build(ref state);

        __idsToDisable = new NativeList<int>(Allocator.Persistent);
        __idsToDestroy = new NativeList<CopyMatrixToTransformInstanceID>(Allocator.Persistent);
        __entities = new NativeParallelMultiHashMap<FixedString128Bytes, Entity>(1, Allocator.Persistent);
        __entityPrefabReferences = new NativeParallelMultiHashMap<int, EntityPrefabReference>(1, Allocator.Persistent);
        __loaders = new NativeParallelMultiHashMap<EntityPrefabReference, RigidTransform>(1, Allocator.Persistent);

        __prefabLoader = new PrefabLoader(ref state);

        state.EntityManager.CreateSingleton(__CreateSingleton());
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __idsToDisable.Dispose();
        __idsToDestroy.Dispose();
        __loaders.Dispose();
        __entityPrefabReferences.Dispose();
        __entities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        
        var entityManager = state.EntityManager;
        
        if (!__loaders.IsEmpty)
        {
            var prefabLoader = __prefabLoader.AsWriter();
            using (var keys = __loaders.GetKeyArray(Allocator.Temp))
            {
                NativeList<Entity> entities = default;
                NativeList<RigidTransform> transforms = default;
                Entity entity;
                EntityPrefabReference key;
                int numEntities, entityOffset, numKeys = keys.Unique(), capacity = keys.Length;
                for (int i = 0; i < numKeys; ++i)
                {
                    key = keys[i];
                    if(!prefabLoader.TryGetOrLoadPrefabRoot(key, out entity))
                        continue;
                
                    numEntities = __loaders.CountValuesForKey(key);

                    if (!entities.IsCreated)
                    {
                        entities = new NativeList<Entity>(capacity, Allocator.Temp);
                        transforms = new NativeList<RigidTransform>(capacity, Allocator.Temp);
                    }

                    entityOffset = entities.Length;
                    entities.ResizeUninitialized(entityOffset + numEntities);
 
                    entityManager.Instantiate(entity, entities.AsArray().GetSubArray(entityOffset, numEntities));
                    
                    foreach (var loader in __loaders.GetValuesForKey(key))
                        transforms.Add(loader);

                    __loaders.Remove(key);
                }

                if (entities.IsCreated)
                {
                    __localTransforms.Update(ref state);

                    LocalTransform localTransform;
                    localTransform.Scale = 1.0f;

                    RigidTransform transform;
                    int length = entities.Length;
                    for(int i = 0; i < length; ++i)
                    {
                        transform = transforms[i];
                        localTransform.Position = transform.pos;
                        localTransform.Rotation = transform.rot;

                        __localTransforms[entities[i]] = localTransform;
                    }
                    
                    entities.Dispose();
                    transforms.Dispose();
                }
            }
        }
        
        __idType.Update(ref state);
        
        __prefabType.Update(ref state);

        if (!__groupToEnable.IsEmpty)
        {
            SaveEx save;
            save.idType = __idType;
            save.prefabType = __prefabType;
            save.entityPrefabReferences = __entityPrefabReferences;
            save.RunByRef(__groupToEnable);
            
            entityManager.SetComponentEnabled<InstancePrefab>(__groupToEnable, false);
        }
        
        if (!__groupToDisable.IsEmpty)
        {
            Restore restore;
            restore.idType = __idType;
            restore.prefabType = __prefabType;
            restore.ids = __idsToDisable;
            restore.RunByRef(__groupToDisable);

            /*using (var ids =
                   __groupToDisable.ToComponentDataArray<CopyMatrixToTransformInstanceID>(Allocator.Temp))
            {
                foreach (var id in ids)
                {
                    __entityPrefabReferences.Remove(id.value);
                    
                    InstanceManager.Destroy(id.value, false);
                }
            }*/

            entityManager.SetComponentEnabled<Instance>(__groupToDisable, true);
            
            entityManager.RemoveComponent<CopyMatrixToTransformInstanceID>(__groupToDisable);
        }
        
        if (!__groupToDestroy.IsEmpty)
        {
            __idType.Update(ref state);

            CollectIDs collectIDs;
            collectIDs.idType = __idType;
            collectIDs.ids = __idsToDestroy;
            collectIDs.RunByRef(__groupToDestroy);
            
            /*using (var ids =
                   __groupToDestroy.ToComponentDataArray<CopyMatrixToTransformInstanceID>(Allocator.Temp))
            {
                UnityEngine.Transform transform;
                EntityPrefabReference entityPrefabReference;
                NativeParallelMultiHashMapIterator<int> iterator;
                foreach (var id in ids)
                {
                    if (__entityPrefabReferences.TryGetFirstValue(id.value, out entityPrefabReference,
                            out iterator))
                    {
                        transform = UnityEngine.Resources.InstanceIDToObject(id.value) as UnityEngine.Transform;
                        if (transform != null)
                        {
                            do
                            {
                                __loaders.Add(entityPrefabReference, math.RigidTransform(transform.rotation, transform.position));
                            } while (__entityPrefabReferences.TryGetNextValue(out entityPrefabReference, ref iterator));
                        }

                        __entityPrefabReferences.Remove(id.value);
                    }

                    InstanceManager.Destroy(id.value, id.isSendMessageOnDestroy);
                }
            }*/

            entityManager.RemoveComponent<CopyMatrixToTransformInstanceID>(__groupToDestroy);
        }
        
        if (!__groupToCreate.IsEmpty)
        {
            __entityType.Update(ref state);
            __instanceType.Update(ref state);

            //__entities.Clear();

            CollectEx collect;
            collect.entityType = __entityType;
            collect.instanceType = __instanceType;
            collect.entities = __entities;
            collect.RunByRef(__groupToCreate);

            /*using (var names = __entities.GetKeyArray(Allocator.Temp))
            using (var entities = new NativeList<Entity>(Allocator.Temp))
            {
                int count = names.Unique();
                FixedString128Bytes name;
                for (int i = 0; i < count; ++i)
                {
                    name = names[i];

                    entities.Clear();
                    foreach (var entity in __entities.GetValuesForKey(name))
                        entities.Add(entity);

                    InstanceManager.Instantiate(
                        name.ToString(),
                        system,
                        entities.AsArray(),
                        __localToWorlds,
                        ref __instanceIDs);
                }
            }*/

            entityManager.SetComponentEnabled<Instance>(__groupToCreate, false);
        }
    }

    private InstanceSingleton __CreateSingleton()
    {
        return new InstanceSingleton(__idsToDisable, __idsToDestroy, __entities,
            __entityPrefabReferences, __loaders);
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup)), 
 UpdateAfter(typeof(InstanceSystemUnmanaged))]
public partial class InstanceSystem : SystemBase
{
    //private ComponentLookup<LocalToWorld> __localToWorlds;
    //private ComponentLookup<CopyMatrixToTransformInstanceID> __instanceIDs;
    
    protected override void OnCreate()
    {
        base.OnCreate();
        
        //__localToWorlds = GetComponentLookup<LocalToWorld>(true);
        //__instanceIDs = GetComponentLookup<CopyMatrixToTransformInstanceID>();
        
        RequireForUpdate<InstanceSingleton>();
    }
    
    protected override void OnUpdate()
    {
        SystemAPI.GetSingleton<InstanceSingleton>().Update(this);//, __localToWorlds, ref __instanceIDs);
    }
}