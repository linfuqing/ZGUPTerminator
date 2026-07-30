using Unity.Entities;

/// <summary>
/// Publishes a high-precision producer timestamp before Simulation/Presentation writers run.
/// Completing the previous producer window prevents an old job from committing under a newer epoch.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(LevelRecordingEarlyCoopGateSystem))]
public partial struct LevelRecordingCaptureClockSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        state.EntityManager.CompleteAllTrackedJobs();
        LevelRecordingHarnessOps.TickRecordingCaptureClock();
    }
}
