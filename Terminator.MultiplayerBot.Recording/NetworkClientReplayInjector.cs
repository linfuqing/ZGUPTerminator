using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

internal static class NetworkClientReplayInjector
{
    private const int ClientDataPipelineIndex = 0;
    private static bool s_loggedFirstInject;
    private static bool s_loggedDriverUnavailable;
    private static bool s_loggedWireBuildFailure;
    private static int s_injectAttemptCount;
    private static int s_injectSuccessCount;

    public static bool TryInjectInbound(uint remotePlayerId, byte[] recordedPayload)
    {
        ++s_injectAttemptCount;

        if (LevelRecordingPayloadUtility.TryGetReplyMessageType(recordedPayload, out var messageType) &&
            messageType == ReplyMessageType.SelectSkill &&
            !LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(
                recordedPayload,
                LevelRecordingReplayState.MutableSimulatedActiveSkillValues))
        {
            BotReplayLog.Warn(
                "Skip SelectSkill replay frame: chain validation failed (originIndex mismatch vs simulated SkillActiveIndex; prior pick missing, wrong baseline, or out-of-order recording).");
            return false;
        }

        if (!LevelRecordingEditorReplayPayloadUtility.TryBuildCollectInboundWire(
                remotePlayerId, recordedPayload, out var wirePayload))
        {
            if (!s_loggedWireBuildFailure)
            {
                s_loggedWireBuildFailure = true;
                BotReplayLog.Warn("Inject skipped: could not build inbound wire payload.");
            }

            return false;
        }

        if (wirePayload.Length > ushort.MaxValue)
            return false;

        if (!TryGetClient(out var client, out var pipeline))
            return false;

        // Package API mirrors PopEvents Data: each chunk is ushort length + payload bytes.
        using var framed = new NativeArray<byte>(sizeof(ushort) + wirePayload.Length, Allocator.Temp);
        var writer = new DataStreamWriter(framed);
        writer.WriteUShort((ushort)wirePayload.Length);
        using var wireNative = new NativeArray<byte>(wirePayload, Allocator.Temp);
        writer.WriteBytes(wireNative);

        var reader = new DataStreamReader(framed.GetSubArray(0, writer.Length));
        client.TryEnqueueSyntheticData(in pipeline, ref reader);

        ++s_injectSuccessCount;
        if (!s_loggedFirstInject)
        {
            s_loggedFirstInject = true;
            if (LevelRecordingPayloadUtility.TryGetReplyMessageType(recordedPayload, out var type))
            {
                BotReplayLog.Diag(
                    $"First inbound inject: {type}, remoteId={remotePlayerId}, {wirePayload.Length} bytes");
            }
        }

        return true;
    }

    public static void ResetDebugState()
    {
        s_loggedFirstInject = false;
        s_loggedDriverUnavailable = false;
        s_loggedWireBuildFailure = false;
        s_injectAttemptCount = 0;
        s_injectSuccessCount = 0;
    }

    public static (int attempts, int successes) GetDebugCounters() => (s_injectAttemptCount, s_injectSuccessCount);

    private static bool TryGetClient(out NetworkClient client, out NetworkPipeline pipeline)
    {
        client = default;
        pipeline = default;

        // Recording harness calls Shutdown() but keeps ClientData.driver alive for synthetic inject.
        // Do not require the ECS NetworkClientDriver singleton — it may be absent after suppress.
        if (IClientData.instance is not ClientData clientData)
        {
            if (!s_loggedDriverUnavailable)
            {
                s_loggedDriverUnavailable = true;
                BotReplayLog.Warn("Inject blocked: ClientData not available.");
            }

            return false;
        }

        var driver = clientData.driver;
        if (!driver.isCreated)
        {
            if (!s_loggedDriverUnavailable)
            {
                s_loggedDriverUnavailable = true;
                BotReplayLog.Warn("Inject blocked: ClientData driver not created yet.");
            }

            return false;
        }

        client = driver.instance;
        pipeline = driver.sendBuffer.GetPipeline(ClientDataPipelineIndex);
        return true;
    }
}
