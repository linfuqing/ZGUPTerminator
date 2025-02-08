using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Scenes;
using Unity.Transforms;

[CreateAfter(typeof(PrefabLoaderSystem)), 
 CreateAfter(typeof(CopyMatrixToTransformSystem)), 
 UpdateInGroup(typeof(InitializationSystemGroup))/*,
 UpdateAfter(typeof(MessageSystem))*/]
public partial class InstanceSystem : SystemBase
{
    /*private struct Scene
    {
        public int count;
        public Entity entity;
    }*/
    
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
    
    private EntityTypeHandle __entityType;

    private BufferTypeHandle<InstancePrefab> __prefabType;

    private ComponentTypeHandle<CopyMatrixToTransformInstanceID> __idType;
        
    private ComponentTypeHandle<Instance> __instanceType;
    private ComponentLookup<LocalToWorld> __localToWorlds;
    private ComponentLookup<LocalTransform> __localTransforms;
    private ComponentLookup<CopyMatrixToTransformInstanceID> __instanceIDs;

    private EntityQuery __groupToEnable;
    private EntityQuery __groupToDisable;
    private EntityQuery __groupToDestroy;
    private EntityQuery __groupToCreate;
    private NativeParallelMultiHashMap<FixedString128Bytes, Entity> __entities;
    private NativeParallelMultiHashMap<int, EntityPrefabReference> __entityPrefabReferences;
    private NativeParallelMultiHashMap<EntityPrefabReference, RigidTransform> __loaders;
    //private NativeHashMap<EntityPrefabReference, Scene> __scenes;
    //private NativeHashMap<Entity, EntityPrefabReference> __instances;
    private PrefabLoader __prefabLoader;

    protected override void OnCreate()
    {
        base.OnCreate();

        __entityType = GetEntityTypeHandle();
        __prefabType = GetBufferTypeHandle<InstancePrefab>(true);
        __idType = GetComponentTypeHandle<CopyMatrixToTransformInstanceID>(true);
        __instanceType = GetComponentTypeHandle<Instance>(true);
        __localToWorlds = GetComponentLookup<LocalToWorld>(true);
        __localTransforms = GetComponentLookup<LocalTransform>();
        __instanceIDs = GetComponentLookup<CopyMatrixToTransformInstanceID>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToEnable = builder
                .WithAll<CopyMatrixToTransformInstanceID, InstancePrefab>()
                .Build(this);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDisable = builder
                .WithAll<Disabled, CopyMatrixToTransformInstanceID>()
                .WithPresent<Instance>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(this);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDestroy = builder
                .WithAll<CopyMatrixToTransformInstanceID>()
                .WithAbsent<Instance>()
                //.WithNone<Message>()
                .Build(this);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCreate = builder
                .WithAll<LocalTransform>()
                .WithAllRW<Instance>()
                //.WithNone<CopyMatrixToTransformInstanceID>()
                .Build(this);

        __entities = new NativeParallelMultiHashMap<FixedString128Bytes, Entity>(1, Allocator.Persistent);
        __entityPrefabReferences = new NativeParallelMultiHashMap<int, EntityPrefabReference>(1, Allocator.Persistent);
        __loaders = new NativeParallelMultiHashMap<EntityPrefabReference, RigidTransform>(1, Allocator.Persistent);

        __prefabLoader = new PrefabLoader(this);
    }

    protected override void OnDestroy()
    {
        __loaders.Dispose();
        __entityPrefabReferences.Dispose();
        __entities.Dispose();
        
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        CompleteDependency();
        
        var world = World.Unmanaged;
        var entityManager = world.EntityManager;
        
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
                    if(!prefabLoader.GetOrLoadPrefabRoot(key, out entity))
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
                    __localTransforms.Update(this);

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
        
        if (!__groupToEnable.IsEmpty)
        {
            __prefabType.Update(this);
            __idType.Update(this);

            SaveEx save;
            save.idType = __idType;
            save.prefabType = __prefabType;
            save.entityPrefabReferences = __entityPrefabReferences;
            save.RunByRef(__groupToEnable);
            
            entityManager.SetComponentEnabled<InstancePrefab>(__groupToEnable, false);
        }
        
        if (!__groupToDisable.IsEmpty)
        {
            using (var ids =
                   __groupToDisable.ToComponentDataArray<CopyMatrixToTransformInstanceID>(Allocator.Temp))
            {
                foreach (var id in ids)
                {
                    __entityPrefabReferences.Remove(id.value);
                    
                    InstanceManager.Destroy(id.value, false);
                }
            }

            EntityManager.SetComponentEnabled<Instance>(__groupToDisable, true);
            
            EntityManager.RemoveComponent<CopyMatrixToTransformInstanceID>(__groupToDisable);
        }

        if (!__groupToDestroy.IsEmpty)
        {
            using (var ids =
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
            }

            EntityManager.RemoveComponent<CopyMatrixToTransformInstanceID>(__groupToDestroy);
        }

        if (!__groupToCreate.IsEmpty)
        {
            __entityType.Update(this);
            __instanceType.Update(this);

            __entities.Clear();

            CollectEx collect;
            collect.entityType = __entityType;
            collect.instanceType = __instanceType;
            collect.entities = __entities;
            collect.RunByRef(__groupToCreate);

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
                        this,
                        entities.AsArray(),
                        __localToWorlds,
                        ref __instanceIDs);
                }
            }

            EntityManager.SetComponentEnabled<Instance>(__groupToCreate, false);
            //EntityManager.RemoveComponent<Instance>(__group);
            //EntityManager.DestroyEntity(__group);
        }
    }
}

/*[UpdateInGroup(typeof(InitializationSystemGroup)), UpdateBefore(typeof(InstanceCreateSystem))]
public partial class InstanceDestroySystem : SystemBase
{
    private EntityQuery __group;

    protected override void OnCreate()
    {
        base.OnCreate();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAllRW<CopyMatrixToTransformInstanceID>()
                .WithNone<LocalTransform>()
                .Build(this);
        
        RequireForUpdate(__group);
    }

    protected override void OnUpdate()
    {
        using (var instanceIDs = __group.ToComponentDataArray<CopyMatrixToTransformInstanceID>(Allocator.Temp))
        {
            var mananger = InstanceManager.instance;
            foreach (var instanceID in instanceIDs)
                mananger.Destroy(instanceID.value);
        }

        EntityManager.RemoveComponent<CopyMatrixToTransformInstanceID>(__group);
    }
}*/