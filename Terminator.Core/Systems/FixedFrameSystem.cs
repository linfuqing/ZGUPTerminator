using Unity.Burst;
using Unity.Entities;
using Unity.Physics;

public struct FixedFrame : IComponentData
{
    public int count;

    public float deltaTime;
    public double elapsedTime;
}

[BurstCompile, UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct FixedFrameSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton<FixedFrame>();
        
        state.RequireForUpdate<FixedFrame>();

        var physicsStep = PhysicsStep.Default;
        physicsStep.SolverIterationCount = 1;
        physicsStep.SolverStabilizationHeuristicSettings = default;
        state.EntityManager.CreateSingleton(physicsStep);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var fixedFrame = SystemAPI.GetSingleton<FixedFrame>();
        ++fixedFrame.count;

        fixedFrame.deltaTime = SystemAPI.Time.DeltaTime;
        fixedFrame.elapsedTime = SystemAPI.Time.ElapsedTime;
        
        SystemAPI.SetSingleton(fixedFrame);
    }
}
