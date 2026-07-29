using Unity.Entities;
using ZG;

/// <summary>
/// Keep ReplyServer socket down during harness sessions so Connect does not reset coop seed
/// and sendBuffer.Apply does not flush EndWrites before capture.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(LevelRecordingCaptureSystem))]
public partial struct LevelRecordingNetworkSuppressSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!RecordingCoopHarness.EcsHarnessActive)
        {
            return;
        }

        LevelRecordingHarnessOps.SuppressLiveNetworkDuringHarness();
    }
}
