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
        
        public LevelSpawners.ReadOnly spawners;

        [ReadOnly]
        public ComponentLookup<SpawnerDefinitionData> spawnerDefinitions;

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

        [NativeDisableParallelForRestriction]
        public BufferLookup<SpawnerStatus> spawnerStates;

        public EntityCommandBuffer.ParallelWriter entityManager;
        
        public bool Execute(int index)
        {
            var stages = this.stages[index];
            int numStages = stages.Length;
            var stageResultStates = this.stageResultStates[index];
            stageResultStates.Resize(numStages, NativeArrayOptions.ClearMemory);

            var stageConditionStates = this.stageConditionStates[index];
            bool isResultChanged = stageConditionStates.Length < 1;
            int conditionOffset = 0,
                conditionCount,
                numConditions,
                numConditionInheritances,
                stageConditionOffset,
                numNextStageIndices,
                numResults,
                i, j, k, l;
            float spawnerTime = (float)(time - this.spawnerTime.ValueRO.value);
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

                stageConditionOffset = conditionOffset;

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
                                    spawners, 
                                    prefabs,
                                    spawnerPrefabs,
                                    spawnerStates, 
                                    spawnerDefinitions, 
                                    ref definition.areas,
                                    ref stageConditionStates.ElementAt(conditionOffset + k)))
                                break;
                        }

                        conditionOffset = conditionCount;

                        if (k < numConditions)
                            continue;

                        /*for (k = 0; k < numConditions; ++k)
                            stageConditionStates.ElementAt(conditionOffset - k - 1) = default;*/
                    }
                    
                    for (k = j + 1; k < numNextStageIndices; ++k)
                    {
                        ref var nextStageIndexTemp = ref stageDefinition.nextStageIndies[k];
                        numConditions = definition.stages[nextStageIndexTemp].conditions.Length;
                        if (numConditions > 0)
                        {
                            conditionOffset += numConditions;
                            if (conditionOffset > stageConditionStates.Length)
                            {
                                conditionOffset = stageConditionStates.Length;
                                
                                break;
                            }
                        }
                    }

                    conditionCount = stageConditionOffset;

                    ref var stageDefinitionTemp = ref definition.stages[nextStageIndex];
                    numResults = stageDefinitionTemp.nextStageIndies.Length;
                    for (k = 0; k < numResults; ++k)
                    {
                        ref var nextStage = ref definition.stages[stageDefinitionTemp.nextStageIndies[k]];
                        conditionCount += nextStage.conditions.Length;
                        
                        numConditionInheritances = nextStage.conditionInheritances.Length;
                        for (l = 0; l < numConditionInheritances; ++l)
                        {
                            ref var conditionInheritance = ref nextStage.conditionInheritances[l];

                            if (conditionInheritance.stageName != stageDefinitionTemp.name)
                                continue;

                            stageConditionStates.Add(stageConditionStates[conditionInheritance.previousConditionIndex]);
                        }
                    }

                    if (conditionCount < conditionOffset)
                        stageConditionStates.RemoveRange(conditionCount, conditionOffset - conditionCount);
                    else if (conditionCount > conditionOffset)
                    {
                        numResults = conditionCount - conditionOffset;
                        for (k = 0; k < numResults; ++k)
                            stageConditionStates.Insert(conditionOffset, default);

                        conditionCount = conditionOffset;
                    }

                    for (k = stageConditionOffset; k < conditionCount; ++k)
                        stageConditionStates.ElementAt(k) = default;
                    
                    numResults = stageDefinitionTemp.nextStageIndies.Length;
                    for (k = 0; k < numResults; ++k)
                    {
                        ref var nextStage = ref definition.stages[stageDefinitionTemp.nextStageIndies[k]];
                        conditionCount += nextStage.conditions.Length;
                        
                        numConditionInheritances = nextStage.conditionInheritances.Length;
                        for (l = numConditionInheritances - 1; l >= 0; --l)
                        {
                            ref var conditionInheritance = ref nextStage.conditionInheritances[l];

                            if (conditionInheritance.stageName != stageDefinitionTemp.name)
                                continue;

                            numConditions = stageConditionStates.Length - 1;
                            stageConditionStates[conditionInheritance.currentConditionIndex] =
                                stageConditionStates[numConditions];
                            
                            stageConditionStates.Resize(numConditions, NativeArrayOptions.UninitializedMemory);
                        }
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
                            time, 
                            ref spawnerStates, 
                            ref entityManager,
                            ref definition.areas,
                            ref playerPosition,
                            ref random,
                            ref status,
                            ref this.spawnerTime.ValueRW, 
                            ref spawnerLayerMaskInclude,
                            ref spawnerLayerMaskExclude,
                            spawnerSingleton,
                            spawners, 
                            prefabs,
                            spawnerPrefabs,
                            spawnerDefinitions);
                    }
                    
                    ref var stageResultStatus = ref stageResultStates.ElementAt(i);

                    stageResultStatus.layerMaskInclude = spawnerLayerMaskInclude.value;
                    stageResultStatus.layerMaskExclude = spawnerLayerMaskExclude.value;

                    isResultChanged = true;
                }
            }

            if (isResultChanged)
            {
                //this.spawnerTime.ValueRW.value = time - spawnerTime;
                
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

            return isResultChanged;
        }
    }

    [BurstCompile]
    private struct UpdateEx : IJobChunk
    {
        public float deltaTime;
        public double time;
        
        public Entity spawnerLayerMaskEntity;
        
        public Entity playerEntity;
        
        public LevelSpawners.ReadOnly spawners;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTarget> effectTargets;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerLayerMaskInclude> spawnerLayerMaskIncludes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerLayerMaskExclude> spawnerLayerMaskExcludes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerTime> spawnerTimes;

        [ReadOnly]
        public ComponentLookup<SpawnerLayerMaskOverride> spawnerLayerMaskOverrides;

        [ReadOnly]
        public ComponentLookup<SpawnerDefinitionData> spawnerDefinitions;

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

        [NativeDisableParallelForRestriction]
        public BufferLookup<SpawnerStatus> spawnerStates;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!localTransforms.HasComponent(playerEntity) || 
                !effectTargets.HasComponent(playerEntity) || 
                !spawnerLayerMaskIncludes.HasComponent(spawnerLayerMaskEntity) ||
                !spawnerLayerMaskExcludes.HasComponent(spawnerLayerMaskEntity) || 
                !spawnerLayerMaskOverrides.HasComponent(spawnerLayerMaskEntity) ||
                !spawnerTimes.HasComponent(spawnerLayerMaskEntity))
                return;

            if (effectTargets[playerEntity].hp < 1)
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
            update.spawnerDefinitions = spawnerDefinitions;
            update.spawnerPrefabs = spawnerPrefabs;
            update.spawnerSingleton = spawnerSingleton;
            update.prefabs = chunk.GetBufferAccessor(ref prefabType);
            update.instances = chunk.GetNativeArray(ref instanceType);
            update.states = chunk.GetNativeArray(ref statusType);
            update.stages = chunk.GetBufferAccessor(ref stageType);
            update.stageConditionStates = chunk.GetBufferAccessor(ref stageConditionStatusType);
            update.stageResultStates = chunk.GetBufferAccessor(ref stageResultStatusType);
            update.spawnerStates = spawnerStates;
            update.entityManager = entityManager;

            bool result = false;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                result = update.Execute(i) || result;

            if (result)
            {
                ref var playerEffectTarget = ref effectTargets.GetRefRW(playerEntity).ValueRW;
                playerEffectTarget.invincibleTime = math.max(playerEffectTarget.invincibleTime, deltaTime);
            }
        }

    }

    private ComponentLookup<LocalTransform> __localTransforms;
    private ComponentLookup<EffectTarget> __effectTargets;

    private ComponentLookup<SpawnerDefinitionData> __spawnerDefinitions;

    private ComponentLookup<SpawnerLayerMaskOverride> __spawnerLayerMaskOverrides;
    private ComponentLookup<SpawnerLayerMaskInclude> __spawnerLayerMaskIncludes;
    private ComponentLookup<SpawnerLayerMaskExclude> __spawnerLayerMaskExcludes;

    private ComponentLookup<SpawnerTime> __spawnerTimes;

    private BufferLookup<SpawnerStatus> __spawnerStates;

    private BufferLookup<SpawnerPrefab> __spawnerPrefabs;

    private BufferTypeHandle<LevelPrefab> __prefabType;

    private ComponentTypeHandle<LevelDefinitionData> __instanceType;
    private ComponentTypeHandle<LevelStatus> __statusType;
    
    private BufferTypeHandle<LevelStage> __stageType;

    private BufferTypeHandle<LevelStageConditionStatus> __stageConditionStatusType;

    private BufferTypeHandle<LevelStageResultStatus> __stageResultStatusType;
    
    private EntityQuery __group;

    private LevelSpawners __spawners;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localTransforms = state.GetComponentLookup<LocalTransform>();
        __effectTargets = state.GetComponentLookup<EffectTarget>();
        __spawnerDefinitions = state.GetComponentLookup<SpawnerDefinitionData>(true);
        __spawnerLayerMaskOverrides = state.GetComponentLookup<SpawnerLayerMaskOverride>(true);
        __spawnerLayerMaskIncludes = state.GetComponentLookup<SpawnerLayerMaskInclude>();
        __spawnerLayerMaskExcludes = state.GetComponentLookup<SpawnerLayerMaskExclude>();
        __spawnerTimes = state.GetComponentLookup<SpawnerTime>();
        __spawnerStates = state.GetBufferLookup<SpawnerStatus>();
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

        __spawners = new LevelSpawners(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __spawners.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __localTransforms.Update(ref state);
        __effectTargets.Update(ref state);
        __spawnerDefinitions.Update(ref state);
        __spawnerLayerMaskOverrides.Update(ref state);
        __spawnerLayerMaskIncludes.Update(ref state);
        __spawnerLayerMaskExcludes.Update(ref state);
        __spawnerTimes.Update(ref state);
        __spawnerPrefabs.Update(ref state);
        __prefabType.Update(ref state);
        __instanceType.Update(ref state);
        __statusType.Update(ref state); 
        __stageType.Update(ref state);
        __stageConditionStatusType.Update(ref state);
        __stageResultStatusType.Update(ref state);
        __spawnerStates.Update(ref state);

        ref readonly var time = ref SystemAPI.Time;

        var jobHandle = state.Dependency;
        
        UpdateEx update;
        update.deltaTime = time.DeltaTime;
        update.time = time.ElapsedTime;
        update.playerEntity = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        update.spawners = __spawners.AsReadOnly(ref state, ref jobHandle);
        update.effectTargets = __effectTargets;
        update.localTransforms = __localTransforms;
        update.spawnerLayerMaskEntity = SystemAPI.GetSingletonEntity<SpawnerLayerMask>();
        update.spawnerLayerMaskOverrides = __spawnerLayerMaskOverrides;
        update.spawnerLayerMaskIncludes = __spawnerLayerMaskIncludes;
        update.spawnerLayerMaskExcludes = __spawnerLayerMaskExcludes;
        update.spawnerTimes = __spawnerTimes;
        update.spawnerPrefabs = __spawnerPrefabs;
        update.spawnerSingleton = SystemAPI.GetSingleton<SpawnerSingleton>();
        update.spawnerDefinitions = __spawnerDefinitions;
        update.prefabType = __prefabType;
        update.instanceType = __instanceType;
        update.statusType = __statusType;
        update.stageType = __stageType;
        update.stageConditionStatusType = __stageConditionStatusType;
        update.stageResultStatusType = __stageResultStatusType;
        update.spawnerStates = __spawnerStates;
        update.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        state.Dependency = update.ScheduleParallelByRef(__group, jobHandle);
    }
}
