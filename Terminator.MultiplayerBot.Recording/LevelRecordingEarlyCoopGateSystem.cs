using Unity.Entities;
using ZG;

/// <summary>
/// LoginManager.__Start reads remotePlayerCount at line ~1994 before ApplyStage.
/// Run coop gate repair as early as possible each frame (before MonoBehaviour Update).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
public partial struct LevelRecordingEarlyCoopGateSystem : ISystem
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
