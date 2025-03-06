using System;
using System.Threading;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Math = ZG.Mathematics.Math;
using Random = Unity.Mathematics.Random;

[BurstCompile, 
 CreateAfter(typeof(PrefabLoaderSystem)), 
 UpdateInGroup(typeof(TransformSystemGroup), OrderLast = true), UpdateBefore(typeof(CopyMatrixToTransformSystem))]
public partial struct EffectSystem : ISystem
{
    [Flags]
    private enum EnabledFlags
    {
        Message = 0x01,
        StatusTarget = 0x02, 
        Destroyed = 0x04, 
        Recovery = 0x08
    }
    
    private struct DamageInstance
    {
        public int index;
        public float scale;
        public RigidTransform transform;
        public Entity entity;
        public Entity parent;
        public EntityPrefabReference entityPrefabReference;
    }

    [BurstCompile]
    private struct Instantiate : IJob
    {
        public NativeQueue<DamageInstance> damageInstances;

        public PrefabLoader.Writer prefabLoader;

        public EntityCommandBuffer entityManager;

        public void Execute()
        {
            EffectDamageParent damageParent;
            damageParent.index = -1;
            
            DamageInstance damageInstance;
            EffectDamage damage;
            Parent parent;
            Entity entity;
            int count = damageInstances.Count;
            for(int i = 0; i < count; ++i)
            {
                damageInstance = damageInstances.Dequeue();
                if (prefabLoader.TryGetOrLoadPrefabRoot(damageInstance.entityPrefabReference, out entity))
                {
                    entity = entityManager.Instantiate(entity);

                    entityManager.SetComponent(entity,
                        LocalTransform.FromPositionRotation(damageInstance.transform.pos,
                            damageInstance.transform.rot));

                    if (damageInstance.parent != Entity.Null)
                    {
                        parent.Value = damageInstance.parent;
                        entityManager.AddComponent(entity, parent);
                    }

                    if (damageInstance.entity != Entity.Null)
                    {
                        damageParent.entity = damageInstance.entity;
                        entityManager.AddComponent(entity, damageParent);
                    }

                    damage.scale = damageInstance.scale;
                    entityManager.AddComponent(entity, damage);
                }
                else
                    damageInstances.Enqueue(damageInstance);
            }
        }
    }

    private struct Destroy
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<KinematicCharacterBody> characterBodies;

        [ReadOnly]
        public BufferAccessor<Child> children;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(int index)
        {
            if (index >= characterBodies.Length || characterBodies[index].IsGrounded)
            {
                if (index < children.Length)
                {
                    var children = this.children[index];
                    foreach (var child in children)
                        entityManager.DestroyEntity(int.MaxValue - 1, child.Value);
                }

                entityManager.DestroyEntity(0, entityArray[index]);
            }
        }
    }

    [BurstCompile]
    private struct DestroyEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        [ReadOnly]
        public BufferTypeHandle<Child> childType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Destroy destroy;
            destroy.entityArray = chunk.GetNativeArray(entityType);
            destroy.characterBodies = chunk.GetNativeArray(ref characterBodyType);
            destroy.children = chunk.GetBufferAccessor(ref childType);
            destroy.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                destroy.Execute(i);
        }
    }

    private struct Clear
    {
        public double time;

        [ReadOnly]
        public NativeArray<EffectDefinitionData> instances;

        [ReadOnly] 
        public BufferAccessor<SimulationEvent> simulationEvents;

        public BufferAccessor<EffectStatusTarget> statusTargets;

        public NativeArray<EffectStatus> states;

        public bool Execute(int index)
        {
            if (index < states.Length)
            {
                var status = states[index];
                if (status.time < math.DBL_MIN_NORMAL)
                {
                    status.index = 0;

                    status.count = 0;

                    status.time = time;

                    ref var definition = ref instances[index].definition.Value;

                    status.time += definition.effects.Length > 0 ? definition.effects[0].startTime : 0.0f;

                    states[index] = status;
                }
            }

            EffectStatusTarget statusTarget;
            var statusTargets = this.statusTargets[index];
            var simulationEvents = this.simulationEvents[index];
            int numStatusTargets = statusTargets.Length;
            bool isContains;
            for (int i = 0; i < numStatusTargets; ++i)
            {
                statusTarget = statusTargets[i];
                
                isContains = false;
                foreach (var simulationEvent in simulationEvents)
                {
                    if (simulationEvent.entity == statusTarget.entity)
                    {
                        isContains = true;

                        break;
                    }
                }

                if (!isContains)
                {
                    statusTargets.RemoveAtSwapBack(i--);

                    --numStatusTargets;
                }
            }

            return numStatusTargets > 0;
        }
    }

    [BurstCompile]
    private struct ClearEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentTypeHandle<EffectDefinitionData> instanceType;

        [ReadOnly] 
        public BufferTypeHandle<SimulationEvent> simulationEventType;

        public BufferTypeHandle<EffectStatusTarget> statusTargetType;

        public ComponentTypeHandle<EffectStatus> statusType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Clear clear;
            clear.time = time;
            clear.instances = chunk.GetNativeArray(ref instanceType);
            clear.simulationEvents = chunk.GetBufferAccessor(ref simulationEventType);
            clear.statusTargets = chunk.GetBufferAccessor(ref statusTargetType);
            clear.states = chunk.GetNativeArray(ref statusType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(!clear.Execute(i))
                    chunk.SetComponentEnabled(ref statusTargetType, i, false);
            }
        }
    }

    private struct Collect
    {
        public float deltaTime;
        public double time;

        public quaternion inverseCameraRotation;
        
        public Random random;

        [ReadOnly]
        public ComponentLookup<EffectDamageParent> damageParentMap;

        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterProperties> characterProperties;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> damages;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<EffectDamageParent> damageParents;

        [ReadOnly]
        public NativeArray<EffectDefinitionData> instances;

        [ReadOnly] 
        public NativeArray<SimulationCollision> simulationCollisions;

        [ReadOnly] 
        public BufferAccessor<SimulationEvent> simulationEvents;

        [ReadOnly] 
        public BufferAccessor<EffectPrefab> prefabs;

        [ReadOnly] 
        public BufferAccessor<EffectMessage> inputMessages;

        public BufferAccessor<Message> outputMessages;

        public BufferAccessor<EffectStatusTarget> statusTargets;

        public NativeArray<EffectStatus> states;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetDamage> targetDamages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<DelayDestroy> delayDestroies;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<DropToDamage> dropToDamages;

        [NativeDisableParallelForRestriction] 
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [NativeDisableParallelForRestriction] 
        public ComponentLookup<LocalToWorld> localToWorlds;

        [NativeDisableParallelForRestriction]
        public BufferLookup<EffectDamageStatistic> damageStatistics;

        public EntityCommandBuffer.ParallelWriter entityManager;
        
        public PrefabLoader.ParallelWriter prefabLoader;
        
        public NativeQueue<DamageInstance>.ParallelWriter damageInstances;

        public EnabledFlags Execute(int index)
        {
            var status = states[index];
            if (status.time > time)
                return 0;
            
            ref var definition = ref instances[index].definition.Value;
            int numEffects = definition.effects.Length;
            if (status.index >= numEffects)
                return 0;

            EnabledFlags enabledFlags = 0;
            ref var effect = ref definition.effects[status.index];
            if (status.time > math.DBL_MIN_NORMAL)
            {
                bool result = true;
                int resultCount = 0, entityCount = 0;
                if (effect.time > math.FLT_MIN_NORMAL)
                {
                    resultCount = (int)math.ceil((time - status.time) / effect.time);
                    if (resultCount < 1)
                        return 0;

                    result = (effect.count > 0 ? math.min(resultCount, effect.count - status.count) : resultCount) >
                             (int)math.ceil((time - deltaTime - status.time) / effect.time);
                }

                var prefabs = index < this.prefabs.Length ? this.prefabs[index] : default;
                Entity entity = entityArray[index];

                if (!EffectDamageParent.TryGetComponent(
                        entity,
                        damageParentMap,
                        damages,
                        out EffectDamage instanceDamage, 
                        out _))
                    instanceDamage.scale = 1.0f;
                
                EffectDamageParent instanceDamageParent;
                if (index < damageParents.Length)
                    instanceDamageParent = damageParents[index].GetRoot(damageParentMap, damages);
                else
                {
                    instanceDamageParent.index = -1;
                    instanceDamageParent.entity = entity;
                }

                LocalToWorld source = localToWorlds[entity];
                var statusTargets = this.statusTargets[index];
                if (result)
                {
                    var inputMessages = index < this.inputMessages.Length ? this.inputMessages[index] : default;
                    var outputMessages = index < this.outputMessages.Length ? this.outputMessages[index] : default;
                    var simulationEvents = this.simulationEvents[index];
                    EffectStatusTarget statusTarget;
                    PhysicsCollider physicsCollider;
                    RefRW<KinematicCharacterBody> characterBody;
                    EffectMessage inputMessage;
                    Message outputMessage;
                    MessageParameter messageParameter;
                    Entity instance;
                    LocalToWorld destination;
                    float3 forceResult, force;
                    float delayDestroyTime,
                        mass,
                        lengthSQ;
                    int totalCount = 0, 
                        totalDamageValue = 0, 
                        damageValue,
                        dropDamageValue, 
                        layerMask,
                        belongsTo,
                        numMessageIndices,
                        numDamageIndices,
                        damageIndex,
                        i;
                    bool isResult, isContains;
                    foreach (var simulationEvent in simulationEvents)
                    {
                        isContains = false;
                        foreach (var temp in statusTargets)
                        {
                            if (temp.entity == simulationEvent.entity)
                            {
                                isContains = true;

                                break;
                            }
                        }

                        if (isContains)
                            continue;

                        belongsTo = physicsColliders.TryGetComponent(simulationEvent.entity, out physicsCollider) &&
                                    physicsCollider.Value.IsCreated
                            ? (int)physicsCollider.Value.Value.GetCollisionFilter(simulationEvent.colliderKey).BelongsTo
                            : -1;
                        damageIndex = -1;
                        numDamageIndices = effect.damageIndices.Length;
                        for (i = 0; i < numDamageIndices; ++i)
                        {
                            damageIndex = effect.damageIndices[i];
                            layerMask = definition.damages[damageIndex].layerMask;
                            if (layerMask == 0 || (layerMask & belongsTo) != 0)
                                break;
                        }

                        if (i == numDamageIndices)
                            continue;

                        ref var damage = ref definition.damages[damageIndex];

                        statusTarget.entity = simulationEvent.entity;
                        statusTargets.Add(statusTarget);

                        if (damage.entityLayerMask == 0 || (damage.entityLayerMask & belongsTo) != 0)
                            ++entityCount;

                        isResult = false;

                        if (delayDestroies.HasComponent(simulationEvent.entity))
                        {
                            delayDestroyTime = damage.delayDestroyTime; // * damageScale;
                            if (math.abs(delayDestroyTime) > math.FLT_MIN_NORMAL)
                            {
                                Math.InterlockedAdd(
                                    ref this.delayDestroies.GetRefRW(simulationEvent.entity).ValueRW.time,
                                    delayDestroyTime);

                                isResult = true;
                            }
                        }

                        if (!localToWorlds.TryGetComponent(simulationEvent.entity, out destination))
                            destination.Value = float4x4.identity;

                        characterBody = characterBodies.GetRefRWOptional(simulationEvent.entity);
                        if (targetDamages.HasComponent(simulationEvent.entity))
                        {
                            //++times;

                            damageValue = (int)math.ceil(damage.value * instanceDamage.scale);

                            totalDamageValue += damageValue;

                            isResult = damageValue != 0;

                            ref var targetDamage = ref targetDamages.GetRefRW(simulationEvent.entity).ValueRW;

                            if (isResult)
                            {
                                targetDamage.Add(damageValue, damage.messageLayerMask);
                                targetDamages.SetComponentEnabled(simulationEvent.entity, true);
                            }

                            if (characterBody.IsValid)
                            {
                                dropDamageValue = (int)math.ceil(damage.valueToDrop * instanceDamage.scale);

                                if (dropDamageValue != 0 && dropToDamages.HasComponent(simulationEvent.entity))
                                {
                                    isResult = true;
                                    
                                    totalDamageValue += dropDamageValue;

                                    ref var dropToDamage = ref dropToDamages.GetRefRW(simulationEvent.entity).ValueRW;

                                    dropToDamage.Add(dropDamageValue, damage.messageLayerMask);

                                    dropToDamage.isGrounded = characterBody.ValueRO.IsGrounded;

                                    dropToDamages.SetComponentEnabled(simulationEvent.entity, true);
                                }
                            }

                            if (result)
                                ++totalCount;
                        }
                        else
                            damageValue = 0;

                        if (characterBody.IsValid)
                        {
                            ref var characterBodyRW = ref characterBody.ValueRW;
                            if (characterBodyRW.IsGrounded &&
                                (damage.explosion > math.FLT_MIN_NORMAL ||
                                 damage.spring > math.FLT_MIN_NORMAL))
                            {
                                forceResult = float3.zero;
                                if ( /*effect.suction > math.FLT_MIN_NORMAL || */damage.explosion > math.FLT_MIN_NORMAL)
                                {
                                    force = destination.Position - source.Position;
                                    force -= math.project(force, characterBodyRW.GroundingUp);
                                    lengthSQ = math.lengthsq(force);
                                    if (lengthSQ > math.FLT_MIN_NORMAL)
                                    {
                                        forceResult += force * math.rsqrt(lengthSQ) * damage.explosion;

                                        //result.force -= force * effect.suction;
                                    }
                                }

                                if (damage.spring > math.FLT_MIN_NORMAL)
                                    forceResult += characterBodyRW.GroundingUp * damage.spring;

                                if (math.lengthsq(forceResult) > math.FLT_MIN_NORMAL)
                                {
                                    forceResult = math.rotate(source.Value, forceResult);

                                    mass = characterProperties.HasComponent(simulationEvent.entity)
                                        ? characterProperties[simulationEvent.entity].Mass
                                        : 0;
                                    if (mass > math.FLT_MIN_NORMAL)
                                    {
                                        forceResult /= mass;
                                        CharacterControlUtilities.StandardJump(
                                            ref characterBodyRW,
                                            forceResult,
                                            true,
                                            MathUtilities.ClampToMaxLength(forceResult, 1.0f));
                                    }

                                    isResult = true;
                                }
                            }
                        }

                        __Drop(
                            math.RigidTransform(destination.Value),
                            simulationEvent.entity,
                            instanceDamage, 
                            instanceDamageParent, 
                            prefabs,
                            ref damage.prefabs);

                        if (isResult)
                        {
                            enabledFlags |= EnabledFlags.StatusTarget;

                            numMessageIndices = damage.messageIndices.Length;
                            for (i = 0; i < numMessageIndices; ++i)
                            {
                                inputMessage = inputMessages[damage.messageIndices[i]];
                                outputMessage.key = random.NextInt();
                                outputMessage.name = inputMessage.name;
                                outputMessage.value = inputMessage.value;

                                if (inputMessage.entityPrefabReference.Equals(default))
                                {
                                    enabledFlags |= EnabledFlags.Message;

                                    outputMessages.Add(outputMessage);
                                }
                                else if ((damageValue != 0 || outputMessage.name.IsEmpty) &&
                                         prefabLoader.TryGetOrLoadPrefabRoot(
                                             inputMessage.entityPrefabReference,
                                             out instance))
                                {
                                    instance = entityManager.Instantiate(0, instance);

                                    if (!outputMessage.name.IsEmpty)
                                    {
                                        entityManager.SetBuffer<Message>(1, instance).Add(outputMessage);
                                        entityManager.SetComponentEnabled<Message>(1, instance, true);

                                        messageParameter.messageKey = outputMessage.key;
                                        messageParameter.value = -damageValue;
                                        messageParameter.id = (int)EffectAttributeID.Damage;
                                        entityManager.SetBuffer<MessageParameter>(1, instance)
                                            .Add(messageParameter);
                                    }

                                    entityManager.SetComponent(1, instance,
                                        LocalTransform.FromPositionRotation(destination.Position,
                                            inverseCameraRotation));
                                }
                            }
                        }
                    }

                    if(totalDamageValue != 0)
                        EffectDamageStatistic.Add(
                            totalCount, 
                            totalDamageValue, 
                            instanceDamageParent, 
                            damageParentMap, 
                            ref damageStatistics);
                }

                resultCount = resultCount > 0 ? resultCount - 1 : entityCount;
                if (resultCount > 0)
                {
                    var transform = math.RigidTransform(source.Value);
                    
                    int count = resultCount + status.count;
                    if (count < effect.count || effect.count < 1)
                    {
                        if(effect.time > math.FLT_MIN_NORMAL)
                            statusTargets.Clear();

                        status.time += (count - status.count) * effect.time;
                        status.count = count;
                    }
                    else
                    {
                        status.time += (effect.count - status.count) * effect.time;
                        status.count = 0;

                        if (++status.index == numEffects)
                        {
                            if (index < simulationCollisions.Length)
                            {
                                var closestHit = simulationCollisions[index].closestHit;
                                if (closestHit.Entity != Entity.Null)
                                {
                                    transform = math.RigidTransform(
                                        Math.FromToRotation(math.up(), closestHit.SurfaceNormal),
                                        closestHit.Position);
                                    
                                    LocalToWorld localToWorld;
                                    localToWorld.Value = math.float4x4(transform);
                                    localToWorlds[entity] = localToWorld;
                                }
                            }

                            entityManager.DestroyEntity(int.MaxValue, entity);

                            enabledFlags |= EnabledFlags.Destroyed;
                        }
                        else
                        {
                            //statusTargets.Clear();
                            
                            status.time += definition.effects[status.index].startTime;
                        }
                    }
                    
                    for(int i = 0; i < resultCount; ++i)
                        __Drop(
                            transform,
                            entity, 
                            instanceDamage, 
                            instanceDamageParent, 
                            prefabs,
                            ref effect.prefabs);
                }
            }
            else
                status.time = time + effect.startTime;

            states[index] = status;

            return enabledFlags;
        }

        public void __Drop(
            in RigidTransform transform, 
            in Entity parent, 
            in EffectDamage instanceDamage, 
            in EffectDamageParent instanceDamageParent, 
            in DynamicBuffer<EffectPrefab> prefabs, 
            ref BlobArray<EffectDefinition.Prefab> prefabsDefinition)
        {
            Parent instanceParent;
            instanceParent.Value = parent;
            
            DamageInstance damageInstance;
            damageInstance.index = instanceDamageParent.index;
            damageInstance.scale = instanceDamage.scale;
            damageInstance.entity = instanceDamageParent.entity;
            
            bool isContains = false;
            int numPrefabs = prefabsDefinition.Length;
            float chance = random.NextFloat(), totalChance = 0.0f;
            Entity instance;
            for (int i = 0; i < numPrefabs; ++i)
            {
                ref var prefab = ref prefabsDefinition[i];
                totalChance += prefab.chance;
                if (totalChance > 1.0f)
                {
                    totalChance -= 1.0f;

                    chance = random.NextFloat();

                    isContains = false;
                }

                if (isContains || totalChance < chance)
                    continue;

                damageInstance.entityPrefabReference = prefabs[prefab.index].entityPrefabReference;
                if (prefabLoader.TryGetOrLoadPrefabRoot(
                        damageInstance.entityPrefabReference, out instance))
                {
                    instance = entityManager.Instantiate(0, instance);

                    switch (prefab.space)
                    {
                        case EffectSpace.World:
                            entityManager.SetComponent(2, instance,
                                LocalTransform.FromPositionRotation(transform.pos,
                                    transform.rot));

                            break;
                        case EffectSpace.Local:
                            entityManager.AddComponent(2, instance, instanceParent);

                            break;
                    }
                    
                    if(instanceDamageParent.entity != Entity.Null)
                        entityManager.AddComponent(2, instance, instanceDamageParent);
                    
                    entityManager.AddComponent(2, instance, instanceDamage);
                }
                else
                {
                    switch (prefab.space)
                    {
                        case EffectSpace.World:
                            damageInstance.parent = Entity.Null;
                            damageInstance.transform = transform;

                            damageInstances.Enqueue(damageInstance);

                            break;
                        case EffectSpace.Local:
                            damageInstance.parent = parent;
                            damageInstance.transform = RigidTransform.identity;

                            damageInstances.Enqueue(damageInstance);

                            break;
                    }
                }

                isContains = true;
            }

        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public float deltaTime;
        public double time;

        public quaternion inverseCameraRotation;

        [ReadOnly]
        public ComponentLookup<EffectDamageParent> damageParents;

        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterProperties> characterProperties;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> damages;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<EffectDamageParent> damageParentType;

        [ReadOnly]
        public ComponentTypeHandle<EffectDefinitionData> instanceType;

        [ReadOnly] 
        public ComponentTypeHandle<SimulationCollision> simulationCollisionType;

        [ReadOnly] 
        public BufferTypeHandle<SimulationEvent> simulationEventType;

        [ReadOnly] 
        public BufferTypeHandle<EffectPrefab> prefabType;

        [ReadOnly] 
        public BufferTypeHandle<EffectMessage> inputMessageType;

        public BufferTypeHandle<Message> outputMessageType;

        public BufferTypeHandle<EffectStatusTarget> statusTargetType;

        public ComponentTypeHandle<EffectStatus> statusType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetDamage> targetDamages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<DelayDestroy> delayDestroies;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<DropToDamage> dropToDamages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalToWorld> localToWorlds;

        [NativeDisableParallelForRestriction]
        public BufferLookup<EffectDamageStatistic> damageStatistics;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;

        public NativeQueue<DamageInstance>.ParallelWriter damageInstances;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ulong hash = math.asulong(time);
            
            Collect collect;
            collect.deltaTime = deltaTime;
            collect.time = time;
            collect.random = Random.CreateFromIndex((uint)hash ^ (uint)(hash >> 32) ^ (uint)unfilteredChunkIndex);
            collect.inverseCameraRotation = inverseCameraRotation;
            collect.damageParentMap = damageParents;
            collect.physicsColliders = physicsColliders;
            collect.characterProperties = characterProperties;
            collect.damages = damages;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.damageParents = chunk.GetNativeArray(ref damageParentType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.simulationCollisions = chunk.GetNativeArray(ref simulationCollisionType);
            collect.simulationEvents = chunk.GetBufferAccessor(ref simulationEventType);
            collect.prefabs = chunk.GetBufferAccessor(ref prefabType);
            collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            collect.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            collect.statusTargets = chunk.GetBufferAccessor(ref statusTargetType);
            collect.states = chunk.GetNativeArray(ref statusType);
            collect.targetDamages = targetDamages;
            collect.delayDestroies = delayDestroies;
            collect.dropToDamages = dropToDamages;
            collect.characterBodies = characterBodies;
            collect.localToWorlds = localToWorlds;
            collect.damageStatistics = damageStatistics;
            collect.entityManager = entityManager;
            collect.prefabLoader = prefabLoader;
            collect.damageInstances = damageInstances;

            EnabledFlags enabledFlags;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                enabledFlags = collect.Execute(i);
                if((enabledFlags & EnabledFlags.Destroyed) == EnabledFlags.Destroyed)
                    chunk.SetComponentEnabled(ref statusType, i, false);
                
                if((enabledFlags & EnabledFlags.Message) == EnabledFlags.Message)
                    chunk.SetComponentEnabled(ref outputMessageType, i, true);
                
                if((enabledFlags & EnabledFlags.StatusTarget) == EnabledFlags.StatusTarget)
                    chunk.SetComponentEnabled(ref statusTargetType, i, true);
            }
        }
    }

    private struct Apply
    {
        public double time;

        public quaternion inverseCameraRotation;

        public Random random;
        
        [NativeDisableParallelForRestriction]
        public RefRW<LevelStatus> levelStatus;

        [ReadOnly]
        public BufferAccessor<EffectTargetMessage> targetMessages;

        [ReadOnly]
        public BufferAccessor<Child> children;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<LocalToWorld> localToWorlds;

        [ReadOnly]
        public NativeArray<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public NativeArray<EffectTargetData> instances;

        [ReadOnly] 
        public NativeArray<EffectTargetLevel> targetLevels;

        [ReadOnly] 
        public NativeArray<EffectTargetDamageScale> targetDamageScales;

        [ReadOnly]
        public NativeArray<EffectTargetInvulnerabilityDefinitionData> targetInvulnerabilities;

        public NativeArray<EffectTargetInvulnerabilityStatus> targetInvulnerabilityStates;

        public NativeArray<EffectTargetDamage> targetDamages;

        public NativeArray<EffectTargetHP> targetHPs;

        public NativeArray<EffectTarget> targets;

        public NativeArray<ThirdPersionCharacterGravityFactor> characterGravityFactors;

        public BufferAccessor<Message> messages;

        public BufferAccessor<MessageParameter> messageParameters;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;

        public EnabledFlags Execute(int index)
        {
            EnabledFlags result = 0;
            var target = targets[index];
            if (target.invincibleTime < time)
            {
                var instance = instances[index];
                
                var targetHP = targetHPs[index];
                if (targetHP.value != 0)
                {
                    target.hp += targetHP.value;

                    target.invincibleTime = time + instance.recoveryInvincibleTime;
                }

                var targetDamage = targetDamages[index];
                
                int damage = (int)math.ceil(targetDamage.value *
                                            (index < targetDamageScales.Length
                                                ? targetDamageScales[index].value
                                                : 1.0f));
                if (damage > 0 && index < targetInvulnerabilityStates.Length)
                {
                    bool isInvulnerability;
                    var targetInvulnerabilityStatus = targetInvulnerabilityStates[index];
                    if (targetInvulnerabilityStatus.damage > damage || targetInvulnerabilityStatus.times > 0)
                    {
                        if (targetInvulnerabilityStatus.damage > damage)
                            targetInvulnerabilityStatus.damage -= damage;
                        else if (targetInvulnerabilityStatus.damage > 0)
                        {
                            damage = targetInvulnerabilityStatus.damage;

                            targetInvulnerabilityStatus.damage = 0;
                        }

                        if (targetInvulnerabilityStatus.times > 0)
                            --targetInvulnerabilityStatus.times;

                        isInvulnerability = true;
                    }
                    else
                        isInvulnerability = false;

                    if (targetInvulnerabilityStatus.damage == 0 &&
                        targetInvulnerabilityStatus.times == 0 &&
                        index < targetInvulnerabilities.Length)
                    {
                        ref var definition = ref targetInvulnerabilities[index].definition.Value;
                        while (definition.invulnerabilities.Length > targetInvulnerabilityStatus.index)
                        {
                            ref var invulnerablilitity =
                                ref definition.invulnerabilities[targetInvulnerabilityStatus.index];

                            if (isInvulnerability)
                            {
                                target.invincibleTime = time + invulnerablilitity.time;

                                ++targetInvulnerabilityStatus.count;
                            }

                            if (invulnerablilitity.count == 0 ||
                                invulnerablilitity.count > targetInvulnerabilityStatus.count)
                            {
                                targetInvulnerabilityStatus.damage = invulnerablilitity.damage;
                                targetInvulnerabilityStatus.times = invulnerablilitity.times;

                                if (!isInvulnerability)
                                {
                                    if (targetInvulnerabilityStatus.damage > damage)
                                        targetInvulnerabilityStatus.damage -= damage;
                                    else if (targetInvulnerabilityStatus.damage > 0)
                                    {
                                        damage = targetInvulnerabilityStatus.damage;

                                        targetInvulnerabilityStatus.damage = 0;
                                    }

                                    if (targetInvulnerabilityStatus.times > 0)
                                        --targetInvulnerabilityStatus.times;

                                    isInvulnerability = targetInvulnerabilityStatus.damage == 0 &&
                                                        targetInvulnerabilityStatus.times == 0;

                                    if (isInvulnerability)
                                        continue;
                                }

                                break;
                            }

                            targetInvulnerabilityStatus.count = 0;
                            ++targetInvulnerabilityStatus.index;
                        }
                    }


                    targetInvulnerabilityStates[index] = targetInvulnerabilityStatus;
                }

                target.hp += -damage;
                
                Message message;
                var messages = index < this.messages.Length ? this.messages[index] : default;
                if (index < targetMessages.Length && 
                    (targetHP.value != 0 && targetHP.layerMask != 0 || 
                     targetDamage.value != 0 && targetDamage.layerMask != 0))
                {
                    float3 position = localToWorlds[index].Position;
                    Entity messageReceiver;
                    MessageParameter messageParameter;
                    var messageParameters = index < this.messageParameters.Length
                        ? this.messageParameters[index]
                        : default;
                    var targetMessages = this.targetMessages[index];
                    foreach (var targetMessage in targetMessages)
                    {
                        if (targetMessage.layerMask == 0 ||
                            (targetMessage.layerMask & (targetHP.layerMask | targetDamage.layerMask)) != 0)
                        {
                            message.key = random.NextInt();
                            message.name = targetMessage.messageName;
                            message.value = targetMessage.messageValue;

                            if (!targetMessage.entityPrefabReference.Equals(default))
                            {
                                if (prefabLoader.TryGetOrLoadPrefabRoot(
                                        targetMessage.entityPrefabReference, out messageReceiver))
                                {
                                    messageReceiver = entityManager.Instantiate(0, messageReceiver);

                                    if (!targetMessage.messageName.IsEmpty)
                                    {
                                        entityManager.SetBuffer<Message>(1, messageReceiver).Add(message);
                                        entityManager.SetComponentEnabled<Message>(1, messageReceiver, true);

                                        messageParameter.messageKey = message.key;
                                        messageParameter.value = -targetDamage.value;
                                        messageParameter.id = (int)EffectAttributeID.Damage;
                                        entityManager.SetBuffer<MessageParameter>(1, messageReceiver)
                                            .Add(messageParameter);
                                    }

                                    entityManager.SetComponent(1, messageReceiver,
                                        LocalTransform.FromPositionRotation(position,
                                            inverseCameraRotation));
                                }
                            }
                            else if (index < this.messages.Length)
                            {
                                messages.Add(message);

                                if (messageParameters.IsCreated)
                                {
                                    messageParameter.messageKey = message.key;

                                    if ((targetMessage.layerMask & targetDamage.layerMask) != 0)
                                    {
                                        messageParameter.value = -targetDamage.value;
                                        messageParameter.id = (int)EffectAttributeID.Damage;
                                        messageParameters.Add(messageParameter);
                                    }

                                    messageParameter.value = target.hp;
                                    messageParameter.id = (int)EffectAttributeID.HP;
                                    messageParameters.Add(messageParameter);
                                }

                                result |= EnabledFlags.Message;
                            }
                        }
                    }
                }

                targetHP.value = 0;
                if (target.hp <= 0)
                {
                    if (target.times > 0 && instance.recoveryChance > random.NextFloat())
                    {
                        if (index < characterGravityFactors.Length)
                        {
                            ThirdPersionCharacterGravityFactor characterGravityFactor;
                            characterGravityFactor.value = 1.0f;
                            characterGravityFactors[index] = characterGravityFactor;
                        }

                        --target.times;

                        target.invincibleTime = time + instance.recoveryTime;
                        target.hp = 0;
                        
                        targetHP.value = instance.hpMax;

                        result |= EnabledFlags.Recovery;

                        if (!instance.recoveryMessageName.IsEmpty && messages.IsCreated)
                        {
                            message.key = 0;
                            message.name = instance.recoveryMessageName;
                            message.value = instance.recoveryMessageValue;
                            messages.Add(message);
                        }
                    }
                    else
                    {
                        if (index < targetLevels.Length && this.levelStatus.IsValid)
                        {
                            var targetLevel = targetLevels[index];

                            ref var levelStatus = ref this.levelStatus.ValueRW;
                            Interlocked.Add(ref levelStatus.value, targetLevel.value);
                            Interlocked.Add(ref levelStatus.exp, targetLevel.exp);
                            Interlocked.Add(ref levelStatus.gold, targetLevel.gold);

                            Interlocked.Increment(ref levelStatus.count);
                        }

                        if (index < characterBodies.Length && !characterBodies[index].IsGrounded)
                        {
                            if (index < characterGravityFactors.Length)
                            {
                                ThirdPersionCharacterGravityFactor characterGravityFactor;
                                characterGravityFactor.value = 1.0f;
                                characterGravityFactors[index] = characterGravityFactor;
                            }

                            entityManager.AddComponent<FallToDestroy>(0, entityArray[index]);
                        }
                        else
                        {
                            if (index < children.Length)
                            {
                                var children = this.children[index];
                                foreach (var child in children)
                                    entityManager.DestroyEntity(int.MaxValue - 1, child.Value);
                            }

                            entityManager.DestroyEntity(int.MaxValue, entityArray[index]);
                        }
                    }
                }

                targets[index] = target;

                targetHPs[index] = targetHP;
            }

            targetDamages[index] = default;

            return result;
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public double time;
        
        public quaternion inverseCameraRotation;
        
        public Entity levelStatusEntity;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LevelStatus> levelStates;
        
        [ReadOnly]
        public BufferTypeHandle<EffectTargetMessage> targetMessageType;

        [ReadOnly]
        public BufferTypeHandle<Child> childType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        [ReadOnly]
        public ComponentTypeHandle<EffectTargetData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<EffectTargetLevel> targetLevelType;

        [ReadOnly] 
        public ComponentTypeHandle<EffectTargetDamageScale> targetDamageScaleType;

        [ReadOnly]
        public ComponentTypeHandle<EffectTargetInvulnerabilityDefinitionData> targetInvulnerabilityType;

        public ComponentTypeHandle<EffectTargetInvulnerabilityStatus> targetInvulnerabilityStatusType;

        public ComponentTypeHandle<EffectTargetDamage> targetDamageType;

        public ComponentTypeHandle<EffectTargetHP> targetHPType;

        public ComponentTypeHandle<EffectTarget> targetType;

        public ComponentTypeHandle<ThirdPersionCharacterGravityFactor> characterGravityFactorType;

        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        public BufferTypeHandle<Message> messageType;

        public BufferTypeHandle<MessageParameter> messageParameterType;

        public EntityCommandBuffer.ParallelWriter entityManager;
        
        public PrefabLoader.ParallelWriter prefabLoader;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ulong hash = math.asulong(time);
            
            Apply apply;
            apply.time = time;
            apply.inverseCameraRotation = inverseCameraRotation;
            apply.random = Random.CreateFromIndex((uint)hash ^ (uint)(hash >> 32) ^ (uint)unfilteredChunkIndex);
            apply.levelStatus = levelStates.HasComponent(levelStatusEntity) ? levelStates.GetRefRW(levelStatusEntity) : default;
            apply.targetMessages = chunk.GetBufferAccessor(ref targetMessageType);
            apply.children = chunk.GetBufferAccessor(ref childType);
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.localToWorlds = chunk.GetNativeArray(ref localToWorldType);
            apply.characterBodies = chunk.GetNativeArray(ref characterBodyType);
            apply.instances = chunk.GetNativeArray(ref instanceType);
            apply.targetLevels = chunk.GetNativeArray(ref targetLevelType);
            apply.targetDamageScales = chunk.GetNativeArray(ref targetDamageScaleType);
            apply.targetInvulnerabilities = chunk.GetNativeArray(ref targetInvulnerabilityType);
            apply.targetInvulnerabilityStates = chunk.GetNativeArray(ref targetInvulnerabilityStatusType);
            apply.targetHPs = chunk.GetNativeArray(ref targetHPType);
            apply.targetDamages = chunk.GetNativeArray(ref targetDamageType);
            apply.targets = chunk.GetNativeArray(ref targetType);
            apply.characterGravityFactors = chunk.GetNativeArray(ref characterGravityFactorType);
            apply.messages = chunk.GetBufferAccessor(ref messageType);
            apply.messageParameters = chunk.GetBufferAccessor(ref messageParameterType);
            apply.entityManager = entityManager;
            apply.prefabLoader = prefabLoader;

            bool isRecovery;
            EnabledFlags result;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                result = apply.Execute(i);
                
                if((result & EnabledFlags.Message) == EnabledFlags.Message)
                    chunk.SetComponentEnabled(ref messageType, i, true);

                isRecovery = (result & EnabledFlags.Recovery) == EnabledFlags.Recovery;
                if (isRecovery)
                {
                    chunk.SetComponentEnabled(ref characterBodyType, i, false);

                    chunk.SetComponentEnabled(ref targetHPType, i, true);
                }
                else if(!chunk.IsComponentEnabled(ref targetHPType, i))
                    chunk.SetComponentEnabled(ref characterBodyType, i, true);

                chunk.SetComponentEnabled(ref targetDamageType, i, false);
            }
        }
    }

    private ComponentLookup<PhysicsCollider> __physicsColliders;

    private ComponentLookup<KinematicCharacterProperties> __characterProperties;

    private ComponentLookup<LevelStatus> __levelStates;

    private ComponentLookup<EffectDamage> __damages;

    private ComponentLookup<EffectDamageParent> __damageParents;

    private EntityTypeHandle __entityType;
    
    private BufferTypeHandle<Child> __childType;

    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    private ComponentTypeHandle<EffectDamageParent> __damageParentType;

    private ComponentTypeHandle<EffectDefinitionData> __instanceType;

    private ComponentTypeHandle<EffectTargetData> __targetInstanceType;

    private ComponentTypeHandle<SimulationCollision> __simulationCollisionType;

    private BufferTypeHandle<SimulationEvent> __simulationEventType;

    private BufferTypeHandle<MessageParameter> __messageParameterType;

    private BufferTypeHandle<Message> __outputMessageType;

    private BufferTypeHandle<EffectMessage> __inputMessageType;

    private BufferTypeHandle<EffectTargetMessage> __targetMessageType;
    
    private BufferTypeHandle<EffectPrefab> __prefabType;

    private BufferTypeHandle<EffectStatusTarget> __statusTargetType;

    private ComponentTypeHandle<EffectStatus> __statusType;

    private ComponentTypeHandle<EffectTargetLevel> __targetLevelType;

    private ComponentTypeHandle<EffectTargetInvulnerabilityDefinitionData> __targetInvulnerabilityType;

    private ComponentTypeHandle<EffectTargetInvulnerabilityStatus> __targetInvulnerabilityStatusType;

    private ComponentTypeHandle<EffectTargetDamageScale> __targetDamageScaleType;

    private ComponentTypeHandle<EffectTargetDamage> __targetDamageType;
    private ComponentTypeHandle<EffectTargetHP> __targetHPType;

    private ComponentTypeHandle<EffectTarget> __targetType;

    private ComponentTypeHandle<KinematicCharacterBody> __characterBodyType;

    private ComponentTypeHandle<ThirdPersionCharacterGravityFactor> __characterGravityFactorType;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private ComponentLookup<EffectTargetDamage> __targetDamages;

    private ComponentLookup<DelayDestroy> __delayDestroies;
    
    private ComponentLookup<DropToDamage> __dropToDamages;

    private ComponentLookup<LocalToWorld> __localToWorlds;

    private BufferLookup<EffectDamageStatistic> __damageStatistics;

    private EntityQuery __groupToDestroy;

    private EntityQuery __groupToClear;

    private EntityQuery __groupToCollect;

    private EntityQuery __groupToApply;
    
    private PrefabLoader __prefabLoader;
    
    private NativeQueue<DamageInstance> __damageInstances;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __characterProperties = state.GetComponentLookup<KinematicCharacterProperties>(true);
        __levelStates = state.GetComponentLookup<LevelStatus>();
        __damages = state.GetComponentLookup<EffectDamage>(true);
        __damageParents = state.GetComponentLookup<EffectDamageParent>(true);
        __entityType = state.GetEntityTypeHandle();
        __childType = state.GetBufferTypeHandle<Child>(true);
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>(true);
        __damageParentType = state.GetComponentTypeHandle<EffectDamageParent>(true);
        __instanceType = state.GetComponentTypeHandle<EffectDefinitionData>(true);
        __targetInstanceType = state.GetComponentTypeHandle<EffectTargetData>(true);
        __simulationCollisionType = state.GetComponentTypeHandle<SimulationCollision>(true);
        __simulationEventType = state.GetBufferTypeHandle<SimulationEvent>(true);
        __messageParameterType = state.GetBufferTypeHandle<MessageParameter>();
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __inputMessageType = state.GetBufferTypeHandle<EffectMessage>(true);
        __targetMessageType = state.GetBufferTypeHandle<EffectTargetMessage>(true);
        __prefabType = state.GetBufferTypeHandle<EffectPrefab>(true);
        __statusTargetType = state.GetBufferTypeHandle<EffectStatusTarget>();
        __statusType = state.GetComponentTypeHandle<EffectStatus>();
        __targetLevelType = state.GetComponentTypeHandle<EffectTargetLevel>(true);
        __targetInvulnerabilityType = state.GetComponentTypeHandle<EffectTargetInvulnerabilityDefinitionData>(true);
        __targetInvulnerabilityStatusType = state.GetComponentTypeHandle<EffectTargetInvulnerabilityStatus>();
        __targetDamageScaleType = state.GetComponentTypeHandle<EffectTargetDamageScale>();
        __targetDamageType = state.GetComponentTypeHandle<EffectTargetDamage>();
        __targetHPType = state.GetComponentTypeHandle<EffectTargetHP>();
        __targetType = state.GetComponentTypeHandle<EffectTarget>();
        __characterBodyType = state.GetComponentTypeHandle<KinematicCharacterBody>();
        __characterGravityFactorType = state.GetComponentTypeHandle<ThirdPersionCharacterGravityFactor>();
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>();
        __targetDamages = state.GetComponentLookup<EffectTargetDamage>();
        __delayDestroies = state.GetComponentLookup<DelayDestroy>();
        __dropToDamages = state.GetComponentLookup<DropToDamage>();
        __localToWorlds = state.GetComponentLookup<LocalToWorld>();
        __damageStatistics = state.GetBufferLookup<EffectDamageStatistic>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDestroy = builder
                .WithAll<FallToDestroy>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToClear = builder
                .WithAllRW<EffectStatusTarget>()
                .WithPresent<SimulationEvent>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCollect = builder
                .WithAll<EffectDefinitionData>()
                .WithAllRW<EffectStatus>()
                .WithPresentRW<EffectStatusTarget>()
                .WithAny<EffectPrefab, SimulationEvent>()
                .Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToApply = builder
                .WithAllRW<EffectTarget>()
                .WithAny<EffectTargetDamage, EffectTargetHP>()
                .Build(ref state);
        
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();

        __prefabLoader = new PrefabLoader(ref state);

        __damageInstances = new NativeQueue<DamageInstance>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __damageInstances.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityCommandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        var entityManager = entityCommandBuffer.AsParallelWriter();

        Instantiate instantiate;
        instantiate.damageInstances = __damageInstances;
        instantiate.entityManager = entityCommandBuffer;
        instantiate.prefabLoader = __prefabLoader.AsWriter();
        var jobHandle = instantiate.Schedule(state.Dependency);
            
        __entityType.Update(ref state);
        __childType.Update(ref state);
        __characterBodyType.Update(ref state);
        
        DestroyEx destroy;
        destroy.entityType = __entityType;
        destroy.characterBodyType = __characterBodyType;
        destroy.childType = __childType;
        destroy.entityManager = entityManager;
        jobHandle = destroy.ScheduleParallelByRef(__groupToDestroy, jobHandle);

        __instanceType.Update(ref state);
        __simulationEventType.Update(ref state);
        __statusTargetType.Update(ref state);
        __statusType.Update(ref state);
        
        double time = SystemAPI.Time.ElapsedTime;

        ClearEx clear;
        clear.time = time;
        clear.instanceType = __instanceType;
        clear.simulationEventType = __simulationEventType;
        clear.statusTargetType = __statusTargetType;
        clear.statusType = __statusType;
        jobHandle = clear.ScheduleParallelByRef(__groupToClear, jobHandle);

        __damageParents.Update(ref state);
        __physicsColliders.Update(ref state);
        __characterProperties.Update(ref state);
        __damages.Update(ref state);
        __damageParentType.Update(ref state);
        __simulationCollisionType.Update(ref state);
        __prefabType.Update(ref state);
        __outputMessageType.Update(ref state);
        __inputMessageType.Update(ref state);
        __targetDamages.Update(ref state);
        __delayDestroies.Update(ref state);
        __dropToDamages.Update(ref state);
        __characterBodies.Update(ref state);
        __localToWorlds.Update(ref state);
        __damageStatistics.Update(ref state);
        
        var prefabLoader = __prefabLoader.AsParallelWriter();

        quaternion inverseCameraRotation = SystemAPI.TryGetSingleton<MainCameraTransform>(out var mainCameraTransform)
            ? math.inverse(mainCameraTransform.value.rot)
            : quaternion.identity;
        CollectEx collect;
        collect.deltaTime = SystemAPI.Time.DeltaTime;
        collect.time = time;
        collect.inverseCameraRotation = inverseCameraRotation;
        collect.damageParents = __damageParents;
        collect.physicsColliders = __physicsColliders;
        collect.characterProperties = __characterProperties;
        collect.damages = __damages;
        collect.entityType = __entityType;
        collect.damageParentType = __damageParentType;
        collect.instanceType = __instanceType;
        collect.simulationCollisionType = __simulationCollisionType;
        collect.simulationEventType = __simulationEventType;
        collect.prefabType = __prefabType;
        collect.outputMessageType = __outputMessageType;
        collect.inputMessageType = __inputMessageType;
        collect.statusTargetType = __statusTargetType;
        collect.statusType = __statusType;
        collect.targetDamages = __targetDamages;
        collect.delayDestroies = __delayDestroies;
        collect.dropToDamages = __dropToDamages;
        collect.characterBodies = __characterBodies;
        collect.localToWorlds = __localToWorlds;
        collect.damageStatistics = __damageStatistics;
        collect.entityManager = entityManager;
        collect.prefabLoader = prefabLoader;
        collect.damageInstances = __damageInstances.AsParallelWriter();
        jobHandle = collect.ScheduleParallelByRef(__groupToCollect,  jobHandle);

        ApplyEx apply;
        SystemAPI.TryGetSingletonEntity<LevelStatus>(out apply.levelStatusEntity);
        __levelStates.Update(ref state);
        __localToWorldType.Update(ref state);
        __characterGravityFactorType.Update(ref state);
        __targetInstanceType.Update(ref state);
        __targetType.Update(ref state);
        __targetLevelType.Update(ref state);
        __targetInvulnerabilityType.Update(ref state);
        __targetInvulnerabilityStatusType.Update(ref state);
        __targetMessageType.Update(ref state);
        __targetDamageScaleType.Update(ref state);
        __targetDamageType.Update(ref state);
        __targetHPType.Update(ref state);
        __messageParameterType.Update(ref state);

        apply.time = time;
        apply.inverseCameraRotation = inverseCameraRotation;
        apply.levelStates = __levelStates;
        apply.targetMessageType = __targetMessageType;
        apply.childType = __childType;
        apply.entityType = __entityType;
        apply.localToWorldType = __localToWorldType;
        apply.instanceType = __targetInstanceType;
        apply.targetLevelType = __targetLevelType;
        apply.targetDamageScaleType = __targetDamageScaleType;
        apply.targetDamageType = __targetDamageType;
        apply.targetHPType = __targetHPType;
        apply.targetInvulnerabilityType = __targetInvulnerabilityType;
        apply.targetInvulnerabilityStatusType = __targetInvulnerabilityStatusType;
        apply.targetType = __targetType;
        apply.characterBodyType = __characterBodyType;
        apply.characterGravityFactorType = __characterGravityFactorType;
        apply.messageType = __outputMessageType;
        apply.messageParameterType = __messageParameterType;
        apply.entityManager = entityManager;
        apply.prefabLoader = prefabLoader;
        state.Dependency = apply.ScheduleParallelByRef(__groupToApply, jobHandle);
    }
}
