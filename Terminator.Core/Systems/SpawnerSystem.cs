using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.GraphicsIntegration;
using Unity.Physics.Systems;
using Unity.Transforms;
using ZG;

public struct SpawnerSingleton : IComponentData
{
    public uint version;
    
    public NativeArray<int> instanceCount;
    
    public NativeParallelMultiHashMap<SpawnerEntity, Entity> entities;
}

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct SpawnerRecountSystem : ISystem
{
    private struct Recount
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<SpawnerEntity> inputs;

        public NativeParallelMultiHashMap<SpawnerEntity, Entity>.ParallelWriter outputs;

        public void Execute(int index)
        {
            outputs.Add(inputs[index], entityArray[index]);
        }
    }

    [BurstCompile]
    private struct RecountEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<SpawnerEntity> inputType;

        public NativeParallelMultiHashMap<SpawnerEntity, Entity>.ParallelWriter outputs;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Recount recount;
            recount.entityArray = chunk.GetNativeArray(entityType);
            recount.inputs = chunk.GetNativeArray(ref inputType);
            recount.outputs = outputs;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                recount.Execute(i);
        }
    }

    [BurstCompile]
    private struct Clear : IJobChunk
    {
        public BufferTypeHandle<SpawnerEntityCount> entityCountType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entityCounts = chunk.GetBufferAccessor(ref entityCountType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                entityCounts[i].Clear();
        }
    }

    private uint __version;
    
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<SpawnerEntity> __spawnerEntityType;
    private BufferTypeHandle<SpawnerEntityCount> __entityCountType;

    private EntityQuery __entityGroup;
    private EntityQuery __counterGroup;

    private NativeArray<int> __instanceCount;
    private NativeParallelMultiHashMap<SpawnerEntity, Entity> __entities;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __spawnerEntityType = state.GetComponentTypeHandle<SpawnerEntity>(true);
        __entityCountType = state.GetBufferTypeHandle<SpawnerEntityCount>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __entityGroup = builder
                .WithAll<SpawnerEntity>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __counterGroup = builder
                .WithAll<SpawnerEntityCount>()
                .Build(ref state);

        __instanceCount = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        __entities = new NativeParallelMultiHashMap<SpawnerEntity, Entity>(1, Allocator.Persistent);

        SpawnerSingleton spawnerSingleton;
        spawnerSingleton.version = 0;
        spawnerSingleton.instanceCount = __instanceCount;
        spawnerSingleton.entities = __entities;

        state.EntityManager.CreateSingleton(spawnerSingleton);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __instanceCount.Dispose();
        __entities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        uint version = (uint)__entityGroup.GetCombinedComponentOrderVersion(false);
        if (ChangeVersionUtility.DidChange(version, __version))
        {
            int entityCount = __entityGroup.CalculateEntityCount();

            __instanceCount[0] = entityCount;

            __version = version;

            __entities.Clear();
            __entities.Capacity = math.max(__entities.Capacity, entityCount);

            __entityType.Update(ref state);
            __spawnerEntityType.Update(ref state);

            RecountEx recount;
            recount.entityType = __entityType;
            recount.inputType = __spawnerEntityType;
            recount.outputs = __entities.AsParallelWriter();
            state.Dependency = recount.ScheduleByRef(__entityGroup, state.Dependency);

            SpawnerSingleton spawnerSingleton;
            spawnerSingleton.version = version;
            spawnerSingleton.instanceCount = __instanceCount;
            spawnerSingleton.entities = __entities;

            SystemAPI.SetSingleton(spawnerSingleton);
        }

        __entityCountType.Update(ref state);
        
        Clear clear;
        clear.entityCountType = __entityCountType;
        
        state.Dependency = clear.ScheduleParallelByRef(__counterGroup, state.Dependency);
    }
}

[BurstCompile, CreateAfter(typeof(PrefabLoaderSystem)), UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
public partial struct SpawnerSystem : ISystem
{
    private struct Counter : IComponentData
    {
        public int value;
    }
    
    [BurstCompile]
    private struct Reset : IJob
    {
        public Entity layerMaskEntity;

        public ComponentLookup<SpawnerLayerMaskAndTagsOverride> layerMaskAndTags;

        public void Execute()
        {
            if (!layerMaskAndTags.HasComponent(layerMaskEntity))
                return;
            
            layerMaskAndTags.GetRefRW(layerMaskEntity).ValueRW.value = default;
        }
    }

    private struct Trigger
    {
        public RefRW<SpawnerLayerMaskAndTagsOverride> layerMaskAndTags;

        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public BufferAccessor<SpawnerTrigger> triggers;

        [ReadOnly]
        public BufferAccessor<SimulationEvent> simulationEvents;

        public void Execute(int index)
        {
            bool isContains;
            int rigidBodyIndex;
            var simulationEvents = this.simulationEvents[index];
            foreach (var trigger in triggers[index])
            {
                if (trigger.belongs != 0)
                {
                    isContains = false;
                    foreach (var simulationEvent in simulationEvents)
                    {
                        rigidBodyIndex = collisionWorld.GetRigidBodyIndex(simulationEvent.entity);
                        isContains = rigidBodyIndex != -1 &&
                                     (collisionWorld.Bodies[rigidBodyIndex].Collider.Value.GetCollisionFilter(simulationEvent.colliderKey)
                                          .BelongsTo &
                                      trigger.belongs) != 0;
                        if (isContains)
                            break;
                    }
                    
                    if(!isContains)
                        continue;
                }
                
                layerMaskAndTags.ValueRW.value.InterOr(trigger.layerMaskAndTags);
            }
        }
    }

    [BurstCompile]
    private struct TriggerEx : IJobChunk
    {
        public Entity layerMaskEntity;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerLayerMaskAndTagsOverride> layerMaskAndTags;

        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public BufferTypeHandle<SpawnerTrigger> triggerType;

        [ReadOnly]
        public BufferTypeHandle<SimulationEvent> simulationEventType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!layerMaskAndTags.HasComponent(layerMaskEntity))
                return;
            
            Trigger trigger;
            trigger.layerMaskAndTags = layerMaskAndTags.GetRefRW(layerMaskEntity);
            trigger.collisionWorld = collisionWorld;
            trigger.triggers = chunk.GetBufferAccessor(ref triggerType);
            trigger.simulationEvents = chunk.GetBufferAccessor(ref simulationEventType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                trigger.Execute(i);
        }
    }

    private struct Collect
    {
        public double time;

        public float3 playerPosition;

        public Random random;

        public SpawnerTime spawnerTime;

        public RefRW<Counter> instanceCount;

        [ReadOnly]
        public CollisionWorld collisionWorld;
        
        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;

        [ReadOnly]
        public ComponentLookup<PhysicsGraphicalInterpolationBuffer> physicsGraphicalInterpolationBuffers;
        
        [ReadOnly]
        public ComponentLookup<CharacterInterpolation> characterInterpolations;

        [ReadOnly] 
        public ComponentLookup<EffectTargetLevel> targetLevels;
        
        [ReadOnly] 
        public BufferLookup<MessageParameter> messageParameters;
        
        [ReadOnly] 
        public NativeParallelMultiHashMap<SpawnerEntity, Entity> entities;

        [ReadOnly] 
        public NativeArray<Entity> entityArray;

        [ReadOnly] 
        public NativeArray<SpawnerDefinitionData> instances;

        [ReadOnly]
        public NativeArray<SpawnerLayerMaskAndTags> layerMaskAndTags;
        [ReadOnly]
        public NativeArray<SpawnerLayerMaskAndTagsOverride> layerMaskAndTagsOverrides;
        [ReadOnly]
        public NativeArray<SpawnerLayerMaskAndTagsInclude> layerMaskAndTagsIncludes;
        [ReadOnly]
        public NativeArray<SpawnerLayerMaskAndTagsExclude> layerMaskAndTagsExcludes;
        [ReadOnly]
        public BufferAccessor<SpawnerPrefab> prefabs;

        public BufferAccessor<SpawnerStatus> states;
        public BufferAccessor<SpawnerEntityCount> entityCounts;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;
        
        public void Execute(int index)
        {
            var states = this.states[index];
            var entityCounts = this.entityCounts[index];

            var layerMaskAndTags = this.layerMaskAndTags[index].Get(layerMaskAndTagsOverrides[index], layerMaskAndTagsIncludes[index], layerMaskAndTagsExcludes[index]);

            ref var definition = ref instances[index].definition.Value;
            definition.Update(
                time,
                playerPosition,
                entityArray[index],
                layerMaskAndTags, 
                spawnerTime, 
                collisionWorld,
                colliders, 
                physicsGraphicalInterpolationBuffers, 
                characterInterpolations, 
                targetLevels, 
                messageParameters, 
                entities, 
                prefabs[index], 
                ref states, 
                ref entityCounts, 
                ref entityManager, 
                ref prefabLoader, 
                ref random, 
                ref instanceCount.ValueRW.value);
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public double time;
        public Entity playerEntity;
        
        public SpawnerTime spawnerTime;
        
        [NativeDisableParallelForRestriction]
        public NativeArray<int> instanceCount;

        [ReadOnly] 
        public NativeParallelMultiHashMap<SpawnerEntity, Entity> entities;

        [ReadOnly]
        public CollisionWorld collisionWorld;
        
        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;

        [ReadOnly]
        public ComponentLookup<CharacterInterpolation> characterInterpolations;

        [ReadOnly]
        public ComponentLookup<PhysicsGraphicalInterpolationBuffer> physicsGraphicalInterpolationBuffers;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly] 
        public ComponentLookup<EffectTargetLevel> targetLevels;

        [ReadOnly] 
        public BufferLookup<MessageParameter> messageParameters;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<SpawnerDefinitionData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<SpawnerLayerMaskAndTags> layerMaskAndTagsType;
        [ReadOnly]
        public ComponentTypeHandle<SpawnerLayerMaskAndTagsOverride> layerMaskAndTagsOverrideType;
        [ReadOnly]
        public ComponentTypeHandle<SpawnerLayerMaskAndTagsInclude> layerMaskAndTagsIncludeType;
        [ReadOnly]
        public ComponentTypeHandle<SpawnerLayerMaskAndTagsExclude> layerMaskAndTagsExcludeType;
        [ReadOnly]
        public BufferTypeHandle<SpawnerPrefab> prefabType;

        public BufferTypeHandle<SpawnerStatus> statusType;

        public BufferTypeHandle<SpawnerEntityCount> entityCountType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!localTransforms.TryGetComponent(playerEntity, out var localTransform))
                return;

            long time = math.aslong(this.time);
            
            Collect collect;
            collect.instanceCount = new RefRW<Counter>(instanceCount.Reinterpret<Counter>(), 0);
            collect.time = this.time;
            collect.playerPosition = localTransform.Position;
            collect.random = Random.CreateFromIndex((uint)(unfilteredChunkIndex ^ (int)time ^ (int)(time >> 32)));
            collect.spawnerTime = spawnerTime;
            collect.collisionWorld = collisionWorld;
            collect.colliders = colliders;
            collect.physicsGraphicalInterpolationBuffers = physicsGraphicalInterpolationBuffers;
            collect.characterInterpolations = characterInterpolations;
            collect.targetLevels = targetLevels;
            collect.messageParameters = messageParameters;
            collect.entities = entities;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.layerMaskAndTags = chunk.GetNativeArray(ref layerMaskAndTagsType);
            collect.layerMaskAndTagsOverrides = chunk.GetNativeArray(ref layerMaskAndTagsOverrideType);
            collect.layerMaskAndTagsIncludes = chunk.GetNativeArray(ref layerMaskAndTagsIncludeType);
            collect.layerMaskAndTagsExcludes = chunk.GetNativeArray(ref layerMaskAndTagsExcludeType);
            collect.prefabs = chunk.GetBufferAccessor(ref prefabType);
            collect.states = chunk.GetBufferAccessor(ref statusType);
            collect.entityCounts = chunk.GetBufferAccessor(ref entityCountType);
            collect.entityManager = entityManager;
            collect.prefabLoader = prefabLoader;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    private BufferLookup<MessageParameter> __messageParameters;

    private ComponentLookup<EffectTargetLevel> __targetLevels;

    private ComponentLookup<CharacterInterpolation> __characterInterpolations;

    private ComponentLookup<PhysicsGraphicalInterpolationBuffer> __physicsGraphicalInterpolationBuffers;

    private ComponentLookup<PhysicsCollider> __colliders;

    private ComponentLookup<LocalTransform> __localTransforms;

    private ComponentLookup<SpawnerLayerMaskAndTagsOverride> __layerMaskAndTags;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<SpawnerDefinitionData> __instanceType;
    private ComponentTypeHandle<SpawnerLayerMaskAndTags> __layerMaskAndTagsType;
    private ComponentTypeHandle<SpawnerLayerMaskAndTagsOverride> __layerMaskAndTagsOverrideType;
    private ComponentTypeHandle<SpawnerLayerMaskAndTagsInclude> __layerMaskAndTagsIncludeType;
    private ComponentTypeHandle<SpawnerLayerMaskAndTagsExclude> __layerMaskAndTagsExcludeType;

    private BufferTypeHandle<SimulationEvent> __simulationEventType;

    private BufferTypeHandle<SpawnerTrigger> __triggerType;

    private BufferTypeHandle<SpawnerPrefab> __prefabType;
    private BufferTypeHandle<SpawnerStatus> __statusType;
    private BufferTypeHandle<SpawnerEntityCount> __entityCountType;

    private EntityQuery __groupToTrigger;
    private EntityQuery __groupToCollect;
    
    private PrefabLoader __prefabLoader;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __messageParameters = state.GetBufferLookup<MessageParameter>(true);
        __targetLevels = state.GetComponentLookup<EffectTargetLevel>(true);
        __characterInterpolations = state.GetComponentLookup<CharacterInterpolation>(true);
        __physicsGraphicalInterpolationBuffers = state.GetComponentLookup<PhysicsGraphicalInterpolationBuffer>(true);
        __colliders = state.GetComponentLookup<PhysicsCollider>(true);
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __layerMaskAndTags = state.GetComponentLookup<SpawnerLayerMaskAndTagsOverride>();
        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetComponentTypeHandle<SpawnerDefinitionData>(true);
        __layerMaskAndTagsType = state.GetComponentTypeHandle<SpawnerLayerMaskAndTags>(true);
        __layerMaskAndTagsOverrideType = state.GetComponentTypeHandle<SpawnerLayerMaskAndTagsOverride>(true);
        __layerMaskAndTagsIncludeType = state.GetComponentTypeHandle<SpawnerLayerMaskAndTagsInclude>(true);
        __layerMaskAndTagsExcludeType = state.GetComponentTypeHandle<SpawnerLayerMaskAndTagsExclude>(true);
        __simulationEventType = state.GetBufferTypeHandle<SimulationEvent>(true);
        __triggerType = state.GetBufferTypeHandle<SpawnerTrigger>(true);
        __prefabType = state.GetBufferTypeHandle<SpawnerPrefab>(true);
        __statusType = state.GetBufferTypeHandle<SpawnerStatus>();
        __entityCountType = state.GetBufferTypeHandle<SpawnerEntityCount>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToTrigger = builder
                .WithAll<SpawnerTrigger, SimulationEvent>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCollect = builder
                .WithAll<SpawnerDefinitionData>()
                .WithAllRW<SpawnerStatus>()
                .Build(ref state);

        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<SpawnerSingleton>();
        state.RequireForUpdate<SpawnerLayerMaskAndTags>();
        state.RequireForUpdate<SpawnerTime>();
        state.RequireForUpdate<ThirdPersonPlayer>();

        __prefabLoader = new PrefabLoader(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var jobHandle = state.Dependency;

        var layerMaskEntity = SystemAPI.GetSingletonEntity<SpawnerLayerMaskAndTags>();
        __layerMaskAndTags.Update(ref state);

        Reset reset;
        reset.layerMaskEntity = layerMaskEntity;
        reset.layerMaskAndTags = __layerMaskAndTags;
        var resetJobHandle = reset.ScheduleByRef(jobHandle);
        
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        
        __simulationEventType.Update(ref state);
        __triggerType.Update(ref state);

        TriggerEx trigger;
        trigger.layerMaskEntity = layerMaskEntity;
        trigger.layerMaskAndTags = __layerMaskAndTags;
        trigger.collisionWorld = collisionWorld;
        trigger.triggerType = __triggerType;
        trigger.simulationEventType = __simulationEventType;
        var triggerJobHandle = trigger.ScheduleParallelByRef(__groupToTrigger, resetJobHandle);

        __messageParameters.Update(ref state);
        __targetLevels.Update(ref state);
        __characterInterpolations.Update(ref state);
        __physicsGraphicalInterpolationBuffers.Update(ref state);
        __colliders.Update(ref state);
        __localTransforms.Update(ref state);
        __instanceType.Update(ref state);
        __layerMaskAndTagsType.Update(ref state);
        __layerMaskAndTagsOverrideType.Update(ref state);
        __layerMaskAndTagsIncludeType.Update(ref state);
        __layerMaskAndTagsExcludeType.Update(ref state);
        __entityType.Update(ref state);
        __prefabType.Update(ref state);
        __statusType.Update(ref state);
        __entityCountType.Update(ref state);

        var spawnerSingleton = SystemAPI.GetSingleton<SpawnerSingleton>();
        
        CollectEx collect;
        collect.time = SystemAPI.Time.ElapsedTime;
        collect.playerEntity = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        collect.spawnerTime = SystemAPI.GetSingleton<SpawnerTime>();
        collect.instanceCount = spawnerSingleton.instanceCount;
        collect.entities = spawnerSingleton.entities;
        collect.collisionWorld = collisionWorld;
        collect.colliders = __colliders;
        collect.characterInterpolations = __characterInterpolations;
        collect.physicsGraphicalInterpolationBuffers = __physicsGraphicalInterpolationBuffers;
        collect.localTransforms = __localTransforms;
        collect.targetLevels = __targetLevels;
        collect.messageParameters = __messageParameters;
        collect.entityType = __entityType;
        collect.instanceType = __instanceType;
        collect.layerMaskAndTagsType = __layerMaskAndTagsType;
        collect.layerMaskAndTagsOverrideType = __layerMaskAndTagsOverrideType;
        collect.layerMaskAndTagsIncludeType = __layerMaskAndTagsIncludeType;
        collect.layerMaskAndTagsExcludeType = __layerMaskAndTagsExcludeType;
        collect.prefabType = __prefabType;
        collect.statusType = __statusType;
        collect.entityCountType = __entityCountType;
        collect.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        collect.prefabLoader = __prefabLoader.AsParallelWriter();
        state.Dependency = collect.ScheduleParallelByRef(__groupToCollect,
            JobHandle.CombineDependencies(triggerJobHandle, jobHandle));
    }
}
