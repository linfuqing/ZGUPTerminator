using Unity.Collections;
using ZG;

/// <summary>
/// Editor recording/replay payload helpers (not used by Bot production replay).
/// </summary>
internal static class LevelRecordingEditorReplayPayloadUtility
{
    /// <summary>
    /// Collect 在 StandBy 下从 NetworkClient 入站缓冲读取：type + channel + id + body。
    /// </summary>
    public static bool TryBuildCollectInboundWire(
        uint remotePlayerId,
        byte[] recordedPayload,
        out byte[] wirePayload)
    {
        wirePayload = null;
        if (recordedPayload == null || recordedPayload.Length < 2)
            return false;

        if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(recordedPayload, out var messageType))
            return false;

        if (messageType == ReplyMessageType.PlayerProperty)
            return false;

        if (!LevelRecordingPayloadUtility.TryGetRecordedBodySlice(recordedPayload, out int bodyOffset, out int bodyLength))
            return false;

        using var source = new NativeArray<byte>(recordedPayload, Allocator.Temp);
        var streamCompressionModel = StreamCompressionModel.Default;
        int capacity = 16 + bodyLength;
        using var target = new NativeArray<byte>(capacity, Allocator.Temp);
        var writer = new DataStreamWriter(target);
        writer.WritePackedInt((int)messageType, streamCompressionModel);
        writer.WritePackedInt((int)NetworkRelayType.Channel, streamCompressionModel);
        writer.WritePackedUInt(remotePlayerId, streamCompressionModel);
        writer.WriteBytes(source.GetSubArray(bodyOffset, bodyLength));

        wirePayload = target.GetSubArray(0, writer.Length).ToArray();
        return true;
    }
}
