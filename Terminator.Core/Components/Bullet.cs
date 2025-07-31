using System;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.GraphicsIntegration;
using Unity.Transforms;
using ZG;
using Collider = Unity.Physics.Collider;
using Math = ZG.Mathematics.Math;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

[Flags]
public enum BulletLocation
{
    Ground = 0x01, 
    Air = 0x02
}

public enum BulletSpace
{
    World, 
    Local
}

public enum BulletTargetSpace
{
    Camera, 
    World, 
    Local
}

public enum BulletTargetCoordinate
{
    Origin,
    Center
}

public enum BulletDirection
{
    Forward,
    Horizontal
}

public enum BulletFollowTarget
{
    None, 
    Source, 
    Destination
}

public struct BulletDefinition
{
    private struct Collector : ICollector<DistanceHit>
    {
        private CollisionWorld __collisionWorld;
        //private BlobAssetReference<Collider> __collider;
        private ComponentLookup<KinematicCharacterBody> __characterBodies;
        private NativeArray<BulletTargetStatus> __states;
        private RigidTransform __transform;
        private double __time;
        private float __minDistance;
        private float __dot;
        private uint __hitWith;
        private uint __groundBelongsTo;
        private int __count;
        private int __version;
        private BulletLocation __location;

        public bool EarlyOutOnFirstHit => false;

        public int NumHits { get; private set; }

        public float MaxFraction { get; private set; }

        public DistanceHit closestHit { get; private set; }

        public Collector(
            in RigidTransform transform,
            in ComponentLookup<KinematicCharacterBody> characterBodies, 
            in NativeArray<BulletTargetStatus> states, 
            //in BlobAssetReference<Collider> collider,
            in CollisionWorld collisionWorld,
            double time, 
            //in float3 up, 
            float minDistance,
            float maxDistance, 
            float dot, 
            uint hitWith, 
            uint groundBelongsTo, 
            int version, 
            BulletLocation location)
        {
            __transform = transform;
            __states = states;
            __characterBodies = characterBodies;
            //__collider = collider;
            __collisionWorld = collisionWorld;
            __time = time;
            //__up = up;
            __minDistance = minDistance;
            __dot = dot;

            __count = int.MaxValue;

            __hitWith = hitWith;
            __groundBelongsTo = groundBelongsTo;
            __version = version;
            __location = location;
            
            NumHits = 0;

            MaxFraction = maxDistance;

            closestHit = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            if ((__collisionWorld.Bodies[hit.RigidBodyIndex].Collider.Value.GetCollisionFilter(hit.ColliderKey)
                    .BelongsTo & __hitWith) == 0)
                return false;
            
            if (!__Check(__dot, __minDistance, hit.Position - __transform.pos, __transform.rot))
                return false;
            
            if (!__Check(__location, __groundBelongsTo, hit.Entity, __collisionWorld, __characterBodies))
                return false;

            /*float3 position = hit.Position;
            var input = new ColliderCastInput(
                __collider,
                __transform.pos,
                position,
                quaternion.LookRotationSafe(position - __transform.pos, __up));

            if (!__collisionWorld.CastCollider(input))
                return false;*/

            NumHits = 1;
            
            //if (__Check(__dot, __minDistance, hit.Position - __transform.pos, __transform.rot))
                MaxFraction = hit.Fraction;

            int count = 0;
            Entity entity = hit.Entity;
            foreach (var status in __states)
            {
                if ((status.version == __version || status.cooldown > __time) && status.target == entity)
                    ++count;
            }

            if (count > __count)
                return false;

            __count = count;

            closestHit = hit;

            return true;
        }
    }

    public struct Target
    {
        public AABB aabb;
        public quaternion rotation;
        public float3 randomAxes;
        public float randomAngle;
        
        public float dot;
        public float minDistance;
        public float maxDistance;

        public float cooldown;

        public int prefabLoaderIndex;

        public uint hitWith;
        public uint groundBelongsTo;

        public BulletLocation location;

        public BulletTargetSpace space;

        public BulletTargetCoordinate coordinate;
        
        public static float3 GetRandomPointInEllipse(in float3 axes, ref Random random)
        {
            float theta = random.NextFloat() * 2.0f * math.PI,
                radius = math.sqrt(random.NextFloat()),
                x = radius * math.cos(theta) * axes.x,
                y = radius * math.sin(theta) * axes.y;
            return math.float3(x, y, axes.z);
        }

        public bool Update(
            int version,
            double time, 
            in float3 up,
            in quaternion cameraRotation,
            in float4x4 transform,
            in Entity lookAt,
            in CollisionWorld collisionWorld,
            in ComponentLookup<PhysicsCollider> physicsColliders,
            in ComponentLookup<KinematicCharacterBody> characterBodies,
            in DynamicBuffer<BulletPrefab> prefabs,
            in NativeArray<BulletTargetStatus> states,
            ref PrefabLoader.ParallelWriter prefabLoader,
            ref BulletTargetStatus status,
            ref Random random)
        {
            if (version == status.version)
                return minDistance >= maxDistance || status.target != Entity.Null;

            RigidTransform targetTransform;
            targetTransform.pos = random.NextFloat3(aabb.Min, aabb.Max);
            targetTransform.rot = rotation;

            if (randomAxes.z > math.FLT_MIN_NORMAL)
            {
                float3 point = GetRandomPointInEllipse(randomAxes, ref random), direction = math.normalize(point);
                quaternion randomRotation = Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), direction);
                targetTransform.rot = math.mul(targetTransform.rot, randomRotation);
            }

            if (randomAngle > math.FLT_MIN_NORMAL)
            {
                float randomAngle = random.NextFloat(this.randomAngle);
                float3 randomAxis = random.NextFloat3Direction();
                quaternion randomRotation = quaternion.AxisAngle(randomAxis, randomAngle);
                targetTransform.rot = math.mul(targetTransform.rot, randomRotation);
            }

            switch (space)
            {
                case BulletTargetSpace.Camera:
                    status.transform.rot = math.mul(cameraRotation, targetTransform.rot);
                    status.transform.pos = math.transform(math.RigidTransform(cameraRotation, transform.c3.xyz),
                        targetTransform.pos);
                    break;
                case BulletTargetSpace.World:
                    status.transform.rot = targetTransform.rot;
                    status.transform.pos = math.transform(transform, targetTransform.pos);
                    break;
                case BulletTargetSpace.Local:
                    status.transform.rot = math.mul(math.quaternion(transform), targetTransform.rot);
                    status.transform.pos = math.transform(transform, targetTransform.pos);
                    break;
            }

            if (minDistance < maxDistance)
            {
                bool hasTarget;
                if (time < status.cooldown)
                {
                    int rigidBodyIndex = status.target == Entity.Null
                        ? -1
                        : collisionWorld.GetRigidBodyIndex(status.target);

                    hasTarget = rigidBodyIndex != -1;
                    if (hasTarget)
                    {
                        var body = collisionWorld.Bodies[rigidBodyIndex];
                        switch (coordinate)
                        {
                            case BulletTargetCoordinate.Center:
                                status.targetPosition = body.CalculateAabb().Center;
                                break;
                            default:
                                status.targetPosition = body.WorldFromBody.pos;
                                break;
                        }
                    }
                }
                else
                    hasTarget = false;

                if (!hasTarget)
                {
                    if (prefabLoader.TryGetOrLoadPrefabRoot(prefabs[prefabLoaderIndex].entityPrefabReference, out var prefab) &&
                        physicsColliders.TryGetComponent(prefab, out var physicsCollider) &&
                        physicsCollider.IsValid)
                    {
                        //var rigidTransform = math.RigidTransform(math.inverse(status.transform.rot), status.transform.pos);
                        if (__Check(
                                version,
                                time,
                                lookAt,
                                status.transform,
                                physicsCollider.Value,
                                collisionWorld,
                                characterBodies,
                                states,
                                out status.targetPosition))
                        {
                            status.target = lookAt;

                            hasTarget = true;
                        }
                        else
                            hasTarget = __Check(
                                version,
                                time,
                                status.target,
                                status.transform,
                                physicsCollider.Value,
                                collisionWorld,
                                characterBodies,
                                states,
                                out status.targetPosition);

                        if (!hasTarget)
                        {
                            status.target = Entity.Null;

                            //var filter = physicsCollider.Value.Value.GetCollisionFilter();
                            //filter.CollidesWith = hitWith;

                            var input = new ColliderDistanceInput(physicsCollider.Value, maxDistance, status.transform);
                            //input = status.transform.pos;
                            //input.MaxDistance = maxDistance;
                            //input.Filter = filter;

                            var collector = new Collector(
                                status.transform,
                                characterBodies,
                                states,
                                collisionWorld,
                                time,
                                minDistance,
                                maxDistance,
                                dot,
                                hitWith,
                                groundBelongsTo,
                                version,
                                location);
                            if (collisionWorld.CalculateDistance(input, ref collector))
                            {
                                var closestHit = collector.closestHit;
                                var body = collisionWorld.Bodies[closestHit.RigidBodyIndex];
                                switch (coordinate)
                                {
                                    case BulletTargetCoordinate.Center:
                                        status.targetPosition = body.CalculateAabb().Center;
                                        break;
                                    default:
                                        status.targetPosition = body.WorldFromBody.pos;
                                        break;
                                }

                                status.target = closestHit.Entity;
                            }

                            if (status.target == Entity.Null)
                                return false;
                        }

                        float3 distance = status.targetPosition - status.transform.pos;
                        /*if (space == BulletTargetSpace.Camera)
                        {
                            distance = math.project(distance, math.forward(cameraRotation));

                            status.targetPosition = status.transform.pos + distance;
                        }*/

                        status.transform.rot = math.mul(quaternion.LookRotationSafe(distance, up),
                            /*Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), direction), */status.transform.rot); //rotation;
                        
                        status.cooldown = time + this.cooldown;
                    }
                    else
                    {
                        status.target = Entity.Null;

                        return false;
                    }
                }
            }
            else
                status.targetPosition = status.transform.pos;

            status.version = version;

            return true;
        }

        private bool __Check(
            int version, 
            double time, 
            in Entity entity, 
            in RigidTransform transform, 
            in BlobAssetReference<Collider> collider, 
            in CollisionWorld collisionWorld, 
            in ComponentLookup<KinematicCharacterBody> characterBodies, 
            in NativeArray<BulletTargetStatus> states, 
            out float3 targetPosition)
        {
            int rigidBodyIndex = entity == Entity.Null ? -1 : collisionWorld.GetRigidBodyIndex(entity);
            if (rigidBodyIndex == -1)
            {
                targetPosition = float3.zero;

                return false;
            }

            var rigidBody = collisionWorld.Bodies[rigidBodyIndex];
            switch (coordinate)
            {
                case BulletTargetCoordinate.Center:
                    targetPosition = rigidBody.CalculateAabb().Center;
                    break;
                default:
                    targetPosition = rigidBody.WorldFromBody.pos;
                    break;
            }
            
            /*if (rigidBody.Collider.IsCreated && (rigidBody.Collider.Value.GetCollisionFilter().BelongsTo & hitWith) == 0)
                return false;*/

            /*float distanceSQ = math.lengthsq(distance);
            if (distanceSQ < minDistance * minDistance || distanceSQ > maxDistance * maxDistance)
                return false;*/
            
            if (!rigidBody.CalculateDistance(new ColliderDistanceInput(collider, maxDistance, transform),
                    out var closestHit))
                return false;
            
            if((rigidBody.Collider.Value.GetCollisionFilter(closestHit.ColliderKey).BelongsTo & hitWith) == 0)
                return false;

            if (!BulletDefinition.__Check(dot, minDistance, closestHit.Position - transform.pos, transform.rot))
                return false;
            
            if (!BulletDefinition.__Check(location, groundBelongsTo, entity, collisionWorld, characterBodies))
                return false;
            
            foreach (var status in states)
            {
                if ((status.version == version || status.cooldown > time) && status.target == entity)
                    return false;
            }

            return true;
        }
    }

    public struct Bullet
    {
        public RigidTransform transform;
        public float3 angularSpeed;
        public float linearSpeed;
        public float animationCurveSpeed;
        public float targetPositionInterpolation;
        public float interval;
        public float cooldown;
        public float startTime;
        public float delayTime;
        public int capacity;
        public int times;

        public int targetIndex;
        public int damageIndex;

        public int prefabLoaderIndex;

        public BulletLocation location;
        public BulletLocation targetLocation;
        public BulletSpace space;
        public BulletSpace targetSpace;
        public BulletDirection direction;
        public BulletFollowTarget followTarget;
        
        public BulletLayerMask layerMask;

        public BlobArray<int> standTimeIndices;
        public BlobArray<int> messageIndices;
    }

    public struct Damage
    {
        public float goldScale;
        public float goldMin;
        public float goldMax;

        public float killCountScale;
        public float killCountMin;
        public float killCountMax;
        
        public float GetScaleByGold(int value)
        {
            float result = 1.0f;
            if (this.goldScale > math.FLT_MIN_NORMAL)
            {
                float goldScale = this.goldScale * value;

                result += math.clamp(goldScale, goldMin, goldMax);
            }
            
            return result;
        }
        
        public float GetScaleByKillCount(int value)
        {
            float result = 1.0f;
            if (this.killCountScale > math.FLT_MIN_NORMAL)
            {
                float killCountScale = this.killCountScale * value;

                result += math.clamp(killCountScale, killCountMin, killCountMax);
            }

            return result;
        }

        public float GetScale(in LevelStatus status)
        {
            return GetScaleByGold(status.gold) * GetScaleByKillCount(status.killCount);
        }
    }

    public struct StandTime
    {
        public float start;
        public float end;
    }

    public float minAirSpeed;
    public float maxAirSpeed;
    public BlobArray<Target> targets;
    public BlobArray<Damage> damages;
    public BlobArray<StandTime> standTimes;
    public BlobArray<Bullet> bullets;

    public bool Update(
        BulletLocation location, 
        int index,
        int version,
        float damageScale,
        double time,
        in float3 up, 
        in quaternion cameraRotation, 
        in float4x4 transform,
        in Entity parent,
        in Entity lookAt, 
        in BulletLayerMask layerMask, 
        in LevelStatus levelStatus,
        in CollisionWorld collisionWorld,
        in ComponentLookup<PhysicsCollider> physicsColliders,
        in ComponentLookup<KinematicCharacterBody> characterBodies, 
        in DynamicBuffer<BulletPrefab> prefabs,
        in DynamicBuffer<BulletMessage> inputMessages,
        ref DynamicBuffer<Message> outputMessages,
        ref DynamicBuffer<ThirdPersonCharacterStandTime> characterStandTimes,
        ref DynamicBuffer<BulletTargetStatus> targetStates,
        ref DynamicBuffer<BulletInstance> instances,
        ref PrefabLoader.ParallelWriter prefabLoader,
        ref BulletStatus status, 
        ref Random random)
    {
        if (status.cooldown > time)
            return false;

        ref var data = ref bullets[index];

        bool result = (data.location == 0 || (data.location & location) != 0) && data.layerMask.BelongsTo(layerMask);
        
        if (targetStates.Length <= data.targetIndex)
            targetStates.Resize(targets.Length, NativeArrayOptions.ClearMemory);

        ref var targetStatus = ref targetStates.ElementAt(data.targetIndex);

        if (result)
        {
            ref var target = ref targets[data.targetIndex];
            result = //(isLocation || target.minDistance >= target.maxDistance) &&
                target.Update(
                    version,
                    time,
                    up,
                    cameraRotation,
                    transform,
                    lookAt,
                    collisionWorld,
                    physicsColliders,
                    characterBodies,
                    prefabs,
                    targetStates.AsNativeArray(),
                    ref prefabLoader,
                    ref targetStatus,
                    ref random);

            if (result && targetStatus.target != Entity.Null && data.targetLocation != 0)
                result = __Check(
                    data.targetLocation,
                    uint.MaxValue,
                    targetStatus.target,
                    collisionWorld,
                    characterBodies);
        }

        /*if (!result || !isLocation && data.capacity < 2 && data.times == 1)
        {
            status.times = 0;
            status.cooldown = 0.0f;

            return false;
        }*/

        if (data.times > 0 && data.times <= status.times)
        {
            if (!result)
            {
                status.times = 0;
                status.cooldown = 0.0f;
            }

            return false;
        }

        if (status.cooldown < math.DBL_MIN_NORMAL)
        {
            status.cooldown = time + data.startTime;

            if (data.startTime > math.FLT_MIN_NORMAL)
                return false;
        }

        //Entity prefab = Entity.Null;
        //result = result &&
        //         prefabLoader.TryGetOrLoadPrefabRoot(prefabs[data.prefabLoaderIndex].entityPrefabReference, out prefab);

        /*double cooldown;
        if (status.count < 0)
        {
            status.count = 0;
            cooldown = status.cooldown;
        }
        else
            cooldown = time;*/
        double cooldown = status.cooldown;

        int statusCount = status.count, entityCount = 0;

        if (data.capacity > status.count && (data.interval > math.FLT_MIN_NORMAL || data.cooldown > math.FLT_MIN_NORMAL))
        {
            //status.cooldown = cooldown;

            int count;
            do
            {
                if (data.interval > math.FLT_MIN_NORMAL)
                {
                    count = (int)math.floor((time - status.cooldown) / data.interval) + 1;

                    if (count + status.count > data.capacity)
                        count = data.capacity - status.count;
                    
                    status.cooldown += data.interval * count;
                }
                else
                    count = 1;
                
                entityCount += count;

                status.count += count;
                if (status.count == data.capacity)
                {
                    status.cooldown += data.cooldown;
                    status.count = 0;
                    if (++status.times >= data.times && data.times > 0)
                    {
                        status.cooldown = 0.0;
                        
                        break;
                    }
                }
                
            } while (status.cooldown <= time);
        }
        else
        {
            if (!result || status.count > 0)
                return false;

            status.count = 1;

            entityCount = 1;
        }

        if (!result)
        {
            status.times = 0;
            
            return false;
        }

        status.version += entityCount;
        
        int numMessageIndices = data.messageIndices.Length;
        if (numMessageIndices > 0)
        {
            Message outputMessage;
            for (int i = 0; i < numMessageIndices; ++i)
            {
                var inputMessage = inputMessages[data.messageIndices[i]];

                outputMessage.key = 0;//random.NextInt();
                outputMessage.name = inputMessage.name;
                outputMessage.value = inputMessage.value;
                outputMessages.Add(outputMessage);
            }
        }

        int numStandTimeIndices = data.standTimeIndices.Length;
        if (numStandTimeIndices > 0 && characterStandTimes.IsCreated)
        {
            ThirdPersonCharacterStandTime destination;
            for (int i = 0; i < numStandTimeIndices; ++i)
            {
                ref var source = ref this.standTimes[data.standTimeIndices[i]];

                destination.time = time + source.start;
                destination.duration = source.end - source.start;

                characterStandTimes.Add(destination);
            }
        }

        RigidTransform transformResult;
        transformResult.pos = targetStatus.transform.pos;

        switch (data.direction)
        {
            case BulletDirection.Horizontal:
                transformResult.rot = MathUtilities.CreateRotationWithUpPriority(up, math.forward(targetStatus.transform.rot));
                break;
            default:
                transformResult.rot = targetStatus.transform.rot;
                break;
        }

        transformResult = math.mul(transformResult, data.transform);

        if (data.targetPositionInterpolation > math.FLT_MIN_NORMAL)
        {
            transformResult.pos = math.lerp(transformResult.pos, targetStatus.targetPosition, data.targetPositionInterpolation);
            transformResult.rot = math.slerp(transformResult.rot, cameraRotation, data.targetPositionInterpolation);

            /*float targetDistance =
                data.distance * math.distance(targetStatus.targetTransform.pos, targetStatus.transform.pos);
            var distance = transformResult.pos - targetStatus.transform.pos;
            float distanceSQ = math.lengthsq(distance);
            if (distanceSQ > targetDistance * targetDistance)
                transformResult.pos =
                    targetDistance * math.rsqrt(distanceSQ) * distance + targetStatus.transform.pos;*/
        }
        
        if (data.space == BulletSpace.Local)
            transformResult = math.mul(math.inverse(math.RigidTransform(transform)), transformResult);

        BulletInstance instance;
        instance.index = index;
        instance.layerMask = layerMask;
        instance.damageScale = damageScale * (data.damageIndex == -1 ? 1.0f : damages[data.damageIndex].GetScale(levelStatus));
        instance.time = cooldown + data.delayTime;
        instance.targetPosition = targetStatus.targetPosition;
        instance.parentPosition = transform.c3.xyz;
        instance.transform = transformResult;
        instance.target = targetStatus.target;
        instance.parent = parent;
        instance.entityPrefabReference = prefabs[data.prefabLoaderIndex].entityPrefabReference;
        
        if(entityCount == 1)
            instances.Add(instance);
        else
        {
            int count, i;
            do
            {
                if (data.interval > math.FLT_MIN_NORMAL)
                {
                    count = data.capacity - statusCount;
                    count = math.min(count, entityCount);
                }
                else
                    count = 1;

                for(i = 0; i < count; ++i)
                {
                    instance.time = cooldown + data.delayTime;

                    instances.Add(instance);
                    
                    cooldown += data.interval;
                }

                statusCount += count;
                if (statusCount == data.capacity)
                {
                    statusCount = 0;

                    cooldown += data.cooldown;
                }
                
                entityCount -= count;
            } while (entityCount > 0);
        }

        return true;
    }

    public void Update(
        BulletLocation location, 
        float damageScale, 
        double time,
        in float3 up, 
        in quaternion cameraRotation, 
        in float4x4 transform,
        in Entity entity,
        in Entity lookAt, 
        in BulletLayerMask layerMask, 
        in LevelStatus levelStatus,
        in CollisionWorld collisionWorld,
        in ComponentLookup<Parent> parents,
        in ComponentLookup<PhysicsCollider> physicsColliders,
        in ComponentLookup<KinematicCharacterBody> characterBodies, 
        in ComponentLookup<AnimationCurveDelta> animationCurveDeltas,
        in DynamicBuffer<BulletPrefab> prefabs,
        in DynamicBuffer<BulletActiveIndex> activeIndices, 
        in DynamicBuffer<BulletMessage> inputMessages,
        ref DynamicBuffer<Message> outputMessages,
        ref DynamicBuffer<ThirdPersonCharacterStandTime> characterStandTimes,
        ref DynamicBuffer<BulletTargetStatus> targetStates,
        ref DynamicBuffer<BulletStatus> states,
        ref DynamicBuffer<BulletInstance> instances,
        ref PrefabLoader.ParallelWriter prefabLoader,
        ref BulletVersion version, 
        ref Random random)
    {
        //time = math.max(time, math.DBL_MIN_NORMAL);
        int numStates = states.Length, numBullets = bullets.Length;
        if (numStates < numBullets)
        {
            Entity temp = entity;
            AnimationCurveDelta animationCurveDelta;
            while(!animationCurveDeltas.TryGetComponent(temp, out animationCurveDelta) && parents.TryGetComponent(temp, out var parent))
                temp = parent.Value;

            if (animationCurveDelta.start > 0.0)
                time = animationCurveDelta.start;

            states.Resize(numBullets, NativeArrayOptions.ClearMemory);

            /*BulletStatus status;
            status.times = 0;
            status.count = 0;
            for (int i = numStates; i < numBullets; ++i)
            {
                status.cooldown = time + bullets[i].startTime;

                states[i] = status;
            }*/
        }
        
        ++version.value;

        //var lookAt = lookAts[index];
        int numActiveIndices = activeIndices.Length;
        BulletActiveIndex activeIndex;
        for (int i = 0; i < numActiveIndices; ++i)
        {
            activeIndex = activeIndices[i];
            Update(
                location, 
                activeIndex.value, 
                version.value,
                activeIndex.damageScale * damageScale,
                time,
                up, 
                cameraRotation, 
                transform,
                entity, 
                lookAt, 
                layerMask, 
                levelStatus, 
                collisionWorld,
                physicsColliders,
                characterBodies, 
                prefabs, 
                inputMessages,
                ref outputMessages,
                ref characterStandTimes, 
                ref targetStates,
                ref instances, 
                ref prefabLoader, 
                ref states.ElementAt(activeIndex.value), 
                ref random);
        }
    }

    private static bool __Check(float minDot, float minDistance, in float3 distance, in quaternion rotation)
    {
        if (minDot < 1.0f || minDistance > math.FLT_MIN_NORMAL)
        {
            float3 vector = math.mul(math.inverse(rotation), distance);
            if (math.abs(vector.z) < minDistance)
                return false;

            if (minDot < 1.0f)
            {
                float dot = math.normalizesafe(vector).z;
                if (dot < minDot)
                    return false;
            }
        }

        return true;
    }
    
    private static bool __Check(
        BulletLocation location, 
        uint groundBelongsTo, 
        in Entity entity, 
        in CollisionWorld collisionWorld, 
        in ComponentLookup<KinematicCharacterBody> characterBodies)
    {
        if (location != 0)
        {
            bool isGrounded = characterBodies.TryGetComponent(entity, out var characterBody) &&
                              characterBodies.IsComponentEnabled(entity) &&
                              characterBody.IsGrounded &&
                              (groundBelongsTo == 0 ||
                               (groundBelongsTo & collisionWorld.Bodies[characterBody.GroundHit.RigidBodyIndex].Collider
                                   .Value
                                   .GetCollisionFilter(characterBody.GroundHit.ColliderKey).BelongsTo) != 0);

            if (!isGrounded)
            {
                int rigidBodyIndex = collisionWorld.GetRigidBodyIndex(entity);
                isGrounded = rigidBodyIndex == -1 || rigidBodyIndex >= collisionWorld.NumDynamicBodies;
            }
            
            if (isGrounded)
            {
                if ((location & BulletLocation.Ground) != BulletLocation.Ground)
                    return false;
            }
            else
            {
                if ((location & BulletLocation.Air) != BulletLocation.Air)
                    return false;
            }
        }

        return true;
    }
}

public struct BulletDefinitionData : IComponentData
{
    public BlobAssetReference<BulletDefinition> definition;
}

public struct BulletLayerMask : IComponentData
{
    public int value;
    public FixedList512Bytes<FixedString32Bytes> tags;

    public static readonly BulletLayerMask AllLayers = new ()
    {
        value = -1,
        tags = default
    };
    
    public bool isEmpty => value == 0 && tags.IsEmpty;
    
    public bool BelongsTo(in BulletLayerMask layerMask)
    {
        if (value != 0 && (value & layerMask.value) == 0)
            return false;

        if (!tags.IsEmpty)
        {
            bool isContains;
            foreach (var destination in tags)
            {
                isContains = false;
                foreach (var source in layerMask.tags)
                {
                    if (source == destination)
                    {
                        isContains = true;
                        
                        break;
                    }
                }

                if (!isContains)
                    return false;
            }
        }

        return true;
    }

    public static BulletLayerMask operator |(in BulletLayerMask x, in BulletLayerMask y)
    {
        BulletLayerMask result;
        result.value = x.value | y.value;
        result.tags = default;
        
        foreach (var tag in x.tags)
            result.tags.Add(tag);
        
        foreach (var tag in y.tags)
            result.tags.Add(tag);

        return result;
    }
}

public struct BulletInstance : IBufferElementData
{
    public int index;
    public float damageScale;
    public double time;
    public float3 targetPosition;
    public float3 parentPosition;
    public RigidTransform transform;
    public Entity target;
    public Entity parent;
    public BulletLayerMask layerMask;
    public EntityPrefabReference entityPrefabReference;

    public bool Apply(
        in double time, 
        in CollisionWorld collisionWorld,
        in ComponentLookup<PhysicsGraphicalInterpolationBuffer> physicsGraphicalInterpolationBuffers, 
        in ComponentLookup<CharacterInterpolation> characterInterpolations, 
        in ComponentLookup<ThirdPersonCharacterControl> characterControls,
        in ComponentLookup<AnimationCurveDelta> animationCurveDeltas,
        ref BulletDefinition definition, 
        ref PrefabLoader.ParallelWriter prefabLoader,
        ref EntityCommandBuffer.ParallelWriter entityManager)
    {
        if (time < this.time)
            return false;

        if (!prefabLoader.TryGetOrLoadPrefabRoot(entityPrefabReference, out var prefabRoot))
            return false;
        
        Entity entity = entityManager.Instantiate(0, prefabRoot);
        
        __Apply(
            entity, 
            prefabRoot, 
            collisionWorld, 
            physicsGraphicalInterpolationBuffers, 
            characterInterpolations, 
            characterControls, 
            animationCurveDeltas, 
            ref definition.bullets[index], 
            ref entityManager);
        
        return true;
    }

    private Entity __Apply(
        in Entity entity, 
        in Entity prefabRoot, 
        in CollisionWorld collisionWorld,
        in ComponentLookup<PhysicsGraphicalInterpolationBuffer> physicsGraphicalInterpolationBuffers, 
        in ComponentLookup<CharacterInterpolation> characterInterpolations, 
        in ComponentLookup<ThirdPersonCharacterControl> characterControls,
        in ComponentLookup<AnimationCurveDelta> animationCurveDeltas,
        ref BulletDefinition.Bullet data, 
        ref EntityCommandBuffer.ParallelWriter entityManager)
    {
        BulletEntity bulletEntity;
        bulletEntity.index = index;
        bulletEntity.parent = parent;
        entityManager.AddComponent(1, entity, bulletEntity);

        if (data.space == BulletSpace.Local)
        {
            Parent temp;
            temp.Value = parent;
            entityManager.AddComponent(1, entity, temp);
        }

        var transformResult = transform;

        int rigidBodyIndex = target == Entity.Null
            ? -1
            : collisionWorld.GetRigidBodyIndex(target);
        float3 targetPosition = rigidBodyIndex == -1
            ? this.targetPosition
            : collisionWorld.Bodies[rigidBodyIndex].WorldFromBody.pos;
        if (data.targetSpace == BulletSpace.Local && target != Entity.Null)
        {
            Parent temp;
            temp.Value = target;
            entityManager.AddComponent(1, entity, temp);

            transformResult.pos -= targetPosition;
        }

        FollowTarget followTarget;
        switch (data.followTarget)
        {
            case BulletFollowTarget.Source:
                followTarget.entity = parent;
                followTarget.offset = data.space == BulletSpace.Local ? transformResult.pos : transformResult.pos - parentPosition;//transform.c3.xyz;
                followTarget.space = FollowTargetSpace.Camera;
                entityManager.SetComponent(1, entity, followTarget);
                entityManager.SetComponentEnabled<FollowTarget>(1, entity, true);
                break;
            case BulletFollowTarget.Destination:
                if (target != Entity.Null)
                {
                    followTarget.entity = target;
                    
                    followTarget.offset = this.targetPosition - targetPosition;
                    followTarget.space = FollowTargetSpace.World;
                    entityManager.SetComponent(1, entity, followTarget);
                    entityManager.SetComponentEnabled<FollowTarget>(1, entity, true);
                }

                break;
        }

        LocalTransform localTransform;
        localTransform.Scale = 1.0f;
        localTransform.Position = transformResult.pos;
        localTransform.Rotation = transformResult.rot;
        entityManager.SetComponent(1, entity, localTransform);

        if (physicsGraphicalInterpolationBuffers.HasComponent(prefabRoot))
        {
            PhysicsGraphicalInterpolationBuffer physicsGraphicalInterpolationBuffer;
            physicsGraphicalInterpolationBuffer.PreviousVelocity = default;
            physicsGraphicalInterpolationBuffer.PreviousTransform =
                math.RigidTransform(localTransform.Rotation, localTransform.Position);
            entityManager.SetComponent(2, entity, physicsGraphicalInterpolationBuffer);
        }
        
        if (characterInterpolations.TryGetComponent(prefabRoot, out var characterInterpolation))
        {
            characterInterpolation.InterpolationFromTransform =
                math.RigidTransform(localTransform.Rotation, localTransform.Position);
            entityManager.SetComponent(2, entity, characterInterpolation);
        }

        if (animationCurveDeltas.HasComponent(prefabRoot))
        {
            AnimationCurveDelta animationCurveDelta;
            animationCurveDelta.elapsed = time;
            animationCurveDelta.start = time;
            entityManager.SetComponent(1, entity, animationCurveDelta);
        }

        if (data.animationCurveSpeed > math.FLT_MIN_NORMAL)
        {
            AnimationCurveSpeed animationCurveSpeed;
            animationCurveSpeed.value = data.animationCurveSpeed;
            entityManager.SetComponent(1, entity, animationCurveSpeed);
        }

        if (data.linearSpeed > math.FLT_MIN_NORMAL)
        {
            var forward = math.forward(transformResult.rot);

            if (characterControls.TryGetComponent(prefabRoot, out var control))
            {
                control.MoveVector = forward * data.linearSpeed;
                entityManager.SetComponent(1, entity, control);
            }
            else
            {
                PhysicsVelocity velocity;
                velocity.Angular = data.angularSpeed;
                velocity.Linear = forward * data.linearSpeed;
                entityManager.SetComponent(1, entity, velocity);
            }
        }

        if (damageScale > math.FLT_MIN_NORMAL)
        {
            EffectDamage effectDamage;
            effectDamage.scale = damageScale;
            effectDamage.bulletLayerMask = layerMask;
            entityManager.AddComponent(1, entity, effectDamage);
        }

        EffectDamageParent damageParent;
        damageParent.index = index;
        damageParent.entity = parent;
        entityManager.AddComponent(1, entity, damageParent);

        return entity;
    }
}

public struct BulletStatus : IBufferElementData
{
    public int version;
    public int times;
    public int count;
    public double cooldown;
}

public struct BulletTargetStatus : IBufferElementData
{
    public int version;
    public double cooldown;
    public Entity target;
    public float3 targetPosition;
    public RigidTransform transform;
}

public struct BulletPrefab : IBufferElementData
{
    public EntityPrefabReference entityPrefabReference;
}

public struct BulletMessage : IBufferElementData
{
    //public FixedString128Bytes key;

    public FixedString128Bytes name;

    public UnityObjectRef<Object> value;
}

public struct BulletActiveIndex : IBufferElementData
{
    public int value;
    public float damageScale;
}

public struct BulletVersion : IComponentData
{
    public int value;
}

public struct BulletEntity : IComponentData
{
    public int index;
    public Entity parent;
}

public struct BulletEntityManaged: IComponentData
{
    
}