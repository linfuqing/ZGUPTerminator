using Unity.Entities;
using ZG;

/// <summary>
/// ReplyMessage Move EndWrites happen after <see cref="NetworkClientSystem"/>; capture again once jobs complete.
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
