using Unity.Entities;
using ZG;

/// <summary>
/// PopEvents may briefly reconnect and run sendBuffer.Apply (resets EndWrite index). Shutdown again after NetworkClient.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(NetworkClientSystem))]
[UpdateBefore(typeof(LevelRecordingAfterNetworkCoopRepairSystem))]
public partial struct LevelRecordingNetworkSuppressAfterClientSystem : ISystem
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
