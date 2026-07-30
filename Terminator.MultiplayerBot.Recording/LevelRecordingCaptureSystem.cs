using Unity.Entities;
using ZG;

/// <summary>
/// Drains the independent recording journal before the transport apply window.
/// Correctness does not depend on this running before <see cref="NetworkClientSystem"/>:
/// journal entries survive transport Apply/Clear and are consumed exactly once.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(NetworkClientSystem))]
public partial struct LevelRecordingCaptureSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        state.EntityManager.CompleteAllTrackedJobs();
        LevelRecordingHarnessOps.TickRecordingCaptureBeforeNetworkApply();
    }
}
