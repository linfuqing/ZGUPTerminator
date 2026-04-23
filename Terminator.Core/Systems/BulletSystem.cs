using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.GraphicsIntegration;
using Unity.Physics.Systems;
using Unity.Transforms;
using ZG;

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct BulletEntitySystem : ISystem
{
    private struct Collect
    {
        [ReadOnly]
        public ComponentLookup<BulletDefinitionData> instances;
        
        [ReadOnly]
        public NativeArray<BulletEntity> bulletEntities;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public NativeList<Entity> results;

        public void Execute(int index)
        {
            if (instances.HasComponent(bulletEntities[index].parent))
                return;
            
            results.Add(entityArray[index]);
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        [ReadOnly]
        public ComponentLookup<BulletDefinitionData> instances;
        
        [ReadOnly]
        public ComponentTypeHandle<BulletEntity> bulletEntityType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        public NativeList<Entity> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.instances = instances;
            collect.bulletEntities = chunk.GetNativeArray(ref bulletEntityType);
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }
    
    private uint __version;
    
    private ComponentLookup<BulletDefinitionData> __instances;
        
    private ComponentTypeHandle<BulletEntity> __bulletEntityType;

    private EntityTypeHandle __entityType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __instances = state.GetComponentLookup<BulletDefinitionData>(true);
        __bulletEntityType = state.GetComponentTypeHandle<BulletEntity>(true);
        __entityType = state.GetEntityTypeHandle();
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<BulletEntity>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = state.EntityManager;
        uint version = (uint)entityManager.GetComponentOrderVersion<BulletDefinitionData>();
        if (!ChangeVersionUtility.DidChange(version, __version))
            return;

        __version = version;
        
        state.CompleteDependency();

        using (var results = new NativeList<Entity>(__group.CalculateEntityCount(), Allocator.TempJob))
        {
            __instances.Update(ref state);
            __bulletEntityType.Update(ref state);
            __entityType.Update(ref state);
            
            CollectEx collect;
            collect.instances = __instances;
            collect.bulletEntityType = __bulletEntityType;
            collect.entityType = __entityType;
            collect.results = results;
            
            collect.RunByRef(__group);
            
            entityManager.DestroyEntity(results.AsArray());
        }
    }
}

[BurstCompile, 
 CreateAfter(typeof(PrefabLoaderSystem)), 
 UpdateInGroup(typeof(AfterPhysicsSystemGroup)), UpdateAfter(typeof(KinematicCharacterPhysicsUpdateGroup))]//, UpdateAfter(typeof(LookAtSystem))]
public partial struct BulletSystem : ISystem
{
    private struct Collect
    {
        public bool isFire;

        public double time;
        
        public float3 gravity;

        public Random random;

        public quaternion cameraRotation;

        public RenderFrustumPlanes renderFrustumPlanes;
        
        public LevelStatus levelStatus;
        
        public FixedLocalToWorld fixedLocalToWorld;

        [ReadOnly] 
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<PhysicsGraphicalInterpolationBuffer> physicsGraphicalInterpolationBuffers;

        [ReadOnly]
        public ComponentLookup<CharacterInterpolation> characterInterpolations;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public ComponentLookup<ThirdPersonCharacterControl> characterControls;

        [ReadOnly] 
        public ComponentLookup<CameraRotation> cameraRotations;

        [ReadOnly]
        public ComponentLookup<AnimationCurveDelta> animationCurveDeltas;

        [ReadOnly] 
        public ComponentLookup<FollowTargetVelocity> followTargetVelocities;
        
        [ReadOnly] 
        public ComponentLookup<EffectDamageParent> effectDamageParents;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> effectDamages;

        //[ReadOnly] 
        //public ComponentLookup<EffectTarget> effectTargets;
        
        [ReadOnly]
        public ComponentLookup<BulletLayerMaskAndTags> bulletLayerMaskAndTags;

        [ReadOnly]
        public ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs;

        [ReadOnly]
        public BufferLookup<Message> messages;

        [ReadOnly]
        public BufferLookup<MessageParameter> messageParameters;

        [ReadOnly] 
        public NativeArray<Entity> entityArray;

        [ReadOnly] 
        public NativeArray<LookAtTarget> lookAtTargets;

        [ReadOnly]
        public NativeArray<BulletDefinitionData> definitions;

        [ReadOnly] 
        public BufferAccessor<BulletActiveIndex> activeIndices;

        [ReadOnly] 
        public BufferAccessor<BulletCollider> colliders;

        [ReadOnly]
        public BufferAccessor<BulletPrefab> prefabs;

        [ReadOnly] 
        public BufferAccessor<BulletMessage> inputMessages;

        public BufferAccessor<Message> outputMessages;

        public BufferAccessor<DelayTime> delayTimes;

        public BufferAccessor<ThirdPersonCharacterStandTime> characterStandTimes;

        public BufferAccessor<BulletMessageSharedIndex> messageSharedIndices;

        public BufferAccessor<BulletStatus> states;

        public BufferAccessor<BulletTargetStatus> targetStates;

        public BufferAccessor<BulletInstance> instances;

        public NativeArray<BulletVersion> versions;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;
        
        public bool Execute(int index)
        {
            Entity entity = entityArray[index];
            /*EffectDamageParent.TryGetComponent(
                entity,
                effectDamageParents,
                effectTargets,
                out var _,
                out var effectTarget);
            
            if (effectTarget != Entity.Null && !effectTargets.IsComponentEnabled(effectTarget))
                return false;*/
            
            EffectDamageParent.TryGetComponent(
                entity,
                effectDamageParents,
                characterBodies,
                out var characterBody,
                out var character);
    
            bool isFire = this.isFire;
            int instanceID;
            BulletLocation location = 0;
            float3 up = math.up();
            quaternion cameraRotation;
            ref var definition = ref definitions[index].definition.Value;
            if (character == Entity.Null)
            {
                cameraRotation = cameraRotations.TryGetComponent(entity, out var temp) ? temp.value : this.cameraRotation;
                
                //characterStandTimes = default;
                location = (BulletLocation)~0;

                instanceID =
                    copyMatrixToTransformInstanceIDs.TryGetComponent(entity, out var copyMatrixToTransformInstanceID)
                        ? copyMatrixToTransformInstanceID.value
                        : 0;
            }
            else
            {
                if (!characterBodies.IsComponentEnabled(character))
                    return false;
                
                cameraRotation = cameraRotations.TryGetComponent(character, out var temp) ? temp.value : this.cameraRotation;
                
                up = characterBody.GroundingUp;

                if (characterBody.IsGrounded)
                    location = BulletLocation.Ground;
                else
                {
                    float dot = math.dot(characterBody.RelativeVelocity, characterBody.GroundingUp);
                    if (dot > definition.minAirSpeed && dot < definition.maxAirSpeed)
                        location = BulletLocation.Air;
                }

                instanceID =
                    copyMatrixToTransformInstanceIDs.TryGetComponent(character, out var copyMatrixToTransformInstanceID)
                        ? copyMatrixToTransformInstanceID.value
                        : 0;

                isFire &= characterBodies.IsComponentEnabled(character);
            }

            var instances = this.instances[index];
            var delayTimes = index < this.delayTimes.Length ? this.delayTimes[index] : default;
            if (DelayTime.IsDelay(ref delayTimes, time, out _))
            {
                instances.Clear();
                
                isFire = false;
            }

            float damageScale;
            LayerMaskAndTags layerMaskAndTags;
            if (EffectDamageParent.TryGetComponent(
                    entity,
                    effectDamageParents,
                    effectDamages,
                    out var effectDamage,
                    out _))
            {
                damageScale = effectDamage.scale;

                layerMaskAndTags = effectDamage.layerMaskAndTags;
            }
            else
            {
                damageScale = 1.0f;
                
                layerMaskAndTags = default;
            }

            if (EffectDamageParent.TryGetComponent(
                    entity,
                    effectDamageParents,
                    this.bulletLayerMaskAndTags,
                    out var bulletLayerMaskAndTags,
                    out _))
                layerMaskAndTags |= bulletLayerMaskAndTags.value;
            else if(layerMaskAndTags.isEmpty)
                layerMaskAndTags = LayerMaskAndTags.AllLayers;

            var messageSharedIndices =
                index < this.messageSharedIndices.Length ? this.messageSharedIndices[index] : default;
            if(messageSharedIndices.IsCreated)
                messageSharedIndices.Clear();
            
            var localToWorld = fixedLocalToWorld.GetMatrix(entity);
            var inputMessages = this.inputMessages[index].AsNativeArray();
            var outputMessages = index < this.outputMessages.Length ? this.outputMessages[index] : default;
            var characterStandTimes = index < this.characterStandTimes.Length ? this.characterStandTimes[index] : default;
            var targetStates = this.targetStates[index];
            var states = this.states[index];
            var version = versions[index];
            definition.Update(
                isFire, 
                location, 
                damageScale, 
                time,
                gravity, 
                up, 
                cameraRotation, 
                renderFrustumPlanes, 
                localToWorld, 
                entity,
                index < lookAtTargets.Length ? lookAtTargets[index].entity : Entity.Null, 
                layerMaskAndTags,
                levelStatus, 
                collisionWorld, 
                fixedLocalToWorld.parents, 
                characterBodies, 
                animationCurveDeltas, 
                colliders[index].AsNativeArray(), 
                prefabs[index].AsNativeArray(),
                activeIndices[index].AsNativeArray(),
                inputMessages,
                ref messageSharedIndices, 
                ref outputMessages,
                ref characterStandTimes, 
                ref targetStates,
                ref states,
                ref instances,
                ref version, 
                ref random);
            
            versions[index] = version;

            int numInstances = instances.Length;
            for (int i = 0; i < numInstances; i++)
            {
                if (instances[i].Apply(
                        instanceID, 
                        time,
                        collisionWorld,
                        physicsGraphicalInterpolationBuffers,
                        characterInterpolations, 
                        characterControls,
                        animationCurveDeltas,
                        followTargetVelocities, 
                        messages, 
                        messageParameters, 
                        inputMessages, 
                        ref definition,
                        ref prefabLoader, 
                        ref entityManager, 
                        ref random))
                {
                    instances.RemoveAtSwapBack(i--);
                    
                    --numInstances;
                }
            }

            return outputMessages.IsCreated && outputMessages.Length > 0;
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public bool isFire;
        
        public double time;

        public float3 gravity;

        public quaternion cameraRotation;

        public RenderFrustumPlanes renderFrustumPlanes;

        public Entity levelEntity;

        public FixedLocalToWorld fixedLocalToWorld;

        [ReadOnly] 
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<PhysicsGraphicalInterpolationBuffer> physicsGraphicalInterpolationBuffers;

        [ReadOnly]
        public ComponentLookup<CharacterInterpolation> characterInterpolations;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public ComponentLookup<ThirdPersonCharacterControl> characterControls;

        [ReadOnly] 
        public ComponentLookup<CameraRotation> cameraRotations;

        [ReadOnly]
        public ComponentLookup<AnimationCurveDelta> animationCurveDeltas;

        [ReadOnly] 
        public ComponentLookup<FollowTargetVelocity> followTargetVelocities;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> effectDamages;

        [ReadOnly] 
        public ComponentLookup<EffectDamageParent> effectDamageParents;

        //[ReadOnly] 
        //public ComponentLookup<EffectTarget> effectTargets;

        [ReadOnly] 
        public ComponentLookup<BulletLayerMaskAndTags> bulletLayerMaskAndTags;

        [ReadOnly] 
        public ComponentLookup<LevelStatus> levelStates;

        [ReadOnly]
        public ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs;

        [ReadOnly]
        public BufferLookup<Message> messages;

        [ReadOnly]
        public BufferLookup<MessageParameter> messageParameters;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly] 
        public ComponentTypeHandle<LookAtTarget> lookAtTargetType;

        [ReadOnly]
        public ComponentTypeHandle<BulletDefinitionData> definitionType;

        [ReadOnly] 
        public BufferTypeHandle<BulletActiveIndex> activeIndexType;

        [ReadOnly] 
        public BufferTypeHandle<BulletCollider> colliderType;

        [ReadOnly]
        public BufferTypeHandle<BulletPrefab> prefabType;

        [ReadOnly]
        public BufferTypeHandle<BulletMessage> inputMessageType;

        [NativeDisableContainerSafetyRestriction]
        public BufferTypeHandle<Message> outputMessageType;

        public BufferTypeHandle<DelayTime> delayTimeType;

        public BufferTypeHandle<ThirdPersonCharacterStandTime> characterStandTimeType;

        public BufferTypeHandle<BulletMessageSharedIndex> messageSharedIndexType;

        public BufferTypeHandle<BulletStatus> statusType;

        public BufferTypeHandle<BulletTargetStatus> targetStatusType;

        public BufferTypeHandle<BulletInstance> instanceType;

        public ComponentTypeHandle<BulletVersion> versionType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            long hash = math.aslong(time);
            
            Collect collect;
            collect.isFire = isFire;
            collect.time = time;
            collect.gravity = gravity;
            collect.random = Random.CreateFromIndex((uint)((int)hash ^ (int)(hash >> 32) ^ unfilteredChunkIndex));
            collect.cameraRotation = cameraRotation;
            collect.renderFrustumPlanes = renderFrustumPlanes;
            collect.levelStatus = levelStates.TryGetComponent(levelEntity, out var levelStatus) ? levelStatus : default;
            collect.fixedLocalToWorld = fixedLocalToWorld;
            collect.collisionWorld = collisionWorld;
            collect.physicsGraphicalInterpolationBuffers = physicsGraphicalInterpolationBuffers;
            collect.characterInterpolations = characterInterpolations;
            collect.characterBodies = characterBodies;
            collect.characterControls = characterControls;
            collect.cameraRotations = cameraRotations;
            collect.animationCurveDeltas = animationCurveDeltas;
            collect.followTargetVelocities = followTargetVelocities;
            collect.effectDamages = effectDamages;
            collect.effectDamageParents = effectDamageParents;
            //collect.effectTargets = effectTargets;
            collect.bulletLayerMaskAndTags = bulletLayerMaskAndTags;
            collect.copyMatrixToTransformInstanceIDs = copyMatrixToTransformInstanceIDs;
            collect.messages = messages;
            collect.messageParameters = messageParameters;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.lookAtTargets = chunk.GetNativeArray(ref lookAtTargetType);
            collect.definitions = chunk.GetNativeArray(ref definitionType);
            collect.activeIndices = chunk.GetBufferAccessor(ref activeIndexType);
            collect.colliders = chunk.GetBufferAccessor(ref colliderType);
            collect.prefabs = chunk.GetBufferAccessor(ref prefabType);
            collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            collect.messageSharedIndices = chunk.GetBufferAccessor(ref messageSharedIndexType);
            collect.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            collect.delayTimes = chunk.GetBufferAccessor(ref delayTimeType);
            collect.characterStandTimes = chunk.GetBufferAccessor(ref characterStandTimeType);
            collect.states = chunk.GetBufferAccessor(ref statusType);
            collect.targetStates = chunk.GetBufferAccessor(ref targetStatusType);
            collect.instances = chunk.GetBufferAccessor(ref instanceType);
            collect.versions = chunk.GetNativeArray(ref versionType);
            collect.entityManager = entityManager;
            collect.prefabLoader = prefabLoader;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(collect.Execute(i))
                    chunk.SetComponentEnabled(ref outputMessageType, i, true);
            }
        }
    }

    private struct SharedMessages
    {
        [ReadOnly]
        public NativeArray<BulletEntity> entities;

        [ReadOnly] 
        public NativeArray<BulletMessageShared> instances;
        
        [ReadOnly]
        public BufferLookup<BulletMessageSharedIndex> messageSharedIndices;

        [ReadOnly]
        public BufferLookup<BulletMessage> inputMessages;

        public BufferAccessor<Message> outputMessages;

        public void Execute(int index)
        {
            Entity entity = entities[index].parent;
            if(!this.messageSharedIndices.TryGetBuffer(entity, out var messageSharedIndices) || 
               messageSharedIndices.Length < 1)
                return;

            var instance = instances[index];
            BulletMessage inputMessage;
            Message outputMessage;
            var outputMessages = this.outputMessages[index];
            var inputMessages = this.inputMessages[entity];
            foreach (var messageSharedIndex in messageSharedIndices)
            {
                inputMessage = inputMessages[messageSharedIndex.value];
                if(!inputMessage.layerMaskAndTags.Overlaps(instance.layerMaskAndTags))
                    continue;

                outputMessage.key = 0;
                outputMessage.name = inputMessage.name;
                outputMessage.value = inputMessage.value;

                outputMessages.Add(outputMessage);
            }
        }
    }

    [BurstCompile]
    private struct SharedMessagesEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<BulletEntity> entityType;
        [ReadOnly]
        public ComponentTypeHandle<BulletMessageShared> instanceType;
        
        [ReadOnly]
        public BufferLookup<BulletMessageSharedIndex> messageSharedIndices;
        
        [ReadOnly]
        public BufferLookup<BulletMessage> inputMessages;

        public BufferTypeHandle<Message> outputMessageType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            SharedMessages sharedMessages;
            sharedMessages.entities = chunk.GetNativeArray(ref entityType);
            sharedMessages.instances = chunk.GetNativeArray(ref instanceType);
            sharedMessages.messageSharedIndices = messageSharedIndices;
            sharedMessages.inputMessages = inputMessages;
            sharedMessages.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
                sharedMessages.Execute(i);
        }
    }

    private FixedLocalToWorld __fixedLocalToWorld;

    private ComponentLookup<PhysicsGraphicalInterpolationBuffer> __physicsGraphicalInterpolationBuffers;

    private ComponentLookup<CharacterInterpolation> __characterInterpolations;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private ComponentLookup<ThirdPersonCharacterControl> __characterControls;

    private ComponentLookup<CameraRotation> __cameraRotations;

    private ComponentLookup<AnimationCurveDelta> __animationCurveDeltas;

    private ComponentLookup<FollowTargetVelocity> __followTargetVelocities;

    private ComponentLookup<EffectDamage> __effectDamages;

    private ComponentLookup<EffectDamageParent> __effectDamageParents;

    //private ComponentLookup<EffectTarget> __effectTargets;

    private ComponentLookup<BulletLayerMaskAndTags> __bulletLayerMaskAndTags;

    private ComponentLookup<LevelStatus> __levelStates;

    private ComponentLookup<CopyMatrixToTransformInstanceID> __copyMatrixToTransformInstanceIDs;

    private BufferLookup<Message> __messages;

    private BufferLookup<MessageParameter> __messageParameters;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<LookAtTarget> __lookAtType;

    private ComponentTypeHandle<BulletDefinitionData> __definitionType;

    private BufferTypeHandle<BulletActiveIndex> __activeIndexType;
    
    private BufferTypeHandle<BulletCollider> __colliderType;

    private BufferTypeHandle<BulletPrefab> __prefabType;

    private BufferTypeHandle<BulletMessage> __inputMessageType;

    private BufferTypeHandle<BulletInstance> __instanceType;

    private BufferTypeHandle<BulletStatus> __statusType;

    private BufferTypeHandle<BulletTargetStatus> __targetStatusType;

    private BufferTypeHandle<BulletMessageSharedIndex> __messageSharedIndexType;

    private BufferTypeHandle<Message> __outputMessageType;

    private BufferTypeHandle<DelayTime> __delayTimeType;

    private BufferTypeHandle<ThirdPersonCharacterStandTime> __characterStandTimeType;

    private ComponentTypeHandle<BulletVersion> __versionType;

    private ComponentTypeHandle<BulletEntity> __bulletEntityType;
    
    private ComponentTypeHandle<BulletMessageShared> __messageSharedType;
        
    private BufferLookup<BulletMessageSharedIndex> __messageSharedIndices;
        
    private BufferLookup<BulletMessage> __inputMessages;

    private EntityQuery __targetGroup;
    private EntityQuery __shooterGroup;
    private EntityQuery __bulletGroup;

    private PrefabLoader __prefabLoader;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __fixedLocalToWorld = new FixedLocalToWorld(ref state);
        __physicsGraphicalInterpolationBuffers = state.GetComponentLookup<PhysicsGraphicalInterpolationBuffer>(true);
        __characterInterpolations = state.GetComponentLookup<CharacterInterpolation>(true);
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>(true);
        __characterControls = state.GetComponentLookup<ThirdPersonCharacterControl>(true);
        __cameraRotations = state.GetComponentLookup<CameraRotation>(true);
        __animationCurveDeltas = state.GetComponentLookup<AnimationCurveDelta>(true);
        __followTargetVelocities = state.GetComponentLookup<FollowTargetVelocity>(true);
        __effectDamages = state.GetComponentLookup<EffectDamage>(true);
        __effectDamageParents = state.GetComponentLookup<EffectDamageParent>(true);
        //__effectTargets = state.GetComponentLookup<EffectTarget>(true);
        __bulletLayerMaskAndTags = state.GetComponentLookup<BulletLayerMaskAndTags>(true);
        __levelStates = state.GetComponentLookup<LevelStatus>(true);
        __copyMatrixToTransformInstanceIDs = state.GetComponentLookup<CopyMatrixToTransformInstanceID>(true);
        __messages = state.GetBufferLookup<Message>(true);
        __messageParameters = state.GetBufferLookup<MessageParameter>(true);
        __entityType = state.GetEntityTypeHandle();
        __lookAtType = state.GetComponentTypeHandle<LookAtTarget>(true);
        __definitionType = state.GetComponentTypeHandle<BulletDefinitionData>(true);
        __activeIndexType = state.GetBufferTypeHandle<BulletActiveIndex>(true);
        __colliderType = state.GetBufferTypeHandle<BulletCollider>(true);
        __prefabType = state.GetBufferTypeHandle<BulletPrefab>(true);
        __inputMessageType = state.GetBufferTypeHandle<BulletMessage>(true);
        __instanceType = state.GetBufferTypeHandle<BulletInstance>();
        __statusType = state.GetBufferTypeHandle<BulletStatus>();
        __targetStatusType = state.GetBufferTypeHandle<BulletTargetStatus>();
        __messageSharedIndexType = state.GetBufferTypeHandle<BulletMessageSharedIndex>();
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __delayTimeType = state.GetBufferTypeHandle<DelayTime>();
        __characterStandTimeType = state.GetBufferTypeHandle<ThirdPersonCharacterStandTime>();
        __versionType = state.GetComponentTypeHandle<BulletVersion>();
        __bulletEntityType = state.GetComponentTypeHandle<BulletEntity>(true);
        __messageSharedType = state.GetComponentTypeHandle<BulletMessageShared>(true);
        __messageSharedIndices = state.GetBufferLookup<BulletMessageSharedIndex>(true);
        __inputMessages = state.GetBufferLookup<BulletMessage>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __targetGroup = builder
                .WithAll<EffectTargetData>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __shooterGroup = builder
                .WithAll<LocalToWorld, BulletDefinitionData, BulletActiveIndex>()
                .WithAllRW<BulletStatus, BulletTargetStatus>()
                .WithAllRW<BulletInstance, BulletVersion>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __bulletGroup = builder
                .WithAll<BulletEntity, BulletMessageShared>()
                .Build(ref state);

        __prefabLoader = new PrefabLoader(ref state);

        state.RequireForUpdate<MainCameraTransform>();
        
        state.RequireForUpdate<PhysicsWorldSingleton>();
        
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __fixedLocalToWorld.Update(ref state);
        __physicsGraphicalInterpolationBuffers.Update(ref state);
        __characterInterpolations.Update(ref state);
        __characterBodies.Update(ref state);
        __characterControls.Update(ref state);
        __cameraRotations.Update(ref state);
        __animationCurveDeltas.Update(ref state);
        __followTargetVelocities.Update(ref state);
        __effectDamages.Update(ref state);
        __effectDamageParents.Update(ref state);
        //__effectTargets.Update(ref state);
        __bulletLayerMaskAndTags.Update(ref state);
        __levelStates.Update(ref state);
        __copyMatrixToTransformInstanceIDs.Update(ref state);
        __messages.Update(ref state);
        __messageParameters.Update(ref state);
        __entityType.Update(ref state);
        __lookAtType.Update(ref state);
        __definitionType.Update(ref state);
        __activeIndexType.Update(ref state);
        __colliderType.Update(ref state);
        __prefabType.Update(ref state);
        __instanceType.Update(ref state);
        __statusType.Update(ref state);
        __targetStatusType.Update(ref state);
        __inputMessageType.Update(ref state);
        __messageSharedIndexType.Update(ref state);
        __outputMessageType.Update(ref state);
        __delayTimeType.Update(ref state);
        __characterStandTimeType.Update(ref state);
        __versionType.Update(ref state);
        //__prefabLoader.Update(ref state);

        var mainCameraEntity = SystemAPI.GetSingletonEntity<MainCameraTransform>();

        CollectEx collect;
        collect.isFire = __targetGroup.CalculateEntityCount() > 1;
        collect.time = SystemAPI.Time.ElapsedTime;
        collect.gravity = SystemAPI.TryGetSingleton<PhysicsStep>(out var physicsStep) ? physicsStep.Gravity : PhysicsStep.Default.Gravity;
        collect.cameraRotation = SystemAPI.GetComponent<MainCameraTransform>(mainCameraEntity).rotation;
        collect.renderFrustumPlanes = SystemAPI.GetComponent<RenderFrustumPlanes>(mainCameraEntity);
        SystemAPI.TryGetSingletonEntity<LevelStatus>(out collect.levelEntity);
        collect.collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        collect.fixedLocalToWorld = __fixedLocalToWorld;
        collect.physicsGraphicalInterpolationBuffers = __physicsGraphicalInterpolationBuffers;
        collect.characterInterpolations = __characterInterpolations;
        collect.characterBodies = __characterBodies;
        collect.characterControls = __characterControls;
        collect.cameraRotations = __cameraRotations;
        collect.animationCurveDeltas = __animationCurveDeltas;
        collect.followTargetVelocities = __followTargetVelocities;
        collect.effectDamages = __effectDamages;
        collect.effectDamageParents = __effectDamageParents;
        //collect.effectTargets = __effectTargets;
        collect.bulletLayerMaskAndTags = __bulletLayerMaskAndTags;
        collect.levelStates = __levelStates;
        collect.copyMatrixToTransformInstanceIDs = __copyMatrixToTransformInstanceIDs;
        collect.messages = __messages;
        collect.messageParameters = __messageParameters;
        collect.entityType = __entityType;
        collect.lookAtTargetType = __lookAtType;
        collect.definitionType = __definitionType;
        collect.activeIndexType = __activeIndexType;
        collect.colliderType = __colliderType;
        collect.prefabType = __prefabType;
        collect.instanceType = __instanceType;
        collect.statusType = __statusType;
        collect.targetStatusType = __targetStatusType;
        collect.inputMessageType = __inputMessageType;
        collect.messageSharedIndexType = __messageSharedIndexType;
        collect.outputMessageType = __outputMessageType;
        collect.delayTimeType = __delayTimeType;
        collect.characterStandTimeType = __characterStandTimeType;
        collect.versionType = __versionType;
        collect.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        collect.prefabLoader = __prefabLoader.AsParallelWriter();

        var jobHandle = collect.ScheduleParallelByRef(__shooterGroup, state.Dependency);
        
        __bulletEntityType.Update(ref state);
        __messageSharedType.Update(ref state);
        __messageSharedIndices.Update(ref state);
        __inputMessages.Update(ref state);

        SharedMessagesEx sharedMessages;
        sharedMessages.entityType = __bulletEntityType;
        sharedMessages.instanceType = __messageSharedType;
        sharedMessages.messageSharedIndices = __messageSharedIndices;
        sharedMessages.inputMessages = __inputMessages;
        sharedMessages.outputMessageType = __outputMessageType;
        
        state.Dependency = sharedMessages.ScheduleParallelByRef(__bulletGroup, jobHandle);
    }
}
