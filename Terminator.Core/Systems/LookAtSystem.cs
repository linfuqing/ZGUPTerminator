using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Physics.GraphicsIntegration;
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
        public NativeArray<Entity> entityArray;
        
        [ReadOnly]
        public NativeArray<FollowTargetParent> followTargetParents;

        [ReadOnly]
        public NativeArray<LookAtAndFollow> lookAtAndFollows;

        [ReadOnly]
        public NativeArray<LookAt> instances;

        public NativeArray<LookAtOrigin> origins;
        
        public NativeArray<LookAtTarget> targets;

        public NativeArray<ThirdPersonCharacterLookAt> characterLookAts;

        public BufferAccessor<ThirdPersonCharacterStandTime> characterStandTimes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<FollowTarget> followTargets;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var instance = instances[index];
            var localTransform = localTransforms[entity];

            if (index < origins.Length)
            {
                LookAtOrigin origin;
                origin.transform = math.RigidTransform(localTransform.Rotation, localTransform.Scale);
                origins[index] = origin;
            }

            var transform = math.RigidTransform(localTransform.Rotation, localTransform.Position);
            float4x4 parentToWorld;
            if (parents.TryGetComponent(entity, out var parent) &&
                TryGetLocalToWorld(parent.Value, parents, localTransforms, out var matrix))
            {
                parentToWorld = matrix;

                transform = math.mul(math.RigidTransform(matrix), transform);
            }
            else
                parentToWorld = float4x4.identity;

            if (index < characterStandTimes.Length &&
                ThirdPersonCharacterStandTime.IsStand(time, characterStandTimes[index]))
            {
                if (index < targets.Length)
                {
                    var target = targets[index];
                    target.time = time;
                    target.origin = transform.rot;
                    targets[index] = target;
                }
                
                return;
            }

            CollisionFilter filter;
            filter.GroupIndex = 0;
            filter.BelongsTo = ~0u;
            filter.CollidesWith = (uint)instance.layerMask;
            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.MaxDistance = instance.maxDistance;
            pointDistanceInput.Position = transform.pos;
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
                        transform.pos, 
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
                        transform.pos, 
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
                    transform.pos, 
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
                    target.origin = transform.rot;
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
                    parentToWorld, 
                    transform, 
                    entity, 
                    closestHit, 
                    ref localTransform);
        }

        private void __Apply(
            int index, 
            float speed, 
            in float4x4 parentToWorld, 
            in RigidTransform transform, 
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
                    target.origin = transform.rot;
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
                math.normalizesafe(closestHit.Position - transform.pos));

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
        public ComponentTypeHandle<FollowTargetParent> followTargetParentType;

        [ReadOnly]
        public ComponentTypeHandle<LookAtAndFollow> lookAtAndFollowType;

        [ReadOnly]
        public ComponentTypeHandle<LookAt> instanceType;

        public ComponentTypeHandle<LookAtOrigin> originType;

        public ComponentTypeHandle<LookAtTarget> targetType;

        public ComponentTypeHandle<ThirdPersonCharacterLookAt> characterLookAtType;

        public BufferTypeHandle<ThirdPersonCharacterStandTime> characterStandTimeType;

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
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.followTargetParents = chunk.GetNativeArray(ref followTargetParentType);
            apply.instances = chunk.GetNativeArray(ref instanceType);
            apply.origins = chunk.GetNativeArray(ref originType);
            apply.targets = chunk.GetNativeArray(ref targetType);
            apply.lookAtAndFollows = chunk.GetNativeArray(ref lookAtAndFollowType);
            apply.characterLookAts = chunk.GetNativeArray(ref characterLookAtType);
            apply.characterStandTimes = chunk.GetBufferAccessor(ref characterStandTimeType);
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

    private ComponentTypeHandle<FollowTargetParent> __followTargetParentType;

    private ComponentTypeHandle<LookAtAndFollow> __lookAtAndFollowType;

    private ComponentTypeHandle<LookAt> __instanceType;

    private ComponentTypeHandle<LookAtOrigin> __originType;

    private ComponentTypeHandle<LookAtTarget> __targetType;

    private ComponentTypeHandle<ThirdPersonCharacterLookAt> __characterLookAtType;

    private BufferTypeHandle<ThirdPersonCharacterStandTime> __characterStandTimeType;

    private EntityQuery __group;
    
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
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __localTransforms = state.GetComponentLookup<LocalTransform>();
        __followTargets = state.GetComponentLookup<FollowTarget>();
        __parents = state.GetComponentLookup<Parent>(true);
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>(true);
        __followTargetParentType = state.GetComponentTypeHandle<FollowTargetParent>(true);
        __lookAtAndFollowType = state.GetComponentTypeHandle<LookAtAndFollow>(true);
        __instanceType = state.GetComponentTypeHandle<LookAt>(true);
        __originType = state.GetComponentTypeHandle<LookAtOrigin>();
        __targetType = state.GetComponentTypeHandle<LookAtTarget>();
        __characterLookAtType = state.GetComponentTypeHandle<ThirdPersonCharacterLookAt>();
        __characterStandTimeType = state.GetBufferTypeHandle<ThirdPersonCharacterStandTime>();
        
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
        __followTargetParentType.Update(ref state);
        __lookAtAndFollowType.Update(ref state);
        __instanceType.Update(ref state);
        __originType.Update(ref state);
        __targetType.Update(ref state);
        __characterLookAtType.Update(ref state);
        __characterStandTimeType.Update(ref state);
        
        ApplyEx apply;
        apply.time = SystemAPI.Time.ElapsedTime;
        apply.cameraDirection = math.forward(SystemAPI.GetSingleton<MainCameraTransform>().rotation);
        apply.collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        apply.entityType = __entityType;
        apply.parents = __parents;
        apply.characterBodies = __characterBodies;
        apply.followTargetParentType = __followTargetParentType;
        apply.lookAtAndFollowType = __lookAtAndFollowType;
        apply.instanceType = __instanceType;
        apply.originType = __originType;
        apply.targetType = __targetType;
        apply.characterLookAtType = __characterLookAtType;
        apply.characterStandTimeType = __characterStandTimeType;
        apply.localTransforms = __localTransforms;
        apply.followTargets = __followTargets;

        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(TransformSystemGroup)), UpdateBefore(typeof(LocalToWorldSystem))]
public partial struct LookAtTransformSystem : ISystem
{
    private struct Transform
    {
        public float normalizedTimeAhead;
        
        [ReadOnly]
        public ComponentLookup<Parent> parentMap;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransformMap;
        
        [ReadOnly]
        public NativeArray<LocalTransform> localTransforms;
        
        [ReadOnly]
        public NativeArray<LookAtOrigin> origins;

        [ReadOnly]
        public NativeArray<Parent> parents;

        public NativeArray<LocalToWorld> localToWorlds;

        public void Execute(int index)
        {
            var localTransform = localTransforms[index];

            var transform = GraphicalSmoothingUtility.Interpolate(
                origins[index].transform, math.RigidTransform(localTransform.Rotation, localTransform.Position), normalizedTimeAhead);

            var matrix = float4x4.TRS(transform.pos, transform.rot, localTransform.Scale);
            float4x4 localToParent;
            if (index < parents.Length && 
                LookAtSystem.TryGetLocalToWorld(parents[index].Value, parentMap, localTransformMap, out localToParent))
                matrix = math.mul(localToParent, matrix);

            LocalToWorld localToWorld;
            localToWorld.Value = matrix;
            localToWorlds[index] = localToWorld;
        }
    }
    
    [BurstCompile]
    private struct TransformEx : IJobChunk
    {
        public float normalizedTimeAhead;
        
        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;
        
        [ReadOnly]
        public ComponentTypeHandle<LocalTransform> localTransformType;
        
        [ReadOnly]
        public ComponentTypeHandle<LookAtOrigin> originType;

        [ReadOnly]
        public ComponentTypeHandle<Parent> parentType;

        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Transform transform;
            transform.normalizedTimeAhead = normalizedTimeAhead;
            transform.parentMap = parents;
            transform.localTransformMap = localTransforms;
            transform.localTransforms = chunk.GetNativeArray(ref localTransformType);
            transform.origins = chunk.GetNativeArray(ref originType);
            transform.parents = chunk.GetNativeArray(ref parentType);
            transform.localToWorlds = chunk.GetNativeArray(ref localToWorldType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
                transform.Execute(i);
        }
    }
    
    private ComponentLookup<Parent> __parents;

    private ComponentLookup<LocalTransform> __localTransforms;
        
    private ComponentTypeHandle<LocalTransform> __localTransformType;
        
    private ComponentTypeHandle<LookAtOrigin> __originType;

    private ComponentTypeHandle<Parent> __parentType;

    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __parents = state.GetComponentLookup<Parent>(true);
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>(true);
        __originType = state.GetComponentTypeHandle<LookAtOrigin>(true);
        __parentType = state.GetComponentTypeHandle<Parent>(true);
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>();
        
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LookAtTarget, LookAtOrigin, LocalTransform>()
                .WithNone<ThirdPersonCharacterLookAt, PhysicsGraphicalSmoothing>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup)
                .Build(ref state);
        
        state.RequireForUpdate<FixedFrame>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var fixedFrame = SystemAPI.GetSingleton<FixedFrame>();
        var timeAhead = (float)(SystemAPI.Time.ElapsedTime - fixedFrame.elapsedTime);
        if (timeAhead < 0.0f || fixedFrame.deltaTime < math.FLT_MIN_NORMAL)
            return;
        
        __parents.Update(ref state);
        __localTransforms.Update(ref state);
        __localTransformType.Update(ref state);
        __originType.Update(ref state);
        __parentType.Update(ref state);
        __localToWorldType.Update(ref state);

        TransformEx transform;
        transform.normalizedTimeAhead = math.saturate(timeAhead / fixedFrame.deltaTime);
        transform.parents = __parents;
        transform.localTransforms = __localTransforms;
        transform.localTransformType = __localTransformType;
        transform.originType = __originType;
        transform.parentType = __parentType;
        transform.localToWorldType = __localToWorldType;

        state.Dependency = transform.ScheduleParallelByRef(__group, state.Dependency);
    }
}
