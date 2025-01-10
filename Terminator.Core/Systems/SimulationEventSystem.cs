using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

[BurstCompile, UpdateInGroup(typeof(PhysicsSystemGroup)), UpdateAfter(typeof(PhysicsSimulationGroup))]
public partial struct SimulationEventSystem : ISystem
{
    [BurstCompile]
    private struct Clear : IJobChunk
    {
        public BufferTypeHandle<SimulationEvent> instanceType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var simulationEvents = chunk.GetBufferAccessor(ref instanceType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                simulationEvents[i].Clear();

                chunk.SetComponentEnabled(ref instanceType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct CollectStatic
    {
        private struct Collector<T> : ICollector<T> where T : struct, IQueryResult
        {
            //private int __dynamicBodyCount;
            private DynamicBuffer<SimulationEvent> __instances;
            
            public bool EarlyOutOnFirstHit => false;

            public int NumHits
            {
                get;

                private set;
            }

            public float MaxFraction
            {
                get;
            }

            public T closestHit
            {
                get;

                private set;
            }

            public Collector(/*int dynamicBodyCount, */float maxFraction, ref DynamicBuffer<SimulationEvent> instances)
            {
                //__dynamicBodyCount = dynamicBodyCount;
                __instances = instances;

                NumHits = 0;
                MaxFraction = maxFraction;
                closestHit = default;
            }

            public bool AddHit(T hit)
            {
                if (closestHit.Entity == Entity.Null || closestHit.Fraction > hit.Fraction)
                    closestHit = hit;
                
                /*if (hit.RigidBodyIndex < __dynamicBodyCount)
                    return false;*/

                SimulationEvent instance;
                instance.entity = hit.Entity;
                instance.colliderKey = hit.ColliderKey;
                if (SimulationEvent.AppendOrReplace(ref __instances, instance))
                {
                    ++NumHits;
                    
                    return true;
                }

                return false;
            }
        }

        public bool isCollision;

        [ReadOnly] 
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public NativeArray<Entity> entityArray; 
        
        public NativeArray<SimulationCollision> collisions;

        public BufferAccessor<SimulationEvent> instances;

        public bool Execute(int index)
        {
            int rigidBodyIndex = collisionWorld.GetRigidBodyIndex(entityArray[index]);
            if (rigidBodyIndex == -1)
                return false;
            
            var body = collisionWorld.Bodies[rigidBodyIndex];
            var localToWorld = GetLocalToWorld(body.Entity);
            var transform = math.RigidTransform(localToWorld);

            var instances = this.instances[index];
            bool result;
            if (isCollision)
            {
                var collision = collisions[index];
                var collector = new Collector<ColliderCastHit>(//collisionWorld.NumDynamicBodies,
                    1.0f, ref instances);
                result = collisionWorld.CastCollider(
                    new ColliderCastInput(body.Collider, collision.position, transform.pos, transform.rot),
                    ref collector);

                if (collector.closestHit.Entity != collision.closestHit.Entity)
                    collision.closestHit = collector.closestHit;
                
                collision.position = transform.pos;
                collisions[index] = collision;
            }
            else
            {
                var collector = new Collector<DistanceHit>(//collisionWorld.NumDynamicBodies,
                    collisionWorld.CollisionTolerance, ref instances);
                result = collisionWorld.CalculateDistance(
                    new ColliderDistanceInput(body.Collider, collisionWorld.CollisionTolerance, transform),
                    ref collector);

                if (index < collisions.Length && collisions[index].closestHit.Entity == Entity.Null)
                {
                    SimulationCollision collision;
                    collision.position = transform.pos;
                    collision.closestHit = default;
                    collisions[index] = collision;
                }
            }

            return result;
        }

        public float4x4 GetLocalToWorld(in Entity entity)
        {
            var local = localTransforms.TryGetComponent(entity, out var localTransform) ? localTransform.ToMatrix() : float4x4.identity;
            if (parents.TryGetComponent(entity, out var parent))
                return math.mul(GetLocalToWorld(parent.Value), local);

            return local;
        }
    }

    [BurstCompile]
    private struct CollectStaticEx : IJobChunk
    {
        [ReadOnly] 
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public EntityTypeHandle entityType; 
        
        public ComponentTypeHandle<SimulationCollision> collisionType;

        public BufferTypeHandle<SimulationEvent> instanceType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CollectStatic collectStatic;
            collectStatic.collisionWorld = collisionWorld;
            collectStatic.localTransforms = localTransforms;
            collectStatic.parents = parents;
            collectStatic.entityArray = chunk.GetNativeArray(entityType);
            collectStatic.collisions = chunk.GetNativeArray(ref collisionType);
            collectStatic.instances = chunk.GetBufferAccessor(ref instanceType);

            int numCollisions = collectStatic.collisions.Length;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                collectStatic.isCollision = i < numCollisions &&
                                            chunk.IsComponentEnabled(ref collisionType, i);
                if (collectStatic.Execute(i))
                {
                    chunk.SetComponentEnabled(ref instanceType, i, true);

                    //if(collectStatic.isCollision)
                    //    chunk.SetComponentEnabled(ref collisionType, i, false);
                }
                else if (!collectStatic.isCollision && i < numCollisions)
                    chunk.SetComponentEnabled(ref collisionType, i, true);
            }
        }
    }

    [BurstCompile]
    private struct CollectTriggers : ITriggerEventsJob
    {
        public BufferLookup<SimulationEvent> instances;

        public void Execute(TriggerEvent triggerEvent)
        {
            if (this.instances.TryGetBuffer(triggerEvent.EntityA, out var instances))
            {
                SimulationEvent simulationEvent;
                simulationEvent.entity = triggerEvent.EntityB;
                //simulationEvent.bodyIndex = triggerEvent.BodyIndexB;
                simulationEvent.colliderKey = triggerEvent.ColliderKeyB;

                SimulationEvent.Append(instances, simulationEvent);
                this.instances.SetBufferEnabled(triggerEvent.EntityA, true);
            }
            
            if (this.instances.TryGetBuffer(triggerEvent.EntityB, out instances))
            {
                SimulationEvent simulationEvent;
                simulationEvent.entity = triggerEvent.EntityA;
                //simulationEvent.bodyIndex = triggerEvent.BodyIndexA;
                simulationEvent.colliderKey = triggerEvent.ColliderKeyA;
                
                SimulationEvent.Append(instances, simulationEvent);
                this.instances.SetBufferEnabled(triggerEvent.EntityB, true);
            }
        }
    }
    
    
    [BurstCompile]
    private struct CollectCollisions : ICollisionEventsJob
    {
        public BufferLookup<SimulationEvent> instances;

        public void Execute(CollisionEvent collisionEvent)
        {
            if (this.instances.TryGetBuffer(collisionEvent.EntityA, out var simulationEvents))
            {
                SimulationEvent simulationEvent;
                simulationEvent.entity = collisionEvent.EntityB;
                //simulationEvent.bodyIndex = collisionEvent.BodyIndexB;
                simulationEvent.colliderKey = collisionEvent.ColliderKeyB;
                
                SimulationEvent.Append(simulationEvents, simulationEvent);
                this.instances.SetBufferEnabled(collisionEvent.EntityA, true);
            }
            
            if (this.instances.TryGetBuffer(collisionEvent.EntityB, out simulationEvents))
            {
                SimulationEvent simulationEvent;
                simulationEvent.entity = collisionEvent.EntityA;
                //simulationEvent.bodyIndex = collisionEvent.BodyIndexA;
                simulationEvent.colliderKey = collisionEvent.ColliderKeyA;
                
                SimulationEvent.Append(simulationEvents, simulationEvent);
                this.instances.SetBufferEnabled(collisionEvent.EntityB, true);
            }
        }
    }

    private EntityTypeHandle __entityType;
    private ComponentLookup<LocalTransform> __localTransforms;
    private ComponentLookup<Parent> __parents;
    private ComponentTypeHandle<SimulationCollision> __collisionType;
    private BufferTypeHandle<SimulationEvent> __instanceType;
    private BufferLookup<SimulationEvent> __instances;
    private EntityQuery __eventGroup;
    private EntityQuery __staticGroup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __parents = state.GetComponentLookup<Parent>(true);
        __collisionType = state.GetComponentTypeHandle<SimulationCollision>();
        __instanceType = state.GetBufferTypeHandle<SimulationEvent>();
        __instances = state.GetBufferLookup<SimulationEvent>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __eventGroup = builder
                .WithAllRW<SimulationEvent>()
                .Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __staticGroup = builder
                .WithPresentRW<SimulationEvent>()
                .WithAll<PhysicsCollider>()
                .WithNone<PhysicsVelocity>()
                .AddAdditionalQuery()
                .WithPresentRW<SimulationEvent, SimulationCollision>()
                .WithAll<PhysicsCollider>()
                .Build(ref state);
        
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        __instanceType.Update(ref state);
        
        Clear clear;
        clear.instanceType = __instanceType;
        var jobHandle = clear.ScheduleParallelByRef(__eventGroup, state.Dependency);
        
        __localTransforms.Update(ref state);
        __parents.Update(ref state);
        __entityType.Update(ref state);
        __collisionType.Update(ref state);
        
        CollectStaticEx collectStatic;
        collectStatic.collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        collectStatic.localTransforms = __localTransforms;
        collectStatic.parents = __parents;
        collectStatic.entityType = __entityType;
        collectStatic.collisionType = __collisionType;
        collectStatic.instanceType = __instanceType;
        jobHandle = collectStatic.ScheduleParallelByRef(__staticGroup, jobHandle);

        __instances.Update(ref state);

        var simulation = SystemAPI.GetSingleton<SimulationSingleton>();

        CollectTriggers collectTriggers;
        collectTriggers.instances = __instances;
        jobHandle = collectTriggers.Schedule(simulation, jobHandle);

        CollectCollisions collectCollisions;
        collectCollisions.instances = __instances;
        state.Dependency = collectCollisions.Schedule(simulation, jobHandle);
    }
}
