using Unity.Entities;
using ZG;

/// <summary>
/// Reply Connect (PopEvents) resets remotePlayerCount inside <see cref="NetworkClientSystem"/>.
/// Repair coop gate after PopEvents (next-frame __Start / __SubmitStage gate).
/// Must NOT use UpdateBefore(<see cref="LevelRecordingCaptureSystem"/>): Capture runs Before NetworkClient.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(NetworkClientSystem))]
[UpdateBefore(typeof(LevelRecordingHarnessWireSystem))]
public partial struct LevelRecordingAfterNetworkCoopRepairSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        LevelRecordingHarnessOps.RepairCoopGateAfterNetworkPop();
    }
}
