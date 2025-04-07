using Unity.Burst;
using Unity.Entities;

public struct FixedFrame : IComponentData
{
    public int count;
    public double time;
}

[BurstCompile, UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct FixedFrameSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton<FixedFrame>();
        
        state.RequireForUpdate<FixedFrame>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var fixedFrame = SystemAPI.GetSingleton<FixedFrame>();
        ++fixedFrame.count;
        fixedFrame.time = SystemAPI.Time.ElapsedTime;
        
        SystemAPI.SetSingleton(fixedFrame);
    }
}
