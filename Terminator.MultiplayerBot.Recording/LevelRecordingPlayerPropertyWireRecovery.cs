using Unity.Collections;
using ZG;

/// <summary>
/// Builds the same sendBuffer snapshot bytes as ClientData.__Send default path for PlayerProperty.
/// Used only when handshake diagnostics prove ApplyStage applied local property but EndWrite never landed.
/// </summary>
internal static class LevelRecordingPlayerPropertyWireRecovery
{
    private static bool s_nativeResendAttempted;

    public static void ResetNativeResendAttempt()
    {
        s_nativeResendAttempted = false;
    }

    /// <summary>
    /// Same ClientData path as LoginManager.__SubmitStage; lets capture read EndWrite from sendBuffer.
    /// </summary>
    public static bool TryNativeResendViaClientData()
    {
        if (s_nativeResendAttempted)
        {
            return false;
        }

        if (IClientData.instance is not ClientData clientData)
        {
            return false;
        }

        ref readonly var property = ref LevelPlayerShared<LocalPlayer>.property;
        if (LevelRecordingReplayPropertyOps.IsPropertyEmpty(in property))
        {
            return false;
        }

        s_nativeResendAttempted = true;
        var writer = clientData.BeginSend(
            ClientMessagePlayerProperty.messageType,
            ClientMessagePlayerProperty.capacity);
        property.Write(ref writer, StreamCompressionModel.Default);
        int byteLength = writer.Length;
        clientData.EndSend(writer);

        BotReplayLog.Diag(
            "Native PlayerProperty re-send via ClientData BeginSend/EndSend " +
            $"(post-ApplyStage, bytes={byteLength}). Original __SubmitStage send was skipped.");
        return true;
    }

    public static bool TryBuildRecordedPayload(in LevelPlayerProperty property, out byte[] payload)
    {
        payload = null;
        if (LevelRecordingReplayPropertyOps.IsPropertyEmpty(in property))
        {
            return false;
        }

        using var scratch = new NativeArray<byte>(8192, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        writer.WriteReplyHeader((int)ReplyMessageType.PlayerProperty, NetworkRelayType.Channel);
        property.Write(ref writer, StreamCompressionModel.Default);

        if (writer.Length < 1)
        {
            return false;
        }

        payload = scratch.GetSubArray(0, writer.Length).ToArray();
        return payload.Length > 0;
    }
}
