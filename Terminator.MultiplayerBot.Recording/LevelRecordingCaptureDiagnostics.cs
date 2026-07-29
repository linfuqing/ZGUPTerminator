using System.Collections.Generic;
using ZG;

/// <summary>
/// EndRecording 诊断：PlayerProperty 缺失时不造假帧，输出可行动的根因。
/// </summary>
internal static class LevelRecordingCaptureDiagnostics
{
    internal struct CaptureStats
    {
        public int endOfFrameAttempts;
        public int skippedNoClientOrSendBuffer;
        public int framesCaptured;
        public bool everHadSendBuffer;
    }

    public static void ReportFrameSummaryForSave(LevelRecordingSession session)
    {
        if (session == null || session.frames == null)
        {
            return;
        }

        var summary = LevelRecordingReplayPropertyOps.DescribeFrameTypes(session);
        if (!__HasPlayerPropertyFrame(session.frames))
        {
            summary +=
                $". coopEnd: remoteStatus={RemotePlayer.status}, remotePlayerCount={ReplyMessageShared.remotePlayerCount}, " +
                $"isHost={ReplyMessageShared.isHost}, ecsHarness={RecordingCoopHarness.EcsHarnessActive}, " +
                $"ecsRecording={RecordingCoopHarness.EcsRecordingActive}";
        }

        BotReplayLog.Diag("Save frame summary: " + summary);
    }

    public static void ReportMissingPlayerProperty(
        LevelRecordingSession session,
        in CaptureStats stats,
        int[] slotReadCursors)
    {
        if (session == null || session.frames == null)
        {
            BotReplayLog.Warn("[RecordingDiag] No session to diagnose.");
            return;
        }

        if (__HasPlayerPropertyFrame(session.frames))
        {
            return;
        }

        var frameSummary = LevelRecordingReplayPropertyOps.DescribeFrameTypes(session);
        var pendingSummary = __DescribeUncapturedPending(slotReadCursors);
        var coopSummary = __DescribeCoopStateAtEnd();
        var likelyCause = __InferLikelyCause(in stats, coopSummary);

        BotReplayLog.Warn(
            "[RecordingDiag] Recording has no PlayerProperty frame. " +
            $"framesCaptured={stats.framesCaptured}, captureAttempts={stats.endOfFrameAttempts}, " +
            $"skippedNoSendBuffer={stats.skippedNoClientOrSendBuffer}, everHadSendBuffer={stats.everHadSendBuffer}. " +
            $"{frameSummary}. {pendingSummary}. {coopSummary}. " +
            $"Likely cause: {likelyCause}");
    }

    private static string __InferLikelyCause(
        in CaptureStats stats,
        string coopSummary)
    {
        if (!stats.everHadSendBuffer)
        {
            return "ClientData.sendBuffer never available during recording — ApplyStage EndWrite could not be captured.";
        }

        if (coopSummary.Contains("remoteStatus=Disabled"))
        {
            return "LoginManager.__SubmitStage skipped PlayerProperty (RemotePlayer was Disabled when ApplyStage ran). Ensure recording harness re-seeds remotePlayerCount after Reply Connect.";
        }

        if (stats.framesCaptured > 0 && stats.skippedNoClientOrSendBuffer > 0)
        {
            return "sendBuffer was intermittently unavailable — PlayerProperty EndWrite at ApplyStage may have landed during a skipped LateUpdate window.";
        }

        if (stats.framesCaptured > 0)
        {
            if (coopSummary.Contains("remoteStatus=Disabled"))
            {
                return "ApplyStage ran but ClientData.__Send did not EndWrite PlayerProperty (Remote was Disabled at __SubmitStage instant, or sendBuffer BeginWrite failed). Handshake recovery may have inserted wire from LocalPlayer.property.";
            }

            return "ClientData.__Send did not EndWrite PlayerProperty despite open coop gate at handshake probe — suspect sendBuffer BeginWrite failure after disconnected capture.";
        }

        return "No sendBuffer frames captured at all — recording may have ended before gameplay or capture never ran.";
    }

    private static string __DescribeUncapturedPending(int[] slotReadCursors)
    {
        if (!LevelRecordingSendBufferAccess.TryGetSendBuffer(out var sendBuffer))
        {
            return "sendBuffer=unavailable at EndRecording";
        }

        return LevelRecordingSendBufferAccess.DescribeSlotCursors(slotReadCursors) +
               $", pendingUncapturedSlots={LevelRecordingSendBufferAccess.CountUncapturedPendingSlots(slotReadCursors)}, " +
               $"sendBufferSlots={sendBuffer.GetPendingSendSlotCount()}";
    }

    private static string __DescribeCoopStateAtEnd()
    {
        return
            $"remoteStatus={RemotePlayer.status}, remoteOnline={LevelPlayerShared<RemotePlayer>.isOnline}, " +
            $"remotePlayerCount={ReplyMessageShared.remotePlayerCount}, isHost={ReplyMessageShared.isHost}";
    }

    private static bool __HasPlayerPropertyFrame(List<LevelRecordingFrame> frames)
    {
        for (int i = 0; i < frames.Count; ++i)
        {
            var payload = frames[i].payload;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            if (LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType) &&
                messageType == ReplyMessageType.PlayerProperty)
            {
                return true;
            }
        }

        return false;
    }
}
