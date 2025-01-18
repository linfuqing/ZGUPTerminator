using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Scenes;
using Random = Unity.Mathematics.Random;

public enum SpawnerSpace
{
    World, 
    Local
}


/*public struct SpawnerAttribute
{
    public int hp;
    public int hpMax;
    public int level;
    public int levelMax;
    public int exp;
    public int expMax;
        
    public float speedScale;
    public float speedScaleMax;
        
    public float speedScaleBuff;
    public float hpBuff;
    public float levelBuff;
    public float expBuff;

    public float interval;
}

public struct SpawnerInstance
{
    public double time;
    public Entity loader;
    public SpawnerEntity entity;
    public LocalTransform localTransform;

    public SpawnerAttribute attribute;

    public bool Instantiate(
        in ComponentLookup<PrefabLoadResult> prefabLoadResults, 
        ref EntityCommandBuffer.ParallelWriter entityManager)
    {
        if (!prefabLoadResults.TryGetComponent(loader, out var prefabLoadResult))
            return false;

        var entity = entityManager.Instantiate(0, prefabLoadResult.PrefabRoot);
        
        entityManager.AddComponent(1, prefabLoadResult.PrefabRoot, this.entity);

        localTransform.Scale = 1.0f;
        entityManager.SetComponent(2, entity, localTransform);

        if (attributeIndex != -1)
        {
            ref var attribute = ref attributes[attributeIndex];

            float times = attribute.interval > math.FLT_MIN_NORMAL ? math.floor(time / attribute.interval) : time;
            
            if (attribute.speedScale > math.FLT_MIN_NORMAL)
            {
                FollowTargetSpeed followTargetSpeed;
                followTargetSpeed.scale = math.min(attribute.speedScale + attribute.speedScaleBuff * times, attribute.speedScaleMax);
                entityManager.SetComponent(2, entity, followTargetSpeed);
            }

            if (attribute.hp != 0)
            {
                EffectTarget effectTarget;
                effectTarget.times = 0;
                effectTarget.hp = math.min((int)math.round(attribute.hp + attribute.hpBuff * times), attribute.hpMax);
                entityManager.SetComponent(2, entity, effectTarget);
            }

            if (attribute.level != 0 || attribute.exp != 0)
            {
                EffectTargetLevel effectTargetLevel;
                effectTargetLevel.value = math.min((int)math.round(attribute.level + attribute.levelBuff * times), attribute.levelMax);
                effectTargetLevel.exp = math.min((int)math.round(attribute.exp + attribute.expBuff * times), attribute.expMax);
                entityManager.SetComponent(2, entity, effectTargetLevel);
            }
        }
    }
}*/

public struct SpawnerDefinition
{
    public struct Attribute
    {
        public int hp;
        public int hpMax;
        public int level;
        public int levelMax;
        public int exp;
        public int expMax;
        public int gold;
        public int goldMax;

        public float damageScale;
        public float damageScaleMax;

        public float speedScale;
        public float speedScaleMax;

        public float speedScaleBuff;
        public float damageScaleBuff;
        public float hpBuff;
        public float levelBuff;
        public float expBuff;
        public float goldBuff;

        public float interval;
    }

    public struct Area
    {
        public SpawnerSpace space;

        public quaternion from;
        public quaternion to;
        public AABB aabb;
    }
    
    public struct AreaIndex
    {
        public int value;
        public int layerMask;
        public int attributeIndex;
    }

    public struct LoaderIndex
    {
        public int value;
        public float chance;
    }

    public struct Spawner
    {
        public float startTime;
        public float endTime;
        public float interval;
        public float cooldown;
        
        public int maxCount;
        public int maxCountToNextTime;
        public int minCountToNextTime;
        public int countPerTime;
        public int times;
        public int tryTimesPerArea;
        public int layerMask;

        public BlobArray<LoaderIndex> loaderIndices;
        public BlobArray<AreaIndex> areaIndices;
    }

    public BlobArray<Area> areas;
    
    public BlobArray<Attribute> attributes;

    public BlobArray<Spawner> spawners;

    public void Update(
        int layerMask,
        double time,
        in float3 playerPosition,
        in Entity entity,
        in CollisionWorld collisionWorld, 
        in ComponentLookup<PhysicsCollider> colliders, 
        in ComponentLookup<PrefabLoadResult> prefabLoadResults,
        in NativeParallelMultiHashMap<SpawnerEntity, Entity> entities,
        in DynamicBuffer<SpawnerPrefab> prefabs, 
        ref DynamicBuffer<SpawnerStatus> states, 
        ref EntityCommandBuffer.ParallelWriter entityManager,
        ref Random random, 
        ref int instanceCount)
    {
        int /*numStates = states.Length, */numSpawners = this.spawners.Length;
        /*if (numStates < numPrefabs)
        {
            states.Resize(numPrefabs, NativeArrayOptions.UninitializedMemory);

            SpawnerStatus status;
            status.times = 0;
            status.count = 0;
            for (int i = numStates; i < numPrefabs; ++i)
            {
                ref var prefab = ref this.prefabs[i];
                status.cooldown = time + prefab.startTime;
                status.time = time + prefab.endTime;

                states[i] = status;
            }
        }*/
        states.Resize(numSpawners, NativeArrayOptions.ClearMemory);

        int i, j, numLoaderIndices;
        float randomValue;
        SpawnerEntity spawnerEntity;
        PrefabLoadResult prefabLoadResult;
        for (i = 0; i < numSpawners; ++i)
        {
            ref var spawner = ref this.spawners[i];

            randomValue = random.NextFloat();
            numLoaderIndices = spawner.loaderIndices.Length;
            for (j = 0; j < numLoaderIndices; ++j)
            {
                ref var loaderIndex = ref spawner.loaderIndices[j];
                if (loaderIndex.chance < randomValue)
                {
                    randomValue -= loaderIndex.chance;

                    continue;
                }

                break;
            }

            if (j == numLoaderIndices)
                continue;

            if (!prefabLoadResults.TryGetComponent(prefabs[spawner.loaderIndices[j].value].loader, out prefabLoadResult))
                continue;

            spawnerEntity.spawner = entity;
            spawnerEntity.spawnerIndex = i;
            spawnerEntity.loaderIndex = j;

            Update(
                layerMask,
                time,
                playerPosition,
                prefabLoadResult.PrefabRoot,
                spawnerEntity, 
                collisionWorld,
                colliders, 
                entities, 
                ref spawner, 
                ref states.ElementAt(i), 
                ref entityManager, 
                ref random, 
                ref instanceCount);
        }
    }
    
    public bool Update(
        int layerMask, 
        double time, 
        in float3 playerPosition, 
        in Entity prefab, 
        in SpawnerEntity spawnerEntity, 
        in CollisionWorld collisionWorld, 
        in ComponentLookup<PhysicsCollider> colliders, 
        in NativeParallelMultiHashMap<SpawnerEntity, Entity> entities, 
        ref Spawner data, 
        ref SpawnerStatus status, 
        ref EntityCommandBuffer.ParallelWriter entityManager, 
        ref Random random, 
        ref int instanceCount)
    {
        if ((layerMask & data.layerMask) != data.layerMask)
        {
            status = default;
            
            return false;
        }

        if (status.startTime < math.DBL_MIN_NORMAL)
            status.startTime = time;

        if (status.startTime + data.startTime > time || data.endTime > math.FLT_MIN_NORMAL && status.startTime + data.endTime < time)
            return false;
        
        int entityCount = entities.CountValuesForKey(spawnerEntity);
        /*if (status.cooldown > time && entityCount >= data.maxCountToNextTime)
            return false;*/

        /*if (status.count == 0 && data.minCountToNextTime > 0 && entityCount > data.minCountToNextTime)
            return false;*/

        bool result = false;
        float currentTime = (float)(time - status.startTime);
        while(status.cooldown < time || status.count == 0 && entityCount < data.maxCountToNextTime)
        {
            if (status.count < data.countPerTime)
            {
                if (data.maxCount > 0 && instanceCount > data.maxCount)
                    break;

                ++status.count;

                if(data.interval > math.FLT_MIN_NORMAL)
                    status.cooldown = time + data.interval;

                if (__Apply(
                        layerMask,
                        currentTime, 
                        playerPosition,
                        prefab,
                        spawnerEntity,
                        collisionWorld,
                        colliders,
                        ref random,
                        ref entityManager,
                        ref data))
                {
                    System.Threading.Interlocked.Increment(ref instanceCount);

                    ++entityCount;
                    
                    result = true;
                }
            }
            else if ((data.times < 1 || status.times + 1 < data.times) && data.minCountToNextTime >= entityCount)
            {
                ++status.times;

                status.count = 0;

                status.cooldown = time + data.cooldown;
            }
            else
                break;
            
        } //while (status.cooldown < time);

        return result;
    }

    private bool __Apply(
        int layerMask, 
        float time, 
        in float3 playerPosition,
        in Entity prefab,
        in SpawnerEntity spawnerEntity,
        in CollisionWorld collisionWorld,
        in ComponentLookup<PhysicsCollider> colliders,
        ref Random random,
        ref EntityCommandBuffer.ParallelWriter entityManager, 
        ref Spawner data)
    {
        int areaIndex = -1, attributeIndex = -1, numAreaIndices = data.areaIndices.Length, i;
        for (i = 0; i < numAreaIndices; ++i)
        {
            ref var temp = ref data.areaIndices[i];
            if((layerMask & temp.layerMask) != temp.layerMask)
                continue;
            
            /*if(temp.startTime > time)
                continue;

            if(temp.endTime > math.FLT_MIN_NORMAL && temp.endTime < time)
                continue;*/

            areaIndex = temp.value;

            attributeIndex = temp.attributeIndex;

            break;
        }

        if (areaIndex == -1)
            return false;

        ref var area = ref areas[areaIndex];
        
        LocalTransform localTransform;
        localTransform.Rotation = math.slerp(area.from, area.to, random.NextFloat(0.0f, 1.0f));
        localTransform.Position = random.NextFloat3(area.aabb.Min, area.aabb.Max);
        if (area.space == SpawnerSpace.Local)
        {
            localTransform.Position.x += playerPosition.x;
            localTransform.Position.z += playerPosition.z;
        }

        if (data.tryTimesPerArea >= 0 && colliders.TryGetComponent(prefab, out var physicsCollider) && physicsCollider.IsValid)
        {
            int tryTimes = math.max(1, data.tryTimesPerArea);
            for (i = 0; i < tryTimes; ++i)
            {
                if (!collisionWorld.CalculateDistance(new ColliderDistanceInput(
                        physicsCollider.Value,
                        collisionWorld.CollisionTolerance,
                        math.RigidTransform(localTransform.Rotation, localTransform.Position))))
                    break;
                
                localTransform.Rotation = math.slerp(area.from, area.to, random.NextFloat(0.0f, 1.0f));
                localTransform.Position = random.NextFloat3(area.aabb.Min, area.aabb.Max);
                if (area.space == SpawnerSpace.Local)
                    localTransform.Position += playerPosition;
            }
            
            if (i == tryTimes)
                return false;
        }

        var entity = entityManager.Instantiate(0, prefab);

        entityManager.AddComponent(1, entity, spawnerEntity);

        localTransform.Scale = 1.0f;
        entityManager.SetComponent(2, entity, localTransform);

        if (attributeIndex != -1)
        {
            ref var attribute = ref attributes[attributeIndex];

            float times = attribute.interval > math.FLT_MIN_NORMAL ? math.floor(time / attribute.interval) : time;
            
            if (attribute.speedScale > math.FLT_MIN_NORMAL)
            {
                FollowTargetSpeed followTargetSpeed;
                followTargetSpeed.scale = math.min(attribute.speedScale + attribute.speedScaleBuff * times, attribute.speedScaleMax);
                entityManager.SetComponent(2, entity, followTargetSpeed);
            }

            if (attribute.damageScale > math.FLT_MIN_NORMAL)
            {
                EffectDamage effectDamage;
                effectDamage.scale = math.min(math.round(attribute.damageScale + attribute.damageScaleBuff * times), attribute.damageScaleMax);
                entityManager.SetComponent(2, entity, effectDamage);
            }

            if (attribute.hp != 0)
            {
                EffectTarget effectTarget;
                effectTarget.times = 0;
                effectTarget.hp = math.min((int)math.round(attribute.hp + attribute.hpBuff * times), attribute.hpMax);
                effectTarget.invincibleTime = 0.0;
                entityManager.SetComponent(2, entity, effectTarget);
            }

            if (attribute.level != 0 || attribute.exp != 0 || attribute.gold != 0)
            {
                EffectTargetLevel effectTargetLevel;
                effectTargetLevel.value = math.min((int)math.round(attribute.level + attribute.levelBuff * times), attribute.levelMax);
                effectTargetLevel.exp = math.min((int)math.round(attribute.exp + attribute.expBuff * times), attribute.expMax);
                effectTargetLevel.gold = math.min((int)math.round(attribute.gold + attribute.goldBuff * times), attribute.goldMax);
                entityManager.SetComponent(2, entity, effectTargetLevel);
            }
        }

        return true;
    }
}

public struct SpawnerDefinitionData : IComponentData
{
    public BlobAssetReference<SpawnerDefinition> definition;
}

public struct SpawnerLayerMaskOverride : IComponentData
{
    public int value;
}

public struct SpawnerLayerMaskInclude : IComponentData
{
    public int value;
}

public struct SpawnerLayerMaskExclude : IComponentData
{
    public int value;
}

public struct SpawnerLayerMask : IComponentData
{
    public int value;

    public readonly int Get(
        in SpawnerLayerMaskOverride overrideValue,
        in SpawnerLayerMaskInclude includeValue,
        in SpawnerLayerMaskExclude excludeValue)
    {
        return ((overrideValue.value == 0 ? value : overrideValue.value) | includeValue.value) & ~excludeValue.value;
    }
}

public struct SpawnerPrefab : IBufferElementData
{
    public Entity loader;
}

public struct SpawnerStatus : IBufferElementData
{
    public int times;
    public int count;
    public double cooldown;
    public double startTime;
}

public struct SpawnerEntity : IComponentData, IEquatable<SpawnerEntity>
{
    public Entity spawner;
    public int spawnerIndex;
    public int loaderIndex;

    public bool Equals(SpawnerEntity other)
    {
        return spawner == other.spawner && spawnerIndex == other.spawnerIndex && loaderIndex == other.loaderIndex;
    }

    public override int GetHashCode()
    {
        return spawner.GetHashCode() ^ spawnerIndex ^ loaderIndex;
    }
}


public struct SpawnerTrigger : IComponentData
{
    public int layerMask;
}