using Unity.Entities;
using ZG;

/// <summary>
/// Drains EndWrites committed by <see cref="ReplyMessageSystem"/> after its producer jobs complete.
/// This is a latency boundary only; the recording journal is independent from transport Apply/Clear.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(ReplyMessageSystem))]
public partial struct LevelRecordingPostReplyCaptureSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        state.EntityManager.CompleteAllTrackedJobs();
        LevelRecordingHarnessOps.TickRecordingCaptureAfterReplyWrite();
    }
}
