using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Networking.Transport;
using ZG;

internal static class BotRelayWireTestFixtures
{
    /// <summary>8-byte pipeline prefix from live capture (before payload envelope).</summary>
    public static readonly byte[] PipelineHeader =
    {
        0x01, 0xBC, 0x93, 0xD1, 0xC4, 0x3C, 0x00, 0xF7
    };

    /// <summary>Pipeline + PopEvents-framed SendRelay Invite (canonical ZGUP shape via <see cref="BotRelayCodec.TryBuildWorldInviteRelayApp"/>).</summary>
    public static byte[] BuildPipelineInviteWireBytes(
        uint senderUserId = RealPlayerUserId,
        int squadChannel = 1,
        uint levelId = 0,
        int stage = 0)
    {
        if (!TryBuildInviteApp(senderUserId, squadChannel, levelId, stage, out var app) || app.IsEmpty)
            throw new InvalidOperationException("Canonical invite app build failed.");

        return ToBytes(WrapPipelineFramedApp(PipelineHeader, in app));
    }

    /// <summary>Regression alias — built from codec, not static 2026-06 hex.</summary>
    public static byte[] CapturedLiveInviteWire117 => BuildPipelineInviteWireBytes();

    public static readonly byte[] StatusOutboundWire =
    {
        0x01, 0xBC, 0x93, 0xD1, 0xC4, 0x3C, 0x00, 0xF7,
        0x6B, 0x00, 0x01, 0x00, 0x01
    };

    public const uint RealPlayerUserId = 1656329017u;
    public const uint BotUserId = 999000001u;
    public const uint LiveHostUserId = 776757715u;

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

    public static BotRelayPacket WrapPipelineRawApp(byte[] pipelineHeader, in BotRelayPacket app)
    {
        var header = pipelineHeader ?? PipelineHeader;
        var wire = default(BotRelayPacket);
        wire.length = header.Length + app.length;
        for (int i = 0; i < header.Length; ++i)
            wire.SetByte(i, header[i]);
        for (int i = 0; i < app.length; ++i)
            wire.SetByte(header.Length + i, app.GetByte(i));
        return wire;
    }

    public static BotRelayPacket WrapPipelineFramedApp(byte[] pipelineHeader, in BotRelayPacket app)
    {
        using var appBytes = new NativeArray<byte>(app.length, Allocator.Temp);
        app.TryCopyTo(appBytes);
        using var framed = BotRelayCodec.FrameForTransport(appBytes);
        if (!framed.IsCreated)
            return default;

        var header = pipelineHeader ?? PipelineHeader;
        var wire = default(BotRelayPacket);
        wire.length = header.Length + framed.Length;
        for (int i = 0; i < header.Length; ++i)
            wire.SetByte(i, header[i]);
        for (int i = 0; i < framed.Length; ++i)
            wire.SetByte(header.Length + i, framed[i]);
        return wire;
    }

    /// <summary>
    /// MCP live Play pipeline invite shell: 10-byte pipeline envelope + PopEvents-framed invite (≈104B).
    /// Prefix bytes mirror a captured session; streamStart scan must still find the ushort-framed payload.
    /// </summary>
    public static bool TryBuildLivePlayShapedInviteWire(out BotRelayPacket wire, out int wireLength)
    {
        wire = default;
        wireLength = 0;
        var text = new FixedString512Bytes(LiveHostUserId.ToString());
        if (!BotRelayCodec.TryBuildWorldInviteRelayApp(
                BuildSenderHeader(LiveHostUserId),
                NetworkRelayType.All,
                1,
                0u,
                0,
                in text,
                out var app) ||
            app.IsEmpty)
            return false;

        using var appBytes = new NativeArray<byte>(app.length, Allocator.Temp);
        if (!app.TryCopyTo(appBytes))
            return false;

        using var framed = BotRelayCodec.FrameForTransport(appBytes);
        if (!framed.IsCreated)
            return false;

        byte[] envelope =
        {
            0x01, 0x20, 0xAF, 0x7D, 0xB8, 0x27, 0xA0, 0x54, 0x89, 0x00
        };

        wire.length = envelope.Length + framed.Length;
        for (int i = 0; i < envelope.Length; ++i)
            wire.SetByte(i, envelope[i]);
        for (int i = 0; i < framed.Length; ++i)
            wire.SetByte(envelope.Length + i, framed[i]);

        wireLength = wire.length;
        return true;
    }

    /// <summary>MCP Play 2026-07-11: 104B invite shell envelope (streamStart=10, ushort payload=92B).</summary>
    public static bool TryBuildMcpSessionInvite104Wire(out BotRelayPacket wire, out int wireLength)
    {
        wire = default;
        wireLength = 0;
        var text = new FixedString512Bytes(LiveHostUserId.ToString());
        if (!BotRelayCodec.TryBuildWorldInviteRelayApp(
                BuildSenderHeader(LiveHostUserId),
                NetworkRelayType.All,
                0,
                0u,
                0,
                in text,
                out var app) ||
            app.IsEmpty)
            return false;

        using var appBytes = new NativeArray<byte>(app.length, Allocator.Temp);
        if (!app.TryCopyTo(appBytes))
            return false;

        using var framed = BotRelayCodec.FrameForTransport(appBytes);
        if (!framed.IsCreated)
            return false;

        byte[] envelope = McpCapturedInvite104PipelinePrefix;

        wire.length = envelope.Length + framed.Length;
        for (int i = 0; i < envelope.Length; ++i)
            wire.SetByte(i, envelope[i]);
        for (int i = 0; i < framed.Length; ++i)
            wire.SetByte(envelope.Length + i, framed[i]);

        wireLength = wire.length;
        return wireLength >= 80 && wireLength <= 160;
    }

    /// <summary>
    /// Captured from a real host's targeted Invite: the relay writes a fresh SendRelay
    /// header, then copies the already byte-aligned ReplyMessages.Invite body.
    /// </summary>
    public static BotRelayPacket BuildTargetedInviteFlushBoundaryWirePacket()
    {
        byte[] envelope =
        {
            0x01, 0xDE, 0x67, 0x96, 0x9A, 0x95, 0x53, 0x97, 0x90, 0x00
        };
        byte[] appPrefix =
        {
            0x0B, 0xEE, 0x78, 0xD0, 0x9F, 0xDD, 0x4F, 0x99, 0x05, 0xA2, 0x04,
            0x12, 0x00, 0x50, 0x6C, 0x61, 0x79, 0x4D, 0x6F, 0x64, 0x65, 0x48,
            0x6F, 0x73, 0x74, 0x49, 0x6E, 0x76, 0x69, 0x74, 0x65
        };

        const int appLength = 111;
        var wire = default(BotRelayPacket);
        wire.length = envelope.Length + sizeof(ushort) + appLength;
        int offset = 0;
        for (int i = 0; i < envelope.Length; ++i)
            wire.SetByte(offset++, envelope[i]);

        wire.SetByte(offset++, (byte)appLength);
        wire.SetByte(offset++, 0);
        for (int i = 0; i < appLength; ++i)
            wire.SetByte(offset++, i < appPrefix.Length ? appPrefix[i] : (byte)0);

        return wire;
    }


    /// <summary>Pipeline prefix from MCP Play 2026-07-11 RouteSend log (streamStart=10).</summary>
    public static readonly byte[] McpCapturedInvite104PipelinePrefix =
    {
        0x01, 0xB3, 0xB2, 0xFE, 0xAB, 0x90, 0x81, 0x97, 0x1A, 0x00
    };

    /// <summary>6B PopEvents payload junk before SendRelay header in live capture.</summary>
    public static readonly byte[] McpCapturedInvite104PopEventsJunkPrefix =
    {
        0x0B, 0x8E, 0x7F, 0x8E, 0xF9, 0xA4
    };

    /// <summary>SendRelay tail captured from Play 2026-07-11 (after junk prefix).</summary>
    public static readonly byte[] McpLatestPlayInviteSendRelayTail =
    {
        0x04, 0xD5, 0xA2, 0x01, 0x00, 0x00, 0x17, 0x59, 0x00, 0x00, 0x09, 0x00,
        0x37, 0x37, 0x36, 0x37, 0x35, 0x37, 0x37, 0x31, 0x35
    };

    /// <summary>Latest Play session pipeline prefix (streamStart=10, payload=92B).</summary>
    public static readonly byte[] McpLatestPlayInvitePipelinePrefix =
    {
        0x01, 0x8A, 0x00, 0xDD, 0xB9, 0x04, 0x32, 0xE9, 0xA3, 0x00
    };

    /// <summary>Play 2026-07-11 latest session: 104B shell with junk envelope at offset 8 (streamStart=10).</summary>
    public static readonly byte[] McpLatestPlayInvite104PipelinePrefix =
    {
        0x01, 0x77, 0x22, 0xC3, 0x42, 0x9D, 0x4A, 0x06, 0x0D, 0x00
    };

    /// <summary>Play 2026-07-11 evening session pipeline prefix (streamStart=10, payload=91B).</summary>
    public static readonly byte[] McpLatestPlayInvite103PipelinePrefix =
    {
        0x01, 0xFF, 0x40, 0xEF, 0x8E, 0x65, 0x18, 0xE9, 0x53, 0x00
    };

    public static BotRelayPacket BuildLatestPlayCaptureInvite104WirePacket()
    {
        return BuildLatestPlayCaptureInvite104LiteralWirePacket();
    }

    /// <summary>Live Play 104B shell SendRelay tail after 6B junk (sender id omitted on wire).</summary>
    public static readonly byte[] McpLatestPlayInvite104SendRelayTail =
    {
        0x04, 0xD5, 0xA2, 0x01, 0x00, 0x00, 0x17, 0x59, 0x00, 0x00, 0x0C, 0x00,
        0xE6, 0x9D, 0x80, 0xE9, 0xB1, 0xBC, 0xE6, 0xA0, 0xBC, 0xE6, 0xA0, 0xBC,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    public static bool TryBuildMcpCapturedInvite104JunkPayload(out BotRelayPacket payload)
    {
        payload = default;
        payload.length = McpCapturedInvite104PopEventsJunkPrefix.Length + McpLatestPlayInvite104SendRelayTail.Length;
        int pos = 0;
        for (int i = 0; i < McpCapturedInvite104PopEventsJunkPrefix.Length; ++i)
        {
            payload.SetByte(pos++, McpCapturedInvite104PopEventsJunkPrefix[i]);
        }

        for (int i = 0; i < McpLatestPlayInvite104SendRelayTail.Length; ++i)
        {
            payload.SetByte(pos++, McpLatestPlayInvite104SendRelayTail[i]);
        }

        const int targetPayloadLength = 92;
        while (payload.length < targetPayloadLength)
        {
            payload.SetByte(payload.length, 0);
            payload.length++;
        }

        return payload.length == targetPayloadLength;
    }

    public static BotRelayPacket BuildLatestPlayCaptureInvite104LiteralWirePacket()
    {
        const int streamStart = 10;
        if (!TryBuildMcpCapturedInvite104JunkPayload(out var payload) || payload.IsEmpty)
        {
            throw new InvalidOperationException("Live 104B junk-prefix invite payload build failed.");
        }

        var wire = default(BotRelayPacket);
        wire.length = streamStart + sizeof(ushort) + payload.length;

        for (int i = 0; i < McpLatestPlayInvite104PipelinePrefix.Length; ++i)
        {
            wire.SetByte(i, McpLatestPlayInvite104PipelinePrefix[i]);
        }

        wire.SetByte(streamStart, (byte)(payload.length & 0xFF));
        wire.SetByte(streamStart + 1, (byte)((payload.length >> 8) & 0xFF));

        int pos = streamStart + sizeof(ushort);
        for (int i = 0; i < payload.length; ++i)
        {
            wire.SetByte(pos++, payload.GetByte(i));
        }

        return wire;
    }

    public static BotRelayPacket BuildLatestPlayCaptureInvite104SyntheticWirePacket()
    {
        return BuildLatestPlayCaptureInviteWirePacket(McpLatestPlayInvitePipelinePrefix);
    }

    /// <summary>Play 2026-07-11 afternoon session: 103B shell, payload=91B (0x5B).</summary>
    /// <summary>Live Play 103B shell SendRelay tail after 6B junk (sender id omitted on wire).</summary>
    /// <summary>Live Play 2026-07-11 evening: host Create on server channel 1 (packed 0x0C at inner+8).</summary>
    public static readonly byte[] McpLatestPlayInvite103SendRelayTail =
    {
        0x04, 0x1E, 0x0C, 0x00, 0x00, 0x17, 0x59, 0x00, 0x00, 0x0C, 0x00,
        0xE6, 0x9D, 0x80, 0xE9, 0xB1, 0xBC, 0xE6, 0xA0, 0xBC, 0xE6, 0xA0, 0xBC,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    public static bool TryBuildMcpCapturedInvite103JunkPayload(out BotRelayPacket payload)
    {
        payload = default;
        payload.length = McpCapturedInvite104PopEventsJunkPrefix.Length + McpLatestPlayInvite103SendRelayTail.Length;
        int pos = 0;
        for (int i = 0; i < McpCapturedInvite104PopEventsJunkPrefix.Length; ++i)
        {
            payload.SetByte(pos++, McpCapturedInvite104PopEventsJunkPrefix[i]);
        }

        for (int i = 0; i < McpLatestPlayInvite103SendRelayTail.Length; ++i)
        {
            payload.SetByte(pos++, McpLatestPlayInvite103SendRelayTail[i]);
        }

        const int targetPayloadLength = 91;
        while (payload.length < targetPayloadLength)
        {
            payload.SetByte(payload.length, 0);
            payload.length++;
        }

        return payload.length == targetPayloadLength;
    }

    public static BotRelayPacket BuildLatestPlayCaptureInvite103WirePacket()
    {
        const int streamStart = 10;
        if (!TryBuildMcpCapturedInvite103JunkPayload(out var payload) || payload.IsEmpty)
        {
            throw new InvalidOperationException("Live 103B junk-prefix invite payload build failed.");
        }

        var wire = default(BotRelayPacket);
        wire.length = streamStart + sizeof(ushort) + payload.length;

        for (int i = 0; i < McpLatestPlayInvite103PipelinePrefix.Length; ++i)
        {
            wire.SetByte(i, McpLatestPlayInvite103PipelinePrefix[i]);
        }

        wire.SetByte(streamStart, (byte)(payload.length & 0xFF));
        wire.SetByte(streamStart + 1, (byte)((payload.length >> 8) & 0xFF));

        int pos = streamStart + sizeof(ushort);
        for (int i = 0; i < payload.length; ++i)
        {
            wire.SetByte(pos++, payload.GetByte(i));
        }

        return wire;
    }

    public static BotRelayPacket BuildLatestPlayCaptureInviteWirePacket(byte[] pipelinePrefix)
    {
        const int streamStart = 10;
        if (!TryBuildMcpCapturedInviteJunkPayload(out var payload) || payload.IsEmpty)
        {
            throw new InvalidOperationException("Live junk-prefix invite payload build failed.");
        }

        var wire = default(BotRelayPacket);
        wire.length = streamStart + sizeof(ushort) + payload.length;

        for (int i = 0; i < pipelinePrefix.Length; ++i)
        {
            wire.SetByte(i, pipelinePrefix[i]);
        }

        wire.SetByte(streamStart, (byte)(payload.length & 0xFF));
        wire.SetByte(streamStart + 1, (byte)((payload.length >> 8) & 0xFF));

        int pos = streamStart + sizeof(ushort);
        for (int i = 0; i < payload.length; ++i)
        {
            wire.SetByte(pos++, payload.GetByte(i));
        }

        return wire;
    }

    public static BotRelayPacket BuildMcpCapturedInvite104WirePacket()
    {
        if (!TryBuildMcpSessionInvite104Wire(out var wire, out _))
        {
            throw new InvalidOperationException("Live MCP invite wire build failed.");
        }

        return wire;
    }

    /// <summary>PopEvents payload with live 6B junk prefix before SendRelay header (inner=0 must fail strict decode).</summary>
    public static bool TryBuildMcpCapturedInviteJunkPayload(out BotRelayPacket payload)
    {
        payload = default;
        var text = new FixedString512Bytes(LiveHostUserId.ToString());
        if (!BotRelayCodec.TryBuildWorldInviteRelayApp(
                BuildSenderHeader(LiveHostUserId),
                NetworkRelayType.All,
                0,
                0u,
                0,
                in text,
                out var app) ||
            app.IsEmpty)
        {
            return false;
        }

        payload.length = McpCapturedInvite104PopEventsJunkPrefix.Length + app.length;
        int pos = 0;
        for (int i = 0; i < McpCapturedInvite104PopEventsJunkPrefix.Length; ++i)
        {
            payload.SetByte(pos++, McpCapturedInvite104PopEventsJunkPrefix[i]);
        }

        for (int i = 0; i < app.length; ++i)
        {
            payload.SetByte(pos++, app.GetByte(i));
        }

        return !payload.IsEmpty;
    }

    /// <summary>Extract 92B PopEvents payload from MCP-captured 104B invite shell (streamStart=10).</summary>
    public static bool TryGetMcpCapturedInvitePayloadFromWire(in BotRelayPacket wire, out BotRelayPacket payload)
    {
        payload = default;
        const int streamStart = 10;
        if (wire.length < streamStart + sizeof(ushort) + BotRelayMessageUtility.MinSendRelayInviteAppBytes)
        {
            return false;
        }

        int size = wire.GetByte(streamStart) | (wire.GetByte(streamStart + 1) << 8);
        int pos = streamStart + sizeof(ushort);
        if (size < 1 || size > wire.length - pos)
        {
            return false;
        }

        BotRelayWireBytes.CopyAppFromOffset(in wire, pos, size, out payload);
        return !payload.IsEmpty;
    }

    public static bool TryGetMcpCapturedInvitePayload(out BotRelayPacket payload)
    {
        var wire = BuildMcpCapturedInvite104WirePacket();
        return TryGetMcpCapturedInvitePayloadFromWire(in wire, out payload);
    }

    public static bool TryBuildStatusApp(out BotRelayPacket app)
    {
        app = default;
        using var packets = new NativeArray<BotRelayPacket>(1, Allocator.Temp);
        using var counts = new NativeArray<int>(1, Allocator.Temp);
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var send = BotRelaySlotOps.CreateCaptureSendBuffer(packets, counts, scratch);
        if (!BotRelayCodec.TryWriteStatus(ref send, 0) || counts[0] <= 0)
            return false;

        app = packets[0];
        return !app.IsEmpty;
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

    public static bool TryExtractLiveInviteWire(in BotRelayPacket wire, out BotRelayPacket app)
    {
        app = default;
        if (BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out app) && !app.IsEmpty)
            return BotRelayCodec.TryParseWorldInviteRelayApp(in app, out _);

        return false;
    }

    public static bool TryExtract(
        in BotRelayPacket wire,
        int inboundAppPayloadOffset,
        out BotRelayPacket app)
    {
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        return BotRelayWireBytes.TryExtractRelayAppPayload(
            in wire,
            scratch,
            0,
            default,
            inboundAppPayloadOffset,
            out app);
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
        TryBuildOutboundRelayTypeApp(NetworkRelayMessageType.Mismatch, out app);

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
        BotRelayMessageUtility.WriteRelayStatus(ref writer, channelStatus, in model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryBuildInboundHostCreateEchoApp(int channel, int channelFlag, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Create, model);
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
        writer.WritePackedUInt(remoteUserId, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    public static bool TryExtractRelayAppFromInboundWire(in BotRelayPacket wire, out BotRelayPacket app)
    {
        app = default;
        if (BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out app) && !app.IsEmpty)
            return true;

        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        return BotRelayWireBytes.TryExtractRelayAppPayload(
                   in wire,
                   scratch,
                   0,
                   default,
                   -1,
                   out app) &&
               !app.IsEmpty;
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
        writer.WritePackedInt((int)ClientRemotePlayerFlag.Creator, model);
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
        BuildSenderHeader(remoteUserId).Write(ref writer, model);
        if (writer.Length <= 0)
            return false;

        app.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            app.SetByte(i, scratch[i]);
        return true;
    }

    /// <summary>Server __WriteStatus: [type][channelFlag][userID].</summary>
    public static bool TryBuildInboundRelayStatusApp(int channelFlag, uint senderUserId, out BotRelayPacket app)
    {
        app = default;
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)NetworkRelayMessageType.Status, model);
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
        TryBuildInboundPlayApp(levelId, stage, senderId, default, default, out app);

    public static bool TryBuildInboundPlayApp(
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
        var play = new ClientMessagePlay
        {
            isRestart = false,
            levelID = levelId,
            stage = stage,
            levelName = levelName,
            sceneName = sceneName
        };
        writer.WritePackedInt((int)ClientMessageType.Play, model);
        writer.WritePackedInt((int)NetworkRelayType.Channel, model);
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

        int streamStart = BotRelayWireBytes.ResolvePopEventsStreamStart(in wire);
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

        int streamStart = BotRelayWireBytes.ResolvePopEventsStreamStart(in wire);
        int pos = streamStart;
        if (!__TryAppendPopEventsFrame(ref wire, ref pos, in app0))
            return false;

        if (!__TryAppendPopEventsFrame(ref wire, ref pos, in app1))
            return false;

        wire.length = math.max(wire.length, pos);
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
