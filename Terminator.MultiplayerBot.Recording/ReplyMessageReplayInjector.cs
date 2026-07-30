using Unity.Collections;
using ZG;

internal static class ReplyMessageReplayInjector
{
    public static bool TryParsePlayerProperty(byte[] recordedPayload, out LevelPlayerProperty property)
    {
        property = default;
        if (recordedPayload == null ||
            recordedPayload.Length == 0 ||
            recordedPayload.Length > BotRelayPacket.MaxPayloadSize)
            return false;

        var packet = default(BotRelayPacket);
        packet.length = recordedPayload.Length;
        for (int i = 0; i < recordedPayload.Length; ++i)
            packet.SetByte(i, recordedPayload[i]);

        // Run the Burst-safe exact current-layout validator before constructing LevelPlayerProperty.
        // Unity's FixedString reader logs on malformed lengths before HasFailedReads is observable.
        if (!BotRelayCodec.TryBuildPlayerPropertyPacket(in packet, out _))
            return false;

        using var source = new NativeArray<byte>(recordedPayload, Allocator.Temp);
        var reader = new DataStreamReader(source);
        var model = StreamCompressionModel.Default;
        int messageType = reader.ReadPackedInt(model);
        int relayType = reader.ReadPackedInt(model);
        reader.Flush();
        if (reader.HasFailedReads ||
            messageType != (int)ReplyMessageType.PlayerProperty ||
            relayType != (int)NetworkRelayType.Channel ||
            reader.GetBytesRead() >= reader.Length)
        {
            return false;
        }

        property = new LevelPlayerProperty(ref reader, model);
        return !reader.HasFailedReads &&
               reader.GetBytesRead() == reader.Length;
    }

    public static void ApplySharedProperty(in LevelPlayerProperty property, bool promoteJoined)
    {
        LevelPlayerShared<RemotePlayer>.property = property;

        // LevelPlayerSystem promotes Joined→StandBy after spawn; do not demote during in-level replay.
        if (promoteJoined && RemotePlayer.status != RemotePlayer.Status.StandBy)
            RemotePlayer.SetStatus(RemotePlayer.Status.Joined);
    }
}
