using Unity.Entities;
using ZG;

/// <summary>
/// Replay inject + remote channel sync: after <see cref="NetworkClientSystem"/> PopEvents,
/// Recording capture: (1) before NetworkClient in <see cref="LevelRecordingCaptureSystem"/>,
/// (2) after ReplyMessage EndWrites in <see cref="LevelRecordingPostReplyCaptureSystem"/>.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(NetworkClientSystem))]
[UpdateBefore(typeof(ReplyMessageSystem))]
public partial struct LevelRecordingHarnessWireSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (RecordingCoopHarness.Instance == null)
        {
            return;
        }

        state.EntityManager.CompleteAllTrackedJobs();
        LevelRecordingHarnessOps.TickWireSyncAndReplay();
    }
}
