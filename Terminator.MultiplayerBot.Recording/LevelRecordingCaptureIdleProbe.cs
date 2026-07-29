using ZG;

/// <summary>
/// Logs when recording runs but sendBuffer capture goes quiet (pinpoints gate vs idle).
/// </summary>
internal static class LevelRecordingCaptureIdleProbe
{
    private static float s_lastFrameTimestamp;
    private static float s_lastProbeWallTime;
    private static bool s_loggedInLevel;
    private static int[] s_lastSlotReadCursors;

    public static void Reset()
    {
        s_lastFrameTimestamp = 0f;
        s_lastProbeWallTime = 0f;
        s_loggedInLevel = false;
        s_lastSlotReadCursors = null;
    }

    public static void NotifyCaptureCursor(int[] slotReadCursors)
    {
        s_lastSlotReadCursors = slotReadCursors;
    }

    public static void NotifyFrameCaptured(float frameTimestamp, float wallDurationSeconds)
    {
        s_lastFrameTimestamp = frameTimestamp;
        s_lastProbeWallTime = wallDurationSeconds;
    }

    public static void Tick(float wallDurationSeconds, bool isInLevel)
    {
        if (!isInLevel)
        {
            return;
        }

        if (!s_loggedInLevel)
        {
            s_loggedInLevel = true;
            s_lastProbeWallTime = wallDurationSeconds;
        }

        const float idleThresholdSeconds = 3f;
        float sinceLastFrame = wallDurationSeconds - s_lastFrameTimestamp;
        float sinceLastProbe = wallDurationSeconds - s_lastProbeWallTime;
        if (sinceLastFrame < idleThresholdSeconds || sinceLastProbe < idleThresholdSeconds)
        {
            return;
        }

        s_lastProbeWallTime = wallDurationSeconds;

        int pendingUncapturedSlots = LevelRecordingSendBufferAccess.CountUncapturedPendingSlots(s_lastSlotReadCursors);
        if (pendingUncapturedSlots <= 0)
        {
            return;
        }

        BotReplayLog.Warn(
            "[RecordingAudit] captureIdle " +
            $"wall={wallDurationSeconds:F3}s, sinceLastFrame={sinceLastFrame:F3}s, " +
            $"pendingUncapturedSlots={pendingUncapturedSlots}, " +
            $"replyGateOpen={LevelRecordingSessionAudit.IsReplyWriteGateOpen()}");
    }
}
