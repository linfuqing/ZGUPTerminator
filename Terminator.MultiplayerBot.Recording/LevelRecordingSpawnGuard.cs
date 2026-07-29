using Unity.Entities;
using Unity.Mathematics;
using ZG;
/// <summary>
/// While level recording is active, coop mock sets <see cref="RemotePlayer.Status.Joined"/> so
/// LoginManager.__Start can pass its pre-load gate; demote to Disabled before
/// <see cref="LevelPlayerSystem"/> instantiates a visible remote prefab.
/// </summary>
internal static class LevelRecordingSpawnGuard
{
    /// <summary>
    /// Set when Joined→Disabled demotion runs before <see cref="LevelPlayerSystem"/>.
    /// Blocks harness from promoting back to Waiting (LevelPlayerSystem early-outs on Waiting+Online).
    /// </summary>
    internal static bool RemoteDemotedForLevelSpawn { get; private set; }

    public static void ResetSpawnDemotionState()
    {
        RemoteDemotedForLevelSpawn = false;
    }

    public static bool ShouldSuppressRemoteSpawn(bool suppressRemoteLevelSpawn, RemotePlayer.Status status)
    {
        return suppressRemoteLevelSpawn && status == RemotePlayer.Status.Joined;
    }

    public static bool TryDemoteJoinedForRecording()
    {
        if (RemotePlayer.status != RemotePlayer.Status.Joined)
        {
            return false;
        }

        if (!RemotePlayer.SetStatus(RemotePlayer.Status.Disabled))
        {
            return false;
        }

        RemoteDemotedForLevelSpawn = true;
        return true;
    }
}

/// <summary>
/// Replay must keep <see cref="RemotePlayer.Status.Joined"/> through <see cref="LevelPlayerSystem"/> spawn.
/// Only promote pre-spawn states (Disabled/Waiting). Never demote StandBy back to Joined.
/// </summary>
internal static class LevelRecordingReplaySpawnEnsure
{
    public static bool ShouldPromoteJoinedForReplay(RemotePlayer.Status status, bool replayLoaded, bool waitingForLevel)
    {
        if (!replayLoaded || !waitingForLevel)
            return false;

        if (status == RemotePlayer.Status.Joined)
            return false;

        if (status == RemotePlayer.Status.Canceled)
            return false;

        if (status == RemotePlayer.Status.StandBy)
            return false;

        return status == RemotePlayer.Status.Disabled ||
               status == RemotePlayer.Status.Waiting;
    }

    public static bool TryPromoteJoinedForReplay(RemotePlayer.Status status, bool replayLoaded, bool waitingForLevel)
    {
        if (!ShouldPromoteJoinedForReplay(status, replayLoaded, waitingForLevel))
            return false;

        return RemotePlayer.SetStatus(RemotePlayer.Status.Joined);
    }
}

// LocalSimulation: must run during Editor Play Mode (Editor filter matches Edit Mode world only).
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateBefore(typeof(LevelPlayerSystem))]
public partial struct LevelRecordingSpawnGuardSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (RecordingCoopHarness.SuppressRemoteLevelSpawn)
        {
            if (LevelRecordingSpawnGuard.ShouldSuppressRemoteSpawn(
                    RecordingCoopHarness.SuppressRemoteLevelSpawn,
                    RemotePlayer.status) &&
                LevelRecordingSpawnGuard.TryDemoteJoinedForRecording())
            {
                UnityEngine.Debug.Log(
                    "[LevelRecording] Demoted RemotePlayer Joined→Disabled before level spawn (recording-only mock).");
            }

            return;
        }

        if (!LevelRecordingReplayBridge.IsReplayLoaded || !LevelRecordingReplayBridge.IsWaitingForLevel)
            return;

        if (LevelRecordingReplayPlayer.HasSpawnedRemotePlayerEntity())
            return;

        var status = RemotePlayer.status;
        if (LevelRecordingReplaySpawnEnsure.TryPromoteJoinedForReplay(
                status,
                LevelRecordingReplayBridge.IsReplayLoaded,
                LevelRecordingReplayBridge.IsWaitingForLevel))
        {
            BotReplayLog.Diag(
                $"Promoted RemotePlayer {status}→Joined before level spawn (replay mock).");
            status = RemotePlayer.status;
        }

        // One-shot: write shared property on the same tick LevelPlayerSystem will spawn remote prefab.
        if (LevelRecordingReplayPropertyOps.ShouldApplyBeforeLevelPlayerSpawn(status))
            LevelRecordingReplayPropertyOps.TryApplySharedBeforeRemoteSpawn();
    }
}
