using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Scenes;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public struct LevelSpawners
{
    [BurstCompile]
    private struct Resize : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;
        public NativeList<Entity> entities;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entityArray = chunk.GetNativeArray(entityType);
            if (useEnabledMask)
            {
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    entities.Add(entityArray[i]);
            }
            else
                entities.AddRange(entityArray);
        }
    }
    
    [NativeContainerIsReadOnly]
    public readonly struct ReadOnly
    {
        public readonly uint Version;
        [ReadOnly]
        public readonly NativeList<Entity> Entities;

        public ReadOnly(uint version, NativeList<Entity> entities)
        {
            Version = version;
            Entities = entities;
        }

        public bool IsDone(
            in LayerMaskAndTags layerMaskAndTags, 
            in ComponentLookup<SpawnerDefinitionData> definitions, 
            in BufferLookup<SpawnerStatus> states)
        {
            int i, numSpawners;
            SpawnerStatus status;
            DynamicBuffer<SpawnerStatus> statusBuffer = default;
            SpawnerDefinitionData definitionData;
            foreach (var entity in Entities)
            {
                if (!definitions.TryGetComponent(entity, out definitionData))
                    continue;
                
                ref var definition = ref definitionData.definition.Value;

                numSpawners = definition.spawners.Length;
                for (i = 0; i < numSpawners; ++i)
                {
                    ref var spawner = ref definition.spawners[i];
                    if(!spawner.layerMaskAndTags.Overlaps(layerMaskAndTags))
                        continue;

                    if (!statusBuffer.IsCreated && !states.TryGetBuffer(entity, out statusBuffer))
                        break;

                    if (statusBuffer.Length <= i)
                        return false;

                    status = statusBuffer[i];
                    if (status.count < spawner.countPerTime || status.times < spawner.times)
                        return false;
                }
            }

            return true;
        }

        public void Clear(
            in LayerMaskAndTags layerMaskAndTags, 
            in ComponentLookup<SpawnerDefinitionData> definitions, 
            ref BufferLookup<SpawnerStatus> states)
        {
            int i, numSpawners;
            DynamicBuffer<SpawnerStatus> statusBuffer = default;
            SpawnerDefinitionData definitionData;
            foreach (var entity in Entities)
            {
                if (!definitions.TryGetComponent(entity, out definitionData))
                    continue;
                
                ref var definition = ref definitionData.definition.Value;

                numSpawners = definition.spawners.Length;
                for (i = 0; i < numSpawners; ++i)
                {
                    ref var spawner = ref definition.spawners[i];
                    if(!spawner.layerMaskAndTags.Overlaps(layerMaskAndTags))
                        continue;

                    if (!statusBuffer.IsCreated && !states.TryGetBuffer(entity, out statusBuffer))
                        break;

                    if (statusBuffer.Length <= i)
                        continue;

                    statusBuffer[i] = default;
                }
            }
        }
    }

    private uint __version;
    private EntityTypeHandle __entityType;
    private EntityQuery __group;
    private NativeList<Entity> __entities;

    public LevelSpawners(ref SystemState state)
    {
        __version = 0;
        __entityType = state.GetEntityTypeHandle();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SpawnerStatus>()
                .Build(ref state);

        __entities = new NativeList<Entity>(Allocator.Persistent);
    }
    
    public LevelSpawners(SystemBase system)
    {
        __version = 0;
        __entityType = system.GetEntityTypeHandle();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SpawnerStatus>()
                .Build(system);

        __entities = new NativeList<Entity>(Allocator.Persistent);
    }

    public void Dispose()
    {
        __entities.Dispose();
    }

    public ReadOnly AsReadOnly(ref SystemState state, ref JobHandle jobHandle)
    {
        uint version = (uint)__group.GetCombinedComponentOrderVersion(true);
        if (ChangeVersionUtility.DidChange(version, __version))
        {
            __version = version;
            
            __entityType.Update(ref state);
            
            __entities.Clear();

            Resize resize;
            resize.entityType = __entityType;
            resize.entities = __entities;
            jobHandle = resize.ScheduleByRef(__group, jobHandle);
        }

        return new ReadOnly(__version, __entities);
    }
    
    public ReadOnly AsReadOnly(SystemBase system, ref JobHandle jobHandle)
    {
        uint version = (uint)__group.GetCombinedComponentOrderVersion(true);
        if (ChangeVersionUtility.DidChange(version, __version))
        {
            __version = version;
            
            __entityType.Update(system);
            
            Resize resize;
            resize.entityType = __entityType;
            resize.entities = __entities;
            jobHandle = resize.ScheduleByRef(__group, jobHandle);
        }

        return new ReadOnly(__version, __entities);
    }
}

[Serializable]
public struct LevelStageOption
{
    private enum Status
    {
        None, 
        Start, 
        Finish
    }
    
    public enum Type
    {
        SpawnerTime = 11, 
        SpawnerLayerMask = 12,
        SpawnerLayerMaskInclude = 1,
        SpawnerLayerMaskExclude = 2,
        SpawnerEntityRemaining = 7, 
        PrefabRemaining = 3, 
        PlayerArea = 4, 
        Item = 13, 
        Millisecond = 5,
        Value = 0, 
        Max = 6, 
        ExpMax = 8, 
        Exp = 9, 
        Stage = 10
    }

    [Tooltip("SpawnerLayerMaskOverride:激活对应标签的刷怪圈。SpawnerEntityRemaining：对应刷怪圈标签的怪都被消灭。PrefabRemaining：对应预制体索引的怪都被消灭。Value：关卡进度")]
    public Type type;
    
    public int value;
    
#if ENABLE_PROFILER
    public static readonly ProfilerMarker JudgeProfilerMarker = new ProfilerMarker("Judge");
    public static readonly ProfilerMarker JudgeSpawnerLayerMaskProfilerMarker = new ProfilerMarker("JudgeSpawnerLayerMask");
    public static readonly ProfilerMarker JudgeSpawnerEntityRemainingProfilerMarker = new ProfilerMarker("JudgeSpawnerEntityRemaining");
    public static readonly ProfilerMarker JudgeSpawnerPrefabRemainingProfilerMarker = new ProfilerMarker("JudgeSpawnerPrefabRemaining");
    
    public static readonly ProfilerMarker ApplyProfilerMarker = new ProfilerMarker("Apply");
    public static readonly ProfilerMarker ApplySpawnerLayerMaskProfilerMarker = new ProfilerMarker("ApplySpawnerLayerMaskProfilerMarker");
    public static readonly ProfilerMarker ApplySpawnerEntityRemainingProfilerMarker = new ProfilerMarker("ApplySpawnerEntityRemaining");
    public static readonly ProfilerMarker ApplySpawnerPrefabRemainingProfilerMarker = new ProfilerMarker("ApplySpawnerPrefabRemaining");
#endif
    
    public bool Judge(
        float deltaTime, 
        float spawnerTime,
        in float3 playerPosition, 
        in LevelStatus status, 
        in SpawnerLayerMaskAndTagsOverride spawnerLayerMaskAndTagsOverride, 
        in SpawnerSingleton spawnerSingleton, 
        in LevelSpawners.ReadOnly spawners,
        in DynamicBuffer<LevelItem> levelItems, 
        in NativeArray<LevelPrefab> levelPrefabs, 
        in BufferLookup<SpawnerPrefab> spawnerPrefabs, 
        in BufferLookup<SpawnerStatus> spawnerStates, 
        in ComponentLookup<SpawnerDefinitionData> spawnerDefinitions, 
        ref BlobArray<LayerMaskAndTags> layerMaskAndTags,
        ref BlobArray<LevelDefinition.Area> areas, 
        ref BlobArray<LevelDefinition.Item> items, 
        ref LevelStageConditionStatus condition)
    {
#if ENABLE_PROFILER
        using (JudgeProfilerMarker.Auto())
#endif
        {
            switch (type)
            {
                case Type.Value:
                    return value == 0 ? status.max <= status.value : value <= status.value;
                case Type.Max:
                    return value <= status.max;
                case Type.ExpMax:
                    return value <= status.expMax;
                case Type.Exp:
                    return value == 0 ? status.expMax <= status.exp : value <= status.exp;
                case Type.Stage:
                    return value <= status.stage;
                case Type.SpawnerTime:
                    return value <= spawnerTime;
                case Type.SpawnerLayerMask:
#if ENABLE_PROFILER
                    using (JudgeSpawnerLayerMaskProfilerMarker.Auto())
#endif
                    return spawners.IsDone(layerMaskAndTags[value], spawnerDefinitions, spawnerStates);
                case Type.SpawnerLayerMaskInclude:
                    return spawnerLayerMaskAndTagsOverride.value.IsSupersetOf(layerMaskAndTags[value]);
                case Type.SpawnerLayerMaskExclude:
                    return !layerMaskAndTags[value].Overlaps(spawnerLayerMaskAndTagsOverride.value);
                case Type.SpawnerEntityRemaining:
                    if (condition.version != spawnerSingleton.version)
                    {
#if ENABLE_PROFILER
                        using (JudgeSpawnerEntityRemainingProfilerMarker.Auto())
#endif
                        {
                            condition.version = spawnerSingleton.version;
                            //condition.value = 0;
                            var (spawnerEntities, numKeys) =
                                spawnerSingleton.entities.GetUniqueKeyArray(Allocator.Temp);
                            SpawnerEntity spawnerEntity;
                            SpawnerDefinitionData spawnerDefinition;
                            var spawnerLayerMaskAndTags = layerMaskAndTags[value];
                            int i;
                            for (i = 0; i < numKeys; ++i)
                            {
                                spawnerEntity = spawnerEntities[i];
                                if (!spawnerDefinitions.TryGetComponent(spawnerEntity.spawner, out spawnerDefinition))
                                    continue;

                                ref var definition = ref spawnerDefinition.definition.Value;
                                if (definition.spawners.Length <= spawnerEntity.spawnerIndex)
                                    continue;

                                if (!definition.spawners[spawnerEntity.spawnerIndex].layerMaskAndTags
                                        .Overlaps(spawnerLayerMaskAndTags))
                                    continue;

                                condition.value = (int)Status.Start;

                                break;
                            }

                            spawnerEntities.Dispose();

                            if ((Status)condition.value == Status.Start && i == numKeys)
                                condition.value = (int)Status.Finish;
                        }
                    }

                    return (Status)condition.value == Status.Finish;
                case Type.PrefabRemaining:
                    if (condition.version != spawnerSingleton.version)
                    {
#if ENABLE_PROFILER
                        using (JudgeSpawnerPrefabRemainingProfilerMarker.Auto())
#endif
                        {
                            condition.version = spawnerSingleton.version;
                            //condition.value = 0;
                            var (spawnerEntities, numKeys) =
                                spawnerSingleton.entities.GetUniqueKeyArray(Allocator.Temp);

                            SpawnerEntity spawnerEntity;
                            SpawnerDefinitionData spawnerDefinition;
                            DynamicBuffer<SpawnerPrefab> spawnerPrefabBuffer;
                            var levelPrefab = levelPrefabs[value];
                            int i;
                            for (i = 0; i < numKeys; ++i)
                            {
                                spawnerEntity = spawnerEntities[i];
                                if (!spawnerDefinitions.TryGetComponent(spawnerEntity.spawner, out spawnerDefinition))
                                    continue;

                                ref var definition = ref spawnerDefinition.definition.Value;
                                if (definition.spawners.Length <= spawnerEntity.spawnerIndex)
                                    continue;

                                ref var spawner = ref definition.spawners[spawnerEntity.spawnerIndex];
                                if (spawner.loaderIndices.Length <= spawnerEntity.loaderIndex)
                                    continue;

                                ref var loaderIndex = ref spawner.loaderIndices[spawnerEntity.loaderIndex];

                                spawnerPrefabBuffer = spawnerPrefabs[spawnerEntity.spawner];
                                if (spawnerPrefabBuffer.Length <= loaderIndex.value)
                                    continue;

                                if (spawnerPrefabBuffer[loaderIndex.value].prefab != levelPrefab.reference)
                                    continue;

                                condition.value = (int)Status.Start;

                                break;
                            }

                            spawnerEntities.Dispose();

                            if ((Status)condition.value == Status.Start && i == numKeys)
                                condition.value = (int)Status.Finish;
                        }
                    }

                    return (Status)condition.value == Status.Finish;
                case Type.PlayerArea:
                    return areas[value].Contains(playerPosition);
                case Type.Item:
                    ref var item = ref items[value];
                    if (item.count > 0)
                    {
                        foreach (var levelItem in levelItems)
                        {
                            if (levelItem.name == item.name)
                                return levelItem.count >= item.count;
                        }
                    }
                    else
                    {
                        foreach (var levelItem in levelItems)
                        {
                            if (levelItem.name == item.name)
                                return levelItem.count < -item.count;
                        }

                        return true;
                    }
                    break;
                case Type.Millisecond:
                    if (value > 0)
                    {
                        condition.value = (int)math.round(condition.value + deltaTime * TimeSpan.TicksPerSecond);

                        return condition.value >= value * TimeSpan.TicksPerMillisecond;
                    }

                    //继承后倒计时
                    condition.value = (int)math.round(condition.value - deltaTime * TimeSpan.TicksPerSecond);
                    return condition.value <= value * TimeSpan.TicksPerMillisecond;
            }

            return false;
        }
    }
    
    public void Apply(
        double time, 
        ref BufferLookup<SpawnerStatus> spawnerStates, 
        ref EntityCommandBuffer.ParallelWriter entityManager, 
        ref BlobArray<LayerMaskAndTags> layerMaskAndTags,
        ref BlobArray<LevelDefinition.Area> areas, 
        ref BlobArray<LevelDefinition.Item> items, 
        ref DynamicBuffer<LevelItem> levelItems, 
        ref float3 playerPosition, 
        ref Random random, 
        ref LevelStatus status, 
        ref SpawnerTime spawnerTime,
        ref SpawnerLayerMaskAndTagsInclude spawnerLayerMaskAndTagsInclude, 
        ref SpawnerLayerMaskAndTagsExclude spawnerLayerMaskAndTagsExclude, 
        in SpawnerSingleton spawnerSingleton, 
        in LevelSpawners.ReadOnly spawners,
        in NativeArray<LevelPrefab> levelPrefabs, 
        in BufferLookup<SpawnerPrefab> spawnerPrefabs, 
        in ComponentLookup<SpawnerDefinitionData> spawnerDefinitions)
    {
#if ENABLE_PROFILER
        using (ApplyProfilerMarker.Auto())
#endif
        {
            switch (type)
            {
                case Type.Value:
                    status.value = value;
                    break;
                case Type.Max:
                    status.max = value;
                    break;
                case Type.ExpMax:
                    System.Threading.Interlocked.Add(ref status.expMax, value);
                    break;
                case Type.Exp:
                    System.Threading.Interlocked.Add(ref status.exp, value);
                    break;
                case Type.Stage:
                    //status.killCount = 0;
                    //status.killBossCount = 0;
                    //status.stage = value;
                    System.Threading.Interlocked.Add(ref status.stage, value);
                    break;
                case Type.SpawnerTime:
                    //++spawnerTime.version;

                    spawnerTime.value = time + value * 1000.0f;
                    break;
                case Type.SpawnerLayerMask:
#if ENABLE_PROFILER
                    using (ApplySpawnerLayerMaskProfilerMarker.Auto())
#endif
                    spawners.Clear(layerMaskAndTags[value], spawnerDefinitions, ref spawnerStates);
                    break;
                case Type.SpawnerLayerMaskInclude:
                    spawnerLayerMaskAndTagsInclude.value = layerMaskAndTags[value];
                    break;
                case Type.SpawnerLayerMaskExclude:
                    spawnerLayerMaskAndTagsExclude.value = layerMaskAndTags[value];
                    break;
                case Type.SpawnerEntityRemaining:
#if ENABLE_PROFILER
                    using (ApplySpawnerEntityRemainingProfilerMarker.Auto())
#endif
                    {
                        var (spawnerEntities, numKeys) = spawnerSingleton.entities.GetUniqueKeyArray(Allocator.Temp);

                        var spawnerLayerMaskAndTags = layerMaskAndTags[value];
                        SpawnerEntity spawnerEntity;
                        SpawnerDefinitionData spawnerDefinition;
                        for (int i = 0; i < numKeys; ++i)
                        {
                            spawnerEntity = spawnerEntities[i];
                            if (!spawnerDefinitions.TryGetComponent(spawnerEntity.spawner, out spawnerDefinition))
                                continue;

                            ref var definition = ref spawnerDefinition.definition.Value;
                            if (definition.spawners.Length <= spawnerEntity.spawnerIndex)
                                continue;

                            if (!definition.spawners[spawnerEntity.spawnerIndex].layerMaskAndTags
                                    .Overlaps(spawnerLayerMaskAndTags))
                                continue;

                            //if (spawnerStates.TryGetBuffer(spawnerEntity.spawner, out spawnerStatusBuffer))
                            //    spawnerStatusBuffer.ElementAt(spawnerEntity.spawnerIndex) = default;

                            foreach (var entity in spawnerSingleton.entities.GetValuesForKey(spawnerEntity))
                                entityManager.DestroyEntity(0, entity);
                        }

                        spawnerEntities.Dispose();
                    }
                    break;
                case Type.PrefabRemaining:
#if ENABLE_PROFILER
                    using (ApplySpawnerPrefabRemainingProfilerMarker.Auto())
#endif
                    {
                        var (spawnerEntities, numKeys) = spawnerSingleton.entities.GetUniqueKeyArray(Allocator.Temp);

                        SpawnerEntity spawnerEntity;
                        SpawnerDefinitionData spawnerDefinition;
                        DynamicBuffer<SpawnerPrefab> spawnerPrefabBuffer;
                        var levelPrefab = levelPrefabs[value];
                        for (int i = 0; i < numKeys; ++i)
                        {
                            spawnerEntity = spawnerEntities[i];
                            if (!spawnerDefinitions.TryGetComponent(spawnerEntity.spawner, out spawnerDefinition))
                                continue;

                            ref var definition = ref spawnerDefinition.definition.Value;
                            if (definition.spawners.Length <= spawnerEntity.spawnerIndex)
                                continue;

                            ref var spawner = ref definition.spawners[spawnerEntity.spawnerIndex];
                            if (spawner.loaderIndices.Length <= spawnerEntity.loaderIndex)
                                continue;

                            ref var loaderIndex = ref spawner.loaderIndices[spawnerEntity.loaderIndex];

                            spawnerPrefabBuffer = spawnerPrefabs[spawnerEntity.spawner];
                            if (spawnerPrefabBuffer.Length <= loaderIndex.value)
                                continue;

                            if (spawnerPrefabBuffer[loaderIndex.value].prefab != levelPrefab.reference)
                                continue;

                            foreach (var entity in spawnerSingleton.entities.GetValuesForKey(spawnerEntity))
                                entityManager.DestroyEntity(0, entity);
                        }

                        spawnerEntities.Dispose();
                    }

                    break;
                case Type.PlayerArea:
                    playerPosition = areas[value].GetPosition(ref random);
                    //ZG.Mathematics.Math.InterlockedAdd(ref playerPosition, areas[value].GetPosition(ref random));
                    break;
                case Type.Millisecond:
                    //status.time += value * 1000.0f;
                    break;
                case Type.Item:
                {
                    ref var item = ref items[value];
                    int i, numItems = levelItems.Length;
                    for (i = 0; i < numItems; ++i)
                    {
                        ref var levelItem = ref levelItems.ElementAt(i);
                        if (levelItem.name == item.name)
                        {
                            levelItem.count += item.count;

                            if (levelItem.count < 0)
                                levelItems.RemoveAtSwapBack(i);

                            break;
                        }
                    }

                    if (i == numItems && item.count > 0)
                    {
                        LevelItem levelItem;
                        levelItem.name = item.name;
                        levelItem.count = item.count;

                        levelItems.Add(levelItem);
                    }
                }
                    break;
            }
        }
    }
}

public struct LevelDefinition
{
    public struct DefaultStage
    {
        public int index;
        public int layerMaskAndTagsIncludeIndex;
        public int layerMaskAndTagsExcludeIndex;
    }

    public struct NextStage
    {
        public int index;

        public float chance;
    }
    
    public struct StageConditionInheritance
    {
        public FixedString128Bytes stageName;
        public int previousConditionIndex;
        public int currentConditionIndex;
        public float scale;
    }

    public struct Stage
    {
        //public int spawnerLayerMaskInclude;
        //public int spawnerLayerMaskExclude;

        //public int exp;
        public FixedString128Bytes name;

        public BlobArray<LevelStageOption> results;
        public BlobArray<LevelStageOption> conditions;
        
        public BlobArray<StageConditionInheritance> conditionInheritances;
        public BlobArray<NextStage> nextStages;
    }

    public struct Area
    {
        public AABB aabb;

        public bool Contains(in float3 position)
        {
            return aabb.Contains(position);
        }

        public float3 GetPosition(ref Random random)
        {
            return random.NextFloat3(aabb.Min, aabb.Max);
        }
    }

    public struct Item
    {
        public FixedString32Bytes name;

        public int count;
    }

    public int mainStageIndex;
    public BlobArray<LayerMaskAndTags> layerMaskAndTags;
    public BlobArray<DefaultStage> defaultStages;
    public BlobArray<Stage> stages;
    public BlobArray<Area> areas;
    public BlobArray<Item> items;
}

public struct LevelDefinitionData : IComponentData
{
    public BlobAssetReference<LevelDefinition> definition;
}

public struct LevelLayerMask : IComponentData
{
    public int boss;
}

public struct LevelStatus : IComponentData
{
    public int value;
    public int max;
    public int expMax;
    public int exp;
    public int killCount;
    public int killBossCount;
    public int gold;
    public int stage;
}

public struct LevelPrefab : IBufferElementData
{
    public EntityPrefabReference reference;
}

public struct LevelStage : IBufferElementData
{
    public int value;
}

public struct LevelStageConditionStatus : IBufferElementData
{
    public uint version;
    public int value;
}

public struct LevelStageResultStatus : IBufferElementData
{
    public LayerMaskAndTags layerMaskAndTagsInclude;
    public LayerMaskAndTags layerMaskAndTagsExclude;
}

public struct LevelItem : IBufferElementData
{
    public FixedString32Bytes name;

    public int count;
}

public struct LevelItemMessage : IBufferElementData
{
    public int id;
    public FixedString32Bytes itemName;
    public FixedString32Bytes messageName;
    public UnityObjectRef<UnityEngine.Object> messageValue;
}

public struct LevelObject : IComponentData
{
    
}

public static class LevelShared
{
    private struct Stage
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<Stage>();
    }
    
    private struct Exp
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<Exp>();
    }
    
    private struct ExpMax
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<ExpMax>();
    }
    
    private struct SpawnerAttributeScale
    {
        private static readonly SharedStatic<FixedList512Bytes<SpawnerAttribute.Scale>> Value =
            SharedStatic<FixedList512Bytes<SpawnerAttribute.Scale>>.GetOrCreate<SpawnerAttributeScale>();

        public static ref FixedList512Bytes<SpawnerAttribute.Scale> values => ref Value.Data;
    }

    public static int stage
    {
        get => Stage.Value.Data;
        
        set => Stage.Value.Data = value;
    }
    
    public static int exp
    {
        get => Exp.Value.Data;
        
        set => Exp.Value.Data = value;
    }
    
    public static int expMax
    {
        get => ExpMax.Value.Data;
        
        set => ExpMax.Value.Data = value;
    }

    public static ref FixedList512Bytes<SpawnerAttribute.Scale> spawnerAttributeScales => ref SpawnerAttributeScale.values;
}