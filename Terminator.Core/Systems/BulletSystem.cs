using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
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

        public Random random;

        public quaternion cameraRotation;

        public LevelStatus levelStatus;

        [ReadOnly] 
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly] 
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly]
        public ComponentLookup<PhysicsGraphicalInterpolationBuffer> physicsGraphicalInterpolationBuffers;

        [ReadOnly]
        public ComponentLookup<CharacterInterpolation> characterInterpolations;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public ComponentLookup<ThirdPersonCharacterControl> characterControls;

        [ReadOnly]
        public ComponentLookup<AnimationCurveDelta> animationCurveDeltas;

        [ReadOnly] 
        public ComponentLookup<EffectDamageParent> effectDamageParents;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> effectDamages;

        [ReadOnly]
        public ComponentLookup<BulletLayerMaskAndTags> bulletLayerMaskAndTags;

        [ReadOnly] 
        public NativeArray<Entity> entityArray;

        [ReadOnly] 
        public NativeArray<LookAtTarget> lookAtTargets;

        [ReadOnly]
        public NativeArray<BulletDefinitionData> definitions;

        [ReadOnly]
        public BufferAccessor<BulletPrefab> prefabs;

        [ReadOnly] 
        public BufferAccessor<BulletActiveIndex> activeIndices;
        
        [ReadOnly] 
        public BufferAccessor<BulletMessage> inputMessages;

        public BufferAccessor<Message> outputMessages;

        public BufferAccessor<DelayTime> delayTimes;

        public BufferAccessor<BulletStatus> states;

        public BufferAccessor<BulletTargetStatus> targetStates;

        public BufferAccessor<BulletInstance> instances;

        public NativeArray<BulletVersion> versions;

        [NativeDisableParallelForRestriction] 
        public BufferLookup<ThirdPersonCharacterStandTime> characterStandTimes;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;
        
        public bool Execute(int index)
        {
            Entity entity = entityArray[index];
            EffectDamageParent.TryGetComponent(
                entity,
                effectDamageParents,
                characterBodies,
                out var characterBody,
                out var character);
    
            BulletLocation location = 0;
            float3 up = math.up();
            ref var definition = ref definitions[index].definition.Value;
            DynamicBuffer<ThirdPersonCharacterStandTime> characterStandTimes;
            if (character == Entity.Null)
            {
                characterStandTimes = default;

                location = (BulletLocation)~0;
            }
            else
            {
                up = characterBody.GroundingUp;

                if (characterBody.IsGrounded)
                    location = BulletLocation.Ground;
                else
                {
                    float dot = math.dot(characterBody.RelativeVelocity, characterBody.GroundingUp);
                    if (dot > definition.minAirSpeed && dot < definition.maxAirSpeed)
                        location = BulletLocation.Air;
                }

                this.characterStandTimes.TryGetBuffer(character, out characterStandTimes);
            }

            bool isFire = this.isFire;
            
            var instances = this.instances[index];
            var delayTimes = index < this.delayTimes.Length ? this.delayTimes[index] : default;
            if (DelayTime.IsDelay(ref delayTimes, time, out _))
            {
                instances.Clear();
                
                isFire = false;
            }

            float damageScale;
            LayerMaskAndTags layerMaskAndTags;
            if (isFire)
            {
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
            }
            else
            {
                damageScale = 0.0f;
                
                layerMaskAndTags = default;
            }

            var localToWorld = GetLocalToWorld(entity);
            var outputMessages = index < this.outputMessages.Length ? this.outputMessages[index] : default;
            var targetStates = this.targetStates[index];
            var states = this.states[index];
            var version = versions[index];
            definition.Update(
                location, 
                damageScale, 
                time,
                up, 
                cameraRotation, 
                localToWorld, 
                entity,
                index < lookAtTargets.Length ? lookAtTargets[index].entity : Entity.Null, 
                layerMaskAndTags,
                levelStatus, 
                collisionWorld, 
                parents, 
                physicsColliders,
                characterBodies, 
                animationCurveDeltas, 
                prefabs[index],
                activeIndices[index],
                inputMessages[index],
                ref outputMessages,
                ref characterStandTimes, 
                ref targetStates,
                ref states,
                ref instances,
                ref prefabLoader, 
                ref version, 
                ref random);
            
            versions[index] = version;

            int numInstances = instances.Length;
            for (int i = 0; i < numInstances; i++)
            {
                if (instances[i].Apply(
                        time,
                        collisionWorld,
                        physicsGraphicalInterpolationBuffers,
                        characterInterpolations, 
                        characterControls,
                        animationCurveDeltas,
                        ref definition,
                        ref prefabLoader, 
                        ref entityManager))
                {
                    instances.RemoveAtSwapBack(i--);
                    
                    --numInstances;
                }
            }

            return outputMessages.IsCreated && outputMessages.Length > 0;
        }

        public float4x4 GetLocalToWorld(in Entity entity)
        {
            float4x4 matrix = localTransforms.TryGetComponent(entity, out var localTransform)
                ? localTransform.ToMatrix()
                : float4x4.identity;

            if (parents.TryGetComponent(entity, out var parent))
                matrix = math.mul(GetLocalToWorld(parent.Value), matrix);

            return matrix;
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public bool isFire;
        
        public double time;

        public quaternion cameraRotation;

        public Entity levelEntity;

        [ReadOnly] 
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly] 
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly]
        public ComponentLookup<PhysicsGraphicalInterpolationBuffer> physicsGraphicalInterpolationBuffers;

        [ReadOnly]
        public ComponentLookup<CharacterInterpolation> characterInterpolations;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public ComponentLookup<ThirdPersonCharacterControl> characterControls;

        [ReadOnly]
        public ComponentLookup<AnimationCurveDelta> animationCurveDeltas;

        [ReadOnly] 
        public ComponentLookup<EffectDamage> effectDamages;

        [ReadOnly] 
        public ComponentLookup<EffectDamageParent> effectDamageParents;

        [ReadOnly] 
        public ComponentLookup<BulletLayerMaskAndTags> bulletLayerMaskAndTags;

        [ReadOnly] 
        public ComponentLookup<LevelStatus> levelStates;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly] 
        public ComponentTypeHandle<LookAtTarget> lookAtTargetType;

        [ReadOnly]
        public ComponentTypeHandle<BulletDefinitionData> definitionType;

        [ReadOnly]
        public BufferTypeHandle<BulletPrefab> prefabType;

        [ReadOnly] 
        public BufferTypeHandle<BulletActiveIndex> activeIndexType;

        [ReadOnly]
        public BufferTypeHandle<BulletMessage> inputMessageType;

        public BufferTypeHandle<Message> outputMessageType;

        public BufferTypeHandle<DelayTime> delayTimeType;

        public BufferTypeHandle<BulletStatus> statusType;

        public BufferTypeHandle<BulletTargetStatus> targetStatusType;

        public BufferTypeHandle<BulletInstance> instanceType;

        public ComponentTypeHandle<BulletVersion> versionType;

        [NativeDisableParallelForRestriction] 
        public BufferLookup<ThirdPersonCharacterStandTime> characterStandTimes;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PrefabLoader.ParallelWriter prefabLoader;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            long hash = math.aslong(time);
            
            Collect collect;
            collect.isFire = isFire;
            collect.time = time;
            collect.random = Random.CreateFromIndex((uint)((int)hash ^ (int)(hash >> 32) ^ unfilteredChunkIndex));
            collect.cameraRotation = cameraRotation;
            collect.levelStatus = levelStates.TryGetComponent(levelEntity, out var levelStatus) ? levelStatus : default;
            collect.collisionWorld = collisionWorld;
            collect.parents = parents;
            collect.localTransforms = localTransforms;
            collect.physicsColliders = physicsColliders;
            collect.physicsGraphicalInterpolationBuffers = physicsGraphicalInterpolationBuffers;
            collect.characterInterpolations = characterInterpolations;
            collect.characterBodies = characterBodies;
            collect.characterControls = characterControls;
            collect.animationCurveDeltas = animationCurveDeltas;
            collect.effectDamages = effectDamages;
            collect.effectDamageParents = effectDamageParents;
            collect.bulletLayerMaskAndTags = bulletLayerMaskAndTags;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.lookAtTargets = chunk.GetNativeArray(ref lookAtTargetType);
            collect.definitions = chunk.GetNativeArray(ref definitionType);
            collect.prefabs = chunk.GetBufferAccessor(ref prefabType);
            collect.activeIndices = chunk.GetBufferAccessor(ref activeIndexType);
            collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            collect.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            collect.delayTimes = chunk.GetBufferAccessor(ref delayTimeType);
            collect.states = chunk.GetBufferAccessor(ref statusType);
            collect.targetStates = chunk.GetBufferAccessor(ref targetStatusType);
            collect.instances = chunk.GetBufferAccessor(ref instanceType);
            collect.versions = chunk.GetNativeArray(ref versionType);
            collect.characterStandTimes = characterStandTimes;
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

    private ComponentLookup<Parent> __parents;

    private ComponentLookup<LocalTransform> __localTransforms;
    
    private ComponentLookup<PhysicsCollider> __physicsColliders;
    
    private ComponentLookup<PhysicsGraphicalInterpolationBuffer> __physicsGraphicalInterpolationBuffers;

    private ComponentLookup<CharacterInterpolation> __characterInterpolations;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private ComponentLookup<ThirdPersonCharacterControl> __characterControls;

    private ComponentLookup<AnimationCurveDelta> __animationCurveDeltas;

    private ComponentLookup<EffectDamage> __effectDamages;

    private ComponentLookup<EffectDamageParent> __effectDamageParents;

    private ComponentLookup<BulletLayerMaskAndTags> __bulletLayerMaskAndTags;

    private ComponentLookup<LevelStatus> __levelStates;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<LookAtTarget> __lookAtType;

    private ComponentTypeHandle<BulletDefinitionData> __definitionType;

    private BufferTypeHandle<BulletPrefab> __prefabType;

    private BufferTypeHandle<BulletActiveIndex> __activeIndexType;
    
    private BufferTypeHandle<BulletInstance> __instanceType;

    private BufferTypeHandle<BulletStatus> __statusType;

    private BufferTypeHandle<BulletTargetStatus> __targetStatusType;

    private BufferTypeHandle<BulletMessage> __inputMessageType;

    private BufferTypeHandle<Message> __outputMessageType;

    private BufferTypeHandle<DelayTime> __delayTimeType;

    private ComponentTypeHandle<BulletVersion> __versionType;

    private BufferLookup<ThirdPersonCharacterStandTime> __characterStandTimes;

    private EntityQuery __group;
    private EntityQuery __targetGroup;

    private PrefabLoader __prefabLoader;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __parents = state.GetComponentLookup<Parent>(true);
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __physicsGraphicalInterpolationBuffers = state.GetComponentLookup<PhysicsGraphicalInterpolationBuffer>(true);
        __characterInterpolations = state.GetComponentLookup<CharacterInterpolation>(true);
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>(true);
        __characterControls = state.GetComponentLookup<ThirdPersonCharacterControl>(true);
        __animationCurveDeltas = state.GetComponentLookup<AnimationCurveDelta>(true);
        __effectDamages = state.GetComponentLookup<EffectDamage>(true);
        __effectDamageParents = state.GetComponentLookup<EffectDamageParent>(true);
        __bulletLayerMaskAndTags = state.GetComponentLookup<BulletLayerMaskAndTags>(true);
        __levelStates = state.GetComponentLookup<LevelStatus>(true);
        __entityType = state.GetEntityTypeHandle();
        __lookAtType = state.GetComponentTypeHandle<LookAtTarget>(true);
        __definitionType = state.GetComponentTypeHandle<BulletDefinitionData>(true);
        __prefabType = state.GetBufferTypeHandle<BulletPrefab>(true);
        __activeIndexType = state.GetBufferTypeHandle<BulletActiveIndex>(true);
        __instanceType = state.GetBufferTypeHandle<BulletInstance>();
        __statusType = state.GetBufferTypeHandle<BulletStatus>();
        __targetStatusType = state.GetBufferTypeHandle<BulletTargetStatus>();
        __inputMessageType = state.GetBufferTypeHandle<BulletMessage>(true);
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __delayTimeType = state.GetBufferTypeHandle<DelayTime>();
        __versionType = state.GetComponentTypeHandle<BulletVersion>();
        __characterStandTimes = state.GetBufferLookup<ThirdPersonCharacterStandTime>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LocalToWorld, BulletDefinitionData, BulletActiveIndex>()
                .WithAllRW<BulletStatus, BulletTargetStatus>()
                .WithAllRW<BulletInstance, BulletVersion>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __targetGroup = builder
                .WithAll<EffectTargetData>()
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
        __parents.Update(ref state);
        __localTransforms.Update(ref state);
        __physicsColliders.Update(ref state);
        __physicsGraphicalInterpolationBuffers.Update(ref state);
        __characterInterpolations.Update(ref state);
        __characterBodies.Update(ref state);
        __characterControls.Update(ref state);
        __animationCurveDeltas.Update(ref state);
        __effectDamages.Update(ref state);
        __effectDamageParents.Update(ref state);
        __bulletLayerMaskAndTags.Update(ref state);
        __levelStates.Update(ref state);
        __entityType.Update(ref state);
        __lookAtType.Update(ref state);
        __definitionType.Update(ref state);
        __prefabType.Update(ref state);
        __activeIndexType.Update(ref state);
        __instanceType.Update(ref state);
        __statusType.Update(ref state);
        __targetStatusType.Update(ref state);
        __inputMessageType.Update(ref state);
        __outputMessageType.Update(ref state);
        __delayTimeType.Update(ref state);
        __versionType.Update(ref state);
        __characterStandTimes.Update(ref state);
        //__prefabLoader.Update(ref state);

        CollectEx collect;
        collect.isFire = __targetGroup.CalculateEntityCount() > 1;
        collect.time = SystemAPI.Time.ElapsedTime;
        collect.cameraRotation = SystemAPI.GetSingleton<MainCameraTransform>().rotation;
        SystemAPI.TryGetSingletonEntity<LevelStatus>(out collect.levelEntity);
        collect.collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        collect.parents = __parents;
        collect.localTransforms = __localTransforms;
        collect.physicsColliders = __physicsColliders;
        collect.physicsGraphicalInterpolationBuffers = __physicsGraphicalInterpolationBuffers;
        collect.characterInterpolations = __characterInterpolations;
        collect.characterBodies = __characterBodies;
        collect.characterControls = __characterControls;
        collect.animationCurveDeltas = __animationCurveDeltas;
        collect.effectDamages = __effectDamages;
        collect.effectDamageParents = __effectDamageParents;
        collect.bulletLayerMaskAndTags = __bulletLayerMaskAndTags;
        collect.levelStates = __levelStates;
        collect.entityType = __entityType;
        collect.lookAtTargetType = __lookAtType;
        collect.definitionType = __definitionType;
        collect.prefabType = __prefabType;
        collect.activeIndexType = __activeIndexType;
        collect.instanceType = __instanceType;
        collect.statusType = __statusType;
        collect.targetStatusType = __targetStatusType;
        collect.inputMessageType = __inputMessageType;
        collect.outputMessageType = __outputMessageType;
        collect.delayTimeType = __delayTimeType;
        collect.versionType = __versionType;
        collect.characterStandTimes = __characterStandTimes;
        collect.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        collect.prefabLoader = __prefabLoader.AsParallelWriter();

        state.Dependency = collect.ScheduleParallelByRef(__group, state.Dependency);
    }
}
