using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using ZG;

public sealed class ReplyMessageRecorder : MonoBehaviour
{
    private int[] __slotReadCursors;
    private LevelRecordingSession __session;
    private LevelRecordingCaptureDiagnostics.CaptureStats __captureStats;
    private bool __loggedPlayerPropertyCapture;

    public LevelRecordingSession Session => __session;
    public bool IsRecording => __session != null && __session.isActive;

    public void BeginRecording()
    {
        RecordingCoopHarness.NotifyRecordingStarted();
        LevelRecordingHarnessOps.ResetRecordingDiagnostics();
        LevelRecordingCaptureIdleProbe.Reset();
        __slotReadCursors = null;
        __captureStats = default;
        __loggedPlayerPropertyCapture = false;
        __session = new LevelRecordingSession
        {
            isActive = true,
            startTime = Time.unscaledTime,
            header = BuildHeader()
        };

        LevelRecordingSendBufferAccess.EstablishRecordingBaseline(
            ref __slotReadCursors,
            __session.frames,
            timestamp: 0f);

        BotReplayLog.Diag(
            $"Recording started; sendBuffer {LevelRecordingSendBufferAccess.DescribeSlotCursors(__slotReadCursors)}, " +
            $"frames={__session.frames.Count}.");
    }

    public void EndRecording()
    {
        if (__session == null)
        {
            return;
        }

        __session.isActive = false;
        RecordingCoopHarness.NotifyRecordingEnded();
        __CaptureEndOfFrameInternal(isFinalFlush: true);
        float wallDuration = Time.unscaledTime - __session.startTime;
        float lastFrameTimestamp = 0f;
        LevelRecordingTiming.TryGetLastFrameTimestamp(__session.frames, out lastFrameTimestamp);
        __session.header.duration = LevelRecordingTiming.ResolveHeaderDuration(__session.frames, wallDuration);
        __session.header.frameCount = __session.frames.Count;
        if (lastFrameTimestamp > 0f && wallDuration - lastFrameTimestamp > 2f)
        {
            BotReplayLog.Diag(
                "Session wall time " +
                $"{wallDuration:F1}s but last captured frame at {lastFrameTimestamp:F1}s " +
                $"(gap {wallDuration - lastFrameTimestamp:F1}s).");
        }

        var audit = LevelRecordingSessionAudit.Build(
            __session.frames,
            wallDuration,
            LevelRecordingSessionAudit.IsReplyWriteGateOpen(),
            LevelPlayerShared<LocalPlayer>.channelStatus,
            LevelPlayerShared<RemotePlayer>.channelStatus);
        LevelRecordingSessionAudit.LogReport(in audit, "EndRecording");
        LevelRecordingCaptureDiagnostics.ReportMissingPlayerProperty(
            __session,
            in __captureStats,
            __slotReadCursors);
        LevelRecordingCaptureDiagnostics.ReportFrameSummaryForSave(__session);
        __session.header.frameCount = __session.frames.Count;
    }

    public void CaptureEndOfFrame()
    {
        if (!IsRecording)
        {
            return;
        }

        __CaptureEndOfFrameInternal(isFinalFlush: false);
    }

    private void __CaptureEndOfFrameInternal(bool isFinalFlush)
    {
        __captureStats.endOfFrameAttempts++;

        if (!LevelRecordingSendBufferAccess.TryGetSendBuffer(out var sendBuffer))
        {
            __captureStats.skippedNoClientOrSendBuffer++;
            if (isFinalFlush)
            {
                BotReplayLog.Warn(
                    "[RecordingDiag] EndRecording flush skipped: ClientData.sendBuffer unavailable.");
            }

            return;
        }

        __captureStats.everHadSendBuffer = true;
        var timestamp = Time.unscaledTime - __session.startTime;
        bool isInLevel = LevelShared.unscaledDeltaTime > Mathf.Epsilon;
        LevelRecordingCaptureIdleProbe.Tick(timestamp, isInLevel);
        int framesBefore = __session.frames.Count;
        NetworkSendBufferCapture.TryCapture(
            sendBuffer,
            ref __slotReadCursors,
            __session.frames,
            timestamp);

        int capturedNow = __session.frames.Count - framesBefore;
        LevelRecordingCaptureIdleProbe.NotifyCaptureCursor(__slotReadCursors);
        if (capturedNow > 0)
        {
            __captureStats.framesCaptured += capturedNow;
            LevelRecordingCaptureIdleProbe.NotifyFrameCaptured(
                __session.frames[__session.frames.Count - 1].timestamp,
                timestamp);
            __LogNewlyCapturedFrames(framesBefore, capturedNow);
            LevelRecordingHandshakeDiagnostics.OnNewFramesCaptured(__session.frames, framesBefore, capturedNow);
        }
    }

    private void __LogNewlyCapturedFrames(int startIndex, int count)
    {
        for (int i = startIndex; i < startIndex + count; ++i)
        {
            var payload = __session.frames[i].payload;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType))
            {
                continue;
            }

            if (messageType == ReplyMessageType.PlayerProperty && !__loggedPlayerPropertyCapture)
            {
                __loggedPlayerPropertyCapture = true;
                LevelRecordingSubmitStageRootCause.NotifyNativePlayerPropertyCaptured();
                BotReplayLog.Diag(
                    "Captured PlayerProperty from sendBuffer " +
                    $"(frameIndex={i}, bytes={payload.Length}, t={__session.frames[i].timestamp:F2}s).");
            }
        }
    }

    private static LevelRecordingFileHeader BuildHeader()
    {
        var header = new LevelRecordingFileHeader();
        LoginManagerRecordingUtility.FillHeader(ref header);
        return header;
    }

    public void RefreshHeaderMetadata()
    {
        if (__session == null)
        {
            return;
        }

        LoginManagerRecordingUtility.FillHeader(ref __session.header);
    }
}
