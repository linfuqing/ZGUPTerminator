using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Scenes;
using Unity.Transforms;

[CreateAfter(typeof(CopyMatrixToTransformSystem)), UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class InstanceSystem : SystemBase
{
    private struct Scene
    {
        public int count;
        public Entity entity;
    }
    
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

    private EntityQuery __groupToClear;
    private EntityQuery __groupToEnable;
    private EntityQuery __groupToDisable;
    private EntityQuery __groupToDestroy;
    private EntityQuery __groupToCreate;
    private NativeParallelMultiHashMap<FixedString128Bytes, Entity> __entities;
    private NativeParallelMultiHashMap<int, EntityPrefabReference> __entityPrefabReferences;
    private NativeParallelMultiHashMap<Entity, RigidTransform> __loaders;
    private NativeHashMap<EntityPrefabReference, Scene> __scenes;
    private NativeHashMap<Entity, EntityPrefabReference> __instances;

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
            __groupToClear = builder
                .WithAll<InstanceEntity>()
                .WithNone<SceneSection>()
                .Build(this);

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
        __loaders = new NativeParallelMultiHashMap<Entity, RigidTransform>(1, Allocator.Persistent);
        __scenes = new NativeHashMap<EntityPrefabReference, Scene>(1, Allocator.Persistent);
        __instances = new NativeHashMap<Entity, EntityPrefabReference>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __instances.Dispose();
        __scenes.Dispose();
        __loaders.Dispose();
        __entityPrefabReferences.Dispose();
        __entities.Dispose();
        
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var world = World.Unmanaged;
        var entityManager = world.EntityManager;
        if (!__loaders.IsEmpty)
        {
            using (var keys = __loaders.GetKeyArray(Allocator.Temp))
            {
                LocalTransform localTransform;
                localTransform.Scale = 1.0f;

                NativeList<Entity> entities = default;
                Entity key, entity, prefabRoot;
                EntityPrefabReference instance;
                Scene scene;
                SceneSystem.SceneStreamingState state;
                int entityOffset, length = keys.Unique();
                for (int i = 0; i < length; ++i)
                {
                    key = keys[i];
                    state = SceneSystem.GetSceneStreamingState(world, key);
                    switch (state)
                    {
                        case SceneSystem.SceneStreamingState.Loading:
                            break;
                        case SceneSystem.SceneStreamingState.LoadedSuccessfully:
                        //case SceneSystem.SceneStreamingState.LoadedSectionEntities:
                        //case SceneSystem.SceneStreamingState.LoadedWithSectionErrors:
                            int numEntities = __loaders.CountValuesForKey(key);
                            if (!entities.IsCreated)
                                entities = new NativeList<Entity>(numEntities, Allocator.Temp);

                            entityOffset = entities.Length;
                            entities.ResizeUninitialized(entityOffset + numEntities);
 
                            prefabRoot = entityManager.GetComponentData<PrefabRoot>(key).Root;
                            entityManager.Instantiate(prefabRoot, entities.AsArray().GetSubArray(entityOffset, numEntities));
                        
                            instance = __instances[key];
                            __localTransforms.Update(this);
                            foreach (var loader in __loaders.GetValuesForKey(key))
                            {
                                entity = entities[--numEntities + entityOffset];
                                
                                localTransform.Position = loader.pos;
                                localTransform.Rotation = loader.rot;

                                __localTransforms[entity] = localTransform;

                                __instances[entity] = instance;
                            }

                            __loaders.Remove(key);

                            //SceneSystem.UnloadScene(world, entity);

                            break;
                        default:
                            UnityEngine.Debug.LogError(state);
                            
                            instance = __instances[key];
                            scene = __scenes[instance];
                            scene.count -= __loaders.CountValuesForKey(key);
                            if (scene.count > 0)
                                __scenes[instance] = scene;
                            else
                            {
                                SceneSystem.UnloadScene(world, scene.entity);
                                
                                __scenes.Remove(instance);

                                __instances.Remove(key);
                            }

                            __loaders.Remove(key);

                            break;
                    }
                }

                if (entities.IsCreated)
                {
                    entityManager.AddComponent<InstanceEntity>(entities.AsArray());
                    
                    entities.Dispose();
                }
            }
        }

        if (!__groupToClear.IsEmpty)
        {
            using (var entities = __groupToClear.ToEntityArray(Allocator.Temp))
            {
                entityManager.RemoveComponent<InstanceEntity>(__groupToClear);
                
                EntityPrefabReference instance;
                foreach (var entity in entities)
                {
                    instance = __instances[entity];
                    var scene = __scenes[instance];
                    if (--scene.count > 0)
                        __scenes[instance] = scene;
                    else
                    {
                        SceneSystem.UnloadScene(World.Unmanaged, scene.entity);
                       
                        __instances.Remove(scene.entity); 
                        __scenes.Remove(instance);
                    }
                    
                    __instances.Remove(entity);
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
                Scene scene;
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
                                if (__scenes.TryGetValue(entityPrefabReference, out scene))
                                    ++scene.count;
                                else
                                {
                                    scene.count = 1;
                                    scene.entity = SceneSystem.LoadPrefabAsync(world, entityPrefabReference);

                                    __instances[scene.entity] = entityPrefabReference;
                                }
                                
                                __scenes[entityPrefabReference] = scene;
                                
                                __loaders.Add(scene.entity, math.RigidTransform(transform.rotation, transform.position));
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