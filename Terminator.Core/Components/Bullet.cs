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
    Horizontal, 
    ParabolaNear, 
    ParabolaFar
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
        private RenderFrustumPlanes __renderFrustumPlanes;
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
            in CollisionWorld collisionWorld,
            in ComponentLookup<KinematicCharacterBody> characterBodies, 
            in NativeArray<BulletTargetStatus> states, 
            in RenderFrustumPlanes renderFrustumPlanes, 
            in RigidTransform transform,
            //in BlobAssetReference<Collider> collider,
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
            __collisionWorld = collisionWorld;
            __characterBodies = characterBodies;
            __states = states;
            __renderFrustumPlanes = renderFrustumPlanes;
            __transform = transform;
            //__collider = collider;
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
            var body = __collisionWorld.Bodies[hit.RigidBodyIndex];
            if ((body.Collider.Value.GetCollisionFilter(hit.ColliderKey)
                    .BelongsTo & __hitWith) == 0)
                return false;
            
            if (!__Check(__dot, __minDistance, hit.Position - __transform.pos, __transform.rot))
                return false;
            
            if (!__Check(__location, __groundBelongsTo, hit.RigidBodyIndex, hit.Entity, __collisionWorld, __characterBodies))
                return false;

            var aabb = body.CalculateAabb();
            if(RenderFrustumPlanes.IntersectResult.Out == __renderFrustumPlanes.Intersect(aabb.Center, aabb.Extents))
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

        public int colliderIndex;

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
            in RenderFrustumPlanes renderFrustumPlanes, 
            in float4x4 transform,
            in Entity lookAt,
            in CollisionWorld collisionWorld,
            in ComponentLookup<KinematicCharacterBody> characterBodies,
            in NativeArray<BulletCollider> colliders,
            in NativeArray<BulletTargetStatus> states,
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
                    if (colliderIndex >= 0 &&
                        colliderIndex < colliders.Length)
                    {
                        var rigidTransform = space == BulletTargetSpace.World ? 
                            math.RigidTransform(math.mul(math.quaternion(transform), targetTransform.rot), status.transform.pos) : 
                            status.transform;
                        var collider = colliders[colliderIndex].value;
                        if (__Check(
                                version,
                                time,
                                lookAt,
                                rigidTransform,
                                renderFrustumPlanes, 
                                collider,
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
                                rigidTransform,
                                renderFrustumPlanes, 
                                collider,
                                collisionWorld,
                                characterBodies,
                                states,
                                out status.targetPosition);

                        if (!hasTarget)
                        {
                            status.target = Entity.Null;

                            var input = new ColliderDistanceInput(collider, maxDistance, rigidTransform);

                            var collector = new Collector(
                                collisionWorld,
                                characterBodies,
                                states,
                                renderFrustumPlanes, 
                                rigidTransform,
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
                        status.transform.rot = math.mul(quaternion.LookRotationSafe(distance, up),
                            /*Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), direction), */status.transform.rot);
                        
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
            in RenderFrustumPlanes renderFrustumPlanes, 
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
            var aabb = rigidBody.CalculateAabb();
            switch (coordinate)
            {
                case BulletTargetCoordinate.Center:
                    targetPosition = aabb.Center;
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
            
            if (!BulletDefinition.__Check(location, groundBelongsTo, rigidBodyIndex, entity, collisionWorld, characterBodies))
                return false;
            
            if(RenderFrustumPlanes.IntersectResult.Out == renderFrustumPlanes.Intersect(aabb.Center, aabb.Extents))
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
        public float3 randomAngles;
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

        public int damageIndex;

        public int prefabLoaderIndex;

        public BulletLocation location;
        public BulletLocation targetLocation;
        public BulletSpace space;
        public BulletSpace targetSpace;
        public BulletDirection direction;
        public BulletFollowTarget followTarget;
        
        public LayerMaskAndTags layerMaskAndTags;

        public BlobArray<int> targetIndices;
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
        bool isFire, 
        BulletLocation location, 
        int index,
        int version,
        float damageScale,
        double time,
        in float3 gravity, 
        in float3 up, 
        in quaternion cameraRotation, 
        in RenderFrustumPlanes renderFrustumPlanes, 
        in float4x4 transform,
        in Entity parent,
        in Entity lookAt, 
        in LayerMaskAndTags layerMaskAndTags, 
        in LevelStatus levelStatus,
        in CollisionWorld collisionWorld,
        in ComponentLookup<KinematicCharacterBody> characterBodies, 
        in NativeArray<BulletCollider> colliders,
        in NativeArray<BulletPrefab> prefabs,
        in NativeArray<BulletMessage> inputMessages,
        ref DynamicBuffer<Message> outputMessages,
        ref DynamicBuffer<ThirdPersonCharacterStandTime> characterStandTimes,
        ref DynamicBuffer<BulletTargetStatus> targetStates,
        ref DynamicBuffer<BulletInstance> instances,
        ref BulletStatus status, 
        ref Random random)
    {
        if (status.cooldown > time)
            return false;

        ref var data = ref bullets[index];

        bool result = (data.location == 0 || (data.location & location) != 0) && data.layerMaskAndTags.BelongsTo(layerMaskAndTags);
        
        int targetStatusIndex = -1;
        if (result)
        {
            result = false;
            
            int numTargets = data.targetIndices.Length, i;
            for(i = 0; i < numTargets; ++i)
            {
                ref int targetIndex = ref data.targetIndices[i];
                
                if (targetStates.Length <= targetIndex)
                    targetStates.Resize(targets.Length, NativeArrayOptions.ClearMemory);

                ref var temp = ref targetStates.ElementAt(targetIndex);

                ref var target = ref targets[targetIndex];
                if (target.Update(
                        version,
                        time,
                        up,
                        cameraRotation,
                        renderFrustumPlanes, 
                        transform,
                        lookAt,
                        collisionWorld,
                        characterBodies,
                        colliders,
                        targetStates.AsNativeArray(),
                        ref temp,
                        ref random))
                {
                    targetStatusIndex = targetIndex;

                    result = temp.target == Entity.Null || data.targetLocation == 0 || __Check(
                        data.targetLocation,
                        uint.MaxValue,
                        -1,
                        temp.target,
                        collisionWorld,
                        characterBodies);
                    
                    break;
                }
            }
        }
        
        /*if (!result || !isLocation && data.capacity < 2 && data.times == 1)
        {
            status.times = 0;
            status.cooldown = 0.0f;

            return false;
        }*/

        if (data.times > 0)
        {
            if (data.times > status.times)
            {
                if (!result)
                    return false;
            }
            else
            {
                if (result)
                    return false;

                //status.times = 0;
                status.cooldown = 0.0f;
            }
        }

        if (status.cooldown < math.DBL_MIN_NORMAL)
        {
            status.cooldown = time + data.startTime;
            
            status.times = result ? data.times : 0;
            if (data.times > 0 || data.startTime > math.FLT_MIN_NORMAL)
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

            if (!isFire)
                return false;
        }
        else
        {
            if (!result || !isFire || status.count > 0)
                return false;

            status.count = 1;

            entityCount = 1;
        }

        if (!result)
        {
            //status.times = 0;
            
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

        ref var targetStatus = ref targetStates.ElementAt(targetStatusIndex);

        RigidTransform transformResult;
        transformResult.pos = targetStatus.transform.pos;

        switch (data.direction)
        {
            case BulletDirection.Horizontal:
                transformResult.rot = MathUtilities.CreateRotationWithUpPriority(up, math.forward(targetStatus.transform.rot));
                break;
            case BulletDirection.ParabolaNear:
            case BulletDirection.ParabolaFar:
                var distance = targetStatus.targetPosition - targetStatus.transform.pos;
                Math.CalculateParabolaAngleAndTime(
                    data.direction == BulletDirection.ParabolaNear, 
                    data.linearSpeed, 
                    gravity, 
                    ref distance);
                transformResult.rot = quaternion.LookRotationSafe(distance, up);
                break;
            default:
                transformResult.rot = targetStatus.transform.rot;
                break;
        }

        transformResult = math.mul(transformResult, data.transform);

        float3 randomAngle = random.NextFloat3(data.randomAngles);
        quaternion randomRotation = quaternion.Euler(randomAngle);
        transformResult.rot = math.mul(transformResult.rot, randomRotation);

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
        instance.layerMaskAndTags = layerMaskAndTags;
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
        bool isFire, 
        BulletLocation location, 
        float damageScale, 
        double time,
        in float3 gravity, 
        in float3 up, 
        in quaternion cameraRotation, 
        in RenderFrustumPlanes renderFrustumPlanes, 
        in float4x4 transform,
        in Entity entity,
        in Entity lookAt, 
        in LayerMaskAndTags layerMaskAndTags, 
        in LevelStatus levelStatus,
        in CollisionWorld collisionWorld,
        in ComponentLookup<Parent> parents,
        in ComponentLookup<KinematicCharacterBody> characterBodies, 
        in ComponentLookup<AnimationCurveDelta> animationCurveDeltas,
        in NativeArray<BulletCollider> colliders,
        in NativeArray<BulletPrefab> prefabs,
        in NativeArray<BulletActiveIndex> activeIndices, 
        in NativeArray<BulletMessage> inputMessages,
        ref DynamicBuffer<Message> outputMessages,
        ref DynamicBuffer<ThirdPersonCharacterStandTime> characterStandTimes,
        ref DynamicBuffer<BulletTargetStatus> targetStates,
        ref DynamicBuffer<BulletStatus> states,
        ref DynamicBuffer<BulletInstance> instances,
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
                isFire, 
                location, 
                activeIndex.value, 
                version.value,
                activeIndex.damageScale * damageScale,
                time,
                gravity, 
                up, 
                cameraRotation, 
                renderFrustumPlanes, 
                transform,
                entity, 
                lookAt, 
                layerMaskAndTags, 
                levelStatus, 
                collisionWorld,
                characterBodies, 
                colliders, 
                prefabs, 
                inputMessages,
                ref outputMessages,
                ref characterStandTimes, 
                ref targetStates,
                ref instances, 
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
        int rigidBodyIndex, 
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
                rigidBodyIndex = rigidBodyIndex == -1 ? collisionWorld.GetRigidBodyIndex(entity) : rigidBodyIndex;
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

public struct BulletLayerMaskAndTags : IComponentData
{
    public LayerMaskAndTags value;
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
    public LayerMaskAndTags layerMaskAndTags;
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
                    
                    FollowTargetVelocity followTargetVelocity;
                    followTargetVelocity.version = 0;
                    followTargetVelocity.distanceIndex = -1;
                    followTargetVelocity.time = 0.0;
                    followTargetVelocity.value = 0.0f;
                    followTargetVelocity.direction = math.forward(transformResult.rot);
                    followTargetVelocity.target = this.targetPosition;
                    followTargetVelocity.lookAt = transformResult.rot;
                    entityManager.SetComponent(1, entity, followTargetVelocity);
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

        EffectDamage effectDamage;
        effectDamage.scale = math.abs(damageScale) > math.FLT_MIN_NORMAL ? damageScale : 1.0f;
        effectDamage.layerMaskAndTags = layerMaskAndTags;
        entityManager.AddComponent(1, entity, effectDamage);

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

public struct BulletCollider : IBufferElementData
{
    public BlobAssetReference<Collider> value;
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