using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;


[BurstCompile, 
 UpdateInGroup(typeof(AfterPhysicsSystemGroup)), UpdateBefore(typeof(KinematicCharacterPhysicsUpdateGroup))]
public partial struct LookAtSystem : ISystem
{
    private struct Collector : ICollector<DistanceHit>
    {
        private int __dynamicBodiesCount;
        private LookAtLocation __location;
        private float __minDot;
        private float __minDistance;
        private float3 __position;
        private float3 __cameraDirection;
        private ComponentLookup<KinematicCharacterBody> __characterBodies;

        public bool EarlyOutOnFirstHit => false;

        public float MaxFraction
        {
            get;

            private set;
        }

        public int NumHits
        {
            get;

            private set;
        }

        public DistanceHit closestHit
        {
            get;

            private set;
        }

        public Collector(
            int dynamicBodiesCount, 
            LookAtLocation location, 
            float minDot, 
            float minDistance, 
            float maxDistance, 
            in float3 position, 
            in float3 cameraDirection, 
            in ComponentLookup<KinematicCharacterBody> characterBodies)
        {
            __dynamicBodiesCount = dynamicBodiesCount;
            __location = location;
            __minDot = minDot;
            __minDistance = minDistance;
            MaxFraction = maxDistance;
            NumHits = 0;

            __position = position;
            __cameraDirection = cameraDirection;

            __characterBodies = characterBodies;

            closestHit = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            float distance = hit.Distance;
            if (distance < __minDistance)
                return false;

            var location = __location;
            if ((location & LookAtLocation.Camera) == LookAtLocation.Camera)
            {
                float dot = math.dot(hit.Position - __position, __cameraDirection);
                if(dot < __minDot * math.max(distance, 0.0f))
                    return false;

                distance = dot;

                location &= ~LookAtLocation.Camera;
            }

            if (hit.RigidBodyIndex >= __dynamicBodiesCount || 
                __characterBodies.TryGetComponent(hit.Entity, out var characterBody) && 
                __characterBodies.IsComponentEnabled(hit.Entity) && 
                characterBody.IsGrounded)
            {
                if (location != 0 && (location & LookAtLocation.Ground) != LookAtLocation.Ground)
                    return false;
            }
            else
            {
                if (location != 0 && (location & LookAtLocation.Air) != LookAtLocation.Air)
                    return false;
            }
            
            MaxFraction = distance;
            NumHits = 1;

            closestHit = hit;

            return true;
        }
    }
    private struct Apply
    {
        public double time;
        
        public float3 cameraDirection;
        
        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly] 
        public BufferAccessor<ThirdPersonCharacterStandTime> characterStandTimes;

        [ReadOnly] 
        public NativeArray<Entity> entityArray;
        
        [ReadOnly]
        public NativeArray<FollowTargetParent> followTargetParents;

        [ReadOnly]
        public NativeArray<LookAtAndFollow> lookAtAndFollows;

        [ReadOnly]
        public NativeArray<LookAt> instances;

        public NativeArray<LookAtTarget> targets;

        public NativeArray<ThirdPersonCharacterLookAt> characterLookAts;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<FollowTarget> followTargets;

        public void Execute(int index)
        {
            if (ThirdPersonCharacterStandTime.IsStand(time, characterStandTimes[index]))
                return;
            
            Entity entity = entityArray[index];
            var instance = instances[index];
            var localTransform = localTransforms[entity];

            float3 position = localTransform.Position;
            float4x4 parentToWorld;
            if (parents.TryGetComponent(entity, out var parent) &&
                TryGetLocalToWorld(parent.Value, parents, localTransforms, out var matrix))
            {
                parentToWorld = matrix;

                position = math.transform(matrix, position);
            }
            else
                parentToWorld = float4x4.identity;

            CollisionFilter filter;
            filter.GroupIndex = 0;
            filter.BelongsTo = ~0u;
            filter.CollidesWith = (uint)instance.layerMask;
            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.MaxDistance = instance.maxDistance;
            pointDistanceInput.Position = position;
            pointDistanceInput.Filter = filter;

            float minDistance = instance.minDistance, maxDistance = instance.maxDistance;
            if (index < lookAtAndFollows.Length)
            {
                var lookAtAndFollow = lookAtAndFollows[index];
                minDistance = math.min(minDistance, lookAtAndFollow.minDistance);
                maxDistance = math.max(maxDistance, lookAtAndFollow.maxDistance);
            }

            DistanceHit closestHit = default;
            Collector collector;
            if (index < targets.Length)
            {
                var target = targets[index];
                int rigidBodyIndex = target.entity == Entity.Null ? -1 : collisionWorld.GetRigidBodyIndex(target.entity);
                if (rigidBodyIndex != -1)
                {
                    collector = new Collector(
                        collisionWorld.NumDynamicBodies, 
                        instance.location, 
                        instance.minDot, 
                        minDistance, 
                        maxDistance, 
                        position, 
                        cameraDirection, 
                        characterBodies);
                    if (collisionWorld.Bodies[rigidBodyIndex].CalculateDistance(pointDistanceInput, ref collector))
                        closestHit = collector.closestHit;
                }
            }

            if (closestHit.Entity == Entity.Null && 
                index < lookAtAndFollows.Length && 
                index < followTargetParents.Length)
            {
                FollowTarget followTarget;
                Entity followTargetParent = followTargetParents[index].entity;
                while (!followTargets.TryGetComponent(followTargetParent, out followTarget))
                {
                    if (!parents.TryGetComponent(followTargetParent, out parent))
                        break;

                    followTargetParent = parent.Value;
                }
                
                int rigidBodyIndex = followTarget.entity == Entity.Null ? -1 : collisionWorld.GetRigidBodyIndex(followTarget.entity);
                if (rigidBodyIndex != -1)
                {
                    collector = new Collector(
                        collisionWorld.NumDynamicBodies, 
                        instance.location, 
                        instance.minDot, 
                        instance.minDistance, 
                        instance.maxDistance,  
                        position, 
                        cameraDirection, 
                        characterBodies);
                    if (collisionWorld.Bodies[rigidBodyIndex].CalculateDistance(pointDistanceInput, ref collector))
                        closestHit = collector.closestHit;
                }
            }

            if (closestHit.Entity == Entity.Null)
            {
                collector = new Collector(
                    collisionWorld.NumDynamicBodies, 
                    instance.location, 
                    instance.minDot, 
                    instance.minDistance, 
                    instance.maxDistance,  
                    position, 
                    cameraDirection, 
                    characterBodies);
                if (collisionWorld.CalculateDistance(pointDistanceInput, ref collector))
                    closestHit = collector.closestHit;
            }

            if (closestHit.Entity == Entity.Null)
            {
                if (index < targets.Length)
                {
                    LookAtTarget target;
                    target.time = time;
                    target.origin = quaternion.identity;
                    target.entity = Entity.Null;
                    targets[index] = target;
                }

                if (index < lookAtAndFollows.Length && followTargets.HasComponent(entity))
                {
                    followTargets[entity] = default;
                    followTargets.SetComponentEnabled(entity, false);
                }
                else if (index < characterLookAts.Length)
                {
                    ThirdPersonCharacterLookAt result;
                    result.direction = default;
                    characterLookAts[index] = result;
                }
            }
            else
                __Apply(
                    index, 
                    instance.speed, 
                    position, 
                    parentToWorld, 
                    entity, 
                    closestHit, 
                    ref localTransform);
        }

        private void __Apply(
            int index, 
            float speed, 
            in float3 position, 
            in float4x4 parentToWorld, 
            in Entity entity, 
            in DistanceHit closestHit, 
            ref LocalTransform localTransform)
        {
            float interpolation = 1.0f;
            quaternion origin = quaternion.identity;
            Entity targetEntity = closestHit.Entity;
            if (index < targets.Length)
            {
                var target = targets[index];
                if (target.entity != targetEntity)
                {
                    target.time = time;
                    target.origin = math.mul(math.quaternion(parentToWorld), localTransforms[entity].Rotation);
                    target.entity = targetEntity;
                    targets[index] = target;
                }

                origin = target.origin;
                if(speed > math.FLT_MIN_NORMAL)
                    interpolation = math.saturate((float)(time - target.time) * speed);
            }
            
            if (index < lookAtAndFollows.Length && followTargets.HasComponent(entity))
            {
                FollowTarget followTarget;
                //followTarget.flag = 0;
                followTarget.space = FollowTargetSpace.World;
                followTarget.entity = targetEntity;
                int rigidBodyIndex = closestHit.RigidBodyIndex;
                if (rigidBodyIndex == -1)
                    rigidBodyIndex = collisionWorld.GetRigidBodyIndex(targetEntity);
                
                var collider = rigidBodyIndex == -1 ? default : collisionWorld.Bodies[rigidBodyIndex].Collider;
                followTarget.offset = collider.IsCreated ? collider.Value.MassProperties.MassDistribution.Transform.pos : float3.zero;

                followTargets[entity] = followTarget;

                float distance = closestHit.Distance;
                var lookAtAndFollow = lookAtAndFollows[index];
                followTargets.SetComponentEnabled(entity, 
                    lookAtAndFollow.minDistance > distance || 
                    lookAtAndFollow.maxDistance < distance || 
                    index >= followTargetParents.Length);

                return;
            }

            quaternion rotation = MathUtilities.CreateRotationWithUpPriority(
                characterBodies.TryGetComponent(entity, out var characterBody) ? characterBody.GroundingUp : math.up(), 
                math.normalizesafe(closestHit.Position - position));

            rotation = math.slerp(origin, rotation, interpolation);

            if (index < characterLookAts.Length)
            {
                ThirdPersonCharacterLookAt result;
                result.direction = rotation;
                characterLookAts[index] = result;
            }
            else
            {
                localTransform.Rotation = math.mul(math.inverse(math.quaternion(parentToWorld)), rotation);
                localTransforms[entity] = localTransform;
            }
        }

        public static bool TryGetLocalToWorld(
            in Entity entity, 
            in ComponentLookup<Parent> parents, 
            in ComponentLookup<LocalTransform> localTransforms, 
            out float4x4 matrix)
        {
            if (!localTransforms.TryGetComponent(entity, out var localTransform))
            {
                matrix = float4x4.identity;

                return false;
            }

            matrix = localTransform.ToMatrix();
            if (parents.TryGetComponent(entity, out var parent) && 
                TryGetLocalToWorld(
                    parent.Value, 
                    parents, 
                    localTransforms, 
                    out var parentMatrix))
                matrix = math.mul(parentMatrix, matrix);

            return true;
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public double time;
        public float3 cameraDirection;

        [ReadOnly]
        public CollisionWorld collisionWorld;
        
        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public BufferTypeHandle<ThirdPersonCharacterStandTime> characterStandTimeType;

        [ReadOnly]
        public ComponentTypeHandle<FollowTargetParent> followTargetParentType;

        [ReadOnly]
        public ComponentTypeHandle<LookAtAndFollow> lookAtAndFollowType;

        [ReadOnly]
        public ComponentTypeHandle<LookAt> instanceType;

        public ComponentTypeHandle<LookAtTarget> targetType;

        public ComponentTypeHandle<ThirdPersonCharacterLookAt> characterLookAtType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<FollowTarget> followTargets;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.time = time;
            apply.cameraDirection = cameraDirection;
            apply.collisionWorld = collisionWorld;
            apply.parents = parents;
            apply.characterBodies = characterBodies;
            apply.characterStandTimes = chunk.GetBufferAccessor(ref characterStandTimeType);
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.followTargetParents = chunk.GetNativeArray(ref followTargetParentType);
            apply.instances = chunk.GetNativeArray(ref instanceType);
            apply.targets = chunk.GetNativeArray(ref targetType);
            apply.lookAtAndFollows = chunk.GetNativeArray(ref lookAtAndFollowType);
            apply.characterLookAts = chunk.GetNativeArray(ref characterLookAtType);
            apply.localTransforms = localTransforms;
            apply.followTargets = followTargets;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                apply.Execute(i);
        }
    }
    
    private EntityTypeHandle __entityType;

    private ComponentLookup<LocalTransform> __localTransforms;

    private ComponentLookup<FollowTarget> __followTargets;

    private ComponentLookup<Parent> __parents;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private BufferTypeHandle<ThirdPersonCharacterStandTime> __characterStandTimeType;

    private ComponentTypeHandle<FollowTargetParent> __followTargetParentType;

    private ComponentTypeHandle<LookAtAndFollow> __lookAtAndFollowType;

    private ComponentTypeHandle<LookAt> __instanceType;

    private ComponentTypeHandle<LookAtTarget> __targetType;

    private ComponentTypeHandle<ThirdPersonCharacterLookAt> __characterLookAtType;

    private EntityQuery __group;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __localTransforms = state.GetComponentLookup<LocalTransform>();
        __followTargets = state.GetComponentLookup<FollowTarget>();
        __parents = state.GetComponentLookup<Parent>(true);
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>(true);
        __characterStandTimeType = state.GetBufferTypeHandle<ThirdPersonCharacterStandTime>(true);
        __followTargetParentType = state.GetComponentTypeHandle<FollowTargetParent>(true);
        __lookAtAndFollowType = state.GetComponentTypeHandle<LookAtAndFollow>(true);
        __instanceType = state.GetComponentTypeHandle<LookAt>(true);
        __targetType = state.GetComponentTypeHandle<LookAtTarget>();
        __characterLookAtType = state.GetComponentTypeHandle<ThirdPersonCharacterLookAt>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LookAt>()
                .WithAllRW<LookAtTarget, LocalTransform>()
                .Build(ref state);
        
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MainCameraTransform>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __entityType.Update(ref state);
        __parents.Update(ref state);
        __localTransforms.Update(ref state);
        __followTargets.Update(ref state);
        __characterBodies.Update(ref state);
        __characterStandTimeType.Update(ref state);
        __followTargetParentType.Update(ref state);
        __lookAtAndFollowType.Update(ref state);
        __instanceType.Update(ref state);
        __targetType.Update(ref state);
        __characterLookAtType.Update(ref state);
        
        ApplyEx apply;
        apply.time = SystemAPI.Time.ElapsedTime;
        apply.cameraDirection = math.forward(SystemAPI.GetSingleton<MainCameraTransform>().rotation);
        apply.collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        apply.entityType = __entityType;
        apply.parents = __parents;
        apply.characterBodies = __characterBodies;
        apply.characterStandTimeType = __characterStandTimeType;
        apply.followTargetParentType = __followTargetParentType;
        apply.lookAtAndFollowType = __lookAtAndFollowType;
        apply.instanceType = __instanceType;
        apply.targetType = __targetType;
        apply.characterLookAtType = __characterLookAtType;
        apply.localTransforms = __localTransforms;
        apply.followTargets = __followTargets;

        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
    }
}
