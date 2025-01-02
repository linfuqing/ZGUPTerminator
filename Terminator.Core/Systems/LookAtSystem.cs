using System.Collections;
using System.Collections.Generic;
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
        private float __minDistance;
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
            float minDistance, 
            float maxDistance, 
            in ComponentLookup<KinematicCharacterBody> characterBodies)
        {
            __dynamicBodiesCount = dynamicBodiesCount;
            __location = location;
            __minDistance = minDistance;
            MaxFraction = maxDistance;
            NumHits = 0;

            __characterBodies = characterBodies;

            closestHit = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            float distance = hit.Distance;
            if (distance < __minDistance)
                return false;

            if (hit.RigidBodyIndex >= __dynamicBodiesCount || 
                __characterBodies.TryGetComponent(hit.Entity, out var characterBody) && 
                __characterBodies.IsComponentEnabled(hit.Entity) && 
                characterBody.IsGrounded)
            {
                if (__location != 0 && (__location & LookAtLocation.Ground) != LookAtLocation.Ground)
                    return false;
            }
            else
            {
                if (__location != 0 && (__location & LookAtLocation.Air) != LookAtLocation.Air)
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
        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

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

        public NativeArray<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<FollowTarget> followTargets;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var instance = instances[index];
            var localTransform = localTransforms[index];

            CollisionFilter filter;
            filter.GroupIndex = 0;
            filter.BelongsTo = ~0u;
            filter.CollidesWith = (uint)instance.layerMask;
            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.MaxDistance = instance.maxDistance;
            pointDistanceInput.Position = localTransform.Position;
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
                    collector = new Collector(collisionWorld.NumDynamicBodies, instance.location, minDistance, maxDistance, characterBodies);
                    if (collisionWorld.Bodies[rigidBodyIndex].CalculateDistance(pointDistanceInput, ref collector))
                        closestHit = collector.closestHit;
                }
            }

            if (closestHit.Entity == Entity.Null && 
                index < lookAtAndFollows.Length && 
                index < followTargetParents.Length)
            {
                Parent parent;
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
                        instance.minDistance, 
                        instance.maxDistance, 
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
                    instance.minDistance, 
                    instance.maxDistance, 
                    characterBodies);
                if (collisionWorld.CalculateDistance(pointDistanceInput, ref collector))
                    closestHit = collector.closestHit;
            }

            if (closestHit.Entity == Entity.Null)
            {
                if (index < targets.Length)
                {
                    LookAtTarget target;
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
                __Apply(index, entity, closestHit, ref localTransform);
        }

        private void __Apply(
            int index, 
            in Entity entity, 
            in DistanceHit closestHit, 
            ref LocalTransform localTransform)
        {
            Entity targetEntity = closestHit.Entity;
            if (index < targets.Length)
            {
                LookAtTarget target;
                target.entity = targetEntity;
                targets[index] = target;
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
                math.normalizesafe(closestHit.Position - localTransform.Position));

            if (index < characterLookAts.Length)
            {
                ThirdPersonCharacterLookAt result;
                result.direction = rotation;
                characterLookAts[index] = result;
            }
            else
            {
                localTransform.Rotation = rotation;
                localTransforms[index] = localTransform;
            }
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        [ReadOnly]
        public CollisionWorld collisionWorld;
        
        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly]
        public ComponentTypeHandle<FollowTargetParent> followTargetParentType;

        [ReadOnly]
        public ComponentTypeHandle<LookAtAndFollow> lookAtAndFollowType;

        [ReadOnly]
        public ComponentTypeHandle<LookAt> instanceType;

        public ComponentTypeHandle<LookAtTarget> targetType;

        public ComponentTypeHandle<ThirdPersonCharacterLookAt> characterLookAtType;

        public ComponentTypeHandle<LocalTransform> localTransformType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<FollowTarget> followTargets;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.collisionWorld = collisionWorld;
            apply.characterBodies = characterBodies;
            apply.parents = parents;
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.followTargetParents = chunk.GetNativeArray(ref followTargetParentType);
            apply.instances = chunk.GetNativeArray(ref instanceType);
            apply.targets = chunk.GetNativeArray(ref targetType);
            apply.lookAtAndFollows = chunk.GetNativeArray(ref lookAtAndFollowType);
            apply.characterLookAts = chunk.GetNativeArray(ref characterLookAtType);
            apply.localTransforms = chunk.GetNativeArray(ref localTransformType);
            apply.followTargets = followTargets;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                apply.Execute(i);
        }
    }
    
    private EntityTypeHandle __entityType;

    private ComponentLookup<Parent> __parents;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private ComponentLookup<FollowTarget> __followTargets;

    private ComponentTypeHandle<FollowTargetParent> __followTargetParentType;

    private ComponentTypeHandle<LookAtAndFollow> __lookAtAndFollowType;

    private ComponentTypeHandle<LookAt> __instanceType;

    private ComponentTypeHandle<LookAtTarget> __targetType;

    private ComponentTypeHandle<ThirdPersonCharacterLookAt> __characterLookAtType;

    private ComponentTypeHandle<LocalTransform> __localTransformType;

    private EntityQuery __group;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __parents = state.GetComponentLookup<Parent>(true);
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>(true);
        __followTargets = state.GetComponentLookup<FollowTarget>();
        __followTargetParentType = state.GetComponentTypeHandle<FollowTargetParent>(true);
        __lookAtAndFollowType = state.GetComponentTypeHandle<LookAtAndFollow>(true);
        __instanceType = state.GetComponentTypeHandle<LookAt>(true);
        __targetType = state.GetComponentTypeHandle<LookAtTarget>();
        __characterLookAtType = state.GetComponentTypeHandle<ThirdPersonCharacterLookAt>();
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LookAt>()
                .WithAllRW<LookAtTarget, LocalTransform>()
                .Build(ref state);
        
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __entityType.Update(ref state);
        __parents.Update(ref state);
        __characterBodies.Update(ref state);
        __followTargets.Update(ref state);
        __followTargetParentType.Update(ref state);
        __lookAtAndFollowType.Update(ref state);
        __instanceType.Update(ref state);
        __targetType.Update(ref state);
        __characterLookAtType.Update(ref state);
        __localTransformType.Update(ref state);
        
        ApplyEx apply;
        apply.collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        apply.entityType = __entityType;
        apply.parents = __parents;
        apply.characterBodies = __characterBodies;
        apply.followTargets = __followTargets;
        apply.followTargetParentType = __followTargetParentType;
        apply.lookAtAndFollowType = __lookAtAndFollowType;
        apply.instanceType = __instanceType;
        apply.targetType = __targetType;
        apply.characterLookAtType = __characterLookAtType;
        apply.localTransformType = __localTransformType;

        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
    }
}