using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using ZG;

internal static class BotRelayCodec
{
    public interface IClientSendBuffer
    {
        bool BeginWrite(out DataStreamWriter writer, ushort capacity = 1024);

        void EndWrite(in DataStreamWriter writer);
    }

    public interface IClientSendBufferReader
    {
        bool TryReadNext(ref int readIndex, out NativeArray<byte> bytes);

        void ClearPending();
    }

    public struct ClientSendBufferWrapper : IClientSendBuffer
    {
        public int pipelineIndex;
        public NetworkClientSendBuffer buffer;

        public bool BeginWrite(out DataStreamWriter writer, ushort capacity = 1024) =>
            buffer.BeginWrite(pipelineIndex, out writer, capacity);

        public void EndWrite(in DataStreamWriter writer) => buffer.EndWrite(writer);
    }

    public struct NetworkSendBufferWrapper : IClientSendBuffer, IClientSendBufferReader
    {
        public NetworkSendBuffer buffer;

        public bool BeginWrite(out DataStreamWriter writer, ushort capacity = 1024) =>
            buffer.BeginWrite(out writer, capacity);

        public void EndWrite(in DataStreamWriter writer) => buffer.EndWrite(writer);

        public bool TryReadNext(ref int readIndex, out NativeArray<byte> bytes) =>
            buffer.ReadNext(ref readIndex, out bytes);

        public void ClearPending() => buffer.Clear();
    }

    public static NativeArray<byte> BuildConnectPayload(in ClientHeader header)
    {
        var bytes = new NativeArray<byte>(256, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        header.Write(ref writer, StreamCompressionModel.Default);
        return bytes.GetSubArray(0, writer.Length);
    }

    public static bool TryBuildConnectPayload(
        in ClientHeader header,
        NativeArray<byte> scratch,
        int scratchOffset,
        out int payloadLength)
    {
        payloadLength = 0;
        if (!scratch.IsCreated || scratchOffset < 0 || scratchOffset + 256 > scratch.Length)
            return false;

        var writer = new DataStreamWriter(scratch.GetSubArray(scratchOffset, 256));
        header.Write(ref writer, StreamCompressionModel.Default);
        payloadLength = writer.Length;
        return payloadLength > 0;
    }

    /// <summary>
    /// Writes a ushort length prefix at <paramref name="scratchOffset"/>; payload bytes must already
    /// occupy [scratchOffset + 2, scratchOffset + 2 + payloadLength).
    /// </summary>
    public static bool TryWriteTransportFramePrefix(
        NativeArray<byte> scratch,
        int scratchOffset,
        int payloadLength,
        out int framedLength)
    {
        framedLength = 0;
        if (!scratch.IsCreated || payloadLength <= 0 || payloadLength > ushort.MaxValue)
            return false;

        if (scratchOffset < 0 || scratchOffset + sizeof(ushort) + payloadLength > scratch.Length)
            return false;

        scratch[scratchOffset] = (byte)(payloadLength & 0xff);
        scratch[scratchOffset + 1] = (byte)(payloadLength >> 8);
        framedLength = sizeof(ushort) + payloadLength;
        return true;
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
    /// Listen inject for bot→server relay apps: NetworkServer PopEvents reads ushort length at stream offset 0.
    /// </summary>
    [BurstDiscard]
    public static bool TryBuildPopEventsListenPacket(
        in BotRelayPacket app,
        NativeArray<byte> scratch,
        int scratchOffset,
        out BotRelayPacket framed)
    {
        framed = default;
        if (app.IsEmpty)
            return false;

        var appBytes = new NativeArray<byte>(app.length, Allocator.Temp);
        try
        {
            for (int i = 0; i < app.length; ++i)
                appBytes[i] = app.GetByte(i);

            using var framedBytes = FrameForTransport(appBytes);
            if (!framedBytes.IsCreated || framedBytes.Length <= sizeof(ushort))
                return false;

            if (scratch.IsCreated && scratchOffset >= 0 && scratchOffset + framedBytes.Length <= scratch.Length)
                NativeArray<byte>.Copy(framedBytes, 0, scratch, scratchOffset, framedBytes.Length);

            framed.length = framedBytes.Length;
            for (int i = 0; i < framedBytes.Length; ++i)
                framed.SetByte(i, framedBytes[i]);
            return true;
        }
        finally
        {
            appBytes.Dispose();
        }
    }

    public static bool TryWriteMatchToSend<T>(ref T sendBuffer, int level) where T : IClientSendBuffer
    {
        if (!sendBuffer.BeginWrite(out var writer))
            return false;

        var streamCompressionModel = StreamCompressionModel.Default;
        NetworkRelayMatch match;
        match.playerCount = 2;
        match.distance = level;
        match.distanceTime = 3.0f;
        BotRelayMessageUtility.WriteClientMatchToSend(ref writer, in match, in streamCompressionModel);

        sendBuffer.EndWrite(writer);
        return true;
    }

    public static bool TryWriteMismatch<T>(ref T sendBuffer) where T : IClientSendBuffer
    {
        if (!sendBuffer.BeginWrite(out var writer))
            return false;

        var model = StreamCompressionModel.Default;
        BotRelayMessageUtility.WriteRelayMismatch(ref writer, in model);
        sendBuffer.EndWrite(writer);
        return true;
    }

    public static bool TryWriteSquadJoin<T>(ref T sendBuffer, uint squadInviteID) where T : IClientSendBuffer
    {
        if (!sendBuffer.BeginWrite(out var writer))
            return false;

        var model = StreamCompressionModel.Default;
        BotRelayMessageUtility.WriteSquadJoin(ref writer, squadInviteID, in model);
        sendBuffer.EndWrite(writer);
        return true;
    }

    public static bool TryWriteApplyMatch<T>(ref T sendBuffer) where T : IClientSendBuffer
    {
        if (!sendBuffer.BeginWrite(out var writer))
            return false;

        BotRelayMessageUtility.WriteApplyMatch(ref writer);
        sendBuffer.EndWrite(writer);
        return true;
    }

    public static bool TryWritePlay<T>(ref T sendBuffer, in ClientMessagePlay play) where T : IClientSendBuffer
    {
        if (!sendBuffer.BeginWrite(out var writer))
            return false;

        BotRelayMessageUtility.WritePlay(ref writer, in play);
        sendBuffer.EndWrite(writer);
        return true;
    }

    public static bool TryWriteSquadLeave<T>(ref T sendBuffer) where T : IClientSendBuffer
    {
        if (!sendBuffer.BeginWrite(out var writer))
            return false;

        var model = StreamCompressionModel.Default;
        BotRelayMessageUtility.WriteRelayLeave(ref writer, in model);
        sendBuffer.EndWrite(writer);
        return true;
    }

    public static bool TryWriteStatus<T>(ref T sendBuffer, int channelStatus) where T : IClientSendBuffer
    {
        if (!sendBuffer.BeginWrite(out var writer))
            return false;

        var model = StreamCompressionModel.Default;
        BotRelayMessageUtility.WriteRelayStatus(ref writer, channelStatus, in model);
        sendBuffer.EndWrite(writer);
        return true;
    }

    public static bool TryWriteRelayPayload<T>(ref T sendBuffer, byte[] payload) where T : IClientSendBuffer
    {
        if (payload == null || payload.Length == 0)
            return false;

        using var bytes = new NativeArray<byte>(payload, Allocator.Temp);
        return TryWriteRelayPayload(ref sendBuffer, bytes);
    }

    public static bool TryWriteRelayPayload<T>(ref T sendBuffer, NativeArray<byte> payload) where T : IClientSendBuffer
    {
        if (!payload.IsCreated || payload.Length == 0)
            return false;

        if (!sendBuffer.BeginWrite(out var writer, (ushort)payload.Length))
            return false;

        writer.WriteBytes(payload);
        sendBuffer.EndWrite(writer);
        return true;
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
        BotRelayMessageUtility.WriteSendRelayHeader(
            ref writer,
            (int)ReplyMessageType.Invite,
            relayType,
            senderHeader.userID,
            in streamCompressionModel);
        // Match NetworkRelayServer.SendRelay's reply-header/body boundary. A continuous packed
        // stream can be interpreted as a valid but wrong squad channel by the server-first reader.
        writer.Flush();
        BotRelayMessageUtility.WriteInviteBody(
            ref writer,
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
    /// Minimum bytes before attempting prefix field reads (avoids DataStreamReader over-read error logs).
    /// </summary>
    public const int MinWorldInviteRelayAppPrefixBytes = BotRelayMessageUtility.MinSendRelayInviteAppBytes;

    /// <summary>
    /// Validates a SendRelay-shaped Invite app (type + relay + sender + Invite body prefix).
    /// Does not require the trailing FixedBytes80 header — live 91B transport frames may truncate it.
    /// </summary>
    public static bool TryParseWorldInviteRelayApp(in BotRelayPacket app, out uint squadInviteId)
    {
        squadInviteId = uint.MaxValue;
        if (app.IsEmpty)
            return false;

        var model = StreamCompressionModel.Default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);

        if (__TryParseInviteSquadChannel(in app, scratch, in model, out squadInviteId))
            return true;

        if (!BotRelayWireBytes.TryNormalizePopEventsPayload(in app, out var normalized))
            return false;

        return __TryParseInviteSquadChannel(in normalized, scratch, in model, out squadInviteId);
    }

    private static bool __TryParseInviteSquadChannel(
        in BotRelayPacket app,
        NativeArray<byte> scratch,
        in StreamCompressionModel model,
        out uint squadInviteId)
    {
        squadInviteId = uint.MaxValue;
        if (BotRelayMessageUtility.TryReadInviteFromPacket(
                in app,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default) &&
            invite.channel >= 0 &&
            invite.stage >= 0)
        {
            squadInviteId = (uint)invite.channel;
            return true;
        }

        if (!BotRelayMessageUtility.TryReadSendRelayInvite(
                in app,
                scratch,
                0,
                in model,
                out invite,
                BotRelayMessageUtility.InviteReadOptions.Default))
        {
            return false;
        }

        if (invite.channel < 0 || invite.stage < 0)
        {
            return false;
        }

        squadInviteId = (uint)invite.channel;
        return true;
    }

    public static bool TryWritePlayerPropertyBody<T>(ref T sendBuffer, byte[] recordedPayload) where T : IClientSendBuffer
    {
        if (!LevelRecordingPayloadUtility.TryGetRecordedBodySlice(recordedPayload, out int bodyOffset, out int bodyLength))
            return false;

        if (!sendBuffer.BeginWrite(out var writer, (ushort)bodyLength))
            return false;

        writer.WriteReplyHeader((int)ReplyMessageType.PlayerProperty, NetworkRelayType.Channel);
        using var body = new NativeArray<byte>(bodyLength, Allocator.Temp);
        NativeArray<byte>.Copy(recordedPayload, bodyOffset, body, 0, bodyLength);
        writer.WriteBytes(body);
        sendBuffer.EndWrite(writer);
        return true;
    }

    /// <summary>
    /// Normalizes a recorded PlayerProperty held in the replay blob without allocating a managed
    /// byte[] or a Temp NativeArray inside ReplayLoadJob.
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
            reader.ReadPackedInt(model);
            reader.ReadPackedInt(model);
            reader.Flush();
            if (reader.HasFailedReads || reader.GetBytesRead() >= reader.Length)
                return false;

            int bodyOffset = reader.GetBytesRead();
            byte* scratch = stackalloc byte[BotRelayPacket.MaxPayloadSize];
            var writer = new DataStreamWriter(scratch, BotRelayPacket.MaxPayloadSize);
            writer.WriteReplyHeader((int)ReplyMessageType.PlayerProperty, NetworkRelayType.Channel);
            for (int i = bodyOffset; i < recordedPayload.length; ++i)
                writer.WriteByte(source[i]);

            if (writer.HasFailedWrites || writer.Length <= 0)
                return false;

            packet.CopyFromPtr(scratch, writer.Length);
            return !packet.IsEmpty;
        }
    }
}
