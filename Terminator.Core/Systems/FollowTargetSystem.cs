using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.CharacterController;
using Unity.Jobs;
using Unity.Physics;
using ZG;

[BurstCompile, UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
public partial struct FollowTargetSystem : ISystem
{
    private struct ApplyTransforms
    {
        public float deltaTimeR;
        
        [ReadOnly] 
        public NativeArray<FollowTargetUp> ups;

        [ReadOnly] 
        public NativeArray<FollowTargetVelocity> velocities;

        //[ReadOnly]
        public NativeArray<LocalTransform> localTransforms;
        
        public NativeArray<PhysicsVelocity> physicsVelocities;

        public NativeArray<ThirdPersonCharacterLookAt> characterLookAts;

        public NativeArray<ThirdPersonCharacterControl> characterControls;

        public void Execute(int index)
        {
            var velocity = velocities[index];
            var localTransform = localTransforms[index];

            bool isRotation = math.lengthsq(velocity.lookAt) > math.FLT_MIN_NORMAL;
            if (isRotation)
            {
                if (index < characterLookAts.Length)
                {
                    ThirdPersonCharacterLookAt characterLookAt;
                    characterLookAt.direction = velocity.lookAt;
                    characterLookAts[index] = characterLookAt;
                }
            }

            //velocity.distance = math.mul(math.float3x3(math.mul(localTransform.ToMatrix(), math.inverse(localToWorlds[index].Value))), velocity.distance);
            if (index < characterControls.Length)
            {
                var characterControl = characterControls[index];

                characterControl.MoveVector = velocity.value * velocity.direction;

                characterControls[index] = characterControl;
            }
            else if(index < physicsVelocities.Length)
            {
                if (isRotation || velocity.value > math.FLT_MIN_NORMAL)
                {
                    localTransform.Rotation = isRotation
                        ? velocity.lookAt
                        : index < ups.Length
                            ? MathUtilities.CreateRotationWithUpPriority(ups[index].value, velocity.direction)
                            : ZG.Mathematics.Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), velocity.direction);
                    
                    localTransforms[index] = localTransform;
                }

                //quaternion rotation = ZG.Mathematics.Math.FromToRotation(localTransform.Forward(), math.normalizesafe(velocity.distance));

                PhysicsVelocity physicsVelocity;
                physicsVelocity.Angular = float3.zero;
                    //ZG.Mathematics.Math.ToAngular(rotation);
                physicsVelocity.Angular *= deltaTimeR;
                physicsVelocity.Linear = velocity.value * velocity.direction;
                physicsVelocities[index] = physicsVelocity;
            }
            else
            {
                if (isRotation)
                    localTransform.Rotation = velocity.lookAt;//math.mul(velocity.direction, localTransform.Rotation);
                
                localTransform.Position = velocity.target;
                localTransforms[index] = localTransform;
            }
        }
    }

    [BurstCompile]
    private struct ApplyTransformsEx : IJobChunk
    {
        public float deltaTimeR;

        [ReadOnly] 
        public ComponentTypeHandle<FollowTargetUp> upType;

        [ReadOnly]
        public ComponentTypeHandle<FollowTargetVelocity> velocityType;
        
        [ReadOnly] 
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;
        
        //[ReadOnly]
        public ComponentTypeHandle<LocalTransform> localTransformType;
        
        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;

        public ComponentTypeHandle<ThirdPersonCharacterLookAt> characterLookAtType;
        public ComponentTypeHandle<ThirdPersonCharacterControl> characterControlsType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            bool isCharacter = chunk.Has(ref characterBodyType);
            
            ApplyTransforms applyTransforms;
            applyTransforms.deltaTimeR = deltaTimeR;
            applyTransforms.ups = chunk.GetNativeArray(ref upType);
            applyTransforms.velocities = chunk.GetNativeArray(ref velocityType);
            applyTransforms.localTransforms = chunk.GetNativeArray(ref localTransformType);
            applyTransforms.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);
            applyTransforms.characterLookAts = chunk.GetNativeArray(ref characterLookAtType);
            applyTransforms.characterControls = chunk.GetNativeArray(ref characterControlsType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(isCharacter && !chunk.IsComponentEnabled(ref characterBodyType, i))
                    continue;
                
                applyTransforms.Execute(i);
            }
        }
    }

    private ComponentTypeHandle<FollowTargetUp> __upType;

    private ComponentTypeHandle<ThirdPersonCharacterLookAt> __characterLookAtType;

    private ComponentTypeHandle<ThirdPersonCharacterControl> __characterControlType;

    private EntityQuery __velocityGroup;

    private FollowTargetSharedData __sharedData;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __upType = state.GetComponentTypeHandle<FollowTargetUp>(true);
        __characterLookAtType = state.GetComponentTypeHandle<ThirdPersonCharacterLookAt>();
        __characterControlType = state.GetComponentTypeHandle<ThirdPersonCharacterControl>();

        __sharedData = new FollowTargetSharedData(false, ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __velocityGroup = builder
                .WithAll<FollowTargetVelocity>()
                .WithAnyRW<PhysicsVelocity, ThirdPersonCharacterControl>()
                .Build(ref state);
        
        state.RequireForUpdate<MainCameraTransform>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __sharedData.Update(ref state, 
            out _, 
            out var localTransformType, 
            out var physicsVelocityType, 
            out var characterBodyType,
            out _, 
            out var velocityType);

        var jobHandle = __sharedData.Update(true, state.Dependency, ref state, out float deltaTimeR);

        __upType.Update(ref state);
        __characterLookAtType.Update(ref state);
        __characterControlType.Update(ref state);
        
        ApplyTransformsEx applyTransforms;
        applyTransforms.deltaTimeR = deltaTimeR;
        applyTransforms.upType = __upType;
        applyTransforms.velocityType = velocityType;
        applyTransforms.characterBodyType = characterBodyType;
        applyTransforms.localTransformType = localTransformType;
        applyTransforms.physicsVelocityType = physicsVelocityType;
        applyTransforms.characterLookAtType = __characterLookAtType;
        applyTransforms.characterControlsType = __characterControlType;

        state.Dependency = applyTransforms.ScheduleParallelByRef(__velocityGroup, jobHandle);
    }
}

[BurstCompile, UpdateInGroup(typeof(TransformSystemGroup), OrderLast = true)]
public partial struct FollowTargetTransformSystem : ISystem
{
    private struct Wrapper : IReadOnlyListWrapper<FollowTargetDistance, DynamicBuffer<FollowTargetDistance>>
    {
        public int GetCount(DynamicBuffer<FollowTargetDistance> list) => list.Length;

        public FollowTargetDistance Get(DynamicBuffer<FollowTargetDistance> list, int index) => list[index];
    }

    private struct ApplyTransforms
    {
        public float deltaTime;
        
        [ReadOnly] 
        public NativeArray<FollowTargetVelocity> velocities;

        //[ReadOnly]
        public NativeArray<LocalTransform> localTransforms;
        
        public void Execute(int index)
        {
            var velocity = velocities[index];
            var localTransform = localTransforms[index];

            bool isRotation = math.lengthsq(velocity.lookAt) > math.FLT_MIN_NORMAL;

            if (isRotation)
                localTransform.Rotation = velocity.lookAt;//math.mul(velocity.direction, localTransform.Rotation);

            if (math.abs(velocity.value) > math.FLT_MIN_NORMAL)
                localTransform.Position += deltaTime * velocity.value * velocity.direction;
            else
                localTransform.Position = velocity.target;
            
            localTransforms[index] = localTransform;
        }
    }

    [BurstCompile]
    private struct ApplyTransformsEx : IJobChunk
    {
        public float deltaTime;
        
        [ReadOnly]
        public ComponentTypeHandle<FollowTargetVelocity> velocityType;
        
        //[ReadOnly]
        public ComponentTypeHandle<LocalTransform> localTransformType;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ApplyTransforms applyTransforms;
            applyTransforms.deltaTime = deltaTime;
            applyTransforms.velocities = chunk.GetNativeArray(ref velocityType);
            applyTransforms.localTransforms = chunk.GetNativeArray(ref localTransformType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                applyTransforms.Execute(i);
        }
    }

    private struct ComputeVelocities
    {
        public double time;
        
        public quaternion cameraRotation;

        [ReadOnly] 
        public ComponentLookup<LocalToWorld> localToWorlds;

        [ReadOnly] 
        public BufferAccessor<FollowTargetDistance> distances;

        [ReadOnly]
        public NativeArray<Parent> parents;

        [ReadOnly]
        public NativeArray<LocalTransform> localTransforms;

        [ReadOnly]
        public NativeArray<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public NativeArray<FollowTargetUp> ups;

        [ReadOnly] 
        public NativeArray<FollowTarget> instances;

        [ReadOnly] 
        public NativeArray<FollowTargetSpeed> speeds;

        public NativeArray<FollowTargetVelocity> velocities;

        public BufferAccessor<Message> messages;

        public void Execute(int index)
        {
            var velocity = velocities[index];
            var instance = instances[index];
            if (!localToWorlds.TryGetComponent(instance.entity, out var localToWorld))
            {
                if (velocity.time > math.FLT_MIN_NORMAL)
                {
                    velocity.target += velocity.value * (float)(time - velocity.time) * velocity.direction;
                    
                    velocity.time = time;
                    ++velocity.version;
                    velocities[index] = velocity;
                }
                
                return;
            }

            float4x4 parentLocalToWorld, targetLocalToWorld = localToWorld.Value;
            var transform = localTransforms[index];
            bool hasParent;
            if (index < parents.Length &&
                localToWorlds.TryGetComponent(parents[index].Value,  out localToWorld))
            {
                parentLocalToWorld = localToWorld.Value;
                transform = LocalTransform.FromMatrix(math.mul(parentLocalToWorld, transform.ToMatrix()));

                hasParent = true;
            }
            else
            {
                parentLocalToWorld = float4x4.identity;
                
                hasParent = false;
            }

            switch (instance.space)
            {
                case FollowTargetSpace.Camera:
                    velocity.lookAt = cameraRotation;
                    velocity.target = targetLocalToWorld.c3.xyz + math.mul(cameraRotation, instance.offset);
                    break;
                default:
                    velocity.lookAt = default;
                    velocity.target = math.transform(targetLocalToWorld, instance.offset);
                    break;
            }

            float3 distance = velocity.target - transform.Position;
            if (index < characterBodies.Length)
            {
                var characterBody = characterBodies[index];
                if (characterBody.ParentEntity == Entity.Null)
                    distance -= math.projectsafe(distance, characterBody.GroundingUp);
                else
                    distance = float3.zero;
            }
            else if (hasParent)
            {
                distance = math.mul(math.inverse(math.float3x3(parentLocalToWorld)), distance);

                velocity.target = transform.Position + distance;
            }
            else if (index < ups.Length)
            {
                var up = ups[index];
                if((up.control & FollowTargetControl.Pitch) == 0)
                    distance -= math.projectsafe(distance, up.value);
            }

            float lengthSQ = math.lengthsq(distance), speed = 0.0f;
            if (index < distances.Length)
            {
                var distances = this.distances[index];
                int numDistances = distances.Length;
                if (numDistances > 0)
                {
                    FollowTargetDistance temp;
                    temp.value = math.sqrt(lengthSQ);
                    temp.speed = 0.0f;
                    temp.messageName = default;
                    temp.messageValue = default;

                    int distanceIndex = distances.BinarySearch(
                        temp,
                        new NativeSortExtension.DefaultComparer<FollowTargetDistance>(),
                        new Wrapper());

                    distanceIndex = math.max(distanceIndex, 0);
                    temp = distances[distanceIndex];
                    if (distanceIndex != velocity.distanceIndex)
                    {
                        velocity.distanceIndex = distanceIndex;

                        if (!temp.messageName.IsEmpty && index < messages.Length)
                        {
                            var messages = this.messages[index];

                            Message message;
                            message.key = 0;
                            message.name = temp.messageName;
                            message.value = temp.messageValue;
                            messages.Add(message);
                        }
                    }

                    speed = temp.speed;
                }
            }

            speed *= speeds[index].scale;

            if (lengthSQ > math.FLT_MIN_NORMAL)
            {
                velocity.value = speed;
                velocity.direction = distance * math.rsqrt(lengthSQ);
            }
            else
            {
                velocity.value = 0.0f;
                velocity.direction = float3.zero;
            }

            //velocity.target = target;
            //velocity.lookAt = default;

            velocity.time = time;
            ++velocity.version;
            velocities[index] = velocity;
        }
    }

    [BurstCompile]
    private struct ComputeVelocitiesEx : IJobChunk
    {
        public double time;

        public quaternion cameraRotation;

        [ReadOnly] 
        public BufferTypeHandle<FollowTargetDistance> distanceType;

        [ReadOnly] 
        public ComponentLookup<LocalToWorld> localToWorlds;

        [ReadOnly]
        public ComponentTypeHandle<Parent> parentType;

        [ReadOnly]
        public ComponentTypeHandle<LocalTransform> localTransformType;

        [ReadOnly] 
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        [ReadOnly] 
        public ComponentTypeHandle<FollowTarget> instanceType;

        [ReadOnly] 
        public ComponentTypeHandle<FollowTargetUp> upType;

        [ReadOnly] 
        public ComponentTypeHandle<FollowTargetSpeed> speedType;

        public ComponentTypeHandle<FollowTargetVelocity> velocityType;

        public BufferTypeHandle<Message> messageType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ComputeVelocities computeVelocities;
            computeVelocities.time = time;
            computeVelocities.cameraRotation = cameraRotation;
            computeVelocities.localToWorlds = localToWorlds;
            computeVelocities.distances = chunk.GetBufferAccessor(ref distanceType);
            computeVelocities.parents = chunk.GetNativeArray(ref parentType);
            computeVelocities.localTransforms = chunk.GetNativeArray(ref localTransformType);
            computeVelocities.characterBodies = chunk.GetNativeArray(ref characterBodyType);
            computeVelocities.instances = chunk.GetNativeArray(ref instanceType);
            computeVelocities.ups = chunk.GetNativeArray(ref upType);
            computeVelocities.speeds = chunk.GetNativeArray(ref speedType);
            computeVelocities.velocities = chunk.GetNativeArray(ref velocityType);
            computeVelocities.messages = chunk.GetBufferAccessor(ref messageType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                computeVelocities.Execute(i);
        }
    }

    private ComponentTypeHandle<Parent> __parentType;

    private ComponentTypeHandle<FollowTargetUp> __upType;

    private ComponentTypeHandle<FollowTargetSpeed> __speedType;

    private BufferTypeHandle<FollowTargetDistance> __distanceType;

    private BufferTypeHandle<Message> __messageType;

    private FollowTargetSharedData __sharedData;
    
    private EntityQuery __instanceGroup;

    private EntityQuery __velocityGroup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __parentType = state.GetComponentTypeHandle<Parent>(true);
        __upType = state.GetComponentTypeHandle<FollowTargetUp>(true);
        __speedType = state.GetComponentTypeHandle<FollowTargetSpeed>(true);
        __distanceType = state.GetBufferTypeHandle<FollowTargetDistance>(true);
        __messageType = state.GetBufferTypeHandle<Message>();

        __sharedData = new FollowTargetSharedData(true, ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __instanceGroup = builder
                .WithAll<FollowTargetVelocity>()
                .WithAllRW<LocalTransform>()
                .WithNone<PhysicsVelocity, KinematicCharacterBody>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __velocityGroup = builder
                .WithAll<LocalTransform>()
                .WithAllRW<FollowTarget, FollowTargetVelocity>()
                //.WithAny<PhysicsVelocity, ThirdPersonCharacterControl>()
                .Build(ref state);
        
        state.RequireForUpdate<MainCameraTransform>();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __sharedData.Update(ref state, 
            out var localToWorlds, 
            out var localTransformType, 
            out _, 
            out var characterBodyType,
            out var instanceType, 
            out var velocityType);
        
        ApplyTransformsEx applyTransforms;
        applyTransforms.deltaTime = SystemAPI.Time.DeltaTime;
        applyTransforms.velocityType = velocityType;
        applyTransforms.localTransformType = localTransformType;

        var jobHandle = applyTransforms.ScheduleParallelByRef(__instanceGroup,  state.Dependency);

        jobHandle = __sharedData.Update(
            false, 
            jobHandle, 
            ref state, 
            out _);
        
        localToWorlds.Update(ref state);
        __parentType.Update(ref state);
        __upType.Update(ref state);
        __speedType.Update(ref state);
        __distanceType.Update(ref state);
        __messageType.Update(ref state);
        
        ComputeVelocitiesEx computeVelocities;
        computeVelocities.time = SystemAPI.Time.ElapsedTime;
        computeVelocities.cameraRotation = SystemAPI.GetSingleton<MainCameraTransform>().rotation;
        computeVelocities.localToWorlds = localToWorlds;
        computeVelocities.localTransformType = localTransformType;
        computeVelocities.parentType = __parentType;
        computeVelocities.characterBodyType = characterBodyType;
        computeVelocities.instanceType = instanceType;
        computeVelocities.upType = __upType;
        computeVelocities.speedType = __speedType;
        computeVelocities.velocityType = velocityType;
        computeVelocities.distanceType = __distanceType;
        computeVelocities.messageType = __messageType;
        state.Dependency = computeVelocities.ScheduleParallelByRef(__velocityGroup, jobHandle);
    }
}

public struct FollowTargetSharedData
{
    private struct Mask
    {
        public readonly bool IsCharacter;

        public readonly bool IsPhysics;
        
        public readonly ArchetypeChunk Chunk;
        
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        public Mask(
            in ArchetypeChunk chunk, 
            ref ComponentTypeHandle<KinematicCharacterBody> characterBodyType, 
            ref ComponentTypeHandle<PhysicsVelocity> physicsVelocityType)
        {
            IsCharacter = chunk.Has(ref characterBodyType);
            
            IsPhysics = chunk.Has(ref physicsVelocityType);

            Chunk = chunk;
            
            this.characterBodyType = characterBodyType;
        }

        public bool Check(bool isInFixedFrame)
        {
            return (IsCharacter || IsPhysics) == isInFixedFrame;
        }

        public bool Check(bool isInFixedFrame, int index)
        {
            bool result = IsCharacter ? Chunk.IsComponentEnabled(ref characterBodyType, index) : IsPhysics;

            return result == isInFixedFrame;
        }
    }

    private struct ComputeParents
    {
        public bool isInFixedFrame;
        public bool hasTarget;
        public float deltaTimeR;
        
        [ReadOnly] 
        public ComponentLookup<Parent> transformParents;
        
        [ReadOnly] 
        public ComponentLookup<LocalToWorld> transformLocalToWorlds;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<FollowTargetParent> parents;

        public NativeArray<FollowTargetParentMotion> parentMotions;

        public NativeArray<FollowTargetVelocity> velocities;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> localTransforms;

        public void Execute(int index)
        {
            var parent = parents[index];
            float4x4 matrix;
            if (isInFixedFrame)
            {
                if (!new FixedLocalToWorld(transformParents, localTransforms).TryGetMatrix(parent.entity, out matrix))
                    return;
            }

            if (transformLocalToWorlds.TryGetComponent(parent.entity, out var localToWorld))
                matrix = localToWorld.Value;
            else
                return;
            
            var velocity = velocities[index];
            var parentMotion = parentMotions[index];
            if (parentMotion.version == -1)
            {
                localTransforms[entityArray[index]] = LocalTransform.FromMatrix(matrix);
                
                parentMotion.matrix = matrix;
                parentMotion.version = velocity.version;
                
                parentMotions[index] = parentMotion;

                velocity.value = 0.0f;
            } 
            else if (!hasTarget)
            {
                if (parentMotion.version == velocity.version)
                {
                    //var localTransform = localTransforms[entityArray[index]];
                    //var targetTransform = localTransform.TransformTransform(LocalTransform.FromMatrix(parentMotion.matrix).Inverse());
                    //targetTransform = targetTransform.TransformTransform(LocalTransform.FromMatrix(localToWorld));

                    //float3 distance = targetTransform.Position - localTransform.Position;
                    float3 distance = matrix.c3.xyz - parentMotion.matrix.c3.xyz;//targetTransform.Position - localTransform.Position;
                    
                    //#if DEBUG
                    //float3 distanceDEBUG = targetTransform.Position - localTransform.Position;
                    //UnityEngine.Debug.LogError(math.length(distance) - math.length(distanceDEBUG));
                   // #endif
                    
                    float lengthSQ = math.lengthsq(distance);
                    if (lengthSQ > math.FLT_MIN_NORMAL)
                    {
                        float lengthR = math.rsqrt(lengthSQ);
                        velocity.direction = distance * lengthR;
                        velocity.value = math.rcp(lengthR) * deltaTimeR;
                    }
                    
                    //velocity.lookAt = targetTransform.Rotation;
                }
                else
                    parentMotion.version = velocity.version;

                parentMotion.matrix = matrix;
                parentMotions[index] = parentMotion;
                
                //velocity.lookAt = math.quaternion(localToWorld);
            }

            //velocity.lookAt = math.quaternion(localToWorld);//math.mul(math.quaternion(localToWorld.Value), math.inverse(math.quaternion(parentMotion.matrix)));

            velocities[index] = velocity;
        }
    }
    
    [BurstCompile]
    private struct ComputeParentsEx : IJobChunk
    {
        public bool isInFixedFrame;
        public float deltaTimeR;
        
        [ReadOnly] 
        public ComponentLookup<Parent> transformParents;

        [ReadOnly] 
        public ComponentLookup<LocalToWorld> transformLocalToWorlds;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;
        
        [ReadOnly]
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        [ReadOnly]
        public ComponentTypeHandle<FollowTarget> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<FollowTargetParent> parentType;

        public ComponentTypeHandle<FollowTargetParentMotion> parentMotionType;

        public ComponentTypeHandle<FollowTargetVelocity> velocityType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> localTransforms;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var mask = new Mask(chunk, ref characterBodyType, ref physicsVelocityType);
            if (!mask.Check(isInFixedFrame))
                return;
            
            ComputeParents computeParents;
            computeParents.isInFixedFrame = isInFixedFrame;
            computeParents.deltaTimeR = deltaTimeR;
            computeParents.transformParents = transformParents;
            computeParents.transformLocalToWorlds = transformLocalToWorlds;
            computeParents.entityArray = chunk.GetNativeArray(entityType);
            computeParents.parents = chunk.GetNativeArray(ref parentType);
            computeParents.parentMotions = chunk.GetNativeArray(ref parentMotionType);
            computeParents.velocities = chunk.GetNativeArray(ref velocityType);
            computeParents.localTransforms = localTransforms;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (!mask.Check(isInFixedFrame, i))
                    continue;
                
                computeParents.hasTarget = chunk.IsComponentEnabled(ref instanceType, i);
                
                computeParents.Execute(i);
            }
        }
    }

    private struct ApplyBeziers
    {
        public float deltaTime;
        
        [ReadOnly] 
        public BufferAccessor<BezierControlPoint> bezierControlPoints;

        [ReadOnly] 
        public NativeArray<BezierSpeed> speeds;

        [ReadOnly] 
        public NativeArray<LocalTransform> localTransforms;

        public NativeArray<FollowTargetVelocity> velocities;

        public NativeArray<BezierDistance> bezierDistances;

        public void Execute(int index)
        {
            var localTransform = localTransforms[index];
            var bezierDistance = bezierDistances[index];
            if (bezierDistance.value < math.FLT_MIN_NORMAL)
            {
                bezierDistance.motion.rot = localTransform.Rotation;
                bezierDistance.motion.pos = localTransform.Position;
            }

            bezierDistance.value =
                math.saturate(bezierDistance.value + speeds[index].value * deltaTime);
            var velocity = velocities[index];
            float3 targetPosition = math.transform(math.inverse(bezierDistance.motion), velocity.target);
            var bezierControlPoints = this.bezierControlPoints[index].Reinterpret<float3>().AsNativeArray();
            targetPosition = BezierUtility.Calculate(
                bezierDistance.value,
                float3.zero,
                targetPosition,
                bezierControlPoints);

            targetPosition = math.transform(bezierDistance.motion, targetPosition);

            float3 distance = targetPosition - localTransform.Position;
            float lengthSQ = math.lengthsq(distance);
            if (lengthSQ > math.FLT_MIN_NORMAL)
            {
                float lengthR = math.rsqrt(lengthSQ);
                velocity.direction = distance * lengthR;
                velocity.value = math.rcp(deltaTime * lengthR);
            }
            
            velocities[index] = velocity;

            //bezierDistance.motion.pos = targetPosition;
            bezierDistances[index] = bezierDistance;
        }
    }

    [BurstCompile]
    private struct ApplyBeziersEx : IJobChunk
    {
        public bool isInFixedFrame;
        public float deltaTime;
        
        [ReadOnly]
        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;
        
        [ReadOnly]
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        [ReadOnly] 
        public BufferTypeHandle<BezierControlPoint> bezierControlPointType;

        [ReadOnly]
        public ComponentTypeHandle<BezierSpeed> speedType;

        [ReadOnly]
        public ComponentTypeHandle<LocalTransform> localTransformType;

        public ComponentTypeHandle<FollowTargetVelocity> velocityType;

        public ComponentTypeHandle<BezierDistance> bezierDistanceType;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var mask = new Mask(chunk, ref characterBodyType, ref physicsVelocityType);
            if (!mask.Check(isInFixedFrame))
                return;

            ApplyBeziers applyBeziers;
            applyBeziers.deltaTime = deltaTime;
            applyBeziers.bezierControlPoints = chunk.GetBufferAccessor(ref bezierControlPointType);
            applyBeziers.speeds = chunk.GetNativeArray(ref speedType);
            applyBeziers.localTransforms = chunk.GetNativeArray(ref localTransformType);
            applyBeziers.velocities = chunk.GetNativeArray(ref velocityType);
            applyBeziers.bezierDistances = chunk.GetNativeArray(ref bezierDistanceType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (!mask.Check(isInFixedFrame, i))
                    continue;

                applyBeziers.Execute(i);
            }
        }
    }

    private EntityTypeHandle __entityType;

    private ComponentLookup<Parent> __parents;

    private ComponentLookup<LocalToWorld> __localToWorlds;

    private ComponentLookup<LocalTransform> __localTransforms;

    private ComponentTypeHandle<LocalTransform> __localTransformType;

    private ComponentTypeHandle<PhysicsVelocity> __physicsVelocityType;

    private ComponentTypeHandle<FollowTarget> __instanceType;

    private ComponentTypeHandle<FollowTargetVelocity> __velocityType;

    private ComponentTypeHandle<FollowTargetParent> __parentType;

    private ComponentTypeHandle<FollowTargetParentMotion> __parentMotionType;

    private ComponentTypeHandle<KinematicCharacterBody> __characterBodyType;

    private ComponentTypeHandle<BezierSpeed> __bezierSpeedType;

    private ComponentTypeHandle<BezierDistance> __bezierDistanceType;

    private BufferTypeHandle<BezierControlPoint> __bezierControlPointType;

    private EntityQuery __parentGroup;
    private EntityQuery __bezierGroup;

    public FollowTargetSharedData(bool isPhysicsVelocityTypeReadOnly, ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __parents = state.GetComponentLookup<Parent>(true);
        __localToWorlds = state.GetComponentLookup<LocalToWorld>(true);
        __localTransforms = state.GetComponentLookup<LocalTransform>();
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>();
        __physicsVelocityType = state.GetComponentTypeHandle<PhysicsVelocity>(isPhysicsVelocityTypeReadOnly);
        __characterBodyType = state.GetComponentTypeHandle<KinematicCharacterBody>(true);
        __instanceType = state.GetComponentTypeHandle<FollowTarget>(true);
        __velocityType = state.GetComponentTypeHandle<FollowTargetVelocity>();
        __parentType = state.GetComponentTypeHandle<FollowTargetParent>();
        __parentMotionType = state.GetComponentTypeHandle<FollowTargetParentMotion>();
        __bezierControlPointType = state.GetBufferTypeHandle<BezierControlPoint>(true);
        __bezierSpeedType = state.GetComponentTypeHandle<BezierSpeed>(true);
        __bezierDistanceType = state.GetComponentTypeHandle<BezierDistance>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __parentGroup = builder
                .WithAll<FollowTargetParent>()
                .WithAllRW<FollowTargetParentMotion, FollowTargetVelocity>()
                //.WithNone<FollowTarget>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __bezierGroup = builder
                .WithAll<LocalTransform, /*FollowTarget, */BezierControlPoint>()
                .WithAllRW<FollowTargetVelocity, BezierDistance>()
                //.WithAny<PhysicsVelocity, ThirdPersonCharacterControl>()
                .Build(ref state);
    }

    public void Update(
        ref SystemState state,
        out ComponentLookup<LocalToWorld> localToWorlds,
        out ComponentTypeHandle<LocalTransform> localTransformType,
        out ComponentTypeHandle<PhysicsVelocity> physicsVelocityType,
        out ComponentTypeHandle<KinematicCharacterBody> characterBodyType,
        out ComponentTypeHandle<FollowTarget> instanceType,
        out ComponentTypeHandle<FollowTargetVelocity> velocityType)
    {
        __parents.Update(ref state);
        __localToWorlds.Update(ref state);
        __entityType.Update(ref state);
        __physicsVelocityType.Update(ref state);
        __characterBodyType.Update(ref state);
        __instanceType.Update(ref state);
        __parentType.Update(ref state);
        __parentMotionType.Update(ref state);
        __velocityType.Update(ref state);
        __localTransforms.Update(ref state);

        localToWorlds = __localToWorlds;
        physicsVelocityType = __physicsVelocityType;
        characterBodyType = __characterBodyType;
        instanceType = __instanceType;

        __bezierControlPointType.Update(ref state);
        __bezierSpeedType.Update(ref state);
        __localTransformType.Update(ref state);
        __bezierDistanceType.Update(ref state);

        localTransformType = __localTransformType;
        velocityType = __velocityType;
    }

    public JobHandle Update(
        bool isInFixedFrame, 
        in JobHandle inputDeps, 
        ref SystemState state, 
        out float deltaTimeR)
    {
        float deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        if (deltaTime > math.FLT_MIN_NORMAL)
        {
            deltaTimeR = math.rcp(deltaTime);

            ComputeParentsEx computeParents;
            computeParents.isInFixedFrame = isInFixedFrame;
            computeParents.deltaTimeR = deltaTimeR;
            computeParents.transformParents = __parents;
            computeParents.transformLocalToWorlds = __localToWorlds;
            computeParents.entityType = __entityType;
            computeParents.physicsVelocityType = __physicsVelocityType;
            computeParents.characterBodyType = __characterBodyType;
            computeParents.instanceType = __instanceType;
            computeParents.parentType = __parentType;
            computeParents.parentMotionType = __parentMotionType;
            computeParents.velocityType = __velocityType;
            computeParents.localTransforms = __localTransforms;
            var jobHandle = computeParents.ScheduleParallelByRef(__parentGroup, inputDeps);

            ApplyBeziersEx applyBeziers;
            applyBeziers.isInFixedFrame = isInFixedFrame;
            applyBeziers.deltaTime = deltaTime;
            applyBeziers.physicsVelocityType = __physicsVelocityType;
            applyBeziers.characterBodyType = __characterBodyType;
            applyBeziers.bezierControlPointType = __bezierControlPointType;
            applyBeziers.speedType = __bezierSpeedType;
            applyBeziers.localTransformType = __localTransformType;
            applyBeziers.velocityType = __velocityType;
            applyBeziers.bezierDistanceType = __bezierDistanceType;
            return applyBeziers.ScheduleParallelByRef(__bezierGroup, jobHandle);
        }
        
        deltaTimeR = 0.0f;

        return inputDeps;
    }
}
