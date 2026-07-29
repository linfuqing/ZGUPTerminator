using System;
using Unity.Collections;
using ZG;

/// <summary>
/// Offline / audit classification for captured sendBuffer payloads.
/// Mirrors Bot relay routing (Invite, handshake ClientMessage, gameplay Reply).
/// </summary>
internal enum LevelRecordingRecordedPayloadKind : byte
{
    Empty = 0,
    GameplayReply = 1,
    Invite = 2,
    RelayControl = 3,
    HandshakeClient = 4,
    Unrecognized = 5,
}

internal static class LevelRecordingPayloadClassification
{
    public static bool TryReadRecordedWireType(byte[] payload, out int wireType)
    {
        wireType = 0;
        if (payload == null || payload.Length == 0)
        {
            return false;
        }

        using var bytes = new NativeArray<byte>(payload, Allocator.Temp);
        var reader = new DataStreamReader(bytes);
        if (reader.GetBytesRead() >= reader.Length)
        {
            return false;
        }

        wireType = reader.ReadPackedInt(StreamCompressionModel.Default);
        return true;
    }

    public static bool TryClassifyRecordedPayload(
        byte[] payload,
        out LevelRecordingRecordedPayloadKind kind,
        out int wireType,
        out ReplyMessageType gameplayMessageType)
    {
        kind = LevelRecordingRecordedPayloadKind.Unrecognized;
        wireType = 0;
        gameplayMessageType = default;

        if (payload == null || payload.Length == 0)
        {
            kind = LevelRecordingRecordedPayloadKind.Empty;
            return true;
        }

        if (!TryReadRecordedWireType(payload, out wireType))
        {
            return false;
        }

        if (wireType == (int)ReplyMessageType.Invite)
        {
            kind = LevelRecordingRecordedPayloadKind.Invite;
            return true;
        }

        if (wireType >= 0 && wireType <= (int)NetworkRelayMessageType.Query)
        {
            kind = LevelRecordingRecordedPayloadKind.RelayControl;
            return true;
        }

        if (wireType >= (int)ReplyMessageType.Chat && wireType <= (int)ReplyMessageType.PlayerProperty)
        {
            kind = LevelRecordingRecordedPayloadKind.GameplayReply;
            gameplayMessageType = (ReplyMessageType)wireType;
            return true;
        }

        if (wireType >= (int)NetworkRelayMessageType.Query + 2 && wireType < (int)ReplyMessageType.Chat)
        {
            kind = LevelRecordingRecordedPayloadKind.HandshakeClient;
            return true;
        }

        return true;
    }

    public static string FormatWireTypeLabel(int wireType, LevelRecordingRecordedPayloadKind kind)
    {
        switch (kind)
        {
            case LevelRecordingRecordedPayloadKind.GameplayReply:
                return ((ReplyMessageType)wireType).ToString();
            case LevelRecordingRecordedPayloadKind.Invite:
                return nameof(ReplyMessageType.Invite);
            case LevelRecordingRecordedPayloadKind.RelayControl:
                if (Enum.IsDefined(typeof(NetworkRelayMessageType), wireType))
                {
                    return ((NetworkRelayMessageType)wireType).ToString();
                }

                return "Relay(" + wireType + ")";
            case LevelRecordingRecordedPayloadKind.HandshakeClient:
                if (Enum.IsDefined(typeof(ClientMessageType), wireType))
                {
                    return ((ClientMessageType)wireType).ToString();
                }

                return "ClientMessage(" + wireType + ")";
            case LevelRecordingRecordedPayloadKind.Empty:
                return "Empty";
            default:
                return "Unrecognized(" + wireType + ")";
        }
    }
}
