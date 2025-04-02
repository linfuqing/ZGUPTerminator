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

[UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup))]
[BurstCompile]
public partial struct ThirdPersonCharacterPhysicsUpdateSystem : ISystem
{
    private EntityQuery _characterQuery;
    private ThirdPersonCharacterUpdateContext _context;
    private KinematicCharacterUpdateContext _baseContext;
    
    private NativeQueue<ThirdPersonCharacterSimulationEventResult> __simulationEventResults;


    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _characterQuery = KinematicCharacterUtilities.GetBaseCharacterQueryBuilder()
            .WithAll<
                ThirdPersonCharacterComponent,
                ThirdPersonCharacterControl>()
            .Build(ref state);

        _context = new ThirdPersonCharacterUpdateContext();
        _context.OnSystemCreate(ref state);
        _baseContext = new KinematicCharacterUpdateContext();
        _baseContext.OnSystemCreate(ref state);

        __simulationEventResults = new NativeQueue<ThirdPersonCharacterSimulationEventResult>(Allocator.Persistent);

        state.RequireForUpdate(_characterQuery);
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
        _context.OnSystemUpdate(ref state);
        _baseContext.OnSystemUpdate(ref state, SystemAPI.Time, SystemAPI.GetSingleton<PhysicsWorldSingleton>());
        
        ThirdPersonCharacterPhysicsUpdateJob job = new ThirdPersonCharacterPhysicsUpdateJob
        {
            Context = _context,
            BaseContext = _baseContext,
            simulationEventResults = __simulationEventResults.AsParallelWriter()
        };
        job.ScheduleParallelByRef(_characterQuery);

        Apply apply;
        apply.simulationEventResults = __simulationEventResults;
        apply.simulationEvents = _context.simulationEvents;
        state.Dependency = apply.ScheduleByRef(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    public partial struct ThirdPersonCharacterPhysicsUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
    {
        public ThirdPersonCharacterUpdateContext Context;
        public KinematicCharacterUpdateContext BaseContext;

        public NativeQueue<ThirdPersonCharacterSimulationEventResult>.ParallelWriter simulationEventResults;

        private ArchetypeChunk __chunk;
        [NativeDisableContainerSafetyRestriction]
        private BufferAccessor<SimulationEvent> __simulationEvents;

        void Execute([EntityIndexInQuery] int entityIndexInQuery, in Entity entity, ThirdPersonCharacterAspect characterAspect)
        {
            var simulationEvents = entityIndexInQuery < __simulationEvents.Length
                ? __simulationEvents[entityIndexInQuery]
                : default;
            int numSimulationEvents = simulationEvents.IsCreated ? simulationEvents.Length : 0;
            characterAspect.PhysicsUpdate(entity, ref Context, ref BaseContext, ref simulationEvents, ref simulationEventResults);
            if (numSimulationEvents == 0 && simulationEvents.IsCreated && simulationEvents.Length > 0)
                __chunk.SetComponentEnabled(ref Context.simulationEventType, entityIndexInQuery, true);
        }

        public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            __chunk = chunk;
            __simulationEvents = chunk.GetBufferAccessor(ref Context.simulationEventType);
            
            BaseContext.EnsureCreationOfTmpCollections();
            return true;
        }

        public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
        {
        }
    }
    
    [BurstCompile]
    private struct Apply : IJob
    {
        public NativeQueue<ThirdPersonCharacterSimulationEventResult> simulationEventResults;
        
        public BufferLookup<SimulationEvent> simulationEvents;

        public void Execute()
        {
            while (simulationEventResults.TryDequeue(out var result))
                simulationEvents[result.entity].Add(result.value);
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
    private EntityQuery _characterQuery;
    private ThirdPersonCharacterUpdateContext _context;
    private KinematicCharacterUpdateContext _baseContext;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _characterQuery = KinematicCharacterUtilities.GetBaseCharacterQueryBuilder()
            .WithAll<
                ThirdPersonCharacterComponent,
                ThirdPersonCharacterControl>()
            .Build(ref state);

        _context = new ThirdPersonCharacterUpdateContext();
        _context.OnSystemCreate(ref state);
        _baseContext = new KinematicCharacterUpdateContext();
        _baseContext.OnSystemCreate(ref state);
        
        state.RequireForUpdate(_characterQuery);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _context.OnSystemUpdate(ref state);
        _baseContext.OnSystemUpdate(ref state, SystemAPI.Time, SystemAPI.GetSingleton<PhysicsWorldSingleton>());
        
        ThirdPersonCharacterVariableUpdateJob job = new ThirdPersonCharacterVariableUpdateJob
        {
            Context = _context,
            BaseContext = _baseContext
        };
        job.ScheduleParallel(_characterQuery);
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
