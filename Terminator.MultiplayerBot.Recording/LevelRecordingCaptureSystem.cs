using Unity.Entities;
using ZG;

/// <summary>
/// Capture sendBuffer EndWrites before <see cref="NetworkClientSystem"/> Apply clears pending payloads.
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
