using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Math = ZG.Mathematics.Math;

[BurstCompile, UpdateInGroup(typeof(AfterPhysicsSystemGroup), OrderLast = true)]
public partial struct SuctionSystem : ISystem
{
    private struct Clear
    {
        public NativeArray<SuctionTargetVelocity> targetVelocities;

        public NativeArray<PhysicsVelocity> physicsVelocities;

        public NativeArray<PhysicsMass> physicsMasses;

        public NativeArray<KinematicCharacterBody> characterBodies;

        public void Execute(int index)
        {
            var targetVelocity = targetVelocities[index];
            if (index < characterBodies.Length)
            {
                physicsVelocities[index] = default;

                if(physicsMasses.IsCreated)
                    physicsMasses[index] = PhysicsMass.CreateKinematic(MassProperties.UnitSphere);
                
                var characterBody = characterBodies[index];
                characterBody.RelativeVelocity = targetVelocity.tangent;
                characterBodies[index] = characterBody;
            }
            else
            {
                //var physicsVelocity = physicsVelocities[index];
                PhysicsVelocity physicsVelocity;
                physicsVelocity.Linear = targetVelocity.tangent;
                physicsVelocity.Angular = float3.zero;
                physicsVelocities[index] = physicsVelocity;
            }

            targetVelocities[index] = default;
        }
    }

    [BurstCompile]
    private struct ClearEx : IJobChunk
    {
        public ComponentTypeHandle<SuctionTargetVelocity> targetVelocityType;

        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;

        public ComponentTypeHandle<PhysicsMass> physicsMassType;

        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Clear clear;
            clear.targetVelocities = chunk.GetNativeArray(ref targetVelocityType);
            clear.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);
            clear.physicsMasses = chunk.GetNativeArray(ref physicsMassType);
            clear.characterBodies = chunk.GetNativeArray(ref characterBodyType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                clear.Execute(i);
                
                chunk.SetComponentEnabled(ref targetVelocityType, i, false);
                
                if(i < clear.characterBodies.Length)
                    chunk.SetComponentEnabled(ref characterBodyType, i, true);
            }
        }
    }
    
    private struct Collect
    {
        public float deltaTime;
        
        [ReadOnly]
        public BufferLookup<SimulationEvent> simulationEvents;

        [ReadOnly]
        public NativeArray<Suction> instances;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SuctionTargetVelocity> velocities;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var simulationEvents = GetEvents(entity);
            if (simulationEvents.IsCreated)
            {
                var instance = this.instances[index];

                var localToWorld = GetLocalToWorld(entity);
                RigidTransform transform;
                transform.pos = math.transform(localToWorld, instance.center);
                
                LocalTransform targetLocalTransform;
                quaternion rotation;
                float3x3 matrix;
                float3 velocity, tangentVelocity;
                float /*maxDistanceSQ = instance.maxDistance * instance.maxDistance, */distance, springSpeed, angle;
                foreach (var simulationEvent in simulationEvents)
                {
                    if (!localTransforms.TryGetComponent(simulationEvent.entity, out targetLocalTransform))
                        continue;

                    velocity = transform.pos - targetLocalTransform.Position;
                    velocity.y = 0.0f;
                    distance = math.lengthsq(velocity);
                    //if (distance > maxDistanceSQ)
                    //    continue;

                    if (distance > math.FLT_MIN_NORMAL)
                    {
                        if (!velocities.HasComponent(simulationEvent.entity))
                            continue;

                        ref var targetVelocity = ref velocities.GetRefRW(simulationEvent.entity).ValueRW;

                        distance = math.rsqrt(distance);
                        velocity *= distance;
                        
                        matrix.c0 = velocity;
                        matrix.c1 = math.up();
                        matrix.c2 = math.cross(matrix.c0, matrix.c1);

                        tangentVelocity = math.mul(matrix, instance.tangentSpeed);
                        Math.InterlockedAdd(ref targetVelocity.tangent, tangentVelocity);
                        
                        Math.Swap(ref matrix.c0, ref matrix.c2);
                        matrix.c0 = -matrix.c0;
                        transform.rot = math.quaternion(matrix);
                        UnityEngine.Assertions.Assert.AreApproximatelyEqual(velocity.x, math.forward(transform.rot).x);
                        UnityEngine.Assertions.Assert.AreApproximatelyEqual(velocity.y, math.forward(transform.rot).y);
                        UnityEngine.Assertions.Assert.AreApproximatelyEqual(velocity.z, math.forward(transform.rot).z);

                        springSpeed = instance.minDistance * distance;
                        if (springSpeed > math.FLT_MIN_NORMAL && springSpeed > 1.0f)
                        {
                            springSpeed = instance.minDistance * (1.0f - math.rcp(springSpeed)) / deltaTime;
                            velocity *= -math.min(instance.linearSpeed, springSpeed);
                            
                            distance = 1.0f;
                        }
                        else
                        {
                            velocity *= math.min(instance.linearSpeed, (math.rcp(distance) - instance.minDistance) / deltaTime);

                            if (instance.maxDistance > math.FLT_MIN_NORMAL)
                            {
                                distance *= instance.maxDistance;
                                distance = 1.0f - math.rcp(distance);
                            }
                            else
                                distance = 1.0f;
                        }

                        if (distance > math.FLT_MIN_NORMAL)
                        {
                            //velocity -= math.projectsafe(velocity, tangentVelocity);
                            
                            Math.InterlockedAdd(ref targetVelocity.linear, velocity * distance);

                            angle = math.abs(math.angle(transform.rot, targetLocalTransform.Rotation));
                            if (angle > math.FLT_MIN_NORMAL)
                            {
                                angle = math.min(angle, instance.angularSpeed * deltaTime) / angle;

                                rotation = math.slerp(targetLocalTransform.Rotation,  transform.rot, angle);

                                rotation = MathUtilities.FromToRotation(targetLocalTransform.Rotation, rotation);
                            }
                            else
                                rotation = quaternion.identity;

                            Math.InterlockedAdd(ref targetVelocity.angular,
                                distance / deltaTime * Math.ToAngular(rotation));
                        }

                        velocities.SetComponentEnabled(simulationEvent.entity, true);
                    }
                    //velocity += suction.GetVelocity(targetLocalToWorld.Position, position, deltaTime);
                }
            }
        }

        public float4x4 GetLocalToWorld(in Entity entity)
        {
            if (!localTransforms.TryGetComponent(entity, out var localTransform))
                return float4x4.identity;

            var matrix = localTransform.ToMatrix();
            if (parents.TryGetComponent(entity, out var parent))
                matrix = math.mul(GetLocalToWorld(parent.Value), matrix);

            return matrix;
        }

        public DynamicBuffer<SimulationEvent> GetEvents(in Entity entity)
        {
            if (simulationEvents.TryGetBuffer(entity, out var results))
                return results;

            if (parents.TryGetComponent(entity, out var parent))
                return GetEvents(parent.Value);

            return default;
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public float deltaTime;
        
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferLookup<SimulationEvent> simulationEvents;

        [ReadOnly]
        public ComponentTypeHandle<Suction> instanceType;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SuctionTargetVelocity> velocities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.deltaTime = deltaTime;
            collect.simulationEvents = simulationEvents;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.parents = parents;
            collect.localTransforms = localTransforms;
            collect.velocities = velocities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }
    
    private struct Apply
    {
        public NativeArray<SuctionTargetVelocity> targetVelocities;

        public NativeArray<PhysicsVelocity> physicsVelocities;

        //public NativeArray<KinematicCharacterBody> characterBodies;

        public NativeArray<PhysicsMass> physicsMasses;

        public NativeArray<KinematicCharacterProperties> characterProperties;

        public void Execute(int index)
        {
            var physicsVelocity = physicsVelocities[index];
            var targetVelocity = targetVelocities[index];
            if (index < characterProperties.Length)
            {
                var characterProperties = this.characterProperties[index];
                var physicsMass = PhysicsUtilities.GetKinematicCharacterPhysicsMass(characterProperties);
                if(!characterProperties.SimulateDynamicBody)
                    physicsMass.InverseMass = 1.0f / characterProperties.Mass;
                
                physicsMasses[index] = physicsMass;
            }
            
            physicsVelocity.Linear = targetVelocity.linear + targetVelocity.tangent;
            physicsVelocity.Angular = targetVelocity.angular;
            physicsVelocities[index] = physicsVelocity;
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public ComponentTypeHandle<SuctionTargetVelocity> targetVelocityType;

        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;

        public ComponentTypeHandle<PhysicsMass> physicsMassType;

        public ComponentTypeHandle<KinematicCharacterProperties> characterPropertiesType;

        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.targetVelocities = chunk.GetNativeArray(ref targetVelocityType);
            apply.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);
            apply.physicsMasses = chunk.GetNativeArray(ref physicsMassType);
            apply.characterProperties = chunk.GetNativeArray(ref characterPropertiesType);

            bool isCharacter = chunk.Has(ref characterBodyType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                apply.Execute(i);
                
                if(isCharacter)
                    chunk.SetComponentEnabled(ref characterBodyType, i, false);
            }
        }
    }

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<PhysicsMass> __physicsMassType;

    private ComponentTypeHandle<KinematicCharacterProperties> __characterPropertiesType;

    private ComponentTypeHandle<KinematicCharacterBody> __characterBodyType;

    private ComponentTypeHandle<Suction> __instanceType;

    private ComponentTypeHandle<SuctionTargetVelocity> __targetVelocityType;

    private ComponentTypeHandle<PhysicsVelocity> __physicsVelocityType;

    private ComponentLookup<Parent> __parents;

    private ComponentLookup<LocalTransform> __localTransforms;

    private ComponentLookup<SuctionTargetVelocity> __velocities;

    private BufferLookup<SimulationEvent> __simulationEvents;

    private EntityQuery __targetGroup;
    private EntityQuery __instanceGroup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __physicsMassType = state.GetComponentTypeHandle<PhysicsMass>();
        __characterPropertiesType = state.GetComponentTypeHandle<KinematicCharacterProperties>();
        __characterBodyType = state.GetComponentTypeHandle<KinematicCharacterBody>();
        __instanceType = state.GetComponentTypeHandle<Suction>(true);
        __targetVelocityType = state.GetComponentTypeHandle<SuctionTargetVelocity>();
        __physicsVelocityType = state.GetComponentTypeHandle<PhysicsVelocity>();
        __parents = state.GetComponentLookup<Parent>(true);
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __velocities = state.GetComponentLookup<SuctionTargetVelocity>();
        __simulationEvents = state.GetBufferLookup<SimulationEvent>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __targetGroup = builder
                .WithAllRW<SuctionTargetVelocity, PhysicsVelocity>()
                .Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __instanceGroup = builder
                .WithAll<Suction, LocalTransform>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __targetVelocityType.Update(ref state);
        __physicsVelocityType.Update(ref state);
        __physicsMassType.Update(ref state);
        __characterBodyType.Update(ref state);
        
        ClearEx clear;
        clear.targetVelocityType = __targetVelocityType;
        clear.physicsVelocityType = __physicsVelocityType;
        clear.physicsMassType = __physicsMassType;
        clear.characterBodyType = __characterBodyType;
        var jobHandle = clear.ScheduleParallelByRef(__targetGroup, state.Dependency);
        
        __entityType.Update(ref state);
        __instanceType.Update(ref state);
        __parents.Update(ref state);
        __localTransforms.Update(ref state);
        __velocities.Update(ref state);
        __simulationEvents.Update(ref state);

        CollectEx collect;
        collect.deltaTime = SystemAPI.Time.DeltaTime;
        collect.entityType = __entityType;
        collect.simulationEvents = __simulationEvents;
        collect.instanceType = __instanceType;
        collect.parents = __parents;
        collect.localTransforms = __localTransforms;
        collect.velocities = __velocities;
        jobHandle = collect.ScheduleParallelByRef(__instanceGroup, jobHandle);

        __characterPropertiesType.Update(ref state);
        
        ApplyEx apply;
        apply.targetVelocityType = __targetVelocityType;
        apply.physicsVelocityType = __physicsVelocityType;
        apply.physicsMassType = __physicsMassType;
        apply.characterPropertiesType = __characterPropertiesType;
        apply.characterBodyType = __characterBodyType;
        state.Dependency = apply.ScheduleParallelByRef(__targetGroup, jobHandle);
    }
}
