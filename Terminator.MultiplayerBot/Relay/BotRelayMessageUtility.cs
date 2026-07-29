using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Bot Relay 热路径消息的读写权威：字段顺序对齐 ZGUP（SendRelay / ReplyMessages / NetworkRelayMessageType）。
/// 放在 Terminator.MultiplayerBot 程序集内；ZGUP protocol struct 变更时只改本类 + Regression fixture。
/// </summary>
internal static class BotRelayMessageUtility
{
    /// <summary>Invite 尾部 FixedBytes80 不足 80 字节时不读（PopEvents 帧可能截断尾部）。</summary>
    public struct InviteReadOptions
    {
        public bool allowTruncatedHeader;

        public static InviteReadOptions Default => new InviteReadOptions { allowTruncatedHeader = true };
        public static InviteReadOptions RequireFullHeader => new InviteReadOptions { allowTruncatedHeader = false };
    }
    public const int MinSendRelayInviteAppBytes = 24;

    public const int SendRelayHeaderMinBytes = 3;

    public static void WriteSendRelayHeader(
        ref DataStreamWriter writer,
        int messageType,
        NetworkRelayType relayType,
        uint senderId,
        in StreamCompressionModel model)
    {
        writer.WritePackedInt(messageType, model);
        writer.WritePackedInt((int)relayType, model);
        writer.WritePackedUInt(senderId, model);
    }

    public static bool TryReadSendRelayHeader(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out int messageType,
        out NetworkRelayType relayType,
        out uint senderId)
    {
        messageType = 0;
        relayType = default;
        senderId = 0;
        if (reader.Length - reader.GetBytesRead() < SendRelayHeaderMinBytes)
            return false;

        messageType = reader.ReadPackedInt(model);
        relayType = (NetworkRelayType)reader.ReadPackedInt(model);
        senderId = reader.ReadPackedUInt(model);
        return true;
    }

    public static void WriteInviteBody(
        ref DataStreamWriter writer,
        uint levelID,
        int stage,
        int squadChannel,
        in FixedString512Bytes text,
        in FixedBytes80 header,
        in StreamCompressionModel model)
    {
        writer.WritePackedUInt(levelID, model);
        writer.WritePackedInt(stage, model);
        writer.WritePackedInt(squadChannel, model);
        writer.WriteFixedString512(text);
        header.Write(ref writer);
    }

    /// <summary>Field order matches ZGUP <see cref="ReplyMessages.Invite"/> constructor.</summary>
    public static bool TryReadInviteBody(
        ref DataStreamReader reader,
        NetworkRelayType relayType,
        uint senderId,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        invite = default;
        if (reader.Length - reader.GetBytesRead() < 1)
            return false;

        invite.type = relayType;
        invite.id = senderId;
        invite.levelID = reader.ReadPackedUInt(model);

        if (reader.Length - reader.GetBytesRead() < 1)
            return false;
        invite.stage = reader.ReadPackedInt(model);
        if (invite.stage < 0)
            return false;

        if (reader.Length - reader.GetBytesRead() < 1)
            return false;
        invite.channel = reader.ReadPackedInt(model);
        // Squad relay channel index is 0-based (first Create → channel 0); only CHANNEL_NULL (-1) is invalid.
        if (invite.channel < 0)
            return false;

        invite.text = default;
        if (reader.Length - reader.GetBytesRead() >= 2)
        {
            if (!__TryReadFixedString512(ref reader, out invite.text))
                return false;
        }

        invite.header = default;
        int remaining = reader.Length - reader.GetBytesRead();
        if (remaining >= ClientHeaderFixedBytes)
            invite.header = new FixedBytes80(ref reader);
        else if (!options.allowTruncatedHeader && remaining > 0)
            return false;

        return true;
    }

    private static bool __TryReadFixedString512(ref DataStreamReader reader, out FixedString512Bytes text)
    {
        text = default;
        int remaining = reader.Length - reader.GetBytesRead();
        if (remaining < 2)
            return false;

        int saved = reader.GetBytesRead();
        ushort lengthBytes = reader.ReadUShort();
        reader.SeekSet(saved);

        const int maxUtf8Bytes = 509;
        if (lengthBytes > maxUtf8Bytes)
            return false;

        if (remaining < 2 + lengthBytes)
            return false;

        text = reader.ReadFixedString512();
        return true;
    }

    public static void WriteSendRelayInvite(
        ref DataStreamWriter writer,
        NetworkRelayType relayType,
        uint senderId,
        uint levelID,
        int stage,
        int squadChannel,
        in FixedString512Bytes text,
        in FixedBytes80 header,
        in StreamCompressionModel model)
    {
        WriteSendRelayHeader(ref writer, (int)ReplyMessageType.Invite, relayType, senderId, in model);
        // NetworkRelayServer writes the relay envelope before the Invite body. Preserve that
        // packet boundary in locally-built probes so the reader's server-first Flush path is exact.
        writer.Flush();
        WriteInviteBody(ref writer, levelID, stage, squadChannel, in text, in header, in model);
    }

    /// <summary>
    /// L1: rebuild SendRelay-shaped Invite for SlotInbox (type prefix at byte 0).
    /// Uses only fields parsed from <paramref name="app"/> — no shell byte scan.
    /// </summary>
    public static bool TryCanonicalizeInviteForInbox(
        in BotRelayPacket app,
        out BotRelayPacket sendRelayApp)
    {
        sendRelayApp = default;
        if (app.IsEmpty)
        {
            return false;
        }

        var options = InviteReadOptions.Default;
        if (BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out _))
        {
            sendRelayApp = app;
            return true;
        }

        if (!__TryReadInviteEitherShape(in app, out ReplyMessages.Invite invite, options))
        {
            return false;
        }

        if (!__TryPrepareInviteForCanonicalWrite(ref invite, in app, default))
        {
            return false;
        }

        return __TryWriteCanonicalizedSendRelayApp(in invite, out sendRelayApp);
    }

    /// <summary>
    /// Downstream inbox/peel expects SendRelay-shaped Invite (type 104 prefix). Live pipeline shells may
    /// extract Reply-payload slices after inner offset alignment — rebuild SendRelay bytes when needed.
    /// </summary>
    public static bool TryCanonicalizeInviteAppToSendRelay(
        in BotRelayPacket app,
        out BotRelayPacket sendRelayApp)
    {
        return TryCanonicalizeInviteForInbox(in app, out sendRelayApp);
    }

    public static bool TryCanonicalizeInviteAppToSendRelay(
        in BotRelayPacket app,
        in BotRelayPacket shellWire,
        out BotRelayPacket sendRelayApp)
    {
        sendRelayApp = default;
        if (app.IsEmpty)
        {
            return false;
        }

        if (__TryCanonicalizeStrictInviteWithShellRecovery(in app, in shellWire, out sendRelayApp))
        {
            return true;
        }

        var options = InviteReadOptions.Default;
        if (!__TryReadInviteEitherShape(in app, out ReplyMessages.Invite invite, options))
        {
            return false;
        }

        if (!__TryPrepareInviteForCanonicalWrite(ref invite, in app, in shellWire))
        {
            return false;
        }

        invite.type = NetworkRelayType.All;
        return __TryWriteCanonicalizedSendRelayApp(in invite, out sendRelayApp);
    }

    public static bool TryCanonicalizeInviteAppToSendRelay(
        in BotRelayPacket app,
        in BotRelayPacket shellWire,
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket sendRelayApp)
    {
        sendRelayApp = default;
        if (app.IsEmpty)
        {
            return false;
        }

        if (__TryCanonicalizeStrictInviteWithShellRecovery(in app, in shellWire, out sendRelayApp))
        {
            return true;
        }

        var options = InviteReadOptions.Default;
        if (!__TryReadInviteEitherShape(in app, out ReplyMessages.Invite invite, options))
        {
            return false;
        }

        if (!__TryPrepareInviteForCanonicalWrite(ref invite, in app, in shellWire, ref catalog))
        {
            return false;
        }

        invite.type = NetworkRelayType.All;
        return __TryWriteCanonicalizedSendRelayApp(in invite, out sendRelayApp);
    }

    /// <summary>
    /// Pipeline peel may already yield strict SendRelay bytes with squad channel 0 while the outer shell
    /// still carries the junk anchor for server channel 1 — rewrite instead of returning the raw app.
    /// </summary>
    private static bool __TryCanonicalizeStrictInviteWithShellRecovery(
        in BotRelayPacket app,
        in BotRelayPacket shellWire,
        out BotRelayPacket sendRelayApp)
    {
        sendRelayApp = default;
        if (!BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out ReplyMessages.Invite invite))
        {
            return false;
        }

        if (invite.channel != 0 || shellWire.IsEmpty)
        {
            sendRelayApp = app;
            return true;
        }

        BotRelayInviteChannelAuthority.TryResolveSquadChannel(ref invite, in app, in shellWire);
        if (invite.channel == 0)
        {
            sendRelayApp = app;
            return true;
        }

        invite.type = NetworkRelayType.All;
        return __TryWriteCanonicalizedSendRelayApp(in invite, out sendRelayApp);
    }

    private static bool __TryPrepareInviteForCanonicalWrite(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire)
    {
        if (invite.channel < 0 || invite.stage < 0)
        {
            return false;
        }

        BotRelayInviteChannelAuthority.TryResolveSquadChannel(ref invite, in app, in shellWire);

        if (invite.id < BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
        {
            __TryRecoverInviteSenderFromInviteText(ref invite);
        }

        if (invite.id < BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
        {
            BotRelayInviteDecodeOps.TryRecoverInviteSenderFromAsciiDigitTail(in app, out uint tailSender);
            if (tailSender >= BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
            {
                invite.id = tailSender;
            }
        }

        if (invite.id < BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
        {
            if (BotRelayInviteDecodeOps.TryRecoverInviteSenderFromStructuralInviteApp(in app, out uint recoveredFromApp) &&
                recoveredFromApp >= BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
            {
                invite.id = recoveredFromApp;
            }
            else
            {
                __TryRecoverInviteSenderFromShell(in shellWire, ref invite);
            }
        }

        return invite.channel >= 0 && invite.stage >= 0;
    }

    private static bool __TryPrepareInviteForCanonicalWrite(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire,
        ref BotRelayCatalogBlob catalog)
    {
        if (!__TryPrepareInviteForCanonicalWrite(ref invite, in app, in shellWire))
        {
            return false;
        }

        if (invite.channel == 0)
        {
            var session = default(BotSessionState);
            session.pendingHostSquadChannel = BotRelayInviteChannelAuthority.UnsetChannel;
            BotRelayInviteChannelAuthority.TryResolveSquadChannel(
                ref invite,
                in app,
                in shellWire,
                in session,
                ref catalog);
        }

        if (invite.id >= BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
        {
            return true;
        }

        if (catalog.IsValid)
        {
            __TryRecoverInviteSender(ref invite, in shellWire, ref catalog);
        }

        return invite.channel >= 0 && invite.stage >= 0;
    }

    private static bool __TryWriteCanonicalizedSendRelayApp(
        in ReplyMessages.Invite invite,
        out BotRelayPacket sendRelayApp)
    {
        sendRelayApp = default;
        unsafe
        {
            byte* buffer = stackalloc byte[BotRelayPacket.MaxPayloadSize];
            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                BotRelayPacket.MaxPayloadSize,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var writer = new DataStreamWriter(bytes);
            var model = StreamCompressionModel.Default;
            WriteSendRelayInvite(
                ref writer,
                invite.type,
                invite.id,
                invite.levelID,
                invite.stage,
                invite.channel,
                in invite.text,
                in invite.header,
                in model);

            if (writer.Length <= 0 || writer.Length > BotRelayPacket.MaxPayloadSize)
            {
                return false;
            }

            sendRelayApp.length = writer.Length;
            for (int i = 0; i < writer.Length; ++i)
            {
                sendRelayApp.SetByte(i, buffer[i]);
            }
        }

        return !sendRelayApp.IsEmpty;
    }

    private static bool __TryRecoverInviteSenderFromShell(
        in BotRelayPacket shellWire,
        ref ReplyMessages.Invite invite)
    {
        if (invite.id >= BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
        {
            return true;
        }

        if (!BotRelayInviteDecodeOps.TryRecoverInviteSenderFromPipelineShell(in shellWire, out uint recoveredSender))
        {
            return false;
        }

        invite.id = recoveredSender;
        return true;
    }

    private static void __TryRecoverInviteSenderFromInviteText(ref ReplyMessages.Invite invite)
    {
        if (invite.id >= BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
        {
            return;
        }

        if (invite.text.IsEmpty)
        {
            return;
        }

        uint parsed = 0;
        uint place = 1;
        bool hasDigit = false;
        for (int i = invite.text.Length - 1; i >= 0; --i)
        {
            ushort codeUnit = invite.text[i];
            if (codeUnit < (ushort)'0' || codeUnit > (ushort)'9')
            {
                if (hasDigit)
                {
                    break;
                }

                continue;
            }

            hasDigit = true;
            parsed += (uint)(codeUnit - (ushort)'0') * place;
            place *= 10;
        }

        if (parsed >= BotRelayInviteDecodeOps.MinPlausibleInviteSenderId &&
            parsed < 2_000_000_000u)
        {
            invite.id = parsed;
        }
    }

    private static bool __TryRecoverInviteSender(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket shellWire,
        ref BotRelayCatalogBlob catalog)
    {
        if (invite.id >= BotRelayInviteDecodeOps.MinPlausibleInviteSenderId)
        {
            return true;
        }

        if (BotRelayInviteDecodeOps.TryRecoverInviteSenderFromPipelineShell(in shellWire, out uint recoveredSender))
        {
            invite.id = recoveredSender;
            return true;
        }

        if (!catalog.IsValid)
        {
            return false;
        }

        if (!BotRelayInviteDecodeOps.TryGetFirstConnectClientWire(ref catalog, out var connectWire))
        {
            return false;
        }

        if (!BotRelayInviteDecodeOps.TryRecoverInviteSenderFromPipelineShell(in connectWire, out recoveredSender))
        {
            return false;
        }

        invite.id = recoveredSender;
        return true;
    }

    public static bool TryReadInviteFromBytes(
        NativeArray<byte> bytes,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        invite = default;
        if (!bytes.IsCreated || bytes.Length < MinSendRelayInviteAppBytes)
        {
            return false;
        }

        var packet = default(BotRelayPacket);
        packet.length = bytes.Length;
        for (int i = 0; i < bytes.Length; ++i)
        {
            packet.SetByte(i, bytes[i]);
        }

        return TryReadInviteFromPacket(in packet, out invite, options);
    }

    public static bool TryReadInviteFromPacket(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        return __TryReadInviteEitherShape(in app, out invite, options);
    }

    private static bool __TryReadInviteEitherShape(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite,
        InviteReadOptions options)
    {
        invite = default;
        if (BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out invite))
        {
            BotRelayInviteChannelAuthority.TryResolveSquadChannel(ref invite, in app, default);
            return true;
        }

        if (BotRelayInviteDecodeOps.TryReadJunkAnchoredPipelineInvite(in app, out invite))
        {
            return invite.channel >= 0 && invite.stage >= 0;
        }

        if (__TryReadJunkAnchoredAtAnyOffset(in app, out invite))
        {
            return invite.channel >= 0 && invite.stage >= 0;
        }

        if (TryReadSendRelayInviteFromPacket(in app, out invite, options))
        {
            BotRelayInviteChannelAuthority.TryResolveSquadChannel(ref invite, in app, default);
            return true;
        }

        if (!TryReadReplyPayloadInviteFromPacket(in app, out invite, options))
        {
            return false;
        }

        BotRelayInviteChannelAuthority.TryResolveSquadChannel(ref invite, in app, default);
        return true;
    }

    private static bool __TryReadJunkAnchoredAtAnyOffset(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        if (app.IsEmpty || app.length < MinSendRelayInviteAppBytes)
        {
            return false;
        }

        if (BotRelayInviteDecodeOps.TryReadJunkAnchoredPipelineInvite(in app, out invite))
        {
            return true;
        }

        int inner = BotRelayWireBytes.InvitePipelineJunkAnchoredInnerOffset;
        if (inner <= 0 || inner >= app.length)
        {
            return false;
        }

        BotRelayWireBytes.CopyAppFromOffset(
            in app,
            inner,
            app.length - inner,
            out var innerSlice);
        return BotRelayInviteDecodeOps.TryReadJunkAnchoredPipelineInvite(in innerSlice, out invite);
    }

    /// <summary>
    /// Parses a SendRelay Invite broadcast by the relay server:
    /// [SendRelay header][Invite body fields]. The server consumes Invite.Write reply-header
    /// (type + relay) before SendRelay; the bot only sees body fields after sender id.
    /// </summary>
    public static bool TryReadSendRelayInvite(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        invite = default;
        var originalReader = reader;
        if (!TryReadSendRelayHeader(
                ref reader,
                in model,
                out int messageType,
                out var relayType,
                out uint senderId) ||
            messageType != (int)ReplyMessageType.Invite)
        {
            return false;
        }

        // NetworkRelayServer.SendRelay copies the Invite body after Invite.WriteReplyHeader
        // flushed it. Prefer that real server boundary: padded live frames can make the
        // continuous interpretation look superficially valid with a wrong channel.
        var continuousReader = reader;
        reader.Flush();
        if (TryReadInviteBody(
                ref reader,
                relayType,
                senderId,
                in model,
                out invite,
                options))
        {
            return true;
        }

        // Catalog-built probes write the relay header and Invite body as one compressed stream.
        // Keep that compatibility form only after the server's flushed-boundary form.
        reader = continuousReader;
        if (TryReadInviteBody(
                ref reader,
                relayType,
                senderId,
                in model,
                out invite,
                options))
        {
            return true;
        }

        // Restore a deterministic reader position for callers that inspect it after a failed
        // parse. The two supported representations above have both been exhausted.
        reader = originalReader;
        return false;
    }

    internal static bool TryReadSendRelayInviteFromBytes(
        NativeArray<byte> bytes,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        invite = default;
        if (!bytes.IsCreated || bytes.Length < MinSendRelayInviteAppBytes)
            return false;

        var reader = new DataStreamReader(bytes);
        return TryReadSendRelayInvite(ref reader, in model, out invite, options);
    }

    private const int MaxPackedIntProbePadBytes = 8;

    /// <summary>
    /// Reply payload Invite (after outer type=104): [relay channel][sender id][Invite body…].
    /// Matches <see cref="BotRelayInbox"/> / ZGUP <c>ReplyMessages.Invite</c> constructor path.
    /// </summary>
    public static bool TryReadReplyPayloadInviteFromPacket(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        return TryReadReplyPayloadInviteFromPacket(
            in app,
            StreamCompressionModel.Default,
            out invite,
            options);
    }

    /// <summary>
    /// Reply payload Invite (after outer type=104): [relay channel][sender id][Invite body…].
    /// Matches <see cref="BotRelayInbox"/> / ZGUP <c>ReplyMessages.Invite</c> constructor path.
    /// </summary>
    public static bool TryReadReplyPayloadInviteFromPacket(
        in BotRelayPacket app,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        invite = default;
        if (app.IsEmpty || app.length < MinSendRelayInviteAppBytes)
            return false;

        return __TryReadInviteFromPacketBuffer(
            in app,
            in model,
            replyPayloadShape: true,
            out invite,
            options);
    }

    /// <summary>Burst-safe: reads directly from <see cref="BotRelayPacket.data"/> without Temp allocations.</summary>
    public static bool TryReadSendRelayInviteFromPacket(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        return TryReadSendRelayInviteFromPacket(
            in app,
            StreamCompressionModel.Default,
            out invite,
            options);
    }

    /// <summary>Burst-safe: reads directly from <see cref="BotRelayPacket.data"/> without Temp allocations.</summary>
    public static bool TryReadSendRelayInviteFromPacket(
        in BotRelayPacket app,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        invite = default;
        if (app.IsEmpty || app.length < MinSendRelayInviteAppBytes)
            return false;

        return __TryReadInviteFromPacketBuffer(
            in app,
            in model,
            replyPayloadShape: false,
            out invite,
            options);
    }

    private static bool __TryReadInviteFromPacketBuffer(
        in BotRelayPacket app,
        in StreamCompressionModel model,
        bool replyPayloadShape,
        out ReplyMessages.Invite invite,
        InviteReadOptions options)
    {
        invite = default;
        unsafe
        {
            int capacity = app.length + MaxPackedIntProbePadBytes;
            byte* buffer = stackalloc byte[capacity];
            for (int i = 0; i < app.length; ++i)
            {
                buffer[i] = app.GetByte(i);
            }

            for (int i = app.length; i < capacity; ++i)
            {
                buffer[i] = 0;
            }

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                app.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            if (replyPayloadShape)
            {
                if (reader.Length - reader.GetBytesRead() < 2)
                    return false;

                int channel = reader.ReadPackedInt(model);
                if (reader.HasFailedReads)
                    return false;

                uint senderId = reader.ReadPackedUInt(model);
                if (reader.HasFailedReads)
                    return false;

                return TryReadInviteBody(
                    ref reader,
                    (NetworkRelayType)channel,
                    senderId,
                    in model,
                    out invite,
                    options);
            }

            return TryReadSendRelayInvite(ref reader, in model, out invite, options);
        }
    }

    public static bool TryReadSendRelayInvite(
        in BotRelayPacket app,
        NativeArray<byte> scratch,
        int scratchOffset,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite,
        InviteReadOptions options = default)
    {
        invite = default;
        if (app.IsEmpty || app.length < MinSendRelayInviteAppBytes)
            return false;
        if (!scratch.IsCreated || scratchOffset < 0 || scratchOffset + app.length > scratch.Length)
            return false;
        if (!app.TryCopyBytesTo(scratch, scratchOffset))
            return false;

        return TryReadSendRelayInviteFromBytes(
            scratch.GetSubArray(scratchOffset, app.length),
            in model,
            out invite,
            options);
    }

    public static void WriteRelayStatus(
        ref DataStreamWriter writer,
        int channelStatus,
        in StreamCompressionModel model)
    {
        writer.WritePackedInt((int)NetworkRelayMessageType.Status, model);
        writer.WritePackedInt(channelStatus, model);
    }

    public static void WriteRelayMismatch(ref DataStreamWriter writer, in StreamCompressionModel model)
    {
        writer.WritePackedInt((int)NetworkRelayMessageType.Mismatch, model);
    }

    public static void WriteRelayLeave(ref DataStreamWriter writer, in StreamCompressionModel model)
    {
        writer.WritePackedInt((int)NetworkRelayMessageType.Leave, model);
    }

    public static void WriteClientMatchToSend(
        ref DataStreamWriter writer,
        in NetworkRelayMatch match,
        in StreamCompressionModel model)
    {
        writer.WritePackedInt((int)NetworkRelayMessageType.Match, model);
        match.Write(ref writer, in model);
    }

    public static bool TryReadServerMatchNotification(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out int matchId,
        out int level)
    {
        matchId = 0;
        level = 0;
        // Packed ints are Huffman bit-packed, not byte-aligned. The live first match commonly
        // encodes [Match=11, matchID=1, level=0] into only two bytes total, while both fields
        // after the type are still readable from the remaining bits. A byte-count precheck
        // therefore rejects a valid Match and leaves the bot in the temporary squad without
        // matchPaired, so it never publishes PlayerProperty/Status for level entry.
        matchId = reader.ReadPackedInt(model);
        level = reader.ReadPackedInt(model);
        return !reader.HasFailedReads;
    }

    public static void WriteSquadJoin(
        ref DataStreamWriter writer,
        uint squadInviteId,
        in StreamCompressionModel model)
    {
        writer.WritePackedInt((int)NetworkRelayMessageType.Join, model);
        writer.WritePackedInt((int)squadInviteId, model);
    }

    public static void WriteApplyMatch(ref DataStreamWriter writer)
    {
        writer.WriteReplyHeader((int)ClientMessageType.ApplyMatch, NetworkRelayType.Channel);
    }

    public static void WritePlay(ref DataStreamWriter writer, in ClientMessagePlay play)
    {
        writer.WriteReplyHeader((int)ClientMessageType.Play, NetworkRelayType.Channel);
        play.Write(ref writer);
    }

    public static int RemainingBytes(ref DataStreamReader reader) =>
        reader.Length - reader.GetBytesRead();

    /// <summary>Join/Create remote embed uses FixedBytes80; live frames may truncate the tail.</summary>
    public const int ClientHeaderFixedBytes = 80;

    public static int DecodeChannelStatus(int channelFlag) =>
        (int)((uint)channelFlag >> (int)NetworkRelayChannelFlag.ShiftToStatus);

    public static bool TryReadOptionalRemoteClientHeader(
        uint remoteUserID,
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out ClientHeader header)
    {
        header = default;
        if (RemainingBytes(ref reader) < ClientHeaderFixedBytes)
            return false;

        header = new ClientHeader(remoteUserID, ref reader, model);
        return true;
    }
}
