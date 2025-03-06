using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

[BurstCompile, /*UpdateBefore(typeof(SpawnerSystem)), UpdateAfter(typeof(SpawnerRecountSystem)), */UpdateBefore(typeof(TransformSystemGroup))]
public partial struct LevelSystem : ISystem
{
    private struct Update
    {
        public float deltaTime;
        
        public double time;

        [ReadOnly]
        public RefRO<SpawnerLayerMaskOverride> spawnerLayerMaskOverride;
        
        public RefRW<SpawnerLayerMaskInclude> spawnerLayerMaskInclude;

        public RefRW<SpawnerLayerMaskExclude> spawnerLayerMaskExclude;
        
        public RefRW<SpawnerTime> spawnerTime;

        public RefRW<LocalTransform> playerTransform;

        public Random random;

        [ReadOnly]
        public ComponentLookup<SpawnerDefinitionData> spawners;

        [ReadOnly]
        public SpawnerSingleton spawnerSingleton;

        [ReadOnly]
        public BufferLookup<SpawnerPrefab> spawnerPrefabs;

        [ReadOnly]
        public BufferAccessor<LevelPrefab> prefabs;

        [ReadOnly]
        public NativeArray<LevelDefinitionData> instances;

        public NativeArray<LevelStatus> states;

        public BufferAccessor<LevelStage> stages;

        public BufferAccessor<LevelStageConditionStatus> stageConditionStates;
        public BufferAccessor<LevelStageResultStatus> stageResultStates;

        public EntityCommandBuffer.ParallelWriter entityManager;
        
        public void Execute(int index)
        {
            var stages = this.stages[index];
            int numStages = stages.Length;
            var stageResultStates = this.stageResultStates[index];
            stageResultStates.Resize(numStages, NativeArrayOptions.ClearMemory);

            var stageConditionStates = this.stageConditionStates[index];
            bool isResultChanged = stageConditionStates.Length < 1;
            int conditionOffset = 0, conditionCount, numConditions, numNextStageIndices, numResults, i, j, k;
            float spawnerTime = (float)(this.spawnerTime.ValueRO.value - time);
            float3 playerPosition = this.playerTransform.ValueRO.Position;
            SpawnerLayerMaskInclude spawnerLayerMaskInclude;
            SpawnerLayerMaskExclude spawnerLayerMaskExclude;
            ref var definition = ref instances[index].definition.Value;
            var status = states[index];
            var prefabs = this.prefabs[index].AsNativeArray();
            for(i = 0; i < numStages; ++i)
            {
                ref var stage = ref stages.ElementAt(i);
                if (stage.value < 0 || stage.value >= definition.stages.Length)
                    continue;

                ref var stageDefinition = ref definition.stages[stage.value];
                numNextStageIndices = stageDefinition.nextStageIndies.Length;
                for (j = 0; j < numNextStageIndices; ++j)
                {
                    ref var nextStageIndex = ref stageDefinition.nextStageIndies[j];
                    ref var conditions = ref definition.stages[nextStageIndex].conditions;
                    
                    numConditions = conditions.Length;
                    if (numConditions > 0)
                    {
                        conditionCount = conditionOffset + numConditions;
                        if (stageConditionStates.Length < conditionCount)
                            stageConditionStates.Resize(conditionCount, NativeArrayOptions.ClearMemory);

                        for (k = 0; k < numConditions; ++k)
                        {
                            if (!conditions[k].Judge(
                                    deltaTime,
                                    spawnerTime, 
                                    playerPosition,
                                    status,
                                    spawnerLayerMaskOverride.ValueRO,
                                    spawnerSingleton,
                                    prefabs,
                                    spawnerPrefabs,
                                    spawners, 
                                    ref definition.areas,
                                    ref stageConditionStates.ElementAt(conditionOffset + k)))
                                break;
                        }

                        conditionOffset = conditionCount;

                        if (k < numConditions)
                            continue;

                        for (k = 0; k < numConditions; ++k)
                            stageConditionStates.ElementAt(conditionOffset - k - 1) = default;
                    }
                    
                    stage.value = nextStageIndex;

                    break;
                }
                
                if(j == numNextStageIndices)
                    continue;

                numResults = stageDefinition.results.Length;
                if (numResults > 0)
                {
                    //ref var stageResultStatus = ref stageResultStates.ElementAt(i);
                    spawnerLayerMaskInclude.value = 0;//stageResultStatus.layerMaskInclude;
                    spawnerLayerMaskExclude.value = 0;//stageResultStatus.layerMaskExclude;

                    for (j = 0; j < numResults; ++j)
                    {
                        stageDefinition.results[j].Apply(
                            ref entityManager,
                            ref definition.areas,
                            ref playerPosition,
                            ref random,
                            ref status,
                            ref spawnerTime, 
                            ref spawnerLayerMaskInclude,
                            ref spawnerLayerMaskExclude,
                            spawnerSingleton,
                            prefabs,
                            spawnerPrefabs,
                            spawners);
                    }
                    
                    ref var stageResultStatus = ref stageResultStates.ElementAt(i);
                    stageResultStatus.layerMaskInclude = spawnerLayerMaskInclude.value;
                    stageResultStatus.layerMaskExclude = spawnerLayerMaskExclude.value;

                    isResultChanged = true;
                }
            }

            if (isResultChanged)
            {
                this.spawnerTime.ValueRW.value = spawnerTime + time;
                
                spawnerLayerMaskInclude.value = 0;
                spawnerLayerMaskExclude.value = 0;
                for (i = 0; i < numStages; ++i)
                {
                    ref var stageResultStatus = ref stageResultStates.ElementAt(i);
                    
                    spawnerLayerMaskInclude.value |= stageResultStatus.layerMaskInclude;
                    spawnerLayerMaskExclude.value |= stageResultStatus.layerMaskExclude;
                }
                
                this.spawnerLayerMaskInclude.ValueRW = spawnerLayerMaskInclude;
                this.spawnerLayerMaskExclude.ValueRW = spawnerLayerMaskExclude;

                playerTransform.ValueRW.Position = playerPosition;
            }

            states[index] = status;
        }
    }

    [BurstCompile]
    private struct UpdateEx : IJobChunk
    {
        public float deltaTime;
        public double time;
        
        public Entity spawnerLayerMaskEntity;
        
        public Entity playerEntity;
        
        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerTime> spawnerTimes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerLayerMaskInclude> spawnerLayerMaskIncludes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerLayerMaskExclude> spawnerLayerMaskExcludes;

        [ReadOnly]
        public ComponentLookup<SpawnerLayerMaskOverride> spawnerLayerMaskOverrides;

        [ReadOnly]
        public ComponentLookup<SpawnerDefinitionData> spawners;

        [ReadOnly]
        public BufferLookup<SpawnerPrefab> spawnerPrefabs;

        [ReadOnly]
        public SpawnerSingleton spawnerSingleton;

        [ReadOnly]
        public BufferTypeHandle<LevelPrefab> prefabType;

        [ReadOnly]
        public ComponentTypeHandle<LevelDefinitionData> instanceType;

        public ComponentTypeHandle<LevelStatus> statusType;

        public BufferTypeHandle<LevelStage> stageType;

        public BufferTypeHandle<LevelStageConditionStatus> stageConditionStatusType;
        public BufferTypeHandle<LevelStageResultStatus> stageResultStatusType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!localTransforms.HasComponent(playerEntity) || 
                !spawnerTimes.HasComponent(spawnerLayerMaskEntity) || 
                !spawnerLayerMaskIncludes.HasComponent(spawnerLayerMaskEntity) ||
                !spawnerLayerMaskExcludes.HasComponent(spawnerLayerMaskEntity) || 
                !spawnerLayerMaskOverrides.HasComponent(spawnerLayerMaskEntity))
                return;
            
            long hash = math.aslong(time);

            Update update;
            update.deltaTime = deltaTime;
            update.time = time;
            update.spawnerLayerMaskOverride = spawnerLayerMaskOverrides.GetRefRO(spawnerLayerMaskEntity);
            update.spawnerLayerMaskInclude = spawnerLayerMaskIncludes.GetRefRW(spawnerLayerMaskEntity);
            update.spawnerLayerMaskExclude = spawnerLayerMaskExcludes.GetRefRW(spawnerLayerMaskEntity);
            update.spawnerTime = spawnerTimes.GetRefRW(spawnerLayerMaskEntity);
            update.playerTransform = localTransforms.GetRefRW(playerEntity);
            update.random = Random.CreateFromIndex((uint)(unfilteredChunkIndex ^ (int)hash ^ (int)(hash >> 32)));
            update.spawners = spawners;
            update.spawnerPrefabs = spawnerPrefabs;
            update.spawnerSingleton = spawnerSingleton;
            update.prefabs = chunk.GetBufferAccessor(ref prefabType);
            update.instances = chunk.GetNativeArray(ref instanceType);
            update.states = chunk.GetNativeArray(ref statusType);
            update.stages = chunk.GetBufferAccessor(ref stageType);
            update.stageConditionStates = chunk.GetBufferAccessor(ref stageConditionStatusType);
            update.stageResultStates = chunk.GetBufferAccessor(ref stageResultStatusType);
            update.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                update.Execute(i);
        }

    }

    private ComponentLookup<LocalTransform> __localTransforms;

    private ComponentLookup<SpawnerLayerMaskOverride> __spawnerLayerMaskOverrides;
    private ComponentLookup<SpawnerLayerMaskInclude> __spawnerLayerMaskIncludes;
    private ComponentLookup<SpawnerLayerMaskExclude> __spawnerLayerMaskExcludes;

    private ComponentLookup<SpawnerTime> __spawnerTimes;

    private ComponentLookup<SpawnerDefinitionData> __spawners;

    private BufferLookup<SpawnerPrefab> __spawnerPrefabs;

    private BufferTypeHandle<LevelPrefab> __prefabType;

    private ComponentTypeHandle<LevelDefinitionData> __instanceType;
    private ComponentTypeHandle<LevelStatus> __statusType;
    
    private BufferTypeHandle<LevelStage> __stageType;

    private BufferTypeHandle<LevelStageConditionStatus> __stageConditionStatusType;

    private BufferTypeHandle<LevelStageResultStatus> __stageResultStatusType;
    
    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localTransforms = state.GetComponentLookup<LocalTransform>();
        __spawnerLayerMaskOverrides = state.GetComponentLookup<SpawnerLayerMaskOverride>(true);
        __spawnerLayerMaskIncludes = state.GetComponentLookup<SpawnerLayerMaskInclude>();
        __spawnerLayerMaskExcludes = state.GetComponentLookup<SpawnerLayerMaskExclude>();
        __spawnerTimes = state.GetComponentLookup<SpawnerTime>();
        __spawners = state.GetComponentLookup<SpawnerDefinitionData>(true);
        __spawnerPrefabs = state.GetBufferLookup<SpawnerPrefab>(true);
        __prefabType = state.GetBufferTypeHandle<LevelPrefab>(true);
        __instanceType = state.GetComponentTypeHandle<LevelDefinitionData>(true);
        __statusType = state.GetComponentTypeHandle<LevelStatus>();
        __stageType = state.GetBufferTypeHandle<LevelStage>();
        __stageConditionStatusType = state.GetBufferTypeHandle<LevelStageConditionStatus>();
        __stageResultStatusType = state.GetBufferTypeHandle<LevelStageResultStatus>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LevelDefinitionData, LevelStatus>()
                .WithAllRW<LevelStage>()
                .Build(ref state);
        
        state.RequireForUpdate<SpawnerLayerMask>();
        state.RequireForUpdate<SpawnerSingleton>();
        state.RequireForUpdate<ThirdPersonPlayer>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __localTransforms.Update(ref state);
        __spawnerLayerMaskOverrides.Update(ref state);
        __spawnerLayerMaskIncludes.Update(ref state);
        __spawnerLayerMaskExcludes.Update(ref state);
        __spawnerTimes.Update(ref state);
        __spawners.Update(ref state);
        __spawnerPrefabs.Update(ref state);
        __prefabType.Update(ref state);
        __instanceType.Update(ref state);
        __statusType.Update(ref state); 
        __stageType.Update(ref state);
        __stageConditionStatusType.Update(ref state);
        __stageResultStatusType.Update(ref state);

        ref readonly var time = ref SystemAPI.Time;
        
        UpdateEx update;
        update.deltaTime = time.DeltaTime;
        update.time = time.ElapsedTime;
        update.playerEntity = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        update.localTransforms = __localTransforms;
        update.spawnerLayerMaskEntity = SystemAPI.GetSingletonEntity<SpawnerLayerMask>();
        update.spawnerLayerMaskOverrides = __spawnerLayerMaskOverrides;
        update.spawnerLayerMaskIncludes = __spawnerLayerMaskIncludes;
        update.spawnerLayerMaskExcludes = __spawnerLayerMaskExcludes;
        update.spawnerTimes = __spawnerTimes;
        update.spawnerPrefabs = __spawnerPrefabs;
        update.spawnerSingleton = SystemAPI.GetSingleton<SpawnerSingleton>();
        update.spawners = __spawners;
        update.prefabType = __prefabType;
        update.instanceType = __instanceType;
        update.statusType = __statusType;
        update.stageType = __stageType;
        update.stageConditionStatusType = __stageConditionStatusType;
        update.stageResultStatusType = __stageResultStatusType;
        update.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        state.Dependency = update.ScheduleParallelByRef(__group, state.Dependency);
    }
}
