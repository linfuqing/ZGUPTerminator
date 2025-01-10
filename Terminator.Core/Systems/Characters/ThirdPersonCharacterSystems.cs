using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.CharacterController;

[UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup))]
[BurstCompile]
public partial struct ThirdPersonCharacterPhysicsUpdateSystem : ISystem
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
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _context.OnSystemUpdate(ref state);
        _baseContext.OnSystemUpdate(ref state, SystemAPI.Time, SystemAPI.GetSingleton<PhysicsWorldSingleton>());
        
        ThirdPersonCharacterPhysicsUpdateJob job = new ThirdPersonCharacterPhysicsUpdateJob
        {
            Context = _context,
            BaseContext = _baseContext,
        };
        job.ScheduleParallelByRef(_characterQuery);
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    public partial struct ThirdPersonCharacterPhysicsUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
    {
        public ThirdPersonCharacterUpdateContext Context;
        public KinematicCharacterUpdateContext BaseContext;

        private ArchetypeChunk __chunk;

        void Execute(
            [EntityIndexInQuery] int entityIndexInQuery, 
            ThirdPersonCharacterAspect characterAspect, 
            ref DynamicBuffer<SimulationEvent> simulationEvents)
        {
            int numSimulationEvents = simulationEvents.IsCreated ? simulationEvents.Length : 0;
            characterAspect.PhysicsUpdate(ref Context, ref BaseContext, ref simulationEvents);
            if (numSimulationEvents == 0 && simulationEvents.IsCreated && simulationEvents.Length > 0)
                __chunk.SetComponentEnabled(ref Context.SimulationEventType, entityIndexInQuery, true);
        }

        public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            __chunk = chunk;
            
            BaseContext.EnsureCreationOfTmpCollections();
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
