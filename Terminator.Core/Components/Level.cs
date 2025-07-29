using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
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

        public bool IsDone(int layerMask, 
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
                    if((spawner.layerMask & layerMask) == 0)
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
            int layerMask, 
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
                    if((spawner.layerMask & layerMask) == 0)
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
    
    public bool Judge(
        float deltaTime, 
        float spawnerTime,
        in float3 playerPosition, 
        in LevelStatus status, 
        in SpawnerLayerMaskOverride spawnerLayerMaskOverride, 
        in SpawnerSingleton spawnerSingleton, 
        in LevelSpawners.ReadOnly spawners,
        in NativeArray<LevelPrefab> prefabs, 
        in BufferLookup<SpawnerPrefab> spawnerPrefabs, 
        in BufferLookup<SpawnerStatus> spawnerStates, 
        in ComponentLookup<SpawnerDefinitionData> spawnerDefinitions, 
        ref BlobArray<LevelDefinition.Area> areas, 
        ref LevelStageConditionStatus condition)
    {
        switch (type)
        {
            case Type.Value:
                return value <= status.value;
            case Type.Max:
                return value <= status.max;
            case Type.ExpMax:
                return value <= status.expMax;
            case Type.Exp:
                return value <= status.exp;
            case Type.Stage:
                return value == status.stage;
            case Type.SpawnerTime:
                return value <= spawnerTime;
            case Type.SpawnerLayerMask:
                return spawners.IsDone(value, spawnerDefinitions, spawnerStates);
            case Type.SpawnerLayerMaskInclude:
                return (value & spawnerLayerMaskOverride.value) == value;
            case Type.SpawnerLayerMaskExclude:
                return (value & spawnerLayerMaskOverride.value) == 0;
            case Type.SpawnerEntityRemaining:
                if (condition.version != spawnerSingleton.version)
                {
                    condition.version = spawnerSingleton.version;
                    //condition.value = 0;
                    var (spawnerEntities, numKeys) = spawnerSingleton.entities.GetUniqueKeyArray(Allocator.Temp);
                    SpawnerEntity spawnerEntity;
                    SpawnerDefinitionData spawnerDefinition;
                    int i;
                    for (i = 0; i < numKeys; ++i)
                    {
                        spawnerEntity = spawnerEntities[i];
                        if (!spawnerDefinitions.TryGetComponent(spawnerEntity.spawner, out spawnerDefinition))
                            continue;

                        ref var definition = ref spawnerDefinition.definition.Value;
                        if (definition.spawners.Length <= spawnerEntity.spawnerIndex)
                            continue;

                        if ((definition.spawners[spawnerEntity.spawnerIndex].layerMask & value) == 0)
                            continue;

                        condition.value = (int)Status.Start;

                        break;
                    }

                    spawnerEntities.Dispose();

                    if ((Status)condition.value == Status.Start && i == numKeys)
                        condition.value = (int)Status.Finish;
                }

                return (Status)condition.value == Status.Finish;
            case Type.PrefabRemaining:
                if (condition.version != spawnerSingleton.version)
                {
                    condition.version = spawnerSingleton.version;
                    //condition.value = 0;
                    var (spawnerEntities, numKeys) = spawnerSingleton.entities.GetUniqueKeyArray(Allocator.Temp);

                    SpawnerEntity spawnerEntity;
                    SpawnerDefinitionData spawnerDefinition;
                    DynamicBuffer<SpawnerPrefab> spawnerPrefabBuffer;
                    var prefab = prefabs[value];
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

                        if (spawnerPrefabBuffer[loaderIndex.value].prefab != prefab.reference)
                            continue;

                        condition.value = (int)Status.Start;

                        break;
                    }
                    
                    spawnerEntities.Dispose();

                    if ((Status)condition.value == Status.Start && i == numKeys)
                        condition.value = (int)Status.Finish;
                }

                return (Status)condition.value == Status.Finish;
            case Type.PlayerArea:
                return areas[value].Contains(playerPosition);
            case Type.Millisecond:
                if (value > 0)
                {
                    condition.value += (int)(deltaTime * 1000);

                    return condition.value >= value;
                }

                //继承后倒计时
                condition.value -= (int)(deltaTime * 1000);
                return condition.value <= value;
        }

        return false;
    }
    
    public void Apply(
        double time, 
        ref BufferLookup<SpawnerStatus> spawnerStates, 
        ref EntityCommandBuffer.ParallelWriter entityManager, 
        ref BlobArray<LevelDefinition.Area> areas, 
        ref float3 playerPosition, 
        ref Random random, 
        ref LevelStatus status, 
        ref SpawnerTime spawnerTime,
        ref SpawnerLayerMaskInclude spawnerLayerMaskInclude, 
        ref SpawnerLayerMaskExclude spawnerLayerMaskExclude, 
        in SpawnerSingleton spawnerSingleton, 
        in LevelSpawners.ReadOnly spawners,
        in NativeArray<LevelPrefab> prefabs, 
        in BufferLookup<SpawnerPrefab> spawnerPrefabs, 
        in ComponentLookup<SpawnerDefinitionData> spawnerDefinitions)
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
                status.expMax = value;
                break;
            case Type.Exp:
                status.exp = value;
                break;
            case Type.Stage:
                status.killCount = 0;
                status.killBossCount = 0;
                status.stage = value;
                break;
            case Type.SpawnerTime:
                //++spawnerTime.version;

                spawnerTime.value = time + value * 1000.0f;
                break;
            case Type.SpawnerLayerMask:
                spawners.Clear(value, spawnerDefinitions, ref spawnerStates);
                break;
            case Type.SpawnerLayerMaskInclude:
                spawnerLayerMaskInclude.value = value;
                break;
            case Type.SpawnerLayerMaskExclude:
                spawnerLayerMaskExclude.value = value;
                break;
            case Type.SpawnerEntityRemaining:
                {
                    var (spawnerEntities, numKeys) = spawnerSingleton.entities.GetUniqueKeyArray(Allocator.Temp);

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

                        if ((definition.spawners[spawnerEntity.spawnerIndex].layerMask & value) == 0)
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
                {
                    var (spawnerEntities, numKeys) = spawnerSingleton.entities.GetUniqueKeyArray(Allocator.Temp);

                    SpawnerEntity spawnerEntity;
                    SpawnerDefinitionData spawnerDefinition;
                    DynamicBuffer<SpawnerPrefab> spawnerPrefabBuffer;
                    var prefab = prefabs[value];
                    for(int i = 0; i < numKeys; ++i)
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

                        if (spawnerPrefabBuffer[loaderIndex.value].prefab != prefab.reference)
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
        }
    }
}

public struct LevelDefinition
{
    public struct DefaultStage
    {
        public int index;
        public int layerMaskInclude;
        public int layerMaskExclude;
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
        public BlobArray<int> nextStageIndies;
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

    public BlobArray<DefaultStage> defaultStages;
    public BlobArray<Stage> stages;
    public BlobArray<Area> areas;
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
    public int layerMaskInclude;
    public int layerMaskExclude;
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
}