using System.Collections.Generic;
using Unity.Collections;
using ZG;

/// <summary>
/// One-shot login handshake probes for missing PlayerProperty (Info / [RecordingDiag] only).
/// After root cause is confirmed, review and remove speculative harness paths listed in bot-recording.mdc §复盘.
/// </summary>
internal static class LevelRecordingHandshakeDiagnostics
{
    private static bool s_loggedStartWindow;
    private static bool s_loggedStatusWithoutProperty;

    public static void Reset()
    {
        s_loggedStartWindow = false;
        s_loggedStatusWithoutProperty = false;
    }

    /// <summary>Call while recording; fires once when <see cref="LoginManager.Status.Start"/> is observed.</summary>
    public static void TickDuringRecording()
    {
        if (!RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        var loginManager = LoginManager.instance;
        if (loginManager == null || loginManager.status != LoginManager.Status.Start)
        {
            return;
        }

        if (s_loggedStartWindow || __SessionHasPlayerProperty())
        {
            return;
        }

        s_loggedStartWindow = true;
        BotReplayLog.Diag(
            "LoginManager.Status.Start (ApplyStage / __SubmitStage window); " +
            __BuildSnapshot("startWindow") +
            " " +
            __BuildSubmitStageGateSummary());
    }

    public static void OnNewFramesCaptured(IReadOnlyList<LevelRecordingFrame> frames, int startIndex, int count)
    {
        if (!RecordingCoopHarness.EcsRecordingActive || frames is not List<LevelRecordingFrame> mutableFrames || count < 1)
        {
            return;
        }

        if (__SessionHasPlayerProperty(mutableFrames))
        {
            return;
        }

        for (int i = startIndex; i < startIndex + count; ++i)
        {
            var payload = mutableFrames[i].payload;
            if (!__IsRelayStatusPayload(payload))
            {
                continue;
            }

            if (!s_loggedStatusWithoutProperty)
            {
                s_loggedStatusWithoutProperty = true;
                bool localPropertyEmpty = LevelRecordingReplayPropertyOps.IsPropertyEmpty(
                    in LevelPlayerShared<LocalPlayer>.property);
                int largePending = __CountLargePendingPayloads();
                BotReplayLog.Diag(
                    "Relay Status captured with no PlayerProperty yet " +
                    $"(frameIndex={i}, bytes={payload.Length}, t={mutableFrames[i].timestamp:F2}s); " +
                    __BuildSnapshot("afterSetStatus") +
                    " " +
                    __BuildSubmitStageGateSummary() +
                    " " +
                    __DescribeLoginManagerState() +
                    $", localPropertyEmpty={localPropertyEmpty}, " +
                    LevelRecordingSubmitStageRootCause.DescribeForHandshake(localPropertyEmpty, largePending));
            }

            return;
        }
    }

    private static int __CountLargePendingPayloads()
    {
        if (!LevelRecordingSendBufferAccess.TryGetSendBuffer(out var sendBuffer))
        {
            return 0;
        }

        int largePending = 0;
        int pendingSlots = sendBuffer.GetPendingSendSlotCount();
        for (int slot = 0; slot < pendingSlots; ++slot)
        {
            int readIndex = sendBuffer.GetPendingSendReadIndex(slot);
            NativeArray<byte> bytes;
            int probeIndex = readIndex;
            while (sendBuffer.TryReadPendingSend(slot, ref probeIndex, out bytes))
            {
                if (bytes.IsCreated && bytes.Length > 200)
                {
                    largePending++;
                }
            }
        }

        return largePending;
    }

    private static bool __SessionHasPlayerProperty(IReadOnlyList<LevelRecordingFrame> frames = null)
    {
        if (frames == null)
        {
            var harness = RecordingCoopHarness.Instance;
            if (harness == null || !harness.IsRecording)
            {
                return false;
            }

            var session = harness.Recorder?.Session;
            frames = session?.frames;
            if (frames == null)
            {
                return false;
            }
        }

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

    private static bool __IsRelayStatusPayload(byte[] payload)
    {
        if (payload == null || payload.Length < 2 || payload.Length > 8)
        {
            return false;
        }

        if (payload[0] == 0x8b)
        {
            return false;
        }

        if (LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out _))
        {
            return false;
        }

        return true;
    }

    private static string __BuildSnapshot(string phase)
    {
        var loginManager = LoginManager.instance;
        string loginStatus = loginManager != null ? loginManager.status.ToString() : "null";

        return
            $"phase={phase}, loginStatus={loginStatus}, " +
            $"remoteStatus={RemotePlayer.status}, remoteOnline={LevelPlayerShared<RemotePlayer>.isOnline}, " +
            $"remotePlayerCount={ReplyMessageShared.remotePlayerCount}, isHost={ReplyMessageShared.isHost}, " +
            $"ecsHarness={RecordingCoopHarness.EcsHarnessActive}, ecsRecording={RecordingCoopHarness.EcsRecordingActive}, " +
            $"harnessInstance={(RecordingCoopHarness.Instance != null)}, " +
            __DescribeSendBufferPeek();
    }

    private static string __BuildSubmitStageGateSummary()
    {
        bool remoteDisabled = RemotePlayer.status == RemotePlayer.Status.Disabled;
        bool clientMissing = IClientData.instance == null;
        bool coopCountAtStart = ReplyMessageShared.remotePlayerCount > 0;
        bool wouldSend = !remoteDisabled && !clientMissing;

        return
            "__SubmitStageGate: " +
            $"wouldSendPlayerProperty={wouldSend}, remoteDisabled={remoteDisabled}, clientDataMissing={clientMissing}, " +
            $"coopCountNow={ReplyMessageShared.remotePlayerCount} (LoginManager.__Start needs >0 or SinglePlay→Disabled)";
    }

    private static string __DescribeLoginManagerState()
    {
        var loginManager = LoginManager.instance;
        if (loginManager == null)
        {
            return "loginManager=null";
        }

        return
            $"loginSelectedUserStageID={loginManager.selectedUserStageID}, " +
            $"localChannelStatus={LevelPlayerShared<LocalPlayer>.channelStatus}, " +
            $"remoteChannelStatus={LevelPlayerShared<RemotePlayer>.channelStatus}";
    }

    private static string __DescribeSendBufferPeek()
    {
        if (!LevelRecordingSendBufferAccess.TryGetSendBuffer(out var sendBuffer))
        {
            return "sendBuffer=missing";
        }

        int pendingSlots = sendBuffer.GetPendingSendSlotCount();
        int largePending = 0;
        for (int slot = 0; slot < pendingSlots; ++slot)
        {
            int readIndex = sendBuffer.GetPendingSendReadIndex(slot);
            NativeArray<byte> bytes;
            int probeIndex = readIndex;
            while (sendBuffer.TryReadPendingSend(slot, ref probeIndex, out bytes))
            {
                if (bytes.IsCreated && bytes.Length > 200)
                {
                    largePending++;
                }
            }
        }

        return $"sendBufferSlots={pendingSlots}, largePendingPayloads={largePending}";
    }
}
