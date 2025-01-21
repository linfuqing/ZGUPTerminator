using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Scenes;
using Unity.Transforms;

public struct SpawnerSingleton : IComponentData
{
    public int version;
    
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

    private int __version;
    
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<SpawnerEntity> __spawnerEntityType;

    private EntityQuery __group;

    private NativeArray<int> __instanceCount;
    private NativeParallelMultiHashMap<SpawnerEntity, Entity> __entities;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __spawnerEntityType = state.GetComponentTypeHandle<SpawnerEntity>(true);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SpawnerEntity>()
                .Build(ref state);
        
        //state.RequireForUpdate(__group);

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
        int version = __group.GetCombinedComponentOrderVersion(false);
        if (version == __version)
            return;

        int entityCount = __group.CalculateEntityCount();

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
        state.Dependency = recount.ScheduleByRef(__group, state.Dependency);
        
        SpawnerSingleton spawnerSingleton;
        spawnerSingleton.version = version;
        spawnerSingleton.instanceCount = __instanceCount;
        spawnerSingleton.entities = __entities;

        SystemAPI.SetSingleton(spawnerSingleton);
    }
}

[BurstCompile, UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
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

        public ComponentLookup<SpawnerLayerMaskOverride> layerMasks;

        public void Execute()
        {
            if (!layerMasks.HasComponent(layerMaskEntity))
                return;
            
            layerMasks.GetRefRW(layerMaskEntity).ValueRW.value = 0;
        }
    }

    private struct Trigger
    {
        public RefRW<SpawnerLayerMaskOverride> layerMask;

        [ReadOnly]
        public NativeArray<SpawnerTrigger> triggers;

        public void Execute(int index)
        {
            int origin, layerMask = triggers[index].layerMask;
            do
            {
                origin = this.layerMask.ValueRW.value;
            } while (System.Threading.Interlocked.CompareExchange(ref this.layerMask.ValueRW.value, origin | layerMask, origin) != origin);
        }
    }

    [BurstCompile]
    private struct TriggerEx : IJobChunk
    {
        public Entity layerMaskEntity;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerLayerMaskOverride> layerMasks;

        [ReadOnly]
        public ComponentTypeHandle<SpawnerTrigger> triggerType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!layerMasks.HasComponent(layerMaskEntity))
                return;
            
            Trigger trigger;
            trigger.layerMask = layerMasks.GetRefRW(layerMaskEntity);
            trigger.triggers = chunk.GetNativeArray(ref triggerType);
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

        public RefRW<Counter> instanceCount;

        [ReadOnly]
        public CollisionWorld collisionWorld;
        
        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;
        
        [ReadOnly]
        public ComponentLookup<PrefabLoadResult> prefabLoadResults;

        [ReadOnly] 
        public NativeParallelMultiHashMap<SpawnerEntity, Entity> entities;

        [ReadOnly] 
        public NativeArray<Entity> entityArray;

        [ReadOnly] 
        public NativeArray<SpawnerDefinitionData> instances;

        [ReadOnly]
        public NativeArray<SpawnerLayerMask> layerMasks;
        [ReadOnly]
        public NativeArray<SpawnerLayerMaskOverride> layerMaskOverrides;
        [ReadOnly]
        public NativeArray<SpawnerLayerMaskInclude> layerMaskIncludes;
        [ReadOnly]
        public NativeArray<SpawnerLayerMaskExclude> layerMaskExcludes;
        [ReadOnly]
        public BufferAccessor<SpawnerPrefab> prefabs;

        public BufferAccessor<SpawnerStatus> states;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var states = this.states[index];

            int layerMask = layerMasks[index].Get(layerMaskOverrides[index], layerMaskIncludes[index], layerMaskExcludes[index]);

            ref var definition = ref instances[index].definition.Value;
            definition.Update(
                layerMask,
                time,
                playerPosition,
                entityArray[index],
                collisionWorld,
                colliders, 
                prefabLoadResults, 
                entities, 
                prefabs[index], 
                ref states, 
                ref entityManager, 
                ref random, 
                ref instanceCount.ValueRW.value);
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public double time;
        public Entity playerEntity;
        
        [NativeDisableParallelForRestriction]
        public NativeArray<int> instanceCount;

        [ReadOnly]
        public CollisionWorld collisionWorld;
        
        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;
        
        [ReadOnly]
        public ComponentLookup<PrefabLoadResult> prefabLoadResults;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly] 
        public NativeParallelMultiHashMap<SpawnerEntity, Entity> entities;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<SpawnerLayerMask> layerMaskType;
        [ReadOnly]
        public ComponentTypeHandle<SpawnerLayerMaskOverride> layerMaskOverrideType;
        [ReadOnly]
        public ComponentTypeHandle<SpawnerLayerMaskInclude> layerMaskIncludeType;
        [ReadOnly]
        public ComponentTypeHandle<SpawnerLayerMaskExclude> layerMaskExcludeType;

        [ReadOnly]
        public ComponentTypeHandle<SpawnerDefinitionData> instanceType;
        [ReadOnly]
        public BufferTypeHandle<SpawnerPrefab> prefabType;

        public BufferTypeHandle<SpawnerStatus> statusType;

        public EntityCommandBuffer.ParallelWriter entityManager;

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
            collect.collisionWorld = collisionWorld;
            collect.colliders = colliders;
            collect.prefabLoadResults = prefabLoadResults;
            collect.entities = entities;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.layerMasks = chunk.GetNativeArray(ref layerMaskType);
            collect.layerMaskOverrides = chunk.GetNativeArray(ref layerMaskOverrideType);
            collect.layerMaskIncludes = chunk.GetNativeArray(ref layerMaskIncludeType);
            collect.layerMaskExcludes = chunk.GetNativeArray(ref layerMaskExcludeType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.prefabs = chunk.GetBufferAccessor(ref prefabType);
            collect.states = chunk.GetBufferAccessor(ref statusType);
            collect.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    private ComponentLookup<PhysicsCollider> __colliders;

    private ComponentLookup<PrefabLoadResult> __prefabLoadResults;
    
    private ComponentLookup<LocalTransform> __localTransforms;

    private ComponentLookup<SpawnerLayerMaskOverride> __layerMasks;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<SpawnerLayerMask> __layerMaskType;
    private ComponentTypeHandle<SpawnerLayerMaskOverride> __layerMaskOverrideType;
    private ComponentTypeHandle<SpawnerLayerMaskInclude> __layerMaskIncludeType;
    private ComponentTypeHandle<SpawnerLayerMaskExclude> __layerMaskExcludeType;

    private ComponentTypeHandle<SpawnerTrigger> __triggerType;

    private ComponentTypeHandle<SpawnerDefinitionData> __instanceType;
    private BufferTypeHandle<SpawnerPrefab> __prefabType;
    private BufferTypeHandle<SpawnerStatus> __statusType;

    private EntityQuery __groupToTrigger;
    private EntityQuery __groupToCollect;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __colliders = state.GetComponentLookup<PhysicsCollider>(true);
        __prefabLoadResults = state.GetComponentLookup<PrefabLoadResult>(true);
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __layerMasks = state.GetComponentLookup<SpawnerLayerMaskOverride>();
        __entityType = state.GetEntityTypeHandle();
        __layerMaskType = state.GetComponentTypeHandle<SpawnerLayerMask>(true);
        __layerMaskOverrideType = state.GetComponentTypeHandle<SpawnerLayerMaskOverride>(true);
        __layerMaskIncludeType = state.GetComponentTypeHandle<SpawnerLayerMaskInclude>(true);
        __layerMaskExcludeType = state.GetComponentTypeHandle<SpawnerLayerMaskExclude>(true);
        __triggerType = state.GetComponentTypeHandle<SpawnerTrigger>(true);
        __instanceType = state.GetComponentTypeHandle<SpawnerDefinitionData>(true);
        __prefabType = state.GetBufferTypeHandle<SpawnerPrefab>(true);
        __statusType = state.GetBufferTypeHandle<SpawnerStatus>();

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
        state.RequireForUpdate<SpawnerLayerMask>();
        state.RequireForUpdate<ThirdPersonPlayer>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var jobHandle = state.Dependency;

        var layerMaskEntity = SystemAPI.GetSingletonEntity<SpawnerLayerMask>();
        __layerMasks.Update(ref state);

        Reset reset;
        reset.layerMaskEntity = layerMaskEntity;
        reset.layerMasks = __layerMasks;
        var resetJobHandle = reset.ScheduleByRef(jobHandle);

        __triggerType.Update(ref state);

        TriggerEx trigger;
        trigger.layerMaskEntity = layerMaskEntity;
        trigger.layerMasks = __layerMasks;
        trigger.triggerType = __triggerType;
        var triggerJobHandle = trigger.ScheduleParallelByRef(__groupToTrigger, resetJobHandle);

        __colliders.Update(ref state);
        __prefabLoadResults.Update(ref state);
        __localTransforms.Update(ref state);
        __layerMaskType.Update(ref state);
        __layerMaskOverrideType.Update(ref state);
        __layerMaskIncludeType.Update(ref state);
        __layerMaskExcludeType.Update(ref state);
        __entityType.Update(ref state);
        __instanceType.Update(ref state);
        __prefabType.Update(ref state);
        __statusType.Update(ref state);

        var spawnerSingleton = SystemAPI.GetSingleton<SpawnerSingleton>();
        
        CollectEx collect;
        collect.time = SystemAPI.Time.ElapsedTime;
        collect.playerEntity = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        collect.collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        collect.colliders = __colliders;
        collect.prefabLoadResults = __prefabLoadResults;
        collect.localTransforms = __localTransforms;
        collect.layerMaskType = __layerMaskType;
        collect.layerMaskOverrideType = __layerMaskOverrideType;
        collect.layerMaskIncludeType = __layerMaskIncludeType;
        collect.layerMaskExcludeType = __layerMaskExcludeType;
        collect.instanceCount = spawnerSingleton.instanceCount;
        collect.entities = spawnerSingleton.entities;
        collect.entityType = __entityType;
        collect.instanceType = __instanceType;
        collect.prefabType = __prefabType;
        collect.statusType = __statusType;
        collect.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        state.Dependency = collect.ScheduleParallelByRef(__groupToCollect, JobHandle.CombineDependencies(triggerJobHandle, jobHandle));
    }
}
