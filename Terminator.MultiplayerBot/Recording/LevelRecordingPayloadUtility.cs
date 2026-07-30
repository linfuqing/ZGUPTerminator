using ZG;

internal static class LevelRecordingPayloadUtility
{
    public static bool TryGetReplyMessageType(byte[] payload, out ReplyMessageType messageType)
    {
        return TryGetRecordedReplyHeader(payload, out messageType, out _, out _);
    }

    public static bool TryGetRecordedReplyHeader(
        byte[] payload,
        out ReplyMessageType messageType,
        out NetworkRelayType relayType,
        out int bodyOffset)
    {
        messageType = default;
        relayType = default;
        bodyOffset = 0;
        if (!LevelRecordingPayloadClassification.TryClassifyRecordedPayload(
                payload,
                out var kind,
                out int classifiedWireType,
                out var classifiedMessageType) ||
            kind != LevelRecordingRecordedPayloadKind.GameplayReply ||
            !LevelRecordingPayloadClassification.TryReadRecordedReplyHeader(
                payload,
                out int headerWireType,
                out relayType,
                out bodyOffset) ||
            headerWireType != classifiedWireType ||
            relayType != NetworkRelayType.Channel)
        {
            return false;
        }

        messageType = classifiedMessageType;
        return true;
    }

    public static bool TryGetRecordedBodySlice(byte[] payload, out int bodyOffset, out int bodyLength)
    {
        bodyOffset = 0;
        bodyLength = 0;
        if (!TryGetRecordedReplyHeader(payload, out _, out _, out bodyOffset) ||
            bodyOffset >= payload.Length)
        {
            return false;
        }

        bodyLength = payload.Length - bodyOffset;
        return bodyLength > 0;
    }
}
