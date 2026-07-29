using ZG;

internal static class LevelRecordingReplayBridge
{
    private static LevelRecordingReplayPlayer s_player;

    public static bool IsReplayLoaded => s_player != null && s_player.IsLoaded;

    public static bool IsWaitingForLevel => s_player != null && s_player.IsWaitingForLevel;

    public static LevelRecordingSession ReplaySession => s_player != null ? s_player.Session : null;

    public static void Register(LevelRecordingReplayPlayer player)
    {
        s_player = player;
    }

    public static void Unregister(LevelRecordingReplayPlayer player)
    {
        if (s_player == player)
            s_player = null;
    }

    public static void TryInjectDueFrames()
    {
        s_player?.InjectDueFramesForCurrentTime();
    }

    public static void EnsureRemotePlayerPropertyBeforeSpawn()
    {
        if (LevelRecordingReplayPropertyOps.ShouldApplyBeforeLevelPlayerSpawn(RemotePlayer.status))
            LevelRecordingReplayPropertyOps.TryApplySharedBeforeRemoteSpawn();
    }

    public static void TryReapplyPlayerPropertyBeforeSpawn()
    {
        EnsureRemotePlayerPropertyBeforeSpawn();
    }

    public static void TryBeginWhenLevelReady()
    {
        if (s_player == null || !s_player.IsWaitingForLevel)
            return;

        if (LevelRecordingReplayPlayer.IsLevelReadyForReplay())
            s_player.BeginWhenLevelReady();
    }
}
