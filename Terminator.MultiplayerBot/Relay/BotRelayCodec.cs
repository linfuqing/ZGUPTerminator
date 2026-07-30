using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using ZG;

internal static class BotRelayCodec
{
    public static NativeArray<byte> BuildConnectPayload(in ClientHeader header)
    {
        var bytes = new NativeArray<byte>(256, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        header.Write(ref writer, StreamCompressionModel.Default);
        return bytes.GetSubArray(0, writer.Length);
    }

    /// <summary>
    /// Wraps a relay payload with the ushort length prefix expected by NetworkServer/Data handlers.
    /// </summary>
    public static NativeArray<byte> FrameForTransport(NativeArray<byte> payload, Allocator allocator = Allocator.Temp)
    {
        if (!payload.IsCreated || payload.Length == 0)
            return default;

        if (payload.Length > ushort.MaxValue)
            return default;

        var framed = new NativeArray<byte>(sizeof(ushort) + payload.Length, allocator);
        framed[0] = (byte)(payload.Length & 0xff);
        framed[1] = (byte)(payload.Length >> 8);

        NativeArray<byte>.Copy(payload, 0, framed, sizeof(ushort), payload.Length);
        return framed;
    }

    /// <summary>
    /// Server SendRelay wire to recipients (Invite + relay + sender + body).
    /// </summary>
    public static bool TryBuildWorldInviteRelayApp(
        in ClientHeader senderHeader,
        int squadChannel,
        uint levelID,
        int stage,
        out BotRelayPacket appTemplate)
    {
        return TryBuildWorldInviteRelayApp(
            in senderHeader,
            NetworkRelayType.All,
            squadChannel,
            levelID,
            stage,
            default,
            senderHeader.ToLevelPlayerHeader().ToBytes(),
            out appTemplate);
    }

    public static bool TryBuildWorldInviteRelayApp(
        in ClientHeader senderHeader,
        NetworkRelayType relayType,
        int squadChannel,
        uint levelID,
        int stage,
        in FixedString512Bytes text,
        out BotRelayPacket appTemplate)
    {
        return TryBuildWorldInviteRelayApp(
            in senderHeader,
            relayType,
            squadChannel,
            levelID,
            stage,
            in text,
            senderHeader.ToLevelPlayerHeader().ToBytes(),
            out appTemplate);
    }

    public static bool TryBuildWorldInviteRelayApp(
        in ClientHeader senderHeader,
        NetworkRelayType relayType,
        int squadChannel,
        uint levelID,
        int stage,
        in FixedString512Bytes text,
        in FixedBytes80 header,
        out BotRelayPacket appTemplate)
    {
        appTemplate = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var streamCompressionModel = StreamCompressionModel.Default;
        BotRelayMessageUtility.WriteSendRelayInvite(
            ref writer,
            relayType,
            senderHeader.userID,
            levelID,
            stage,
            squadChannel,
            in text,
            in header,
            in streamCompressionModel);

        if (writer.Length <= 0 || writer.Length > BotRelayPacket.MaxPayloadSize)
            return false;

        appTemplate.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            appTemplate.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>
    /// Validates a recorded PlayerProperty held in the replay blob without allocating a managed
    /// byte[] or constructing the large fixed-list PlayerProperty value inside ReplayLoadJob.
    /// </summary>
    public static unsafe bool TryBuildPlayerPropertyPacket(in BotRelayPacket recordedPayload, out BotRelayPacket packet)
    {
        packet = default;
        if (recordedPayload.length < 2 || recordedPayload.length > BotRelayPacket.MaxPayloadSize)
            return false;

        fixed (byte* source = recordedPayload.data)
        {
            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                source,
                recordedPayload.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            int messageType = reader.ReadPackedInt(model);
            int relayType = reader.ReadPackedInt(model);
            reader.Flush();
            if (reader.HasFailedReads ||
                messageType != (int)ReplyMessageType.PlayerProperty ||
                relayType != (int)NetworkRelayType.Channel ||
                reader.GetBytesRead() >= reader.Length)
                return false;

            if (!TryReadCurrentPlayerPropertyBody(ref reader, in model))
                return false;

            packet = recordedPayload;
            return true;
        }
    }

    internal static bool TryReadCurrentPlayerPropertyBody(
        ref DataStreamReader reader,
        in StreamCompressionModel model)
    {
        reader.ReadPackedInt(model);
        reader.ReadPackedInt(model);
        reader.ReadPackedInt(model);
        reader.ReadPackedFloat(model);
        reader.ReadPackedFloat(model);
        reader.ReadPackedFloat(model);
        reader.ReadPackedFloat(model);
        if (!__TryReadFixedString32(ref reader))
            return false;

        int count = reader.ReadByte();
        if (reader.HasFailedReads ||
            count > default(FixedList512Bytes<FixedString32Bytes>).Capacity)
        {
            return false;
        }

        for (int i = 0; i < count; ++i)
        {
            if (!__TryReadFixedString32(ref reader))
                return false;
        }

        count = reader.ReadByte();
        if (reader.HasFailedReads ||
            count > default(FixedList4096Bytes<LevelPlayerActiveSkill>).Capacity)
        {
            return false;
        }

        for (int i = 0; i < count; ++i)
        {
            if (!__TryReadFixedString32(ref reader))
                return false;

            reader.ReadPackedFloat(model);
        }

        count = reader.ReadByte();
        if (reader.HasFailedReads ||
            count > default(FixedList4096Bytes<LevelPlayerSkillGroup>).Capacity)
        {
            return false;
        }

        for (int i = 0; i < count; ++i)
        {
            if (!__TryReadFixedString32(ref reader))
                return false;

            reader.ReadPackedFloat(model);
        }

        count = reader.ReadByte();
        if (reader.HasFailedReads ||
            count > default(FixedList4096Bytes<LevelPlayerSkillOpcode>).Capacity)
        {
            return false;
        }

        for (int i = 0; i < count; ++i)
        {
            if (!__TryReadFixedString32(ref reader))
                return false;

            reader.ReadByte();
            reader.ReadPackedFloat(model);
        }

        return !reader.HasFailedReads && reader.GetBytesRead() == reader.Length;
    }

    private static bool __TryReadFixedString32(ref DataStreamReader reader)
    {
        var probe = reader;
        ushort utf8Length = probe.ReadUShort();
        if (probe.HasFailedReads ||
            utf8Length > FixedString32Bytes.UTF8MaxLengthInBytes ||
            probe.GetBytesRead() > probe.Length - utf8Length)
        {
            return false;
        }

        reader.ReadFixedString32();
        return !reader.HasFailedReads;
    }
}
