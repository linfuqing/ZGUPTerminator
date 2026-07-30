using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Networking.Transport;
using ZG;

internal static class BotRelayWireTestFixtures
{
    /// <summary>Synthetic transport prefix used only with an explicitly supplied measured app offset.</summary>
    public static readonly byte[] SyntheticTransportPrefix =
    {
        0x01, 0xBC, 0x93, 0xD1, 0xC4, 0x3C, 0x00, 0xF7
    };

    public const uint RealPlayerUserId = 1656329017u;
    public const uint BotUserId = 999000001u;

    public static BotRelayPacket FromBytes(byte[] bytes)
    {
        var packet = default(BotRelayPacket);
        if (bytes == null || bytes.Length == 0)
            return packet;

        packet.length = bytes.Length;
        for (int i = 0; i < bytes.Length; ++i)
            packet.SetByte(i, bytes[i]);
        return packet;
    }

    public static byte[] ToBytes(in BotRelayPacket packet)
    {
        if (packet.IsEmpty)
            return Array.Empty<byte>();

        var bytes = new byte[packet.length];
        for (int i = 0; i < packet.length; ++i)
            bytes[i] = packet.GetByte(i);
        return bytes;
    }

    public static string ToHex(in BotRelayPacket packet, int maxBytes = 64)
    {
        if (packet.IsEmpty)
            return string.Empty;

        int count = Math.Min(packet.length, maxBytes);
        var chars = new char[count * 3 - 1];
        int write = 0;
        for (int i = 0; i < count; ++i)
        {
            if (i > 0)
                chars[write++] = ' ';
            write += AppendHexByte(packet.GetByte(i), chars, write);
        }

        return new string(chars, 0, write);
    }

    public static ClientHeader BuildSenderHeader(uint userId = RealPlayerUserId)
    {
        ClientHeader header;
        header.userID = userId;
        header.power = 0;
        header.userName = default;
        header.userAvatar = default;
        return header;
    }

    public static ClientHeader BuildBotHeader() => BuildSenderHeader(BotUserId);

    public static bool TryBuildInviteApp(
        uint senderUserId,
        int squadChannel,
        uint levelId,
        int stage,
        out BotRelayPacket app)
    {
        return BotRelayCodec.TryBuildWorldInviteRelayApp(
            BuildSenderHeader(senderUserId),
            squadChannel,
            levelId,
            stage,
            out app);
    }

    public static BotRelayPacket WrapFramedAppAtPrefix(byte[] transportPrefix, in BotRelayPacket app)
    {
        using var appBytes = new NativeArray<byte>(app.length, Allocator.Temp);
        app.TryCopyTo(appBytes);
        using var framed = BotRelayCodec.FrameForTransport(appBytes);
        if (!framed.IsCreated)
            return default;

        var header = transportPrefix ?? SyntheticTransportPrefix;
        var wire = default(BotRelayPacket);
        wire.length = header.Length + framed.Length;
        for (int i = 0; i < header.Length; ++i)
            wire.SetByte(i, header[i]);
        for (int i = 0; i < framed.Length; ++i)
            wire.SetByte(header.Length + i, framed[i]);
        return wire;
    }

    public static bool TryBuildStatusApp(out BotRelayPacket app)
    {
        return TryBuildOutboundStatusApp(0, out app);
    }

    public static bool TryLocateBuiltInviteAppInWire(
        in BotRelayPacket wire,
        uint senderUserId,
        int squadChannel,
        uint levelId,
        int stage,
        out int appOffset,
        out BotRelayPacket app)
    {
        appOffset = -1;
        app = default;
        if (!TryBuildInviteApp(senderUserId, squadChannel, levelId, stage, out var expected) || expected.IsEmpty)
            return false;

        int limit = wire.length - expected.length;
        for (int offset = 0; offset <= limit; ++offset)
        {
            bool match = true;
            for (int i = 0; i < expected.length; ++i)
            {
                if (wire.GetByte(offset + i) != expected.GetByte(i))
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            appOffset = offset;
            app = expected;
            return true;
        }

        return false;
    }

    public static int FindInviteTypePackedPrefixLength()
    {
        using var scratch = new NativeArray<byte>(8, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        writer.WritePackedInt((int)ReplyMessageType.Invite, StreamCompressionModel.Default);
        return writer.Length;
    }

    public static byte[] BuildInviteTypePrefix()
    {
        using var scratch = new NativeArray<byte>(8, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        writer.WritePackedInt((int)ReplyMessageType.Invite, StreamCompressionModel.Default);
        var bytes = new byte[writer.Length];
        for (int i = 0; i < writer.Length; ++i)
            bytes[i] = scratch[i];
        return bytes;
    }

    public static bool TryBuildInboundMatchApp(int matchId, int level, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Match, model);
        writer.WritePackedInt(matchId, model);
        writer.WritePackedInt(level, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildInboundMismatchApp(out BotRelayPacket app) =>
        TryBuildInboundMismatchApp(7, 0, out app);

    public static bool TryBuildInboundMismatchApp(
        int matchId,
        uint sourceId,
        out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Mismatch, model);
        writer.WritePackedInt(matchId, model);
        if (sourceId != 0)
            writer.WritePackedUInt(sourceId, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildOutboundRelayTypeApp(NetworkRelayMessageType relayType, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        writer.WritePackedInt((int)relayType, StreamCompressionModel.Default);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildOutboundCreateApp(int squadCapacity, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Create, model);
        writer.WritePackedInt(squadCapacity, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildOutboundStatusApp(int channelStatus, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Status, model);
        writer.WritePackedInt(channelStatus, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildInboundHostCreateEchoApp(int channel, int channelFlag, out BotRelayPacket app) =>
        __TryBuildInboundChannelEchoApp(NetworkRelayMessageType.Create, channel, channelFlag, out app);

    public static bool TryBuildInboundSelfJoinEchoApp(int channel, int channelFlag, out BotRelayPacket app) =>
        __TryBuildInboundChannelEchoApp(NetworkRelayMessageType.Join, channel, channelFlag, out app);

    private static bool __TryBuildInboundChannelEchoApp(
        NetworkRelayMessageType type,
        int channel,
        int channelFlag,
        out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)type, model);
        writer.WritePackedInt(channel, model);
        writer.WritePackedInt(channelFlag, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildInboundHostCreateWithRemoteApp(
        int channel,
        int channelFlag,
        uint remoteUserId,
        out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Create, model);
        writer.WritePackedInt(channel, model);
        writer.WritePackedInt(channelFlag, model);
        writer.Flush();
        writer.WritePackedUInt(remoteUserId, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildOutboundSquadJoinApp(uint squadInviteId, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Join, model);
        writer.WritePackedInt((int)squadInviteId, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildInboundJoinApp(uint squadInviteId, uint remoteUserId, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Join, model);
        writer.WritePackedInt((int)squadInviteId, model);
        writer.WritePackedInt(
            (int)(ClientRemotePlayerFlag.Online | ClientRemotePlayerFlag.Creator),
            model);
        writer.Flush();
        BuildSenderHeader(remoteUserId).Write(ref writer, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>Server SendHeader(Query): [type][channel][channelFlag].</summary>
    public static bool TryBuildInboundRelayQueryApp(int channel, int channelFlag, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Query, model);
        writer.WritePackedInt(channel, model);
        writer.WritePackedInt(channelFlag, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>Server JoinFailed: [type][channel].</summary>
    public static bool TryBuildInboundJoinFailedApp(int channel, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.JoinFailed, model);
        writer.WritePackedInt(channel, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>
    /// Server SendHeader(Query) for another channel member:
    /// [type][channel][channelFlag][connect payload / ClientHeader].
    /// </summary>
    public static bool TryBuildInboundRelayQueryHeaderApp(
        int channel,
        int channelFlag,
        uint remoteUserId,
        out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Query, model);
        writer.WritePackedInt(channel, model);
        writer.WritePackedInt(channelFlag, model);
        writer.Flush();
        BuildSenderHeader(remoteUserId).Write(ref writer, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>Server Connect notification: [type][channelFlag][userID].</summary>
    public static bool TryBuildInboundRelayConnectApp(
        int channelFlag,
        uint senderUserId,
        out BotRelayPacket app) =>
        __TryBuildInboundRelayPresenceApp(
            NetworkRelayMessageType.Connect,
            channelFlag,
            senderUserId,
            out app);

    /// <summary>Server __WriteStatus: [type][channelFlag][userID].</summary>
    public static bool TryBuildInboundRelayStatusApp(
        int channelFlag,
        uint senderUserId,
        out BotRelayPacket app) =>
        __TryBuildInboundRelayPresenceApp(
            NetworkRelayMessageType.Status,
            channelFlag,
            senderUserId,
            out app);

    private static bool __TryBuildInboundRelayPresenceApp(
        NetworkRelayMessageType type,
        int channelFlag,
        uint senderUserId,
        out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)type, model);
        writer.WritePackedInt(channelFlag, model);
        writer.WritePackedUInt(senderUserId, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>Server __WriteID(Disconnect): [type][userID].</summary>
    public static bool TryBuildInboundRelayDisconnectApp(uint senderUserId, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Disconnect, model);
        writer.WritePackedUInt(senderUserId, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>Server Relay SendRelay: [type][relayType][senderId][body].</summary>
    public static bool TryBuildInboundRelayPlayerPropertyApp(uint senderUserId, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)ReplyMessageType.PlayerProperty, model);
        writer.WritePackedInt((int)NetworkRelayType.Channel, model);
        writer.WritePackedUInt(senderUserId, model);
        writer.Flush();
        LevelPlayerProperty property = default;
        property.Write(ref writer, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>ClientMessage ChapterStage after relay header.</summary>
    public static bool TryBuildInboundRelayChapterStageApp(uint senderUserId, uint userStageId, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)ClientMessageType.ChapterStage, model);
        writer.WritePackedInt((int)NetworkRelayType.Channel, model);
        writer.WritePackedUInt(senderUserId, model);
        // Server.SendRelay appends the original client body as raw, byte-aligned payload.
        writer.Flush();
        writer.WritePackedUInt(userStageId, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildInboundPlayApp(uint levelId, int stage, uint senderId, out BotRelayPacket app) =>
        TryBuildInboundPlayApp(
            levelId,
            stage,
            senderId,
            new FixedString32Bytes("TestLevel"),
            new FixedString32Bytes("Scenes/Level1-1.scene"),
            out app);

    public static bool TryBuildInboundPlayApp(
        uint levelId,
        int stage,
        uint senderId,
        FixedString32Bytes levelName,
        FixedString32Bytes sceneName,
        out BotRelayPacket app) =>
        TryBuildInboundPlayApp(
            levelId,
            stage,
            senderId,
            NetworkRelayType.Channel,
            levelName,
            sceneName,
            out app);

    public static bool TryBuildInboundPlayApp(
        uint levelId,
        int stage,
        uint senderId,
        NetworkRelayType relayType,
        FixedString32Bytes levelName,
        FixedString32Bytes sceneName,
        out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        var play = new ClientMessagePlay
        {
            isRestart = false,
            levelID = levelId,
            stage = stage,
            levelName = levelName,
            sceneName = sceneName
        };
        writer.WritePackedInt((int)ClientMessageType.Play, model);
        writer.WritePackedInt((int)relayType, model);
        writer.WritePackedUInt(senderId, model);
        // Match NetworkRelayServerIdentity.SendRelay: envelope fields, then raw client body.
        writer.Flush();
        play.Write(ref writer);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildInboundMatchStartApp(
        int matchId,
        uint userStageId,
        uint levelId,
        int stage,
        uint senderId,
        FixedString32Bytes levelName,
        FixedString32Bytes sceneName,
        out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        var matchStart = new ClientMessageMatchStart
        {
            matchID = matchId,
            userStageID = userStageId,
            isRestart = true,
            levelID = levelId,
            stage = stage,
            levelName = levelName,
            sceneName = sceneName
        };
        writer.WritePackedInt((int)ClientMessageType.MatchStart, model);
        writer.WritePackedInt((int)NetworkRelayType.Channel, model);
        writer.WritePackedUInt(senderId, model);
        writer.Flush();
        matchStart.Write(ref writer);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>
    /// Server→bot Create/Join/Status downlinks reuse captured connect-server UTP shell with PopEvents-framed app.
    /// </summary>
    public static bool TryBuildConnectShapedInboundWire(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket app,
        out BotRelayPacket wire)
    {
        wire = default;
        if (app.IsEmpty || catalog.connectServerWires.Length == 0)
            return false;

        ref var shell = ref BotRelayWireBytes.GetWire(ref catalog, catalog.connectServerWires[0].wireIndex);
        if (!BotRelayWireBytes.TryCopyWireToPacket(ref shell, out wire))
            return false;

        int streamStart = catalog.inboundAppPayloadOffset - sizeof(ushort);
        if (streamStart < 0)
            return false;

        int frameLength = sizeof(ushort) + app.length;
        int required = streamStart + frameLength;
        if (required > BotRelayPacket.MaxPayloadSize)
            return false;

        if (wire.length < required)
            wire.length = required;

        wire.SetByte(streamStart, (byte)(app.length & 0xFF));
        wire.SetByte(streamStart + 1, (byte)((app.length >> 8) & 0xFF));
        int payloadStart = streamStart + sizeof(ushort);
        for (int i = 0; i < app.length; ++i)
        {
            wire.SetByte(payloadStart + i, app.GetByte(i));
        }

        for (int i = payloadStart + app.length; i < required; ++i)
        {
            wire.SetByte(i, 0);
        }

        wire.length = required;
        return true;
    }

    /// <summary>
    /// Server→bot Connect shell carrying multiple PopEvents-framed apps (e.g. ChapterStage + Status).
    /// </summary>
    public static bool TryBuildConnectShapedMultiAppInboundWire(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket app0,
        in BotRelayPacket app1,
        out BotRelayPacket wire)
    {
        wire = default;
        if (app0.IsEmpty || app1.IsEmpty || catalog.connectServerWires.Length == 0)
            return false;

        ref var shell = ref BotRelayWireBytes.GetWire(ref catalog, catalog.connectServerWires[0].wireIndex);
        if (!BotRelayWireBytes.TryCopyWireToPacket(ref shell, out wire))
            return false;

        int streamStart = catalog.inboundAppPayloadOffset - sizeof(ushort);
        if (streamStart < 0)
            return false;

        int pos = streamStart;
        if (!__TryAppendPopEventsFrame(ref wire, ref pos, in app0))
            return false;

        if (!__TryAppendPopEventsFrame(ref wire, ref pos, in app1))
            return false;

        wire.length = pos;
        return true;
    }

    private static bool __TryAppendPopEventsFrame(ref BotRelayPacket wire, ref int pos, in BotRelayPacket app)
    {
        int frameLength = sizeof(ushort) + app.length;
        int required = pos + frameLength;
        if (required > BotRelayPacket.MaxPayloadSize)
            return false;

        if (wire.length < required)
            wire.length = required;

        wire.SetByte(pos, (byte)(app.length & 0xFF));
        wire.SetByte(pos + 1, (byte)((app.length >> 8) & 0xFF));
        for (int i = 0; i < app.length; ++i)
            wire.SetByte(pos + sizeof(ushort) + i, app.GetByte(i));

        pos = required;
        return true;
    }

    private static int AppendHexByte(byte value, char[] destination, int offset)
    {
        destination[offset] = GetHexNibble(value >> 4);
        destination[offset + 1] = GetHexNibble(value & 0x0F);
        return 2;
    }

    private static char GetHexNibble(int value) =>
        (char)(value < 10 ? '0' + value : 'A' + (value - 10));
}
