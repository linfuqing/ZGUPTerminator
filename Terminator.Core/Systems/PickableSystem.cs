using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using ZG;

[BurstCompile, UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
public partial struct PickableSystem : ISystem
{
    private struct Pick
    {
        public float deltaTime;
        public double time;
        
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
        
        public NativeArray<DelayDestroy> delayDestroys;

        public BufferAccessor<Message> messages;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public PickableStatus.Value Execute(int index)
        {
            var simulationEvents = this.simulationEvents[index];
            var instance = instances[index];
            var status = states[index];
            if (status.time > math.DBL_MIN_NORMAL)
            {
                if (status.time > time)
                    return status.value;

                //deltaTime = (float)(time - status.time);

                status.time = time;
            }
            else if (simulationEvents.Length > 0)
            {
                status.entity = simulationEvents[0].entity;
                
                status.time = time + instance.startTime;
                if (status.time > time)
                {
                    if (!instance.startMessageName.IsEmpty && index < messages.Length)
                    {
                        Message message;
                        message.key = 0;
                        message.name = instance.startMessageName;
                        message.value = instance.startMessageValue;
                        messages[index].Add(message);
                    }

                    status.value = PickableStatus.Value.Start;
                    states[index] = status;

                    return status.value;
                }

                //deltaTime = 0.0f;
            } 

            if(!localTransforms.HasComponent(status.entity) && simulationEvents.Length > 0)
                status.entity = simulationEvents[0].entity;
            
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
                    speed = instance.speed,
                    length = speed * deltaTime; // * deltaTime;
                if (distancesq > length * length)
                {
                    status.value = PickableStatus.Value.Move;

                    physicsVelocity.Linear = distance * (speed * math.rsqrt(distancesq));
                }
                else
                {
                    status.value = PickableStatus.Value.Picked;

                    if (!instance.messageName.IsEmpty && index < messages.Length)
                    {
                        Message message;
                        message.key = 0;
                        message.name = instance.messageName;
                        message.value = instance.messageValue;
                        messages[index].Add(message);
                    }

                    if (instance.pickedUpTime > math.FLT_MIN_NORMAL)
                    {
                        DelayDestroy delayDestroy;
                        delayDestroy.time = instance.pickedUpTime;
                        if (index < delayDestroys.Length)
                            delayDestroys[index] = delayDestroy;
                        else
                            entityManager.AddComponent(0, entityArray[index], delayDestroy);
                    }
                    else
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
        public double time;

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

        public ComponentTypeHandle<DelayDestroy> delayDestroyType;

        public BufferTypeHandle<Message> messageType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Pick pick;
            pick.deltaTime = deltaTime;
            pick.time = time;
            pick.localTransforms = localTransforms;
            pick.simulationEvents = chunk.GetBufferAccessor(ref simulationEventType);
            pick.entityArray = chunk.GetNativeArray(entityType);
            pick.instances = chunk.GetNativeArray(ref instanceType);
            pick.states = chunk.GetNativeArray(ref statusType);
            pick.physicsGravityFactors = chunk.GetNativeArray(ref physicsGravityFactorType);
            pick.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);
            pick.delayDestroys = chunk.GetNativeArray(ref delayDestroyType);
            pick.messages = chunk.GetBufferAccessor(ref messageType);
            pick.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                switch (pick.Execute(i))
                {
                    case PickableStatus.Value.Start:
                        chunk.SetComponentEnabled(ref statusType, i, true);
                        
                        if(i < pick.messages.Length)
                            chunk.SetComponentEnabled(ref messageType, i, true);
                        break;
                    case PickableStatus.Value.Move:
                        chunk.SetComponentEnabled(ref statusType, i, true);
                        break;
                    case PickableStatus.Value.Picked:
                        chunk.SetComponentEnabled(ref instanceType, i, false);
                        
                        if(i < pick.messages.Length)
                            chunk.SetComponentEnabled(ref messageType, i, true);
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

    private ComponentTypeHandle<DelayDestroy> __delayDestroyType;

    private BufferTypeHandle<Message> __messageType;

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
        __delayDestroyType = state.GetComponentTypeHandle<DelayDestroy>();
        __messageType = state.GetBufferTypeHandle<Message>();
        
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
        __delayDestroyType.Update(ref state);
        __messageType.Update(ref state);

        PickEx pick;
        pick.deltaTime = SystemAPI.Time.DeltaTime;
        pick.time = SystemAPI.Time.ElapsedTime;
        pick.localTransforms = __localTransforms;
        pick.entityType = __entityType;
        pick.simulationEventType = __simulationEventType;
        pick.instanceType = __instanceType;
        pick.statusType = __statusType;
        pick.physicsGravityFactorType = __physicsGravityFactorType;
        pick.physicsVelocityType = __physicsVelocityType;
        pick.delayDestroyType = __delayDestroyType;
        pick.messageType = __messageType;
        pick.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        state.Dependency = pick.ScheduleParallelByRef(__group, state.Dependency);
    }
}
