using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Unity.Scenes;

[BurstCompile, 
 UpdateInGroup(typeof(AfterPhysicsSystemGroup)), UpdateAfter(typeof(KinematicCharacterPhysicsUpdateGroup))]//, UpdateAfter(typeof(LookAtSystem))]
public partial struct BulletSystem : ISystem
{
    private struct Collect
    {
        public double time;

        public Random random;

        public quaternion cameraRotation;

        [ReadOnly] 
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly] 
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly] 
        public ComponentLookup<PrefabLoadResult> prefabLoadResults;
        
        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterBody> characterBodyMap;

        [ReadOnly] 
        public ComponentLookup<ThirdPersonCharacterControl> characterControls;

        [ReadOnly] 
        public ComponentLookup<FollowTarget> followTargetMap;

        [ReadOnly]
        public ComponentLookup<AnimationCurveDelta> animationCurveDeltas;

        [ReadOnly]
        public ComponentLookup<BulletLayerMask> layerMaskMap;

        [ReadOnly] 
        public ComponentLookup<BulletDamageScale> damageScaleMap;

        [ReadOnly] 
        public NativeArray<Entity> entityArray;

        [ReadOnly] 
        public NativeArray<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public NativeArray<LookAtTarget> lookAtTargets;

        [ReadOnly] 
        public NativeArray<FollowTarget> followTargets;

        [ReadOnly]
        public NativeArray<BulletDefinitionData> instances;

        [ReadOnly] 
        public NativeArray<BulletLayerMask> layerMasks;

        [ReadOnly] 
        public NativeArray<BulletDamageScale> damageScales;

        [ReadOnly]
        public BufferAccessor<BulletPrefab> prefabs;

        [ReadOnly] 
        public BufferAccessor<BulletActiveIndex> activeIndices;
        
        [ReadOnly] 
        public BufferAccessor<BulletMessage> inputMessages;

        public BufferAccessor<Message> outputMessages;

        public BufferAccessor<BulletStatus> states;

        public BufferAccessor<BulletTargetStatus> targetStates;

        public NativeArray<BulletVersion> versions;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public bool Execute(int index)
        {
            int layerMask = index < layerMasks.Length ? layerMasks[index].value : -1;
            float damageScale = index < damageScales.Length ? damageScales[index].value : 1.0f;
            Entity entity = entityArray[index], 
                character = index < characterBodies.Length ? entityArray[index] : Entity.Null;
            KinematicCharacterBody characterBody = default;
            if (character == Entity.Null)
            {
                int rigidBodyIndex = collisionWorld.GetRigidBodyIndex(entity);
                bool isStatic = rigidBodyIndex == -1 || rigidBodyIndex >= collisionWorld.NumDynamicBodies;
                BulletLayerMask layerMaskComponent;
                BulletDamageScale damageScaleComponent;
                character = isStatic && 
                            index < followTargets.Length && 
                            characterBodyMap.TryGetComponent(followTargets[index].entity, out characterBody) ? followTargets[index].entity : Entity.Null;
                if (character == Entity.Null)
                {
                    Entity temp = entity;
                    FollowTarget followTarget;
                    while (parents.TryGetComponent(temp, out var parent))
                    {
                        if (layerMaskMap.TryGetComponent(temp, out layerMaskComponent))
                            layerMask &= layerMaskComponent.value == 0 ? -1 : layerMaskComponent.value;
                        
                        if (damageScaleMap.TryGetComponent(temp, out damageScaleComponent))
                            damageScale *= damageScaleComponent.value;
                        
                        if (characterBodyMap.TryGetComponent(parent.Value, out characterBody))
                        {
                            character = parent.Value;

                            break;
                        }

                        if (isStatic &&
                            followTargetMap.TryGetComponent(parent.Value, out followTarget))
                        {
                            if (layerMaskMap.TryGetComponent(followTarget.entity, out layerMaskComponent))
                                layerMask &= layerMaskComponent.value == 0 ? -1 : layerMaskComponent.value;

                            if (damageScaleMap.TryGetComponent(followTarget.entity, out damageScaleComponent))
                                damageScale *= damageScaleComponent.value;

                            if(characterBodyMap.TryGetComponent(followTarget.entity, out characterBody))
                            {
                                character = followTarget.entity;

                                break;
                            }
                        }

                        temp = parent.Value;
                    }
                }
            }
            else
                characterBody = characterBodies[index];

            BulletLocation location = 0;
            float3 up = math.up();
            ref var definition = ref instances[index].definition.Value;
            if (character != Entity.Null)
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
            }

            var localToWorld = GetLocalToWorld(entity);
            var outputMessages = index < this.outputMessages.Length ? this.outputMessages[index] : default;
            var targetStates = this.targetStates[index];
            var states = this.states[index];
            var version = versions[index];
            definition.Update(
                location, 
                layerMask, 
                damageScale, 
                time,
                up, 
                cameraRotation, 
                localToWorld, 
                entity,
                index < lookAtTargets.Length ? lookAtTargets[index].entity : Entity.Null, 
                collisionWorld, 
                parents, 
                physicsColliders,
                characterBodyMap, 
                characterControls,
                animationCurveDeltas, 
                prefabLoadResults,
                prefabs[index],
                activeIndices[index],
                inputMessages[index],
                ref outputMessages,
                ref targetStates,
                ref states,
                ref entityManager,
                ref version, 
                ref random);
            
            versions[index] = version;

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
        public double time;

        public quaternion cameraRotation;

        [ReadOnly] 
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly] 
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly] 
        public ComponentLookup<PrefabLoadResult> prefabLoadResults;

        [ReadOnly] 
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly] 
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public ComponentLookup<ThirdPersonCharacterControl> characterControls;

        [ReadOnly] 
        public ComponentLookup<FollowTarget> followTargets;

        [ReadOnly]
        public ComponentLookup<AnimationCurveDelta> animationCurveDeltas;

        [ReadOnly] 
        public ComponentLookup<BulletLayerMask> layerMasks;

        [ReadOnly] 
        public ComponentLookup<BulletDamageScale> damageScales;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly] 
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        [ReadOnly] 
        public ComponentTypeHandle<LookAtTarget> lookAtTargetType;

        [ReadOnly] 
        public ComponentTypeHandle<FollowTarget> followTargetType;

        [ReadOnly]
        public ComponentTypeHandle<BulletDefinitionData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<BulletLayerMask> layerMaskType;

        [ReadOnly] 
        public ComponentTypeHandle<BulletDamageScale> damageScaleType;

        [ReadOnly]
        public BufferTypeHandle<BulletPrefab> prefabType;

        [ReadOnly] 
        public BufferTypeHandle<BulletActiveIndex> activeIndexType;

        [ReadOnly]
        public BufferTypeHandle<BulletMessage> inputMessageType;

        public BufferTypeHandle<Message> outputMessageType;

        public BufferTypeHandle<BulletStatus> statusType;

        public BufferTypeHandle<BulletTargetStatus> targetStatusType;

        public ComponentTypeHandle<BulletVersion> versionType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            long hash = math.aslong(time);
            
            Collect collect;
            collect.time = time;
            collect.random = Random.CreateFromIndex((uint)((int)hash ^ (int)(hash >> 32) ^ unfilteredChunkIndex));
            collect.cameraRotation = cameraRotation;
            collect.collisionWorld = collisionWorld;
            collect.parents = parents;
            collect.localTransforms = localTransforms;
            collect.prefabLoadResults = prefabLoadResults;
            collect.physicsColliders = physicsColliders;
            collect.characterBodyMap = characterBodies;
            collect.characterControls = characterControls;
            collect.followTargetMap = followTargets;
            collect.animationCurveDeltas = animationCurveDeltas;
            collect.layerMaskMap = layerMasks;
            collect.damageScaleMap = damageScales;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.lookAtTargets = chunk.GetNativeArray(ref lookAtTargetType);
            collect.followTargets = chunk.GetNativeArray(ref followTargetType);
            collect.characterBodies = chunk.GetNativeArray(ref characterBodyType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.layerMasks = chunk.GetNativeArray(ref layerMaskType);
            collect.damageScales = chunk.GetNativeArray(ref damageScaleType);
            collect.prefabs = chunk.GetBufferAccessor(ref prefabType);
            collect.activeIndices = chunk.GetBufferAccessor(ref activeIndexType);
            collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            collect.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            collect.states = chunk.GetBufferAccessor(ref statusType);
            collect.targetStates = chunk.GetBufferAccessor(ref targetStatusType);
            collect.versions = chunk.GetNativeArray(ref versionType);
            collect.entityManager = entityManager;

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
    
    private ComponentLookup<PrefabLoadResult> __prefabLoadResults;

    private ComponentLookup<PhysicsCollider> __physicsColliders;
    
    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private ComponentLookup<ThirdPersonCharacterControl> __characterControls;

    private ComponentLookup<FollowTarget> __followTargets;

    private ComponentLookup<AnimationCurveDelta> __animationCurveDeltas;

    private ComponentLookup<BulletLayerMask> __layerMasks;

    private ComponentLookup<BulletDamageScale> __damageScales;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<KinematicCharacterBody> __characterBodyType;

    private ComponentTypeHandle<LookAtTarget> __lookAtType;

    private ComponentTypeHandle<FollowTarget> __followTargetType;

    private ComponentTypeHandle<BulletDefinitionData> __instanceType;

    private ComponentTypeHandle<BulletLayerMask> __layerMaskType;

    private ComponentTypeHandle<BulletDamageScale> __damageScaleType;

    private BufferTypeHandle<BulletPrefab> __prefabType;

    private BufferTypeHandle<BulletActiveIndex> __activeIndexType;

    private BufferTypeHandle<BulletStatus> __statusType;

    private BufferTypeHandle<BulletTargetStatus> __targetStatusType;

    private BufferTypeHandle<BulletMessage> __inputMessageType;

    private BufferTypeHandle<Message> __outputMessageType;

    private ComponentTypeHandle<BulletVersion> __versionType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __parents = state.GetComponentLookup<Parent>(true);
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __prefabLoadResults = state.GetComponentLookup<PrefabLoadResult>(true);
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>(true);
        __characterControls = state.GetComponentLookup<ThirdPersonCharacterControl>(true);
        __followTargets = state.GetComponentLookup<FollowTarget>(true);
        __animationCurveDeltas = state.GetComponentLookup<AnimationCurveDelta>(true);
        __layerMasks = state.GetComponentLookup<BulletLayerMask>(true);
        __damageScales = state.GetComponentLookup<BulletDamageScale>(true);
        __entityType = state.GetEntityTypeHandle();
        __lookAtType = state.GetComponentTypeHandle<LookAtTarget>(true);
        __followTargetType = state.GetComponentTypeHandle<FollowTarget>(true);
        __characterBodyType = state.GetComponentTypeHandle<KinematicCharacterBody>(true);
        __instanceType = state.GetComponentTypeHandle<BulletDefinitionData>(true);
        __layerMaskType = state.GetComponentTypeHandle<BulletLayerMask>(true);
        __damageScaleType = state.GetComponentTypeHandle<BulletDamageScale>(true);
        __prefabType = state.GetBufferTypeHandle<BulletPrefab>(true);
        __activeIndexType = state.GetBufferTypeHandle<BulletActiveIndex>(true);
        __statusType = state.GetBufferTypeHandle<BulletStatus>();
        __targetStatusType = state.GetBufferTypeHandle<BulletTargetStatus>();
        __inputMessageType = state.GetBufferTypeHandle<BulletMessage>(true);
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __versionType = state.GetComponentTypeHandle<BulletVersion>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LocalToWorld, BulletDefinitionData, BulletActiveIndex>()
                .WithAllRW<BulletStatus, BulletTargetStatus>()
                .WithAllRW<BulletVersion>()
                .Build(ref state);

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
        __prefabLoadResults.Update(ref state);
        __physicsColliders.Update(ref state);
        __characterBodies.Update(ref state);
        __characterControls.Update(ref state);
        __followTargets.Update(ref state);
        __animationCurveDeltas.Update(ref state);
        __layerMasks.Update(ref state);
        __damageScales.Update(ref state);
        __entityType.Update(ref state);
        __characterBodyType.Update(ref state);
        __lookAtType.Update(ref state);
        __followTargetType.Update(ref state);
        __instanceType.Update(ref state);
        __layerMaskType.Update(ref state);
        __damageScaleType.Update(ref state);
        __prefabType.Update(ref state);
        __activeIndexType.Update(ref state);
        __statusType.Update(ref state);
        __targetStatusType.Update(ref state);
        __inputMessageType.Update(ref state);
        __outputMessageType.Update(ref state);
        __versionType.Update(ref state);

        CollectEx collect;
        collect.time = SystemAPI.Time.ElapsedTime;
        collect.cameraRotation = SystemAPI.GetSingleton<MainCameraTransform>().rotation;
        collect.collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        collect.parents = __parents;
        collect.localTransforms = __localTransforms;
        collect.prefabLoadResults = __prefabLoadResults;
        collect.physicsColliders = __physicsColliders;
        collect.characterBodies = __characterBodies;
        collect.characterControls = __characterControls;
        collect.followTargets = __followTargets;
        collect.animationCurveDeltas = __animationCurveDeltas;
        collect.layerMasks = __layerMasks;
        collect.damageScales = __damageScales;
        collect.entityType = __entityType;
        collect.characterBodyType = __characterBodyType;
        collect.lookAtTargetType = __lookAtType;
        collect.followTargetType = __followTargetType;
        collect.instanceType = __instanceType;
        collect.layerMaskType = __layerMaskType;
        collect.damageScaleType = __damageScaleType;
        collect.prefabType = __prefabType;
        collect.activeIndexType = __activeIndexType;
        collect.statusType = __statusType;
        collect.targetStatusType = __targetStatusType;
        collect.inputMessageType = __inputMessageType;
        collect.outputMessageType = __outputMessageType;
        collect.versionType = __versionType;
        collect.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        state.Dependency = collect.ScheduleParallelByRef(__group, state.Dependency);
    }
}
