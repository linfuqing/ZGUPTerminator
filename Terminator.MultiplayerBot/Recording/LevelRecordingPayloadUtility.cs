using Unity.Collections;
using ZG;

internal static class LevelRecordingPayloadUtility
{
    public static bool TryGetReplyMessageType(byte[] payload, out ReplyMessageType messageType)
    {
        messageType = default;
        if (!LevelRecordingPayloadClassification.TryClassifyRecordedPayload(
                payload,
                out var kind,
                out _,
                out messageType))
        {
            return false;
        }

        return kind == LevelRecordingRecordedPayloadKind.GameplayReply;
    }

    public static bool TryGetRecordedBodySlice(byte[] payload, out int bodyOffset, out int bodyLength)
    {
        bodyOffset = 0;
        bodyLength = 0;
        if (payload == null || payload.Length < 2)
            return false;

        using var bytes = new NativeArray<byte>(payload, Allocator.Temp);
        var reader = new DataStreamReader(bytes);
        var streamCompressionModel = StreamCompressionModel.Default;
        reader.ReadPackedInt(streamCompressionModel);
        reader.ReadPackedInt(streamCompressionModel);
        reader.Flush();
        if (reader.GetBytesRead() >= reader.Length)
            return false;

        bodyOffset = reader.GetBytesRead();
        bodyLength = payload.Length - bodyOffset;
        return bodyLength > 0;
    }
}
