using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.CharacterController;
using Unity.Collections.LowLevel.Unsafe;

[assembly:RegisterGenericJobType(typeof(BufferLookupBuffer<SimulationEvent>))]

[UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup))]
[BurstCompile]
public partial struct ThirdPersonCharacterPhysicsUpdateSystem : ISystem
{
    private EntityQuery __group;
    private BufferLookupBuffer<SimulationEvent> __simulationEventResults;
    private ThirdPersonCharacterUpdateContext __context;
    private KinematicCharacterUpdateContext __baseContext;
    
    //private NativeQueue<ThirdPersonCharacterSimulationEventResult> __simulationEventResults;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __group = KinematicCharacterUtilities.GetBaseCharacterQueryBuilder()
            .WithAll<
                ThirdPersonCharacterComponent,
                ThirdPersonCharacterControl>()
            .WithAllRW<ThirdPersonCharacterStandTime>()
            .Build(ref state);

        __simulationEventResults = new BufferLookupBuffer<SimulationEvent>(ref state, Allocator.Persistent);

        __context.OnSystemCreate(ref __simulationEventResults, ref state);
        __baseContext.OnSystemCreate(ref state);

        state.RequireForUpdate(__group);
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __simulationEventResults.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __context.OnSystemUpdate(ref state);
        __baseContext.OnSystemUpdate(ref state, SystemAPI.Time, SystemAPI.GetSingleton<PhysicsWorldSingleton>());

        ThirdPersonCharacterPhysicsUpdateJob job = new ThirdPersonCharacterPhysicsUpdateJob()
        {
            context = __context,
            baseContext = __baseContext//,
            //simulationEventResults = __simulationEventResults.AsParallelWriter()
        };
        job.ScheduleParallelByRef(__group);

        state.Dependency = __simulationEventResults.Schedule(ref state, state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    public partial struct ThirdPersonCharacterPhysicsUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
    {
        public ThirdPersonCharacterUpdateContext context;
        public KinematicCharacterUpdateContext baseContext;

        //public NativeQueue<ThirdPersonCharacterSimulationEventResult>.ParallelWriter simulationEventResults;

        void Execute(
            in Entity entity, 
            ThirdPersonCharacterAspect characterAspect)
        {
            characterAspect.PhysicsUpdate(entity, ref context, ref baseContext/*, ref simulationEventResults*/);
        }

        public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            baseContext.EnsureCreationOfTmpCollections();
            return true;
        }

        public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
        {
        }
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(ThirdPersonPlayerVariableStepControlSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
[BurstCompile]
public partial struct ThirdPersonCharacterVariableUpdateSystem : ISystem
{
    private EntityQuery __group;
    private BufferLookupBuffer<SimulationEvent> __simulationEventResults;
    private ThirdPersonCharacterUpdateContext __context;
    private KinematicCharacterUpdateContext __baseContext;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __group = KinematicCharacterUtilities.GetBaseCharacterQueryBuilder()
            .WithAll<
                ThirdPersonCharacterComponent,
                ThirdPersonCharacterControl>()
            .WithAllRW<ThirdPersonCharacterStandTime>()
            .Build(ref state);
        
        __simulationEventResults = new BufferLookupBuffer<SimulationEvent>(ref state, Allocator.Persistent);

        __context.OnSystemCreate(ref __simulationEventResults, ref state);
        __baseContext.OnSystemCreate(ref state);
        
        state.RequireForUpdate(__group);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __simulationEventResults.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __context.OnSystemUpdate(ref state);
        __baseContext.OnSystemUpdate(ref state, SystemAPI.Time, SystemAPI.GetSingleton<PhysicsWorldSingleton>());
        
        ThirdPersonCharacterVariableUpdateJob job = new ThirdPersonCharacterVariableUpdateJob
        {
            Context = __context,
            BaseContext = __baseContext
        };
        job.ScheduleParallel(__group);
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    public partial struct ThirdPersonCharacterVariableUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
    {
        public ThirdPersonCharacterUpdateContext Context;
        public KinematicCharacterUpdateContext BaseContext;
        
        void Execute(ThirdPersonCharacterAspect characterAspect)
        {
            characterAspect.VariableUpdate(ref Context, ref BaseContext);
        }

        public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            BaseContext.EnsureCreationOfTmpCollections();
            return true;
        }

        public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
        { }
    }
}
