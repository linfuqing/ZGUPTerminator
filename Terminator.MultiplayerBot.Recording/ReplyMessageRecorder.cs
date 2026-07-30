using System;
using Unity.Entities;
using UnityEngine;
using ZG;

public sealed class ReplyMessageRecorder : MonoBehaviour
{
    private uint __captureGeneration;
    private uint __captureEpoch;
    private NetworkSendBufferCapture.Cursor __captureCursor;
    private LevelRecordingSession __session;
    private LevelRecordingCaptureDiagnostics.CaptureStats __captureStats;
    private bool __loggedPlayerPropertyCapture;
    private bool __loggedCaptureFailure;

    public LevelRecordingSession Session => __session;
    public bool IsRecording => __session != null && __session.isActive;

    public bool BeginRecording()
    {
        __CompleteProducerJobs();
        LevelRecordingHarnessOps.ResetRecordingDiagnostics();
        LevelRecordingCaptureIdleProbe.Reset();
        __captureGeneration = 0;
        __captureEpoch = 0;
        __captureCursor = default;
        __captureStats = default;
        __loggedPlayerPropertyCapture = false;
        __loggedCaptureFailure = false;

        double startTime = Time.unscaledTimeAsDouble;
        __session = new LevelRecordingSession
        {
            isActive = true,
            startTime = startTime,
            header = BuildHeader()
        };

        var initialStamp = new NetworkClientSendBuffer.EndWriteCaptureStamp(
            epoch: 0,
            timestamp: 0.0);
        if (!LevelRecordingSendBufferAccess.TryBeginCapture(
                in initialStamp,
                out __captureGeneration,
                out string error))
        {
            __session.isActive = false;
            __session.captureError = "Could not start EndWrite capture: " + error;
            BotReplayLog.Warn("[RecordingDiag] " + __session.captureError);
            return false;
        }

        RecordingCoopHarness.NotifyRecordingStarted();
        BotReplayLog.Diag(
            $"Recording started; EndWrite capture generation={__captureGeneration}, " +
            LevelRecordingSendBufferAccess.DescribeCaptureJournal() + ".");
        return true;
    }

    public void EndRecording()
    {
        if (__session == null)
        {
            return;
        }

        bool wasActive = __session.isActive;
        __session.isActive = false;
        if (wasActive)
        {
            RecordingCoopHarness.NotifyRecordingEnded();
        }

        __CompleteProducerJobs();
        __CaptureEndOfFrameInternal(isFinalFlush: true);
        __EndCaptureJournalCleanly();

        float wallDuration = __CurrentElapsedTimestamp();
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
            in __captureStats);
        LevelRecordingCaptureDiagnostics.ReportFrameSummaryForSave(__session);
        __session.header.frameCount = __session.frames.Count;
    }

    public void AdvanceCaptureClock()
    {
        if (!IsRecording || __captureGeneration == 0)
        {
            return;
        }

        var stamp = new NetworkClientSendBuffer.EndWriteCaptureStamp(
            unchecked(++__captureEpoch),
            Math.Max(0.0, Time.unscaledTimeAsDouble - __session.startTime));
        if (!LevelRecordingSendBufferAccess.TrySetCaptureStamp(
                __captureGeneration,
                in stamp,
                out string error))
        {
            __FailCapture("Could not advance EndWrite capture clock: " + error);
        }
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
        float wallTimestamp = __CurrentElapsedTimestamp();
        bool isInLevel = LevelShared.unscaledDeltaTime > Mathf.Epsilon;
        LevelRecordingCaptureIdleProbe.Tick(wallTimestamp, isInLevel);

        if (__captureGeneration == 0)
        {
            __captureStats.captureFaults++;
            return;
        }

        if (!LevelRecordingSendBufferAccess.TryGetSendBuffer(out var sendBuffer))
        {
            __captureStats.skippedNoClientOrSendBuffer++;
            __FailCapture(
                isFinalFlush
                    ? "EndRecording flush failed: ClientData.sendBuffer unavailable."
                    : "ClientData.sendBuffer became unavailable during recording.");
            return;
        }

        __captureStats.everHadSendBuffer = true;
        int framesBefore = __session.frames.Count;
        if (!NetworkSendBufferCapture.TryCapture(
                in sendBuffer,
                ref __captureCursor,
                __session.frames,
                out int capturedNow,
                out string error))
        {
            __captureStats.captureFaults++;
            __FailCapture(error);
            return;
        }

        LevelRecordingCaptureIdleProbe.NotifyJournalDrained();
        if (capturedNow > 0)
        {
            __captureStats.framesCaptured += capturedNow;
            LevelRecordingCaptureIdleProbe.NotifyFrameCaptured(
                __session.frames[__session.frames.Count - 1].timestamp,
                wallTimestamp);
            __LogNewlyCapturedFrames(framesBefore, capturedNow);
            LevelRecordingHandshakeDiagnostics.OnNewFramesCaptured(
                __session.frames,
                framesBefore,
                capturedNow);
        }
    }

    private void __EndCaptureJournalCleanly()
    {
        if (__captureGeneration == 0)
        {
            return;
        }

        uint generation = __captureGeneration;
        __captureGeneration = 0;
        if (LevelRecordingSendBufferAccess.TryEndCapture(
                generation,
                discardUnread: false,
                out string error))
        {
            return;
        }

        __RecordCaptureFailure(error);
        if (!LevelRecordingSendBufferAccess.TryEndCapture(
                generation,
                discardUnread: true,
                out string discardError))
        {
            __RecordCaptureFailure(discardError);
        }
    }

    private void __FailCapture(string error)
    {
        __RecordCaptureFailure(error);
        if (__captureGeneration == 0)
        {
            return;
        }

        uint generation = __captureGeneration;
        __captureGeneration = 0;
        if (!LevelRecordingSendBufferAccess.TryEndCapture(
                generation,
                discardUnread: true,
                out string discardError))
        {
            __RecordCaptureFailure(discardError);
        }
    }

    private void __RecordCaptureFailure(string error)
    {
        if (string.IsNullOrEmpty(error))
        {
            error = "Unknown EndWrite capture failure.";
        }

        if (__session != null && string.IsNullOrEmpty(__session.captureError))
        {
            __session.captureError = error;
        }

        if (!__loggedCaptureFailure)
        {
            __loggedCaptureFailure = true;
            BotReplayLog.Warn("[RecordingDiag] EndWrite capture failed: " + error);
        }
    }

    private float __CurrentElapsedTimestamp()
    {
        if (__session == null)
        {
            return 0f;
        }

        double elapsed = Math.Max(0.0, Time.unscaledTimeAsDouble - __session.startTime);
        return elapsed >= float.MaxValue ? float.MaxValue : (float)elapsed;
    }

    private static void __CompleteProducerJobs()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            world.EntityManager.CompleteAllTrackedJobs();
        }
    }

    private void OnDestroy()
    {
        if (IsRecording)
        {
            __session.isActive = false;
            RecordingCoopHarness.NotifyRecordingEnded();
        }

        if (__captureGeneration != 0)
        {
            __CompleteProducerJobs();
            uint generation = __captureGeneration;
            __captureGeneration = 0;
            LevelRecordingSendBufferAccess.TryEndCapture(
                generation,
                discardUnread: true,
                out _);
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
                    "Captured PlayerProperty from EndWrite journal " +
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
