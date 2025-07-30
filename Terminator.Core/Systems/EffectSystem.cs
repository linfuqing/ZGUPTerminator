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
using ZG;
using Math = ZG.Mathematics.Math;
using Random = Unity.Mathematics.Random;

[BurstCompile, 
 CreateAfter(typeof(PrefabLoaderSystem)), 
 UpdateInGroup(typeof(TransformSystemGroup), OrderLast = true), 
 UpdateBefore(typeof(CopyMatrixToTransformSystem)), 
 UpdateAfter(typeof(AnimationCurveUpdateSystem))]
public partial struct EffectSystem : ISystem
{
    [Flags]
    private enum EnabledFlags
    {
        Message = 0x01,
        StatusTarget = 0x02, 
        Destroyed = 0x04, 
        Die = 0x08,
        Drop = 0x10 | Die,
        Recovery = 0x20, 
        Invincible = 0x40
    }
    
    private struct DamageInstance
    {
        public int index;
        public float scale;
        public RigidTransform transform;
        public Entity entity;
        public Entity parent;
        public BulletLayerMask bulletLayerMask;
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

                    damage.bulletLayerMask = damageInstance.bulletLayerMask;
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
        public NativeArray<KinematicCharacterBody> characterBodies;

        public bool Execute(int index)
        {
            return index >= characterBodies.Length || characterBodies[index].IsGrounded;
        }
    }

    [BurstCompile]
    private struct DestroyEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        public ComponentTypeHandle<EffectTarget> targetType;

        public ComponentTypeHandle<EffectTargetDamage> targetDamageType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Destroy destroy;
            destroy.characterBodies = chunk.GetNativeArray(ref characterBodyType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (destroy.Execute(i))
                {
                    chunk.SetComponentEnabled(ref targetType, i, true);
                    chunk.SetComponentEnabled(ref targetDamageType, i, true);
                }
            }
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
        public BufferLookup<Child> children;

        [ReadOnly]
        public ComponentLookup<EffectDamageParent> damageParentMap;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> damages;

        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterProperties> characterProperties;

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

        public BufferAccessor<EffectStatusTarget> statusTargets;

        public NativeArray<EffectStatus> states;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetDamage> targetDamages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetLevel> targetLevels;

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

        [NativeDisableParallelForRestriction]
        public BufferLookup<Message> outputMessages;

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
                    Entity messageEntity = this.outputMessages.HasBuffer(entity)
                        ? entity
                        : (instanceDamageParent.entity != entity &&
                           this.outputMessages.HasBuffer(instanceDamageParent.entity)
                            ? instanceDamageParent.entity
                            : Entity.Null);
                    var inputMessages = index < this.inputMessages.Length ? this.inputMessages[index] : default;
                    var outputMessages = messageEntity == Entity.Null
                        ? default
                        : this.outputMessages[messageEntity];
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
                        damageValueImmunized, 
                        dropDamageValue, 
                        //layerMask,
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
                            ref var damageTemp = ref definition.damages[damageIndex];
                            //layerMask = definition.damages[damageIndex].layerMask;
                            if ((damageTemp.layerMask == 0 || (damageTemp.layerMask & belongsTo) != 0) &&
                                damageTemp.bulletLayerMask.BelongsTo(instanceDamage.bulletLayerMask))
                                break;
                        }

                        if (i == numDamageIndices)
                            continue;

                        ref var damage = ref definition.damages[damageIndex];

                        isResult = false;

                        if (!localToWorlds.TryGetComponent(simulationEvent.entity, out destination))
                            destination.Value = float4x4.identity;

                        characterBody = characterBodies.GetRefRWOptional(simulationEvent.entity);
                        if (targetDamages.HasComponent(simulationEvent.entity))
                        {
                            //++times;

                            damageValue = (int)math.ceil(damage.value * instanceDamage.scale);

                            damageValueImmunized = (int)math.ceil(damage.valueImmunized * instanceDamage.scale);

                            totalDamageValue += damageValue + damageValueImmunized;

                            isResult = damageValue != 0 || damageValueImmunized != 0;

                            ref var targetDamage = ref targetDamages.GetRefRW(simulationEvent.entity).ValueRW;

                            if (isResult)
                            {
                                targetDamage.Add(damageValue,  damageValueImmunized, damage.messageLayerMask);
                                targetDamages.SetComponentEnabled(simulationEvent.entity, true);

                                damageValue += damageValueImmunized;
                            }

                            if (characterBody.IsValid)
                            {
                                dropDamageValue = (int)math.ceil(damage.valueToDrop * instanceDamage.scale);

                                if (dropDamageValue != 0 && dropToDamages.HasComponent(simulationEvent.entity))
                                {
                                    isResult = true;

                                    totalDamageValue += dropDamageValue;

                                    ref var dropToDamage = ref dropToDamages.GetRefRW(simulationEvent.entity).ValueRW;

                                    dropToDamage.Add(dropDamageValue, 0, damage.messageLayerMask);

                                    dropToDamage.isGrounded = characterBody.ValueRO.IsGrounded;

                                    dropToDamages.SetComponentEnabled(simulationEvent.entity, true);
                                }
                            }

                            //if (result)
                                ++totalCount;
                        }
                        else
                            damageValue = 0;

                        if (characterBody.IsValid)
                        {
                            ref var characterBodyRW = ref characterBody.ValueRW;
                            /*if (!characterBodyRW.IsGrounded)
                            {
                                //弹板
                                if (!isResult)
                                    continue;
                            }
                            else */if (damage.explosion > math.FLT_MIN_NORMAL ||
                                     damage.spring > math.FLT_MIN_NORMAL)
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
                            ref damageInstances,
                            ref entityManager, 
                            ref prefabLoader,
                            ref damage.prefabs, 
                            ref random);

                        if (damage.entityLayerMask == 0 || (damage.entityLayerMask & belongsTo) != 0)
                            ++entityCount;

                        statusTarget.entity = simulationEvent.entity;
                        statusTargets.Add(statusTarget);

                        enabledFlags |= EnabledFlags.StatusTarget;

                        if (isResult)
                        {
                            if (math.abs(damage.goldMultiplier) > math.FLT_MIN_NORMAL && targetLevels.HasComponent(simulationEvent.entity))
                            {
                                ref var targetLevel = ref targetLevels.GetRefRW(simulationEvent.entity).ValueRW;
                                int gold = targetLevel.gold;
                                float goldMultiplier = gold * damage.goldMultiplier;

                                gold = (int)math.select(math.floor(goldMultiplier), math.ceil(goldMultiplier),
                                    math.frac(goldMultiplier) > random.NextFloat()) - gold;
                                
                                Interlocked.Add(ref targetLevel.gold, gold);
                            }
                            
                            if (delayDestroies.HasComponent(simulationEvent.entity))
                            {
                                delayDestroyTime = damage.delayDestroyTime; // * damageScale;
                                if (math.abs(delayDestroyTime) > math.FLT_MIN_NORMAL)
                                    Math.InterlockedAdd(
                                        ref this.delayDestroies.GetRefRW(simulationEvent.entity).ValueRW.time,
                                        delayDestroyTime);
                            }
                        }

                        numMessageIndices = damage.messageIndices.Length;
                        for (i = 0; i < numMessageIndices; ++i)
                        {
                            inputMessage = inputMessages[damage.messageIndices[i]];
                            outputMessage.key = random.NextInt();
                            outputMessage.name = inputMessage.name;
                            outputMessage.value = inputMessage.value;

                            if (inputMessage.entityPrefabReference.Equals(default))
                            {
                                if (outputMessages.IsCreated)
                                {
                                    enabledFlags |= EnabledFlags.Message;

                                    outputMessages.Add(outputMessage);

                                    this.outputMessages.SetBufferEnabled(messageEntity, true);
                                }
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
                        resultCount = effect.count - status.count;

                        status.time += resultCount * effect.time;
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

                            __Destroy(int.MaxValue, entity, children, ref entityManager);

                            enabledFlags |= EnabledFlags.Destroyed;
                        }
                        else
                        {
                            statusTargets.Clear();
                            
                            status.time += definition.effects[status.index].startTime;
                        }
                    }

                    for (int i = 0; i < resultCount; ++i)
                        __Drop(
                            transform,
                            entity,
                            instanceDamage,
                            instanceDamageParent,
                            prefabs,
                            ref damageInstances,
                            ref entityManager, 
                            ref prefabLoader,
                            ref effect.prefabs, 
                            ref random);
                }
            }
            else
                status.time = time + effect.startTime;

            states[index] = status;

            return enabledFlags;
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public float deltaTime;
        public double time;

        public quaternion inverseCameraRotation;

        [ReadOnly] 
        public BufferLookup<Child> children;

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

        public BufferTypeHandle<EffectStatusTarget> statusTargetType;

        public ComponentTypeHandle<EffectStatus> statusType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetDamage> targetDamages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetLevel> targetLevels;

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

        [NativeDisableParallelForRestriction]
        public BufferLookup<Message> outputMessages;

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
            collect.children = children;
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
            collect.statusTargets = chunk.GetBufferAccessor(ref statusTargetType);
            collect.states = chunk.GetNativeArray(ref statusType);
            collect.targetDamages = targetDamages;
            collect.targetLevels = targetLevels;
            collect.delayDestroies = delayDestroies;
            collect.dropToDamages = dropToDamages;
            collect.characterBodies = characterBodies;
            collect.localToWorlds = localToWorlds;
            collect.damageStatistics = damageStatistics;
            collect.outputMessages = outputMessages;
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
                
                //if((enabledFlags & EnabledFlags.Message) == EnabledFlags.Message)
                //    chunk.SetComponentEnabled(ref outputMessageType, i, true);
                
                if((enabledFlags & EnabledFlags.StatusTarget) == EnabledFlags.StatusTarget)
                    chunk.SetComponentEnabled(ref statusTargetType, i, true);
            }
        }
    }

    private struct Apply
    {
        public bool isFallToDestroy;
        
        public float deltaTime;

        public double time;

        public quaternion inverseCameraRotation;

        public Random random;
        
        [NativeDisableParallelForRestriction]
        public RefRW<LevelStatus> levelStatus;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> damages;

        [ReadOnly]
        public ComponentLookup<EffectDamageParent> damageParentMap;

        [ReadOnly]
        public BufferLookup<Child> children;

        [ReadOnly] 
        public BufferAccessor<EffectPrefab> prefabs;

        [ReadOnly]
        public BufferAccessor<EffectTargetMessage> targetMessages;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<LocalToWorld> localToWorlds;

        [ReadOnly]
        public NativeArray<KinematicCharacterBody> characterBodies;

        [ReadOnly]
        public NativeArray<EffectDefinitionData> instances;

        [ReadOnly]
        public NativeArray<EffectDamageParent> damageParents;

        [ReadOnly] 
        public NativeArray<EffectTargetData> targetInstances;

        [ReadOnly] 
        public NativeArray<EffectTargetLevel> targetLevels;

        [ReadOnly] 
        public NativeArray<EffectTargetDamageScale> targetDamageScales;

        [ReadOnly]
        public NativeArray<EffectTargetImmunityDefinitionData> targetImmunities;

        public NativeArray<EffectTargetImmunityStatus> targetImmunityStates;

        public NativeArray<EffectTargetDamage> targetDamages;

        public NativeArray<EffectTargetHP> targetHPs;

        public NativeArray<EffectTarget> targets;

        public NativeArray<ThirdPersionCharacterGravityFactor> characterGravityFactors;

        public NativeArray<DelayDestroy> delayDestroys;

        public BufferAccessor<DelayTime> delayTimes;

        public BufferAccessor<Message> messages;

        public BufferAccessor<MessageParameter> messageParameters;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;

        public NativeQueue<DamageInstance>.ParallelWriter damageInstances;

        public EnabledFlags Execute(int index)
        {
            EnabledFlags result = 0;
            var target = targets[index];
            if (target.immunizedTime >= 0.0f)
                target.immunizedTime -= deltaTime;
            
            if (target.invincibleTime >= 0.0f)
                target.invincibleTime -= deltaTime;
            
            var targetDamage = targetDamages[index];
            var targetHP = targetHPs[index];
            if ((targetHP.value != 0 || 
                targetDamage.value != 0 || 
                target.immunizedTime < 0.0f) && 
                target.invincibleTime < 0.0f)
            {
                var targetInstance = targetInstances[index];

                int damage = (int)math.ceil((targetDamage.value + (target.immunizedTime > 0.0f ? 0.0f : targetDamage.valueImmunized)) *
                                        (index < targetDamageScales.Length
                                            ? targetDamageScales[index].value
                                            : 1.0f)), 
                    damageLayerMask;
                if (targetHP.value > targetDamage.value)
                {
                    targetHP.value -=  targetDamage.value;
                    
                    damage = 0;

                    damageLayerMask = targetHP.layerMask;

                    if (targetHP.value > 0)
                        target.hp += targetHP.value;
                    else
                    {
                        target.hp = targetHP.value;
                        
                        if (targetInstance.recoveryInvincibleTime > math.FLT_MIN_NORMAL)
                        {
                            target.invincibleTime = targetInstance.recoveryInvincibleTime;
                            result |= EnabledFlags.Invincible;
                        }
                    }

                    result |= EnabledFlags.Recovery;
                }
                else
                {
                    damageLayerMask = targetDamage.layerMask;

                    if (damage > 0 && index < targetImmunityStates.Length)
                    {
                        bool isImmunity;
                        var targetImmunityStatus = targetImmunityStates[index];
                        if (targetImmunityStatus.damage > damage || targetImmunityStatus.times > 0)
                        {
                            if (targetImmunityStatus.damage > damage)
                                targetImmunityStatus.damage -= damage;
                            else if (targetImmunityStatus.damage > 0)
                            {
                                damage = targetImmunityStatus.damage;

                                targetImmunityStatus.damage = 0;
                            }

                            if (targetImmunityStatus.times > 0)
                                --targetImmunityStatus.times;

                            isImmunity = true;
                        }
                        else
                            isImmunity = false;

                        if (targetImmunityStatus.damage == 0 &&
                            targetImmunityStatus.times == 0 &&
                            index < targetImmunities.Length)
                        {
                            ref var definition = ref targetImmunities[index].definition.Value;
                            while (definition.immunities.Length > targetImmunityStatus.index)
                            {
                                ref var immunity =
                                    ref definition.immunities[targetImmunityStatus.index];

                                if (isImmunity)
                                {
                                    if (immunity.time > math.FLT_MIN_NORMAL)
                                    {
                                        target.immunizedTime = immunity.time;
                                        
                                        result |= EnabledFlags.Invincible;
                                    }

                                    ++targetImmunityStatus.count;
                                }

                                if (immunity.count == 0 ||
                                    immunity.count > targetImmunityStatus.count)
                                {
                                    targetImmunityStatus.damage = immunity.damage;
                                    targetImmunityStatus.times = immunity.times;

                                    if (!isImmunity)
                                    {
                                        if (targetImmunityStatus.damage > damage)
                                            targetImmunityStatus.damage -= damage;
                                        else if (targetImmunityStatus.damage > 0)
                                        {
                                            damage = targetImmunityStatus.damage;

                                            targetImmunityStatus.damage = 0;
                                        }

                                        if (targetImmunityStatus.times > 0)
                                            --targetImmunityStatus.times;

                                        isImmunity = targetImmunityStatus.damage == 0 &&
                                                     targetImmunityStatus.times == 0;

                                        if (isImmunity)
                                            continue;
                                    }

                                    break;
                                }

                                targetImmunityStatus.count = 0;
                                ++targetImmunityStatus.index;
                            }
                        }

                        targetImmunityStates[index] = targetImmunityStatus;
                    }

                    if (target.hp > 0)
                        target.hp += -damage;
                    else
                        damage = 0;
                }

                float delayTime = 0.0f, deadTime = 0.0f;
                Message message;
                var messages = index < this.messages.Length ? this.messages[index] : default;
                if (index < targetMessages.Length &&
                    (targetHP.value != 0 && targetHP.layerMask != 0 ||
                     damage != 0 && damageLayerMask != 0))
                {
                    bool isSelected = false;
                    float chance = random.NextFloat(), totalChance = 0.0f;
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
                            (targetMessage.layerMask & (targetHP.layerMask | damageLayerMask)) != 0)
                        {
                            totalChance += targetMessage.chance;
                            if (totalChance > 1.0f)
                            {
                                totalChance -= 1.0f;
                                
                                chance = random.NextFloat();

                                isSelected = false;
                            }
                            
                            if(isSelected)
                                continue;

                            if (totalChance > chance)
                                isSelected = true;
                            else
                                continue;
                            
                            if (targetMessage.deadTime > math.FLT_MIN_NORMAL && target.hp > 0)
                                continue;

                            deadTime = math.max(deadTime, targetMessage.deadTime);
                            delayTime = math.max(delayTime, targetMessage.delayTime);

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
                                        messageParameter.value = -damage;
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

                                    if ((targetMessage.layerMask & damageLayerMask) != 0)
                                    {
                                        messageParameter.value = -damage;
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
                if (target.hp > 0 && !isFallToDestroy)
                {
                    if (delayTime > math.FLT_MIN_NORMAL)
                    {
                        var delayTimes = index < this.delayTimes.Length ? this.delayTimes[index] : default;
                        DelayTime.Append(ref delayTimes, time, delayTime);
                    }
                }
                else
                {
                    if (deadTime > math.FLT_MIN_NORMAL)
                    {
                        result |= EnabledFlags.Die;

                        Entity entity = entityArray[index];

                        DelayDestroy delayDestroy;
                        delayDestroy.time = deadTime;
                        if (index < delayDestroys.Length)
                            delayDestroys[index] = delayDestroy;
                        else
                            entityManager.AddComponent(0, entity, delayDestroy);
                        
                        entityManager.RemoveComponent<PhysicsCollider>(0, entity);
                    }
                    else if (index < characterBodies.Length && !characterBodies[index].IsGrounded)
                    {
                        result |= EnabledFlags.Drop;

                        target.immunizedTime = 0.0f;
                        target.invincibleTime = 0.0f;

                        if (index < characterGravityFactors.Length)
                        {
                            ThirdPersionCharacterGravityFactor characterGravityFactor;
                            characterGravityFactor.value = 1.0f;
                            characterGravityFactors[index] = characterGravityFactor;
                        }

                        entityManager.AddComponent<FallToDestroy>(0, entityArray[index]);
                    }

                    if((result & EnabledFlags.Die) != EnabledFlags.Die)
                    {
                        result |= EnabledFlags.Die;

                        if (target.times > 0 && targetInstance.recoveryChance > random.NextFloat())
                        {
                            result |= EnabledFlags.Recovery;

                            --target.times;

                            target.invincibleTime = targetInstance.recoveryTime;
                            target.hp = 0;

                            targetHP.value = targetInstance.hpMax;
                            targetHP.layerMask = damageLayerMask;

                            if (!targetInstance.recoveryMessageName.IsEmpty && messages.IsCreated)
                            {
                                message.key = 0;
                                message.name = targetInstance.recoveryMessageName;
                                message.value = targetInstance.recoveryMessageValue;
                                messages.Add(message);
                            }
                        }
                        else if(index >= delayDestroys.Length || delayDestroys[index].time > deltaTime)
                            __Destroy(int.MaxValue, entityArray[index], children, ref entityManager);
                    }
                    
                    if (!isFallToDestroy)
                    {
                        if (index < targetLevels.Length &&
                            this.levelStatus.IsValid)
                        {
                            var targetLevel = targetLevels[index];

                            ref var levelStatus = ref this.levelStatus.ValueRW;
                            Interlocked.Add(ref levelStatus.value, targetLevel.value);
                            Interlocked.Add(ref levelStatus.exp, targetLevel.exp);
                            Interlocked.Add(ref levelStatus.gold, targetLevel.gold);

                            Interlocked.Increment(ref levelStatus.killCount);
                            
                            if(EffectTargetData.TargetType.Boss == targetInstance.targetType)
                                Interlocked.Increment(ref levelStatus.killBossCount);
                        }

                        if (index < instances.Length && index < prefabs.Length)
                        {
                            ref var prefabsDefinition = ref instances[index].definition.Value.prefabs;
                            if (prefabsDefinition.Length > 0)
                            {
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

                                __Drop(
                                    math.RigidTransform(localToWorlds[index].Value),
                                    entity, 
                                    instanceDamage,
                                    instanceDamageParent,
                                    prefabs[index],
                                    ref damageInstances,
                                    ref entityManager,
                                    ref prefabLoader,
                                    ref prefabsDefinition,
                                    ref random);
                            }
                        }
                    }
                }

                targetHPs[index] = targetHP;
            }
            else
                result |= EnabledFlags.Invincible;

            targetDamages[index] = default;

            targets[index] = target;

            return result;
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public uint frameCount;
        public float deltaTime;
        public double time;
        
        public quaternion inverseCameraRotation;
        
        public Entity levelStatusEntity;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LevelStatus> levelStates;
        
        [ReadOnly] 
        public ComponentLookup<EffectDamage> damages;

        [ReadOnly]
        public ComponentLookup<EffectDamageParent> damageParents;

        [ReadOnly] 
        public BufferLookup<Child> children;

        [ReadOnly] 
        public BufferTypeHandle<EffectPrefab> prefabType;

        [ReadOnly]
        public BufferTypeHandle<EffectTargetMessage> targetMessageType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        [ReadOnly]
        public ComponentTypeHandle<FallToDestroy> fallToDestroyType;

        [ReadOnly]
        public ComponentTypeHandle<EffectDamageParent> damageParentType;

        [ReadOnly]
        public ComponentTypeHandle<EffectDefinitionData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<EffectTargetData> targetInstanceType;

        [ReadOnly]
        public ComponentTypeHandle<EffectTargetLevel> targetLevelType;

        [ReadOnly] 
        public ComponentTypeHandle<EffectTargetDamageScale> targetDamageScaleType;

        [ReadOnly]
        public ComponentTypeHandle<EffectTargetImmunityDefinitionData> targetImmunityType;

        public ComponentTypeHandle<EffectTargetImmunityStatus> targetImmunityStatusType;

        public ComponentTypeHandle<EffectTargetDamage> targetDamageType;

        public ComponentTypeHandle<EffectTargetHP> targetHPType;

        public ComponentTypeHandle<EffectTarget> targetType;

        public ComponentTypeHandle<ThirdPersionCharacterGravityFactor> characterGravityFactorType;

        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        public ComponentTypeHandle<DelayDestroy> delayDestroyType;

        public BufferTypeHandle<DelayTime> delayTimeType;

        public BufferTypeHandle<Message> messageType;

        public BufferTypeHandle<MessageParameter> messageParameterType;

        public EntityCommandBuffer.ParallelWriter entityManager;
        
        public PrefabLoader.ParallelWriter prefabLoader;

        public NativeQueue<DamageInstance>.ParallelWriter damageInstances;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.isFallToDestroy = chunk.Has(ref fallToDestroyType);
            apply.deltaTime = deltaTime;
            apply.time = time;
            apply.inverseCameraRotation = inverseCameraRotation;
            apply.random = Random.CreateFromIndex(frameCount ^ (uint)unfilteredChunkIndex);
            apply.levelStatus = levelStates.HasComponent(levelStatusEntity) ? levelStates.GetRefRW(levelStatusEntity) : default;
            apply.damages = damages;
            apply.damageParentMap = damageParents;
            apply.children = children;
            apply.prefabs = chunk.GetBufferAccessor(ref prefabType);
            apply.targetMessages = chunk.GetBufferAccessor(ref targetMessageType);
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.localToWorlds = chunk.GetNativeArray(ref localToWorldType);
            apply.characterBodies = chunk.GetNativeArray(ref characterBodyType);
            apply.instances = chunk.GetNativeArray(ref instanceType);
            apply.damageParents = chunk.GetNativeArray(ref damageParentType);
            apply.targetInstances = chunk.GetNativeArray(ref targetInstanceType);
            apply.targetLevels = chunk.GetNativeArray(ref targetLevelType);
            apply.targetDamageScales = chunk.GetNativeArray(ref targetDamageScaleType);
            apply.targetImmunities = chunk.GetNativeArray(ref targetImmunityType);
            apply.targetImmunityStates = chunk.GetNativeArray(ref targetImmunityStatusType);
            apply.targetHPs = chunk.GetNativeArray(ref targetHPType);
            apply.targetDamages = chunk.GetNativeArray(ref targetDamageType);
            apply.targets = chunk.GetNativeArray(ref targetType);
            apply.characterGravityFactors = chunk.GetNativeArray(ref characterGravityFactorType);
            apply.delayDestroys = chunk.GetNativeArray(ref delayDestroyType);
            apply.delayTimes = chunk.GetBufferAccessor(ref delayTimeType);
            apply.messages = chunk.GetBufferAccessor(ref messageType);
            apply.messageParameters = chunk.GetBufferAccessor(ref messageParameterType);
            apply.entityManager = entityManager;
            apply.prefabLoader = prefabLoader;
            apply.damageInstances = damageInstances;

            bool isCharacter = chunk.Has(ref characterBodyType);
            EnabledFlags result;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                result = apply.Execute(i);
                
                if((result & EnabledFlags.Message) == EnabledFlags.Message)
                    chunk.SetComponentEnabled(ref messageType, i, true);

                if ((result & EnabledFlags.Drop) == EnabledFlags.Drop)
                    chunk.SetComponentEnabled(ref targetType, i, false);
                else if ((result & EnabledFlags.Die) == EnabledFlags.Die)
                {
                    if(isCharacter)
                        chunk.SetComponentEnabled(ref characterBodyType, i, false);

                    if((result & EnabledFlags.Recovery) == EnabledFlags.Recovery)
                        chunk.SetComponentEnabled(ref targetHPType, i, true);
                    else
                        chunk.SetComponentEnabled(ref targetType, i, false);
                }
                else
                {
                    if(isCharacter && (result & EnabledFlags.Recovery) == EnabledFlags.Recovery)
                        chunk.SetComponentEnabled(ref characterBodyType, i, true);

                    chunk.SetComponentEnabled(ref targetHPType, i,
                        (result & EnabledFlags.Invincible) == EnabledFlags.Invincible);
                }

                chunk.SetComponentEnabled(ref targetDamageType, i, false);
            }
        }
    }

    private int __frameCount;

    //private double __time;
    
    private ComponentLookup<PhysicsCollider> __physicsColliders;

    private ComponentLookup<KinematicCharacterProperties> __characterProperties;

    private ComponentLookup<LevelStatus> __levelStates;

    private ComponentLookup<EffectDamage> __damages;

    private ComponentLookup<EffectDamageParent> __damageParents;

    private EntityTypeHandle __entityType;
    
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    private ComponentTypeHandle<FallToDestroy> __fallToDestroyType;

    private ComponentTypeHandle<EffectDamageParent> __damageParentType;

    private ComponentTypeHandle<EffectDefinitionData> __instanceType;

    private ComponentTypeHandle<EffectTargetData> __targetInstanceType;

    private ComponentTypeHandle<DelayDestroy> __delayDestroyType;

    private ComponentTypeHandle<SimulationCollision> __simulationCollisionType;

    private BufferTypeHandle<SimulationEvent> __simulationEventType;

    private BufferTypeHandle<DelayTime> __delayTimeType;

    private BufferTypeHandle<MessageParameter> __messageParameterType;

    private BufferTypeHandle<Message> __outputMessageType;

    private BufferTypeHandle<EffectMessage> __inputMessageType;

    private BufferTypeHandle<EffectTargetMessage> __targetMessageType;
    
    private BufferTypeHandle<EffectPrefab> __prefabType;

    private BufferTypeHandle<EffectStatusTarget> __statusTargetType;

    private ComponentTypeHandle<EffectStatus> __statusType;

    private ComponentTypeHandle<EffectTargetLevel> __targetLevelType;

    private ComponentTypeHandle<EffectTargetImmunityDefinitionData> __targetImmunityType;

    private ComponentTypeHandle<EffectTargetImmunityStatus> __targetImmunityStatusType;

    private ComponentTypeHandle<EffectTargetDamageScale> __targetDamageScaleType;

    private ComponentTypeHandle<EffectTargetDamage> __targetDamageType;
    private ComponentTypeHandle<EffectTargetHP> __targetHPType;

    private ComponentTypeHandle<EffectTarget> __targetType;

    private ComponentTypeHandle<KinematicCharacterBody> __characterBodyType;

    private ComponentTypeHandle<ThirdPersionCharacterGravityFactor> __characterGravityFactorType;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private ComponentLookup<EffectTargetDamage> __targetDamages;

    private ComponentLookup<EffectTargetLevel> __targetLevels;

    private ComponentLookup<DelayDestroy> __delayDestroies;
    
    private ComponentLookup<DropToDamage> __dropToDamages;

    private ComponentLookup<LocalToWorld> __localToWorlds;

    private BufferLookup<Message> __outputMessages;

    private BufferLookup<EffectDamageStatistic> __damageStatistics;

    private BufferLookup<Child> __children;

    private EntityQuery __groupToDestroy;

    private EntityQuery __groupToClear;

    private EntityQuery __groupToCollect;

    private EntityQuery __groupToApply;
    
    private PrefabLoader __prefabLoader;
    
    private NativeQueue<DamageInstance> __damageInstances;

    private static void __Destroy(
        int sortKey, 
        in Entity entity, 
        in BufferLookup<Child> children, 
        ref EntityCommandBuffer.ParallelWriter entityManager)
    {
        if (children.TryGetBuffer(entity, out var buffer))
        {
            foreach (var child in buffer)
                __Destroy(sortKey - 1, child.Value, children, ref entityManager);
        }
        
        entityManager.DestroyEntity(sortKey, entity);
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __characterProperties = state.GetComponentLookup<KinematicCharacterProperties>(true);
        __levelStates = state.GetComponentLookup<LevelStatus>();
        __damages = state.GetComponentLookup<EffectDamage>(true);
        __damageParents = state.GetComponentLookup<EffectDamageParent>(true);
        __entityType = state.GetEntityTypeHandle();
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>(true);
        __fallToDestroyType = state.GetComponentTypeHandle<FallToDestroy>(true);
        __damageParentType = state.GetComponentTypeHandle<EffectDamageParent>(true);
        __instanceType = state.GetComponentTypeHandle<EffectDefinitionData>(true);
        __targetInstanceType = state.GetComponentTypeHandle<EffectTargetData>(true);
        __delayDestroyType = state.GetComponentTypeHandle<DelayDestroy>();
        __simulationCollisionType = state.GetComponentTypeHandle<SimulationCollision>(true);
        __simulationEventType = state.GetBufferTypeHandle<SimulationEvent>(true);
        __delayTimeType = state.GetBufferTypeHandle<DelayTime>();
        __messageParameterType = state.GetBufferTypeHandle<MessageParameter>();
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __inputMessageType = state.GetBufferTypeHandle<EffectMessage>(true);
        __targetMessageType = state.GetBufferTypeHandle<EffectTargetMessage>(true);
        __prefabType = state.GetBufferTypeHandle<EffectPrefab>(true);
        __statusTargetType = state.GetBufferTypeHandle<EffectStatusTarget>();
        __statusType = state.GetComponentTypeHandle<EffectStatus>();
        __targetLevelType = state.GetComponentTypeHandle<EffectTargetLevel>(true);
        __targetImmunityType = state.GetComponentTypeHandle<EffectTargetImmunityDefinitionData>(true);
        __targetImmunityStatusType = state.GetComponentTypeHandle<EffectTargetImmunityStatus>();
        __targetDamageScaleType = state.GetComponentTypeHandle<EffectTargetDamageScale>();
        __targetDamageType = state.GetComponentTypeHandle<EffectTargetDamage>();
        __targetHPType = state.GetComponentTypeHandle<EffectTargetHP>();
        __targetType = state.GetComponentTypeHandle<EffectTarget>();
        __characterBodyType = state.GetComponentTypeHandle<KinematicCharacterBody>();
        __characterGravityFactorType = state.GetComponentTypeHandle<ThirdPersionCharacterGravityFactor>();
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>();
        __targetDamages = state.GetComponentLookup<EffectTargetDamage>();
        __targetLevels = state.GetComponentLookup<EffectTargetLevel>();
        __delayDestroies = state.GetComponentLookup<DelayDestroy>();
        __dropToDamages = state.GetComponentLookup<DropToDamage>();
        __localToWorlds = state.GetComponentLookup<LocalToWorld>();
        __outputMessages = state.GetBufferLookup<Message>();
        __damageStatistics = state.GetBufferLookup<EffectDamageStatistic>();
        __children = state.GetBufferLookup<Child>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDestroy = builder
                .WithAll<FallToDestroy>()
                .WithNone<EffectTarget>()
                .WithPresentRW<EffectTargetDamage>()
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

        state.RequireForUpdate<FixedFrame>();

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
        var fixedFrame = SystemAPI.GetSingleton<FixedFrame>();
        if (fixedFrame.count <= __frameCount)
            return;
        
        float deltaTime = fixedFrame.deltaTime * (fixedFrame.count - __frameCount);

        __frameCount = fixedFrame.count;
        
        var entityCommandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        var entityManager = entityCommandBuffer.AsParallelWriter();

        Instantiate instantiate;
        instantiate.damageInstances = __damageInstances;
        instantiate.entityManager = entityCommandBuffer;
        instantiate.prefabLoader = __prefabLoader.AsWriter();
        var jobHandle = instantiate.ScheduleByRef(state.Dependency);
            
        __characterBodyType.Update(ref state);
        __targetType.Update(ref state);
        __targetDamageType.Update(ref state);
        
        DestroyEx destroy;
        destroy.characterBodyType = __characterBodyType;
        destroy.targetType = __targetType;
        destroy.targetDamageType = __targetDamageType;
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

        __children.Update(ref state);
        __entityType.Update(ref state);
        __damageParents.Update(ref state);
        __physicsColliders.Update(ref state);
        __characterProperties.Update(ref state);
        __damages.Update(ref state);
        __damageParentType.Update(ref state);
        __simulationCollisionType.Update(ref state);
        __prefabType.Update(ref state);
        __inputMessageType.Update(ref state);
        __targetDamages.Update(ref state);
        __targetLevels.Update(ref state);
        __delayDestroies.Update(ref state);
        __dropToDamages.Update(ref state);
        __characterBodies.Update(ref state);
        __localToWorlds.Update(ref state);
        __outputMessages.Update(ref state);
        __damageStatistics.Update(ref state);
        
        var damageInstances = __damageInstances.AsParallelWriter();
        var prefabLoader = __prefabLoader.AsParallelWriter();

        quaternion inverseCameraRotation = SystemAPI.TryGetSingleton<MainCameraTransform>(out var mainCameraTransform)
            ? math.inverse(mainCameraTransform.value.rot)
            : quaternion.identity;
        
        CollectEx collect;
        collect.deltaTime = deltaTime;
        collect.time = time;
        collect.inverseCameraRotation = inverseCameraRotation;
        collect.children = __children;
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
        collect.outputMessages = __outputMessages;
        collect.inputMessageType = __inputMessageType;
        collect.statusTargetType = __statusTargetType;
        collect.statusType = __statusType;
        collect.targetDamages = __targetDamages;
        collect.targetLevels = __targetLevels;
        collect.delayDestroies = __delayDestroies;
        collect.dropToDamages = __dropToDamages;
        collect.characterBodies = __characterBodies;
        collect.localToWorlds = __localToWorlds;
        collect.damageStatistics = __damageStatistics;
        collect.entityManager = entityManager;
        collect.prefabLoader = prefabLoader;
        collect.damageInstances = damageInstances;
        jobHandle = collect.ScheduleParallelByRef(__groupToCollect,  jobHandle);

        ApplyEx apply;
        SystemAPI.TryGetSingletonEntity<LevelStatus>(out apply.levelStatusEntity);
        __levelStates.Update(ref state);
        __localToWorldType.Update(ref state);
        __fallToDestroyType.Update(ref state);
        __characterGravityFactorType.Update(ref state);
        __delayDestroyType.Update(ref state);
        __targetInstanceType.Update(ref state);
        __targetLevelType.Update(ref state);
        __targetImmunityType.Update(ref state);
        __targetImmunityStatusType.Update(ref state);
        __targetMessageType.Update(ref state);
        __targetDamageScaleType.Update(ref state);
        __targetHPType.Update(ref state);
        __outputMessageType.Update(ref state);
        __messageParameterType.Update(ref state);
        __delayTimeType.Update(ref state);

        /*if (deltaTime > math.FLT_MIN_NORMAL)
            __time += deltaTime;
        else if(__frameCount > 0)
            __time += __time / __frameCount;
        
        ++__frameCount;*/

        apply.frameCount = (uint)__frameCount;
        apply.deltaTime = deltaTime;//(float)(__time / __frameCount);
        apply.time = time;
        apply.inverseCameraRotation = inverseCameraRotation;
        apply.levelStates = __levelStates;
        apply.children = __children;
        apply.damages = __damages;
        apply.damageParents = __damageParents;
        apply.prefabType = __prefabType;
        apply.targetMessageType = __targetMessageType;
        apply.entityType = __entityType;
        apply.localToWorldType = __localToWorldType;
        apply.fallToDestroyType = __fallToDestroyType;
        apply.damageParentType = __damageParentType;
        apply.instanceType = __instanceType;
        apply.targetInstanceType = __targetInstanceType;
        apply.targetLevelType = __targetLevelType;
        apply.targetDamageScaleType = __targetDamageScaleType;
        apply.targetDamageType = __targetDamageType;
        apply.targetHPType = __targetHPType;
        apply.targetImmunityType = __targetImmunityType;
        apply.targetImmunityStatusType = __targetImmunityStatusType;
        apply.targetType = __targetType;
        apply.characterBodyType = __characterBodyType;
        apply.characterGravityFactorType = __characterGravityFactorType;
        apply.delayDestroyType = __delayDestroyType;
        apply.delayTimeType = __delayTimeType;
        apply.messageType = __outputMessageType;
        apply.messageParameterType = __messageParameterType;
        apply.entityManager = entityManager;
        apply.prefabLoader = prefabLoader;
        apply.damageInstances = damageInstances;
        state.Dependency = apply.ScheduleParallelByRef(__groupToApply, jobHandle);
    }

    private static void __Drop(
        in RigidTransform transform,
        in Entity parent,
        in EffectDamage instanceDamage,
        in EffectDamageParent instanceDamageParent,
        in DynamicBuffer<EffectPrefab> prefabs,
        ref NativeQueue<DamageInstance>.ParallelWriter damageInstances,
        ref EntityCommandBuffer.ParallelWriter entityManager,
        ref PrefabLoader.ParallelWriter prefabLoader,
        ref BlobArray<EffectDefinition.Prefab> prefabsDefinition,
        ref Random random)
    {
        int numPrefabs = prefabsDefinition.Length;
        if (numPrefabs < 1)
            return;

        Parent instanceParent;
        instanceParent.Value = parent;

        DamageInstance damageInstance;
        damageInstance.index = instanceDamageParent.index;
        damageInstance.entity = instanceDamageParent.entity;
        damageInstance.bulletLayerMask = instanceDamage.bulletLayerMask;

        bool isContains = false;
        float chance = random.NextFloat(), totalChance = 0.0f;
        Entity instance;
        for (int i = 0; i < numPrefabs; ++i)
        {
            ref var prefab = ref prefabsDefinition[i];
            if(!prefab.bulletLayerMask.BelongsTo(instanceDamage.bulletLayerMask))
                continue;
            
            totalChance += prefab.chance;
            if (totalChance > 1.0f)
            {
                totalChance -= 1.0f;

                chance = random.NextFloat();

                isContains = false;
            }

            if (isContains || totalChance < chance)
                continue;

            damageInstance.scale = instanceDamage.scale * (math.abs(prefab.damageScale) > math.FLT_MIN_NORMAL ? prefab.damageScale : 1.0f);
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

                if (instanceDamageParent.entity != Entity.Null)
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
