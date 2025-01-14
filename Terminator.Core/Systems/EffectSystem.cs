using System;
using System.Threading;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Scenes;
using Unity.Transforms;
using Math = ZG.Mathematics.Math;
using Random = Unity.Mathematics.Random;

[BurstCompile, UpdateInGroup(typeof(TransformSystemGroup), OrderLast = true), UpdateBefore(typeof(CopyMatrixToTransformSystem))]
public partial struct EffectSystem : ISystem
{
    [Flags]
    public enum EnabledFlags
    {
        Message = 0x01,
        StatusTarget = 0x02, 
        Destroyed = 0x04, 
    }
    
    /*private struct Result
    {
        //public int layerMask;
        public float3 force;
        public Entity entity;
    }*/

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
                    status.time = time;

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
        public BufferTypeHandle<SimulationEvent> simulationEventType;

        public BufferTypeHandle<EffectStatusTarget> statusTargetType;

        public ComponentTypeHandle<EffectStatus> statusType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Clear clear;
            clear.time = time;
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
        
        //[NativeDisableParallelForRestriction]
        //public RefRW<LevelStatus> levelStatus;
        public Random random;

        [ReadOnly] 
        public ComponentLookup<Parent> parents;

        [ReadOnly] 
        public ComponentLookup<PrefabLoadResult> prefabLoadResults;

        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterProperties> characterProperties;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> damages;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<EffectDefinitionData> instances;

        [ReadOnly] 
        public NativeArray<SimulationCollision> simulationCollisions;

        [ReadOnly] 
        public BufferAccessor<SimulationEvent> simulationEvents;

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

        public EntityCommandBuffer.ParallelWriter entityManager;

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
                int count = 0, entityCount = 0;
                if (effect.time > math.FLT_MIN_NORMAL)
                {
                    count = (int)math.ceil((time - status.time) / effect.time);
                    if (count < 1)
                        return 0;
                    
                    result = math.min(count, effect.count - status.count) > (int)math.ceil((time - deltaTime - status.time) / effect.time);
                }

                Entity entity = entityArray[index];
                var statusTargets = this.statusTargets[index];
                if (result)
                {
                    //ref var levelStatus = ref this.levelStatus.ValueRW;
                    var inputMessages = index < this.inputMessages.Length ? this.inputMessages[index] : default;
                    var outputMessages = index < this.outputMessages.Length ? this.outputMessages[index] : default;
                    var simulationEvents = this.simulationEvents[index];
                    EffectStatusTarget statusTarget;
                    PrefabLoadResult prefabLoadResult;
                    PhysicsCollider physicsCollider;
                    //KinematicCharacterBody characterBody;
                    //EffectTargetLevel targetLevel;
                    //Result result;
                    RefRW<KinematicCharacterBody> characterBody;
                    EffectMessage inputMessage;
                    Message outputMessage;
                    MessageParameter messageParameter;
                    Entity messageReceiver;
                    LocalToWorld source = localToWorlds[entity], destination;
                    float3 forceResult, force;
                    float delayDestroyTime, mass, lengthSQ;
                    int damageScale = EffectDamage.Compute(entity, parents, damages),
                        damageValue,
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

                        ++entityCount;

                        isResult = false;

                        if (delayDestroies.HasComponent(simulationEvent.entity))
                        {
                            delayDestroyTime = damage.delayDestroyTime;// * damageScale;
                            if (math.abs(delayDestroyTime) > math.FLT_MIN_NORMAL)
                            {
                                Math.InterlockedAdd(ref this.delayDestroies.GetRefRW(simulationEvent.entity).ValueRW.time,
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

                            damageValue = damage.value * damageScale;

                            isResult = damageValue != 0;

                            ref var targetDamage = ref targetDamages.GetRefRW(simulationEvent.entity).ValueRW;

                            if (isResult)
                                targetDamage.Add(damageValue, effect.messageLayerMask);

                            if (characterBody.IsValid)
                            {
                                if (damage.valueToDrop != 0 && dropToDamages.HasComponent(simulationEvent.entity))
                                {
                                    ref var dropToDamage = ref dropToDamages.GetRefRW(simulationEvent.entity).ValueRW;

                                    Interlocked.Add(ref dropToDamage.value, damage.valueToDrop);

                                    do
                                    {
                                        layerMask = dropToDamage.layerMask;
                                    } while (Interlocked.CompareExchange(ref dropToDamage.layerMask,
                                                 layerMask | effect.messageLayerMask, layerMask) != layerMask);

                                    dropToDamage.isGrounded = characterBody.ValueRO.IsGrounded;

                                    dropToDamages.SetComponentEnabled(simulationEvent.entity, true);
                                }
                            }

                            if (isResult)
                                targetDamages.SetComponentEnabled(simulationEvent.entity, true);
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

                        if (isResult)
                        {
                            enabledFlags |= EnabledFlags.StatusTarget;

                            numMessageIndices = effect.messageIndices.Length;
                            for (i = 0; i < numMessageIndices; ++i)
                            {
                                inputMessage = inputMessages[effect.messageIndices[i]];
                                outputMessage.key = random.NextInt();
                                outputMessage.name = inputMessage.name;
                                outputMessage.value = inputMessage.value;

                                if (inputMessage.receiverPrefabLoader == Entity.Null)
                                {
                                    enabledFlags |= EnabledFlags.Message;

                                    outputMessages.Add(outputMessage);
                                }
                                else if ((damageValue != 0 || outputMessage.name.IsEmpty) &&
                                         prefabLoadResults.TryGetComponent(
                                             inputMessage.receiverPrefabLoader,
                                             out prefabLoadResult))
                                {
                                    messageReceiver = entityManager.Instantiate(0, prefabLoadResult.PrefabRoot);

                                    if (!outputMessage.name.IsEmpty)
                                    {
                                        entityManager.SetBuffer<Message>(1, messageReceiver).Add(outputMessage);
                                        entityManager.SetComponentEnabled<Message>(1, messageReceiver, true);

                                        messageParameter.messageKey = outputMessage.key;
                                        messageParameter.value = -damageValue;
                                        messageParameter.id = (int)EffectAttributeID.Damage;
                                        entityManager.SetBuffer<MessageParameter>(1, messageReceiver)
                                            .Add(messageParameter);
                                    }

                                    entityManager.SetComponent(1, messageReceiver,
                                        LocalTransform.FromPositionRotation(destination.Position,
                                            inverseCameraRotation));
                                }
                            }
                        }
                    }
                }

                count = count > 0 ? count - 1 : entityCount;
                if (count > 0)
                {
                    count += status.count;
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
                                    var transform = math.RigidTransform(
                                        Math.FromToRotation(math.up(), closestHit.SurfaceNormal),
                                        closestHit.Position);
                                    
                                    LocalToWorld localToWorld;
                                    localToWorld.Value = math.float4x4(transform);
                                    localToWorlds[entity] = localToWorld;
                                }
                            }

                            entityManager.DestroyEntity(0, entity);

                            enabledFlags |= EnabledFlags.Destroyed;
                        }
                        else
                        {
                            //statusTargets.Clear();
                            
                            status.time += definition.effects[status.index].startTime;
                        }
                    }
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
        public ComponentLookup<Parent> parents;

        [ReadOnly] 
        public ComponentLookup<PrefabLoadResult> prefabLoadResults;

        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterProperties> characterProperties;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> damages;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<EffectDefinitionData> instanceType;

        [ReadOnly] 
        public ComponentTypeHandle<SimulationCollision> simulationCollisionType;

        [ReadOnly] 
        public BufferTypeHandle<SimulationEvent> simulationEventType;

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

        //public NativeQueue<Result>.ParallelWriter results;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ulong hash = math.asulong(time);
            
            Collect collect;
            collect.deltaTime = deltaTime;
            collect.time = time;
            collect.random = Random.CreateFromIndex((uint)hash ^ (uint)(hash >> 32) ^ (uint)unfilteredChunkIndex);
            collect.inverseCameraRotation = inverseCameraRotation;
            collect.parents = parents;
            collect.physicsColliders = physicsColliders;
            collect.characterProperties = characterProperties;
            collect.damages = damages;
            collect.prefabLoadResults = prefabLoadResults;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.simulationCollisions = chunk.GetNativeArray(ref simulationCollisionType);
            collect.simulationEvents = chunk.GetBufferAccessor(ref simulationEventType);
            collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            collect.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            collect.statusTargets = chunk.GetBufferAccessor(ref statusTargetType);
            collect.states = chunk.GetNativeArray(ref statusType);
            collect.targetDamages = targetDamages;
            collect.delayDestroies = delayDestroies;
            collect.dropToDamages = dropToDamages;
            collect.characterBodies = characterBodies;
            collect.localToWorlds = localToWorlds;
            //collect.results = results;
            collect.entityManager = entityManager;

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
        public quaternion inverseCameraRotation;

        public Random random;
        
        [NativeDisableParallelForRestriction]
        public RefRW<LevelStatus> levelStatus;

        [ReadOnly] 
        public ComponentLookup<PrefabLoadResult> prefabLoadResults;

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
        public NativeArray<EffectTargetLevel> targetLevels;
        
        [ReadOnly] 
        public NativeArray<EffectTargetDamageScale> targetDamageScales;

        public NativeArray<EffectTargetDamage> targetDamages;

        public NativeArray<EffectTarget> targets;

        public NativeArray<ThirdPersionCharacterGravityFactor> characterGravityFactors;

        public BufferAccessor<Message> messages;

        public BufferAccessor<MessageParameter> messageParameters;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public bool Execute(int index)
        {
            var targetDamage = targetDamages[index];
            var target = targets[index];
            target.hp += -(int)math.ceil(targetDamage.value * (index < targetDamageScales.Length ? targetDamageScales[index].value : 1.0f));
            if (target.hp <= 0)
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

            targets[index] = target;
            
            bool result = false;
            if (targetDamage.value != 0 && targetDamage.layerMask != 0 && index < targetMessages.Length)
            {
                float3 position = localToWorlds[index].Position;
                Entity messageReceiver;
                PrefabLoadResult prefabLoadResult;
                Message message;
                MessageParameter messageParameter;
                var messageParameters = index < this.messageParameters.Length ? this.messageParameters[index] : default;
                var messages = this.messages[index];
                var targetMessages = this.targetMessages[index];
                foreach (var targetMessage in targetMessages)
                {
                    if (targetMessage.layerMask == 0 || (targetMessage.layerMask & targetDamage.layerMask) != 0)
                    {
                        message.key = random.NextInt();
                        message.name = targetMessage.messageName;
                        message.value = targetMessage.messageValue;
                        
                        //targetMessage.value.value.LoadAsync();
                        if(prefabLoadResults.TryGetComponent(
                               targetMessage.receiverPrefabLoader, out prefabLoadResult))
                        {
                            messageReceiver = entityManager.Instantiate(0, prefabLoadResult.PrefabRoot);

                            if (!targetMessage.messageName.IsEmpty)
                            {
                                entityManager.SetBuffer<Message>(1, messageReceiver).Add(message);
                                entityManager.SetComponentEnabled<Message>(1, messageReceiver, true);

                                messageParameter.messageKey = message.key;
                                messageParameter.value = -targetDamage.value;
                                messageParameter.id = (int)EffectAttributeID.Damage;
                                entityManager.SetBuffer<MessageParameter>(1, messageReceiver).Add(messageParameter);
                            }

                            entityManager.SetComponent(1, messageReceiver,
                                LocalTransform.FromPositionRotation(position,
                                    inverseCameraRotation));
                        }
                        else if (index < this.messages.Length)
                        {
                            messages.Add(message);

                            if (messageParameters.IsCreated)
                            {
                                messageParameter.messageKey = message.key;
                                
                                messageParameter.value = -targetDamage.value;
                                messageParameter.id = (int)EffectAttributeID.Damage;
                                messageParameters.Add(messageParameter);
                                
                                messageParameter.value = target.hp;
                                messageParameter.id = (int)EffectAttributeID.HP;
                                messageParameters.Add(messageParameter);
                            }

                            result = true;
                        }
                    }
                }
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
        public ComponentLookup<PrefabLoadResult> prefabLoadResults;

        [ReadOnly]
        public BufferTypeHandle<EffectTargetMessage> targetMessageType;

        [ReadOnly]
        public BufferTypeHandle<Child> childType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        [ReadOnly]
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        [ReadOnly]
        public ComponentTypeHandle<EffectTargetLevel> targetLevelType;

        [ReadOnly] 
        public ComponentTypeHandle<EffectTargetDamageScale> targetDamageScaleType;

        public ComponentTypeHandle<EffectTargetDamage> targetDamageType;

        public ComponentTypeHandle<EffectTarget> targetType;

        public ComponentTypeHandle<ThirdPersionCharacterGravityFactor> characterGravityFactorType;

        public BufferTypeHandle<Message> messageType;

        public BufferTypeHandle<MessageParameter> messageParameterType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ulong hash = math.asulong(time);
            
            Apply apply;
            apply.inverseCameraRotation = inverseCameraRotation;
            apply.random = Random.CreateFromIndex((uint)hash ^ (uint)(hash >> 32) ^ (uint)unfilteredChunkIndex);
            apply.levelStatus = levelStates.HasComponent(levelStatusEntity) ? levelStates.GetRefRW(levelStatusEntity) : default;
            apply.prefabLoadResults = prefabLoadResults;
            apply.targetMessages = chunk.GetBufferAccessor(ref targetMessageType);
            apply.children = chunk.GetBufferAccessor(ref childType);
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.localToWorlds = chunk.GetNativeArray(ref localToWorldType);
            apply.characterBodies = chunk.GetNativeArray(ref characterBodyType);
            apply.targetLevels = chunk.GetNativeArray(ref targetLevelType);
            apply.targetDamageScales = chunk.GetNativeArray(ref targetDamageScaleType);
            apply.targetDamages = chunk.GetNativeArray(ref targetDamageType);
            apply.targets = chunk.GetNativeArray(ref targetType);
            apply.characterGravityFactors = chunk.GetNativeArray(ref characterGravityFactorType);
            apply.messages = chunk.GetBufferAccessor(ref messageType);
            apply.messageParameters = chunk.GetBufferAccessor(ref messageParameterType);
            apply.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(apply.Execute(i))
                    chunk.SetComponentEnabled(ref messageType, i, true);
                
                chunk.SetComponentEnabled(ref targetDamageType, i, false);
            }
        }
    }

    private ComponentLookup<PrefabLoadResult> __prefabLoadResults;

    private ComponentLookup<PhysicsCollider> __physicsColliders;

    private ComponentLookup<KinematicCharacterProperties> __characterProperties;

    private ComponentLookup<LevelStatus> __levelStates;

    private ComponentLookup<EffectDamage> __damages;

    private ComponentLookup<Parent> __parents;

    private EntityTypeHandle __entityType;
    
    private BufferTypeHandle<Child> __childType;

    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    private ComponentTypeHandle<KinematicCharacterBody> __characterBodyType;

    private ComponentTypeHandle<EffectDefinitionData> __instanceType;

    private ComponentTypeHandle<SimulationCollision> __simulationCollisionType;

    private BufferTypeHandle<SimulationEvent> __simulationEventType;

    private BufferTypeHandle<MessageParameter> __messageParameterType;

    private BufferTypeHandle<Message> __outputMessageType;

    private BufferTypeHandle<EffectMessage> __inputMessageType;

    private BufferTypeHandle<EffectTargetMessage> __targetMessageType;
    
    private BufferTypeHandle<EffectStatusTarget> __statusTargetType;

    private ComponentTypeHandle<EffectStatus> __statusType;

    private ComponentTypeHandle<EffectTargetLevel> __targetLevelType;

    private ComponentTypeHandle<EffectTargetDamageScale> __targetDamageScaleType;
    
    private ComponentTypeHandle<EffectTargetDamage> __targetDamageType;

    private ComponentTypeHandle<EffectTarget> __targetType;

    private ComponentTypeHandle<ThirdPersionCharacterGravityFactor> __characterGravityFactorType;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private ComponentLookup<EffectTargetDamage> __targetDamages;

    private ComponentLookup<DelayDestroy> __delayDestroies;
    
    private ComponentLookup<DropToDamage> __dropToDamages;

    private ComponentLookup<LocalToWorld> __localToWorlds;

    private EntityQuery __groupToDestroy;

    private EntityQuery __groupToClear;

    private EntityQuery __groupToCollect;

    private EntityQuery __groupToApply;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __prefabLoadResults = state.GetComponentLookup<PrefabLoadResult>(true);
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __characterProperties = state.GetComponentLookup<KinematicCharacterProperties>(true);
        __levelStates = state.GetComponentLookup<LevelStatus>();
        __damages = state.GetComponentLookup<EffectDamage>(true);
        __parents = state.GetComponentLookup<Parent>(true);
        __entityType = state.GetEntityTypeHandle();
        __childType = state.GetBufferTypeHandle<Child>(true);
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>(true);
        __characterBodyType = state.GetComponentTypeHandle<KinematicCharacterBody>(true);
        __instanceType = state.GetComponentTypeHandle<EffectDefinitionData>(true);
        __simulationCollisionType = state.GetComponentTypeHandle<SimulationCollision>(true);
        __simulationEventType = state.GetBufferTypeHandle<SimulationEvent>(true);
        __messageParameterType = state.GetBufferTypeHandle<MessageParameter>();
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __inputMessageType = state.GetBufferTypeHandle<EffectMessage>(true);
        __targetMessageType = state.GetBufferTypeHandle<EffectTargetMessage>(true);
        __statusTargetType = state.GetBufferTypeHandle<EffectStatusTarget>();
        __statusType = state.GetComponentTypeHandle<EffectStatus>();
        __targetLevelType = state.GetComponentTypeHandle<EffectTargetLevel>(true);
        __targetDamageScaleType = state.GetComponentTypeHandle<EffectTargetDamageScale>();
        __targetDamageType = state.GetComponentTypeHandle<EffectTargetDamage>();
        __targetType = state.GetComponentTypeHandle<EffectTarget>();
        __characterGravityFactorType = state.GetComponentTypeHandle<ThirdPersionCharacterGravityFactor>();
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>();
        __targetDamages = state.GetComponentLookup<EffectTargetDamage>();
        __delayDestroies = state.GetComponentLookup<DelayDestroy>();
        __dropToDamages = state.GetComponentLookup<DropToDamage>();
        __localToWorlds = state.GetComponentLookup<LocalToWorld>();

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
                .WithAll<SimulationEvent, EffectDefinitionData>()
                .WithAllRW<EffectStatus>()
                .WithPresentRW<EffectStatusTarget>()
                .Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToApply = builder
                .WithAllRW<EffectTarget, EffectTargetDamage>()
                .Build(ref state);
        
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __entityType.Update(ref state);
        __childType.Update(ref state);
        __characterBodyType.Update(ref state);
        
        var entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        DestroyEx destroy;
        destroy.entityType = __entityType;
        destroy.characterBodyType = __characterBodyType;
        destroy.childType = __childType;
        destroy.entityManager = entityManager;
        var jobHandle = destroy.ScheduleParallelByRef(__groupToDestroy, state.Dependency);

        __simulationEventType.Update(ref state);
        __statusTargetType.Update(ref state);
        __statusType.Update(ref state);
        
        double time = SystemAPI.Time.ElapsedTime;

        ClearEx clear;
        clear.time = time;
        clear.simulationEventType = __simulationEventType;
        clear.statusTargetType = __statusTargetType;
        clear.statusType = __statusType;
        jobHandle = clear.ScheduleParallelByRef(__groupToClear, jobHandle);
        
        __parents.Update(ref state);
        __physicsColliders.Update(ref state);
        __characterProperties.Update(ref state);
        __damages.Update(ref state);
        __prefabLoadResults.Update(ref state);
        __instanceType.Update(ref state);
        __simulationCollisionType.Update(ref state);
        __outputMessageType.Update(ref state);
        __inputMessageType.Update(ref state);
        __targetDamages.Update(ref state);
        __delayDestroies.Update(ref state);
        __dropToDamages.Update(ref state);
        __characterBodies.Update(ref state);
        __localToWorlds.Update(ref state);

        quaternion inverseCameraRotation = SystemAPI.TryGetSingleton<MainCameraTransform>(out var mainCameraTransform)
            ? math.inverse(mainCameraTransform.value.rot)
            : quaternion.identity;
        CollectEx collect;
        collect.deltaTime = SystemAPI.Time.DeltaTime;
        collect.time = time;
        collect.inverseCameraRotation = inverseCameraRotation;
        collect.parents = __parents;
        collect.prefabLoadResults = __prefabLoadResults;
        collect.physicsColliders = __physicsColliders;
        collect.characterProperties = __characterProperties;
        collect.damages = __damages;
        collect.entityType = __entityType;
        collect.instanceType = __instanceType;
        collect.simulationCollisionType = __simulationCollisionType;
        collect.simulationEventType = __simulationEventType;
        collect.outputMessageType = __outputMessageType;
        collect.inputMessageType = __inputMessageType;
        collect.statusTargetType = __statusTargetType;
        collect.statusType = __statusType;
        collect.targetDamages = __targetDamages;
        collect.delayDestroies = __delayDestroies;
        collect.dropToDamages = __dropToDamages;
        collect.characterBodies = __characterBodies;
        collect.localToWorlds = __localToWorlds;
        collect.entityManager = entityManager;
        jobHandle = collect.ScheduleParallelByRef(__groupToCollect,  jobHandle);

        ApplyEx apply;
        SystemAPI.TryGetSingletonEntity<LevelStatus>(out apply.levelStatusEntity);
        __levelStates.Update(ref state);
        __localToWorldType.Update(ref state);
        __characterGravityFactorType.Update(ref state);
        __targetType.Update(ref state);
        __targetLevelType.Update(ref state);
        __targetMessageType.Update(ref state);
        __targetDamageScaleType.Update(ref state);
        __targetDamageType.Update(ref state);
        __messageParameterType.Update(ref state);

        apply.time = time;
        apply.inverseCameraRotation = inverseCameraRotation;
        apply.levelStates = __levelStates;
        apply.prefabLoadResults = __prefabLoadResults;
        apply.targetMessageType = __targetMessageType;
        apply.childType = __childType;
        apply.entityType = __entityType;
        apply.localToWorldType = __localToWorldType;
        apply.characterBodyType = __characterBodyType;
        apply.targetLevelType = __targetLevelType;
        apply.targetDamageScaleType = __targetDamageScaleType;
        apply.targetDamageType = __targetDamageType;
        apply.targetType = __targetType;
        apply.characterGravityFactorType = __characterGravityFactorType;
        apply.messageType = __outputMessageType;
        apply.messageParameterType = __messageParameterType;
        apply.entityManager = entityManager;
        state.Dependency = apply.ScheduleParallelByRef(__groupToApply, jobHandle);
    }
}
