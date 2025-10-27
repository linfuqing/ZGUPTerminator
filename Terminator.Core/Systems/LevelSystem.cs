using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Random = Unity.Mathematics.Random;

[BurstCompile, /*UpdateBefore(typeof(SpawnerSystem)), UpdateAfter(typeof(SpawnerRecountSystem)), */UpdateBefore(typeof(TransformSystemGroup))]
public partial struct LevelSystem : ISystem
{
    private struct Update
    {
        public float deltaTime;
        
        public double time;

        [ReadOnly]
        public RefRO<SpawnerLayerMaskAndTagsOverride> spawnerLayerMaskAndTagsOverride;
        
        public RefRW<SpawnerLayerMaskAndTagsInclude> spawnerLayerMaskAndTagsInclude;

        public RefRW<SpawnerLayerMaskAndTagsExclude> spawnerLayerMaskAndTagsExclude;
        
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

        public BufferAccessor<LevelItem> items;

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
            bool isResultChanged = stageConditionStates.Length < 1, isEndOfStage = false;
            int conditionOffset = 0,
                conditionCount,
                numConditions,
                numConditionInheritances,
                stageConditionOffset,
                stageConditionOffsetTemp, 
                numNextStages,
                numResults,
                i, j, k, l, m;
            float chance, randomValue, spawnerTime = (float)(time - this.spawnerTime.ValueRO.value);
            float3 playerPosition = this.playerTransform.ValueRO.Position;
            SpawnerLayerMaskAndTagsInclude spawnerLayerMaskAndTagsInclude;
            SpawnerLayerMaskAndTagsExclude spawnerLayerMaskAndTagsExclude;
            ref var definition = ref instances[index].definition.Value;
            var status = states[index];
            var items = index < this.items.Length ? this.items[index] : default;
            var prefabs = index < this.prefabs.Length ? this.prefabs[index].AsNativeArray() : default;
            for(i = 0; i < numStages; ++i)
            {
                ref var stage = ref stages.ElementAt(i);
                if (stage.value < 0 || stage.value >= definition.stages.Length)
                    continue;

                chance = 0;
                randomValue = random.NextFloat();
                
                stageConditionOffset = conditionOffset;

                ref var stageDefinition = ref definition.stages[stage.value];
                numNextStages = stageDefinition.nextStages.Length;
                for (j = 0; j < numNextStages; ++j)
                {
                    ref var nextStage = ref stageDefinition.nextStages[j];
                    ref var nextStageDefinition = ref definition.stages[nextStage.index];
                    
                    numConditions = nextStageDefinition.conditions.Length;
                    if (numConditions > 0)
                    {
                        conditionCount = conditionOffset + numConditions;
                        if (stageConditionStates.Length < conditionCount)
                            stageConditionStates.Resize(conditionCount, NativeArrayOptions.ClearMemory);

                        for (k = 0; k < numConditions; ++k)
                        {
                            if (!nextStageDefinition.conditions[k].Judge(
                                    deltaTime,
                                    spawnerTime, 
                                    playerPosition,
                                    status,
                                    spawnerLayerMaskAndTagsOverride.ValueRO,
                                    spawnerSingleton,
                                    spawners, 
                                    items, 
                                    prefabs,
                                    spawnerPrefabs,
                                    spawnerStates, 
                                    spawnerDefinitions, 
                                    ref definition.layerMaskAndTags, 
                                    ref definition.areas,
                                    ref definition.items,
                                    ref stageConditionStates.ElementAt(conditionOffset + k)))
                                break;
                        }

                        conditionOffset = conditionCount;

                        if (k < numConditions)
                            continue;

                        /*for (k = 0; k < numConditions; ++k)
                            stageConditionStates.ElementAt(conditionOffset - k - 1) = default;*/
                    }

                    chance += nextStage.chance;

                    if(chance < randomValue)
                        continue;

                    for (k = j + 1; k < numNextStages; ++k)
                    {
                        ref var nextStageTemp = ref stageDefinition.nextStages[k];
                        numConditions = definition.stages[nextStageTemp.index].conditions.Length;
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

                    numConditions = stageConditionStates.Length;
                    
                    numResults = nextStageDefinition.nextStages.Length;
                    for (k = 0; k < numResults; ++k)
                    {
                        ref var nextAndNextStageDefinition = ref definition.stages[nextStageDefinition.nextStages[k].index];
                        conditionCount += nextAndNextStageDefinition.conditions.Length;
                        
                        numConditionInheritances = nextAndNextStageDefinition.conditionInheritances.Length;
                        for (l = 0; l < numConditionInheritances; ++l)
                        {
                            ref var conditionInheritance = ref nextAndNextStageDefinition.conditionInheritances[l];

                            stageConditionOffsetTemp = stageConditionOffset;
                            for (m = 0; m < numNextStages; ++m)
                            {
                                ref var stageDefinitionTemp = ref definition.stages[stageDefinition.nextStages[m].index];
                                if (conditionInheritance.stageName == stageDefinitionTemp.name)
                                    stageConditionStates.Add(
                                        stageConditionStates[conditionInheritance.previousConditionIndex + stageConditionOffsetTemp]);

                                stageConditionOffsetTemp += stageDefinitionTemp.conditions.Length;
                            }
                        }
                    }

                    if (conditionCount < conditionOffset)
                    {
                        numResults = conditionOffset - conditionCount;

                        numConditions -= numResults;
                        
                        stageConditionStates.RemoveRange(conditionCount, numResults);

                        conditionOffset = conditionCount;
                    }
                    else if (conditionCount > conditionOffset)
                    {
                        numResults = conditionCount - conditionOffset;
                        
                        numConditions += numResults;

                        for (k = 0; k < numResults; ++k)
                            stageConditionStates.Insert(conditionOffset, default);

                        //conditionCount = conditionOffset;
                    }

                    for (k = stageConditionOffset; k < conditionOffset; ++k)
                        stageConditionStates.ElementAt(k) = default;
                    
                    stageConditionOffsetTemp = stageConditionOffset;

                    conditionCount = numConditions;

                    numResults = nextStageDefinition.nextStages.Length;
                    for (k = 0; k < numResults; ++k)
                    {
                        ref var nextAndNextStageDefinition = ref definition.stages[nextStageDefinition.nextStages[k].index];
                        
                        numConditionInheritances = nextAndNextStageDefinition.conditionInheritances.Length;
                        for (l = 0; l < numConditionInheritances; ++l)
                        {
                            ref var conditionInheritance = ref nextAndNextStageDefinition.conditionInheritances[l];

                            for (m = 0; m < numNextStages; ++m)
                            {
                                ref var stageDefinitionTemp = ref definition.stages[stageDefinition.nextStages[m].index];
                                if (conditionInheritance.stageName != stageDefinitionTemp.name)
                                    continue;

                                ref var stageConditionState =
                                    ref stageConditionStates.ElementAt(stageConditionOffsetTemp +
                                                                       conditionInheritance.currentConditionIndex);
                                
                                stageConditionState = stageConditionStates[numConditions++];

                                stageConditionState.value = (int)math.round(stageConditionState.value * conditionInheritance.scale);
                            }
                        }
                        
                        stageConditionOffsetTemp += nextAndNextStageDefinition.conditions.Length;
                    }
                    
                    stageConditionStates.Resize(conditionCount, NativeArrayOptions.UninitializedMemory);
                    
                    stage.value = nextStage.index;

                    if (numResults < 1 && i == definition.mainStageIndex)
                        isEndOfStage = true;

                    break;
                }
                
                if(j == numNextStages)
                    continue;

                numResults = stageDefinition.results.Length;
                if (numResults > 0)
                {
                    //ref var stageResultStatus = ref stageResultStates.ElementAt(i);
                    spawnerLayerMaskAndTagsInclude.value = default;//stageResultStatus.layerMaskInclude;
                    spawnerLayerMaskAndTagsExclude.value = default;//stageResultStatus.layerMaskExclude;

                    for (j = 0; j < numResults; ++j)
                    {
                        stageDefinition.results[j].Apply(
                            time, 
                            ref spawnerStates, 
                            ref entityManager,
                            ref definition.layerMaskAndTags,
                            ref definition.areas,
                            ref definition.items,
                            ref items, 
                            ref playerPosition,
                            ref random,
                            ref status,
                            ref this.spawnerTime.ValueRW, 
                            ref spawnerLayerMaskAndTagsInclude,
                            ref spawnerLayerMaskAndTagsExclude,
                            spawnerSingleton,
                            spawners, 
                            prefabs,
                            spawnerPrefabs,
                            spawnerDefinitions);
                    }
                    
                    ref var stageResultStatus = ref stageResultStates.ElementAt(i);

                    stageResultStatus.layerMaskAndTagsInclude = spawnerLayerMaskAndTagsInclude.value;
                    stageResultStatus.layerMaskAndTagsExclude = spawnerLayerMaskAndTagsExclude.value;

                    isResultChanged = true;
                }
            }

            if (isResultChanged)
            {
                //this.spawnerTime.ValueRW.value = time - spawnerTime;
                
                spawnerLayerMaskAndTagsInclude.value = default;
                spawnerLayerMaskAndTagsExclude.value = default;
                for (i = 0; i < numStages; ++i)
                {
                    ref var stageResultStatus = ref stageResultStates.ElementAt(i);
                    
                    spawnerLayerMaskAndTagsInclude.value |= stageResultStatus.layerMaskAndTagsInclude;
                    spawnerLayerMaskAndTagsExclude.value |= stageResultStatus.layerMaskAndTagsExclude;
                }
                
                this.spawnerLayerMaskAndTagsInclude.ValueRW = spawnerLayerMaskAndTagsInclude;
                this.spawnerLayerMaskAndTagsExclude.ValueRW = spawnerLayerMaskAndTagsExclude;

                playerTransform.ValueRW.Position = playerPosition;
            }

            states[index] = status;

            return isEndOfStage;
        }
    }

    [BurstCompile]
    private struct UpdateEx : IJobChunk
    {
        public float deltaTime;
        public double time;
        
        public Entity spawnerLayerMaskAndTagsEntity;
        
        public Entity playerEntity;
        
        public LevelSpawners.ReadOnly spawners;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTarget> effectTargets;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerLayerMaskAndTagsInclude> spawnerLayerMaskAndTagsIncludes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerLayerMaskAndTagsExclude> spawnerLayerMaskAndTagsExcludes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SpawnerTime> spawnerTimes;

        [ReadOnly]
        public ComponentLookup<SpawnerLayerMaskAndTagsOverride> spawnerLayerMaskAndTagsOverrides;

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

        public BufferTypeHandle<LevelItem> itemType;

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
                !spawnerLayerMaskAndTagsIncludes.HasComponent(spawnerLayerMaskAndTagsEntity) ||
                !spawnerLayerMaskAndTagsExcludes.HasComponent(spawnerLayerMaskAndTagsEntity) || 
                !spawnerLayerMaskAndTagsOverrides.HasComponent(spawnerLayerMaskAndTagsEntity) ||
                !spawnerTimes.HasComponent(spawnerLayerMaskAndTagsEntity))
                return;

            if (effectTargets[playerEntity].hp < 1)
                return;
            
            long hash = math.aslong(time);

            Update update;
            update.deltaTime = deltaTime;
            update.time = time;
            update.spawnerLayerMaskAndTagsOverride = spawnerLayerMaskAndTagsOverrides.GetRefRO(spawnerLayerMaskAndTagsEntity);
            update.spawnerLayerMaskAndTagsInclude = spawnerLayerMaskAndTagsIncludes.GetRefRW(spawnerLayerMaskAndTagsEntity);
            update.spawnerLayerMaskAndTagsExclude = spawnerLayerMaskAndTagsExcludes.GetRefRW(spawnerLayerMaskAndTagsEntity);
            update.spawnerTime = spawnerTimes.GetRefRW(spawnerLayerMaskAndTagsEntity);
            update.playerTransform = localTransforms.GetRefRW(playerEntity);
            update.random = Random.CreateFromIndex((uint)(unfilteredChunkIndex ^ (int)hash ^ (int)(hash >> 32)));
            update.spawners = spawners;
            update.spawnerDefinitions = spawnerDefinitions;
            update.spawnerPrefabs = spawnerPrefabs;
            update.spawnerSingleton = spawnerSingleton;
            update.prefabs = chunk.GetBufferAccessor(ref prefabType);
            update.instances = chunk.GetNativeArray(ref instanceType);
            update.states = chunk.GetNativeArray(ref statusType);
            update.items = chunk.GetBufferAccessor(ref itemType);
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
                playerEffectTarget.Update(time, deltaTime);
                playerEffectTarget.invincibleTime = math.max(playerEffectTarget.invincibleTime, float.MaxValue);
            }
        }

    }

    private struct SendMessages
    {
        [ReadOnly] 
        public BufferAccessor<LevelItem> items;
        
        [ReadOnly] 
        public BufferAccessor<LevelItemMessage> inputs;
        
        public BufferAccessor<Message> outputs;
        public BufferAccessor<MessageParameter> parameters;

        public void Execute(int index, ref Random random)
        {
            MessageParameter parameter;
            Message message;
            var outputs = this.outputs[index];
            var parameters = this.parameters[index];
            var items = this.items[index];
            foreach (var input in inputs[index])
            {
                foreach (var item in items)
                {
                    if (item.name == input.itemName)
                    {
                        message.key = random.NextInt();
                        message.name = input.messageName;
                        message.value = input.messageValue;
                        outputs.Add(message);
                        
                        parameter.messageKey = message.key;
                        parameter.id = input.id;
                        parameter.value = item.count;
                        parameters.Add(parameter);
                        
                        break;
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct SendMessagesEx : IJobChunk
    {
        public uint hash;
        
        [ReadOnly] 
        public BufferTypeHandle<LevelItem> itemType;
        
        [ReadOnly] 
        public BufferTypeHandle<LevelItemMessage> inputType;
        
        public BufferTypeHandle<Message> outputType;
        public BufferTypeHandle<MessageParameter> parameterType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            var random = new Random(hash ^ (uint)unfilteredChunkIndex);
            
            SendMessages sendMessages;
            sendMessages.items = chunk.GetBufferAccessor(ref itemType);
            sendMessages.inputs = chunk.GetBufferAccessor(ref inputType);
            sendMessages.outputs = chunk.GetBufferAccessor(ref outputType);
            sendMessages.parameters = chunk.GetBufferAccessor(ref parameterType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                sendMessages.Execute(i, ref random);
        }
    }

    private uint __itemVersion;

    private ComponentLookup<LocalTransform> __localTransforms;
    private ComponentLookup<EffectTarget> __effectTargets;

    private ComponentLookup<SpawnerDefinitionData> __spawnerDefinitions;

    private ComponentLookup<SpawnerLayerMaskAndTagsOverride> __spawnerLayerMaskAndTagsOverrides;
    private ComponentLookup<SpawnerLayerMaskAndTagsInclude> __spawnerLayerMaskAndTagsIncludes;
    private ComponentLookup<SpawnerLayerMaskAndTagsExclude> __spawnerLayerMaskAndTagsExcludes;

    private ComponentLookup<SpawnerTime> __spawnerTimes;

    private BufferLookup<SpawnerStatus> __spawnerStates;

    private BufferLookup<SpawnerPrefab> __spawnerPrefabs;

    private BufferTypeHandle<LevelPrefab> __prefabType;

    private ComponentTypeHandle<LevelDefinitionData> __instanceType;
    private ComponentTypeHandle<LevelStatus> __statusType;
    
    private BufferTypeHandle<LevelItem> __itemType;

    private BufferTypeHandle<LevelStage> __stageType;

    private BufferTypeHandle<LevelStageConditionStatus> __stageConditionStatusType;

    private BufferTypeHandle<LevelStageResultStatus> __stageResultStatusType;
    
    public BufferTypeHandle<MessageParameter> __messageParameterType;
    
    public BufferTypeHandle<Message> __outputMessageType;

    public BufferTypeHandle<LevelItemMessage> __inputMessageType;
        
    private EntityQuery __group;
    private EntityQuery __itemMessageGroup;

    private LevelSpawners __spawners;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localTransforms = state.GetComponentLookup<LocalTransform>();
        __effectTargets = state.GetComponentLookup<EffectTarget>();
        __spawnerDefinitions = state.GetComponentLookup<SpawnerDefinitionData>(true);
        __spawnerLayerMaskAndTagsOverrides = state.GetComponentLookup<SpawnerLayerMaskAndTagsOverride>(true);
        __spawnerLayerMaskAndTagsIncludes = state.GetComponentLookup<SpawnerLayerMaskAndTagsInclude>();
        __spawnerLayerMaskAndTagsExcludes = state.GetComponentLookup<SpawnerLayerMaskAndTagsExclude>();
        __spawnerTimes = state.GetComponentLookup<SpawnerTime>();
        __spawnerStates = state.GetBufferLookup<SpawnerStatus>();
        __spawnerPrefabs = state.GetBufferLookup<SpawnerPrefab>(true);
        __prefabType = state.GetBufferTypeHandle<LevelPrefab>(true);
        __instanceType = state.GetComponentTypeHandle<LevelDefinitionData>(true);
        __statusType = state.GetComponentTypeHandle<LevelStatus>();
        __itemType = state.GetBufferTypeHandle<LevelItem>();
        __stageType = state.GetBufferTypeHandle<LevelStage>();
        __stageConditionStatusType = state.GetBufferTypeHandle<LevelStageConditionStatus>();
        __stageResultStatusType = state.GetBufferTypeHandle<LevelStageResultStatus>();
        __messageParameterType = state.GetBufferTypeHandle<MessageParameter>();
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __inputMessageType = state.GetBufferTypeHandle<LevelItemMessage>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LevelDefinitionData, LevelStatus>()
                .WithAllRW<LevelStage>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __itemMessageGroup = builder
                .WithAll<LevelItemMessage, Message, MessageParameter>()
                .Build(ref state);
        
        state.RequireForUpdate<SpawnerLayerMaskAndTags>();
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
        __spawnerLayerMaskAndTagsOverrides.Update(ref state);
        __spawnerLayerMaskAndTagsIncludes.Update(ref state);
        __spawnerLayerMaskAndTagsExcludes.Update(ref state);
        __spawnerTimes.Update(ref state);
        __spawnerPrefabs.Update(ref state);
        __prefabType.Update(ref state);
        __instanceType.Update(ref state);
        __statusType.Update(ref state); 
        __itemType.Update(ref state);
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
        update.spawnerLayerMaskAndTagsEntity = SystemAPI.GetSingletonEntity<SpawnerLayerMaskAndTags>();
        update.spawnerLayerMaskAndTagsOverrides = __spawnerLayerMaskAndTagsOverrides;
        update.spawnerLayerMaskAndTagsIncludes = __spawnerLayerMaskAndTagsIncludes;
        update.spawnerLayerMaskAndTagsExcludes = __spawnerLayerMaskAndTagsExcludes;
        update.spawnerTimes = __spawnerTimes;
        update.spawnerPrefabs = __spawnerPrefabs;
        update.spawnerSingleton = SystemAPI.GetSingleton<SpawnerSingleton>();
        update.spawnerDefinitions = __spawnerDefinitions;
        update.prefabType = __prefabType;
        update.instanceType = __instanceType;
        update.statusType = __statusType;
        update.itemType = __itemType;
        update.stageType = __stageType;
        update.stageConditionStatusType = __stageConditionStatusType;
        update.stageResultStatusType = __stageResultStatusType;
        update.spawnerStates = __spawnerStates;
        update.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        jobHandle = update.ScheduleParallelByRef(__group, jobHandle);

        uint itemVersion = (uint)state.EntityManager.GetComponentOrderVersion<LevelItem>();
        if(ChangeVersionUtility.DidChange(itemVersion, __itemVersion))
        {
            __itemVersion = itemVersion;
            
            __messageParameterType.Update(ref state);
            __outputMessageType.Update(ref state);
            __inputMessageType.Update(ref state);

            var hash = math.aslong(time.ElapsedTime);
            SendMessagesEx sendMessages;
            sendMessages.hash = (uint)hash ^ (uint)(hash >> 32);
            sendMessages.itemType = __itemType;
            sendMessages.inputType = __inputMessageType;
            sendMessages.outputType = __outputMessageType;
            sendMessages.parameterType = __messageParameterType;
            jobHandle = sendMessages.ScheduleParallelByRef(__itemMessageGroup, jobHandle);
        }

        state.Dependency = jobHandle;
    }
}
