using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

[BurstCompile, UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
public partial struct PickableSystem : ISystem
{
    private struct Pick
    {
        public float deltaTime;
        
        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly] 
        public BufferAccessor<SimulationEvent> simulationEvents;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Pickable> instances;

        public NativeArray<PickableStatus> states;

        public NativeArray<PhysicsGravityFactor> physicsGravityFactors;

        public NativeArray<PhysicsVelocity> physicsVelocities;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PickableStatus.Value Execute(int index)
        {
            PickableStatus status;
            var simulationEvents = this.simulationEvents[index];
            status.entity = simulationEvents.Length > 0 ? simulationEvents[0].entity : Entity.Null;
            if (localTransforms.TryGetComponent(status.entity, out var destination))
            {
                if (index < physicsGravityFactors.Length)
                {
                    PhysicsGravityFactor physicsGravityFactor;
                    physicsGravityFactor.Value = 0.0f;
                    physicsGravityFactors[index] = physicsGravityFactor;
                }

                PhysicsVelocity physicsVelocity;
                physicsVelocity.Angular = float3.zero;

                var entity = entityArray[index];
                var source = localTransforms[entity];
                float3 distance = destination.Position - source.Position;
                float distancesq = math.lengthsq(distance),
                    speed = instances[index].speed,
                    length = speed * deltaTime; // * deltaTime;
                if (distancesq > length * length)
                {
                    status.value = PickableStatus.Value.Move;

                    physicsVelocity.Linear = distance * (speed * math.rsqrt(distancesq));
                }
                else
                {
                    status.value = PickableStatus.Value.Picked;

                    entityManager.DestroyEntity(0, entityArray[index]);

                    //source.Position = destination.Position;
                    physicsVelocity.Linear = float3.zero;
                }

                physicsVelocities[index] = physicsVelocity;
                //source.Rotation = quaternion.identity;
                //localTransforms[entity] = source;
            }
            else
            {
                status.value = PickableStatus.Value.None;
                
                if (index < physicsGravityFactors.Length)
                {
                    PhysicsGravityFactor physicsGravityFactor;
                    physicsGravityFactor.Value = 1.0f;
                    physicsGravityFactors[index] = physicsGravityFactor;
                }
            }

            states[index] = status;

            return status.value;
        }
    }

    [BurstCompile]
    private struct PickEx : IJobChunk
    {
        public float deltaTime;

        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [ReadOnly] 
        public BufferTypeHandle<SimulationEvent> simulationEventType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        public ComponentTypeHandle<Pickable> instanceType;

        public ComponentTypeHandle<PickableStatus> statusType;

        public ComponentTypeHandle<PhysicsGravityFactor> physicsGravityFactorType;

        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Pick pick;
            pick.deltaTime = deltaTime;
            pick.localTransforms = localTransforms;
            pick.simulationEvents = chunk.GetBufferAccessor(ref simulationEventType);
            pick.entityArray = chunk.GetNativeArray(entityType);
            pick.instances = chunk.GetNativeArray(ref instanceType);
            pick.states = chunk.GetNativeArray(ref statusType);
            pick.physicsGravityFactors = chunk.GetNativeArray(ref physicsGravityFactorType);
            pick.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);
            pick.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                switch (pick.Execute(i))
                {
                    case PickableStatus.Value.Move:
                        chunk.SetComponentEnabled(ref statusType, i, true);
                        break;
                    case PickableStatus.Value.Picked:
                        chunk.SetComponentEnabled(ref instanceType, i, false);
                        break;
                    default:
                        chunk.SetComponentEnabled(ref statusType, i, false);
                        break;
                }
            }
        }
    }

    private ComponentLookup<LocalTransform> __localTransforms;

    private BufferTypeHandle<SimulationEvent> __simulationEventType;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Pickable> __instanceType;
    private ComponentTypeHandle<PickableStatus> __statusType;
    
    private ComponentTypeHandle<PhysicsGravityFactor> __physicsGravityFactorType;

    private ComponentTypeHandle<PhysicsVelocity> __physicsVelocityType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __simulationEventType = state.GetBufferTypeHandle<SimulationEvent>(true);
        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetComponentTypeHandle<Pickable>();
        __statusType = state.GetComponentTypeHandle<PickableStatus>();
        __physicsGravityFactorType = state.GetComponentTypeHandle<PhysicsGravityFactor>();
        __physicsVelocityType = state.GetComponentTypeHandle<PhysicsVelocity>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAllRW<LocalTransform, Pickable>()
                .WithAnyRW<PickableStatus>()
                .WithAny<SimulationEvent>()
                .Build(ref state);
        
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __localTransforms.Update(ref state);
        __simulationEventType.Update(ref state);
        __entityType.Update(ref state);
        __instanceType.Update(ref state);
        __statusType.Update(ref state);
        __physicsGravityFactorType.Update(ref state);
        __physicsVelocityType.Update(ref state);

        PickEx pick;
        pick.deltaTime = SystemAPI.Time.DeltaTime;
        pick.localTransforms = __localTransforms;
        pick.entityType = __entityType;
        pick.simulationEventType = __simulationEventType;
        pick.instanceType = __instanceType;
        pick.statusType = __statusType;
        pick.physicsGravityFactorType = __physicsGravityFactorType;
        pick.physicsVelocityType = __physicsVelocityType;
        pick.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        state.Dependency = pick.ScheduleParallelByRef(__group, state.Dependency);
    }
}
