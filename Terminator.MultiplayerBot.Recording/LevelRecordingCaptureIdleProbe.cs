using ZG;

/// <summary>
/// Logs when committed EndWrites remain undrained or the recording journal faults.
/// </summary>
internal static class LevelRecordingCaptureIdleProbe
{
    private static float s_lastFrameTimestamp;
    private static float s_lastProbeWallTime;
    private static bool s_loggedInLevel;

    public static void Reset()
    {
        s_lastFrameTimestamp = 0f;
        s_lastProbeWallTime = 0f;
        s_loggedInLevel = false;
    }

    public static void NotifyJournalDrained()
    {
        // The live journal state is queried at probe time; no transport cursor is retained.
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

        if (!LevelRecordingSendBufferAccess.TryGetSendBuffer(out var sendBuffer))
        {
            BotReplayLog.Warn(
                "[RecordingAudit] captureIdle: ClientData.sendBuffer unavailable while recording.");
            return;
        }

        int outstanding = sendBuffer.capturedEndWriteCount;
        var fault = sendBuffer.endWriteCaptureFault;
        if (outstanding <= 0 && fault == NetworkClientSendBuffer.EndWriteCaptureFault.None)
        {
            return;
        }

        BotReplayLog.Warn(
            "[RecordingAudit] captureIdle " +
            $"wall={wallDurationSeconds:F3}s, sinceLastFrame={sinceLastFrame:F3}s, " +
            $"captureOutstanding={outstanding}, captureFault={fault}, " +
            $"replyGateOpen={LevelRecordingSessionAudit.IsReplyWriteGateOpen()}");
    }
}
