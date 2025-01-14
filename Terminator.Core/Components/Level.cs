using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEngine;
using Random = Unity.Mathematics.Random;

[Serializable]
public struct LevelStageOption
{
    public enum Type
    {
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
        ref LevelStageConditionStatus condition, 
        ref BlobArray<LevelDefinition.Area> areas, 
        in float3 playerPosition, 
        in LevelStatus status, 
        in SpawnerLayerMaskOverride spawnerLayerMaskOverride, 
        in SpawnerSingleton spawnerSingleton, 
        in NativeArray<LevelPrefab> prefabs, 
        in BufferLookup<SpawnerPrefab> spawnerPrefabs, 
        in ComponentLookup<SpawnerDefinitionData> spawners, 
        in ComponentLookup<RequestEntityPrefabLoaded> prefabReferences)
    {
        switch (type)
        {
            case Type.Value:
                return value <= status.value;
            case Type.Max:
                return value < status.max;
            case Type.ExpMax:
                return value < status.expMax;
            case Type.Exp:
                return value <= status.exp;
            case Type.Stage:
                return value <= status.stage;
            case Type.SpawnerLayerMaskInclude:
                return (value & spawnerLayerMaskOverride.value) == value;
            case Type.SpawnerLayerMaskExclude:
                return (value & spawnerLayerMaskOverride.value) == 0;
            case Type.SpawnerEntityRemaining:
                if (condition.version != spawnerSingleton.version)
                {
                    condition.version = spawnerSingleton.version;
                    condition.value = 0;
                    using (var spawnerEntities = spawnerSingleton.entities.GetKeyArray(Allocator.Temp))
                    {
                        SpawnerEntity spawnerEntity;
                        SpawnerDefinitionData spawner;
                        int numKeys = spawnerEntities.Unique();
                        for (int i = 0; i < numKeys; ++i)
                        {
                            spawnerEntity = spawnerEntities[i];
                            if (!spawners.TryGetComponent(spawnerEntity.spawner, out spawner))
                                continue;

                            ref var definition = ref spawner.definition.Value;
                            if(definition.spawners.Length <= spawnerEntity.index)
                                continue;

                            if ((definition.spawners[spawnerEntity.index].layerMask & value) == 0)
                                continue;

                            condition.value = 1;

                            break;
                        }
                    }
                }

                return value == 0;
            case Type.PrefabRemaining:
                if (condition.version != spawnerSingleton.version)
                {
                    condition.version = spawnerSingleton.version;
                    condition.value = 0;

                    using (var spawnerEntities = spawnerSingleton.entities.GetKeyArray(Allocator.Temp))
                    {
                        RequestEntityPrefabLoaded prefabReference;
                        SpawnerEntity spawnerEntity;
                        SpawnerDefinitionData spawner;
                        DynamicBuffer<SpawnerPrefab> spawnerPrefabBuffer;
                        var prefab = prefabs[value];
                        int numKeys = spawnerEntities.Unique();
                        for(int i = 0; i < numKeys; ++i)
                        {
                            spawnerEntity = spawnerEntities[i];
                            if (!spawners.TryGetComponent(spawnerEntity.spawner, out spawner))
                                continue;

                            ref var definition = ref spawner.definition.Value;
                            if(definition.spawners.Length <= spawnerEntity.index)
                                continue;

                            spawnerPrefabBuffer = spawnerPrefabs[spawnerEntity.spawner];

                            if (!prefabReferences.TryGetComponent(spawnerPrefabBuffer[definition.spawners[spawnerEntity.index].loaderIndex].loader,
                                    out prefabReference) || prefabReference.Prefab != prefab.reference)
                                continue;

                            condition.value = 1;

                            break;
                        }
                    }
                }

                return condition.value == 0;
            case Type.PlayerArea:
                return areas[value].Contains(playerPosition);
            case Type.Millisecond:
                condition.value += (int)(deltaTime * 1000);

                return condition.value >= value;
        }

        return false;
    }
    
    public void Apply(
        ref EntityCommandBuffer.ParallelWriter entityManager, 
        ref BlobArray<LevelDefinition.Area> areas, 
        ref float3 playerPosition, 
        ref Random random, 
        ref LevelStatus status, 
        ref SpawnerLayerMaskInclude spawnerLayerMaskInclude, 
        ref SpawnerLayerMaskExclude spawnerLayerMaskExclude, 
        in SpawnerSingleton spawnerSingleton, 
        in NativeArray<LevelPrefab> prefabs, 
        in BufferLookup<SpawnerPrefab> spawnerPrefabs, 
        in ComponentLookup<SpawnerDefinitionData> spawners, 
        in ComponentLookup<RequestEntityPrefabLoaded> prefabReferences)
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
                status.stage = value;
                break;
            case Type.SpawnerLayerMaskInclude:
                spawnerLayerMaskInclude.value = value;
                break;
            case Type.SpawnerLayerMaskExclude:
                spawnerLayerMaskExclude.value = value;
                break;
            case Type.SpawnerEntityRemaining:
                using (var spawnerEntities = spawnerSingleton.entities.GetKeyArray(Allocator.Temp))
                {
                    SpawnerEntity spawnerEntity;
                    SpawnerDefinitionData spawner;
                    int numKeys = spawnerEntities.Unique();
                    for (int i = 0; i < numKeys; ++i)
                    {
                        spawnerEntity = spawnerEntities[i];
                        if (!spawners.TryGetComponent(spawnerEntity.spawner, out spawner))
                            continue;

                        ref var definition = ref spawner.definition.Value;
                        if(definition.spawners.Length <= spawnerEntity.index)
                            continue;

                        if ((definition.spawners[spawnerEntity.index].layerMask & value) == 0)
                            continue;

                        foreach (var entity in spawnerSingleton.entities.GetValuesForKey(spawnerEntity))
                            entityManager.DestroyEntity(0, entity);
                    }
                }

                break;
            case Type.PrefabRemaining:
                using (var spawnerEntities = spawnerSingleton.entities.GetKeyArray(Allocator.Temp))
                {
                    RequestEntityPrefabLoaded prefabReference;
                    SpawnerEntity spawnerEntity;
                    SpawnerDefinitionData spawner;
                    DynamicBuffer<SpawnerPrefab> spawnerPrefabBuffer;
                    var prefab = prefabs[value];
                    int numKeys = spawnerEntities.Unique();
                    for(int i = 0; i < numKeys; ++i)
                    {
                        spawnerEntity = spawnerEntities[i];
                        if (!spawners.TryGetComponent(spawnerEntity.spawner, out spawner))
                            continue;

                        ref var definition = ref spawner.definition.Value;
                        if(definition.spawners.Length <= spawnerEntity.index)
                            continue;

                        spawnerPrefabBuffer = spawnerPrefabs[spawnerEntity.spawner];

                        if (!prefabReferences.TryGetComponent(spawnerPrefabBuffer[definition.spawners[spawnerEntity.index].loaderIndex].loader,
                                out prefabReference) || prefabReference.Prefab != prefab.reference)
                            continue;

                        foreach (var entity in spawnerSingleton.entities.GetValuesForKey(spawnerEntity))
                            entityManager.DestroyEntity(0, entity);
                    }
                }
                break;
            case Type.PlayerArea:
                playerPosition += areas[value].GetPosition(ref random);
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
    
    public struct Stage
    {
        //public int spawnerLayerMaskInclude;
        //public int spawnerLayerMaskExclude;

        //public int exp;
        public int nextStageIndex;

        public FixedString128Bytes name;

        public BlobArray<LevelStageOption> conditions;
        public BlobArray<LevelStageOption> results;
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

public struct LevelStatus : IComponentData
{
    public int value;
    public int max;
    public int expMax;
    public int exp;
    public int count;
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
    public int version;
    public int value;
}

public struct LevelStageResultStatus : IBufferElementData
{
    public int layerMaskInclude;
    public int layerMaskExclude;
}
