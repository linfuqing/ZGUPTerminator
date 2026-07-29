using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Networking.Transport;
using ZG;

[BurstCompile]
internal static class BotRelayWireBytes
{
    public const int EmptyWireIndex = 0;

    /// <summary>Captured outbound shorter than this is payload-only app bytes (~14B Join), not a transport shell.</summary>
    public const int MinRelayTransportShellBytes = 16;

    /// <summary>Large inbound server→bot relay payloads (deferred Invite shell) exceed this.</summary>
    public const int MinOutboundTransportWireBytes = 32;

    public static bool IsEmpty(ref BotRelayWireBlob wire) => wire.length <= 0;

    public static bool Equals(ref BotRelayWireBlob left, ref BotRelayWireBlob right)
    {
        if (left.length != right.length)
            return false;

        if (left.length <= 0)
            return true;

        for (int i = 0; i < left.length; ++i)
        {
            if (left.bytes[i] != right.bytes[i])
                return false;
        }

        return true;
    }

    public static bool Equals(ref BotRelayWireBlob wire, in BotRelayPacket packet)
    {
        if (wire.length != packet.length)
            return false;

        if (wire.length <= 0)
            return true;

        for (int i = 0; i < wire.length; ++i)
        {
            if (wire.bytes[i] != packet.GetByte(i))
                return false;
        }

        return true;
    }

    public static bool Equals(in BotRelayPacket left, in BotRelayPacket right)
    {
        if (left.length != right.length)
            return false;

        if (left.length <= 0)
            return true;

        for (int i = 0; i < left.length; ++i)
        {
            if (left.GetByte(i) != right.GetByte(i))
                return false;
        }

        return true;
    }

    public static bool AppMatches(in BotRelayPacket left, ref BotRelayWireBlob right) =>
        !IsEmpty(ref right) && Equals(ref right, in left);

    public static bool AppSameKind(in BotRelayPacket left, ref BotRelayWireBlob right)
    {
        if (IsEmpty(ref right) || left.length != right.length)
            return false;

        int prefix = math.min(2, left.length);
        for (int i = 0; i < prefix; ++i)
        {
            if (left.GetByte(i) != right.bytes[i])
                return false;
        }

        return true;
    }

    public static bool LooksLikeConnectHandshake(in BotRelayPacket wire)
    {
        if (wire.length < 5)
            return false;

        return wire.GetByte(0) == (byte)'U' &&
               wire.GetByte(1) == (byte)'T' &&
               wire.GetByte(2) == (byte)'P' &&
               wire.GetByte(3) == 0x01 &&
               (wire.GetByte(4) == 0x01 || wire.GetByte(4) == 0x02);
    }

    public static bool LooksLikeConnectHandshake(ref BotRelayWireBlob wire)
    {
        if (wire.length < 5)
            return false;

        return wire.bytes[0] == (byte)'U' &&
               wire.bytes[1] == (byte)'T' &&
               wire.bytes[2] == (byte)'P' &&
               wire.bytes[3] == 0x01 &&
               (wire.bytes[4] == 0x01 || wire.bytes[4] == 0x02);
    }

    public static bool TryCopyWireToPacket(ref BotRelayWireBlob wire, out BotRelayPacket packet)
    {
        packet = default;
        if (wire.length <= 0 || wire.length > BotRelayPacket.MaxPayloadSize)
            return false;

        packet.length = wire.length;
        for (int i = 0; i < wire.length; ++i)
            packet.SetByte(i, wire.bytes[i]);
        return true;
    }

    public static bool TryWriteTransportFrame(
        NativeArray<byte> scratch,
        int scratchOffset,
        in BotRelayPacket app,
        out int framedLength)
    {
        framedLength = 0;
        if (app.IsEmpty || !scratch.IsCreated)
            return false;

        framedLength = sizeof(ushort) + app.length;
        if (scratchOffset < 0 || scratchOffset + framedLength > scratch.Length)
            return false;

        scratch[scratchOffset] = (byte)(app.length & 0xff);
        scratch[scratchOffset + 1] = (byte)(app.length >> 8);
        for (int i = 0; i < app.length; ++i)
            scratch[scratchOffset + sizeof(ushort) + i] = app.GetByte(i);
        return true;
    }

    public static bool TryApplySessionPatch(
        ref BotRelayPacket outbound,
        ref BotRelayWireBlob connectTemplate,
        ref BotRelayWireBlob capturedHello,
        in BotRelayPacket liveHello)
    {
        if (outbound.IsEmpty || connectTemplate.length <= 0 || capturedHello.length <= 0 || liveHello.IsEmpty)
            return false;

        if (Equals(ref capturedHello, in liveHello))
            return true;

        int n = math.min(
            outbound.length,
            math.min(connectTemplate.length, math.min(capturedHello.length, liveHello.length)));

        for (int i = 0; i < n; ++i)
        {
            byte ch = capturedHello.bytes[i];
            byte lh = liveHello.GetByte(i);
            if (ch == lh)
                continue;

            byte c = connectTemplate.bytes[i];
            byte o = outbound.GetByte(i);
            if (c == o)
                continue;

            outbound.SetByte(i, (byte)(o + (lh - ch)));
        }

        return true;
    }

    public static bool TryEmbedLiveApp(
        ref BotRelayPacket wirePacket,
        int payloadOffset,
        in BotRelayPacket liveApp,
        ref BotRelayWireBlob capturedApp,
        NativeArray<byte> scratch,
        int scratchOffset)
    {
        if (payloadOffset < 0)
            return false;

        if (!TryWriteTransportFrame(scratch, scratchOffset, in liveApp, out int framedLength))
            return false;

        if (payloadOffset + framedLength > wirePacket.length)
            return false;

        if (__FramedRegionMatches(in wirePacket, payloadOffset, scratch, scratchOffset, framedLength))
            return true;

        for (int i = 0; i < framedLength; ++i)
            wirePacket.SetByte(payloadOffset + i, scratch[scratchOffset + i]);

        return true;
    }

    private static bool __FramedRegionMatches(
        in BotRelayPacket wirePacket,
        int payloadOffset,
        NativeArray<byte> scratch,
        int scratchOffset,
        int framedLength)
    {
        for (int i = 0; i < framedLength; ++i)
        {
            if (wirePacket.GetByte(payloadOffset + i) != scratch[scratchOffset + i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Unpacks one PopEvents ushort frame at a wire-catalog-measured byte offset.
    /// Mirrors <see cref="NetworkServerPopEventsJob{T}"/> — no subsequence scanning.
    /// </summary>
    public static bool TryUnpackPopEventsFrameAtOffset(
        in BotRelayPacket wire,
        int frameOffset,
        out BotRelayPacket app)
    {
        app = default;
        if (frameOffset < 0 || frameOffset + sizeof(ushort) >= wire.length)
            return false;

        int size = wire.GetByte(frameOffset) | (wire.GetByte(frameOffset + 1) << 8);
        int frameLength = sizeof(ushort) + size;
        if (size < 1 || frameOffset + frameLength > wire.length)
            return false;

        __CopyAppFromOffset(in wire, frameOffset, frameLength, out var frameSlice);
        return TryUnpackPopEventsListenFrame(in frameSlice, out app) && !app.IsEmpty;
    }

    /// <summary>
    /// Validates relay app embedded at a Connect-time catalog offset (PopEvents ushort framing).
    /// </summary>
    public static bool TryReadFramedRelayAppAtOffset(
        in BotRelayPacket wire,
        int embedPayloadOffset,
        int expectedMessageType,
        out BotRelayPacket app)
    {
        app = default;
        if (!TryUnpackPopEventsFrameAtOffset(in wire, embedPayloadOffset, out app))
            return false;

        return TryReadRelayMessageTypeHeader(in app, out int type) &&
               type == expectedMessageType;
    }

    /// <summary>
    /// Validates Join relay app embedded at the offset returned by
    /// <see cref="TryResolveJoinEmbedPayloadOffset"/> (Connect-time WireCatalog measurement).
    /// </summary>
    public static bool TryReadFramedJoinRelayAppAtOffset(
        in BotRelayPacket wire,
        int embedPayloadOffset,
        out BotRelayPacket joinApp) =>
        TryReadFramedRelayAppAtOffset(
            in wire,
            embedPayloadOffset,
            (int)NetworkRelayMessageType.Join,
            out joinApp);

    /// <summary>
    /// Resolves catalog Join embed offset then validates PopEvents framing — for tests only.
    /// Hot path must pass the offset already used by <see cref="TryEmbedLiveApp"/>.
    /// </summary>
    public static bool TryValidateJoinOutboundWireAtCatalogOffset(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket wire,
        out BotRelayPacket joinApp)
    {
        joinApp = default;
        if (!TryResolveJoinEmbedPayloadOffset(ref catalog, in wire, out int offset))
            return false;

        return TryReadFramedJoinRelayAppAtOffset(in wire, offset, out joinApp);
    }

    [System.Obsolete("Use TryReadFramedJoinRelayAppAtOffset with catalog-measured embed offset.")]
    public static bool TryReadEmbeddedJoinRelayApp(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket wire,
        out BotRelayPacket joinApp) =>
        TryValidateJoinOutboundWireAtCatalogOffset(ref catalog, in wire, out joinApp);

    /// <summary>
    /// Connect-time catalog frame offset + template app length — no runtime subsequence scan.
    /// </summary>
    public static bool TryResolveCatalogFrameEmbedOffset(
        ref BotRelayCatalogBlob catalog,
        int catalogFrameOffset,
        BotRelayCatalogPacketSlot appSlot,
        in BotRelayPacket shell,
        out int payloadOffset)
    {
        payloadOffset = -1;
        if (catalogFrameOffset < 0 || catalogFrameOffset >= shell.length)
            return false;

        ref var appTemplate = ref GetTemplate(ref catalog, appSlot);
        if (!TryCopyWireToPacket(ref appTemplate, out var appPacket))
            return false;

        int framedLength = sizeof(ushort) + appPacket.length;
        if (catalogFrameOffset + framedLength > shell.length)
            return false;

        payloadOffset = catalogFrameOffset;
        return true;
    }

    public static bool TryResolveJoinEmbedPayloadOffset(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket shell,
        out int payloadOffset) =>
        TryResolveCatalogFrameEmbedOffset(
            ref catalog,
            catalog.squadJoinPayloadOffset,
            BotRelayCatalogPacketSlot.SquadJoinApp,
            in shell,
            out payloadOffset);

    public static bool TryResolveStatusEmbedPayloadOffset(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket shell,
        out int payloadOffset) =>
        TryResolveCatalogFrameEmbedOffset(
            ref catalog,
            catalog.statusOutboundPayloadOffset,
            BotRelayCatalogPacketSlot.StatusApp,
            in shell,
            out payloadOffset);

    public static bool TryResolveMatchEmbedPayloadOffset(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket shell,
        out int payloadOffset) =>
        TryResolveCatalogFrameEmbedOffset(
            ref catalog,
            catalog.matchOutboundPayloadOffset,
            BotRelayCatalogPacketSlot.MatchApp,
            in shell,
            out payloadOffset);

    public static ref BotRelayWireBlob GetWire(ref BotRelayCatalogBlob catalog, int wireIndex)
    {
        if (wireIndex < 0 || wireIndex >= catalog.wires.Length)
            wireIndex = EmptyWireIndex;

        return ref catalog.wires[wireIndex];
    }

    public static ref BotRelayWireBlob GetTemplate(ref BotRelayCatalogBlob catalog, BotRelayCatalogPacketSlot slot)
    {
        int slotIndex = (int)slot;
        if (slotIndex < 0 || slotIndex >= catalog.templateWireIndex.Length)
            return ref GetWire(ref catalog, EmptyWireIndex);

        return ref GetWire(ref catalog, catalog.templateWireIndex[slotIndex]);
    }

    /// <summary>
    /// Join listen inject must use Join/Connect transport shape — not Status/Match/Invite outbound shells.
    /// Connect handshake client wires are for Connect inject only — PopEvents misreads them as Join outbound.
    /// </summary>
    public static bool IsJoinCompatibleOutboundShell(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket wire)
    {
        if (wire.IsEmpty ||
            LooksLikeConnectHandshake(in wire) ||
            IsLinkAckCatalogTemplateWire(ref catalog, in wire) ||
            ShellCarriesInviteRelayApp(ref catalog, in wire) ||
            IsConnectClientCapturedWire(ref catalog, in wire))
            return false;

        if (wire.length <= MinRelayTransportShellBytes)
            return false;

        ref var statusOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.StatusOutbound);
        ref var matchOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.MatchOutbound);
        if (Equals(ref statusOut, in wire) || Equals(ref matchOut, in wire))
            return false;

        return true;
    }

    /// <summary>
    /// Mismatch reuses the Status outbound shell; offset is measured from Status capture.
    /// </summary>
    public static bool TryResolveMismatchEmbedPayloadOffset(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket shell,
        out int payloadOffset) =>
        TryResolveStatusEmbedPayloadOffset(ref catalog, in shell, out payloadOffset);

    /// <summary>
    /// M4: WireCatalog outbound captured as pipeline-only must be UTP-wrapped before EnqueueToListen.
    /// </summary>
    public static bool NeedsPipelineUtpComposeForListenInject(in BotRelayPacket wire) =>
        !wire.IsEmpty &&
        !LooksLikeConnectHandshake(in wire) &&
        LooksLikePipelineWire(in wire);

    /// <summary>
    /// Pipeline-only outbound must be UTP-composed unless the wire already survives a PopEvents ushort walk.
    /// Short (~88B) shells poison PopEvents (ushort at stream offset 0 reads as 19519).
    /// </summary>
    public static bool WouldPoisonPopEventsListenInject(in BotRelayPacket wire)
    {
        if (wire.IsEmpty)
            return true;

        if (LooksLikeConnectHandshake(in wire) && wire.length > MinOutboundTransportWireBytes)
            return false;

        if (TryValidatePopEventsTransportStream(in wire))
            return false;

        if (TryExtractAppViaTransportFraming(in wire, out _))
            return false;

        if (LooksLikePipelineWire(in wire) && wire.length > 96)
            return false;

        return true;
    }

    /// <summary>
    /// Compose when pipeline-only or when direct inject would poison PopEvents ushort framing.
    /// </summary>
    public static bool ShouldComposePipelineListenUtpForOutboundFlush(in BotRelayPacket wire) =>
        NeedsPipelineUtpComposeForListenInject(in wire) ||
        WouldPoisonPopEventsListenInject(in wire);

    /// <summary>
    /// Splice pipeline-only WireCatalog outbound under the UTP connect prefix (all outbound kinds).
    /// When <paramref name="optionalEmbedPayloadOffset"/> &gt;= 0, adds the pipeline align delta after compose.
    /// </summary>
    public static bool TryComposePipelineListenUtpWire(
        ref BotRelayCatalogBlob catalog,
        ref BotRelayPacket wire,
        ref int optionalEmbedPayloadOffset)
    {
        if (wire.IsEmpty)
            return false;

        if (!NeedsPipelineUtpComposeForListenInject(in wire))
            return true;

        if (!__TryFindLongestUtPConnectWire(ref catalog, out var connectWire, out _))
            return false;

        return TryComposePipelineListenUtpWireWithConnect(
            in connectWire,
            ref wire,
            ref optionalEmbedPayloadOffset);
    }

    public static bool TryComposePipelineListenUtpWireWithConnect(
        in BotRelayPacket connectWire,
        ref BotRelayPacket pipelineWire,
        ref int optionalEmbedPayloadOffset)
    {
        if (pipelineWire.IsEmpty)
            return false;

        if (!NeedsPipelineUtpComposeForListenInject(in pipelineWire))
            return true;

        if (connectWire.IsEmpty || !LooksLikeConnectHandshake(in connectWire))
            return false;

        int alignOffset = __FindPipelineAlignOffset(in connectWire, in pipelineWire);
        if (alignOffset < 0)
            alignOffset = __TryTailOverlapPipelineAlignOffset(in connectWire, in pipelineWire);
        if (alignOffset < 0)
            alignOffset = __FallbackPipelineAlignOffset(in connectWire, in pipelineWire);

        if (alignOffset < 0)
            return false;

        int composedLength = math.max(connectWire.length, alignOffset + pipelineWire.length);
        if (composedLength > BotRelayPacket.MaxPayloadSize)
            return false;

        var composed = connectWire;
        composed.length = composedLength;
        for (int i = 0; i < pipelineWire.length; ++i)
            composed.SetByte(alignOffset + i, pipelineWire.GetByte(i));

        if (optionalEmbedPayloadOffset >= 0)
            optionalEmbedPayloadOffset += alignOffset;

        pipelineWire = composed;
        return true;
    }

    [System.Obsolete("Use TryComposePipelineListenUtpWire")]
    public static bool TryComposeJoinListenUtpWire(
        ref BotRelayCatalogBlob catalog,
        ref BotRelayPacket pipelineJoinWire,
        ref int joinPayloadOffset) =>
        TryComposePipelineListenUtpWire(ref catalog, ref pipelineJoinWire, ref joinPayloadOffset);

    private static bool __TryFindLongestUtPConnectWire(
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket connectWire,
        out int wireIndex)
    {
        connectWire = default;
        wireIndex = EmptyWireIndex;
        int bestLen = 0;

        for (int i = 0; i < catalog.connectClientWires.Length; ++i)
        {
            int candidateIndex = catalog.connectClientWires[i].wireIndex;
            if (candidateIndex <= EmptyWireIndex)
                continue;

            ref var candidate = ref GetWire(ref catalog, candidateIndex);
            if (!LooksLikeConnectHandshake(ref candidate))
                continue;

            if (candidate.length <= bestLen)
                continue;

            if (!TryCopyWireToPacket(ref candidate, out var packet))
                continue;

            bestLen = candidate.length;
            wireIndex = candidateIndex;
            connectWire = packet;
        }

        return wireIndex > EmptyWireIndex && !connectWire.IsEmpty;
    }

    private static int __FindPipelineAlignOffset(in BotRelayPacket connectWire, in BotRelayPacket pipelineWire)
    {
        const int minMatchBytes = 6;
        int bestAlign = -1;
        int bestMatchLen = 0;

        for (int pipelineSkip = 0; pipelineSkip <= 2; ++pipelineSkip)
        {
            int available = pipelineWire.length - pipelineSkip;
            if (available < minMatchBytes)
                continue;

            int needleLen = math.min(16, available);
            for (int i = 0; i <= connectWire.length - minMatchBytes; ++i)
            {
                int matchLen = 0;
                for (int j = 0; j < needleLen; ++j)
                {
                    if (connectWire.GetByte(i + j) != pipelineWire.GetByte(pipelineSkip + j))
                        break;

                    ++matchLen;
                }

                if (matchLen < minMatchBytes)
                    continue;

                int alignOffset = i - pipelineSkip;
                if (alignOffset < 0 || alignOffset + pipelineWire.length > BotRelayPacket.MaxPayloadSize)
                    continue;

                if (matchLen > bestMatchLen)
                {
                    bestMatchLen = matchLen;
                    bestAlign = alignOffset;
                }
            }
        }

        return bestAlign;
    }

    /// <summary>Overlay pipeline on the connect tail (capture layout) before append-after-connect fallback.</summary>
    private static int __TryTailOverlapPipelineAlignOffset(in BotRelayPacket connectWire, in BotRelayPacket pipelineWire)
    {
        if (connectWire.IsEmpty || pipelineWire.IsEmpty)
            return -1;

        int alignOffset = connectWire.length - pipelineWire.length;
        if (alignOffset < 0 || alignOffset + pipelineWire.length > BotRelayPacket.MaxPayloadSize)
            return -1;

        return alignOffset;
    }

    /// <summary>Append pipeline after connect prefix when needle/tail overlap align fails (live WireCatalog capture).</summary>
    private static int __FallbackPipelineAlignOffset(in BotRelayPacket connectWire, in BotRelayPacket pipelineWire)
    {
        if (connectWire.IsEmpty || pipelineWire.IsEmpty)
            return -1;

        int alignOffset = connectWire.length;
        if (alignOffset + pipelineWire.length > BotRelayPacket.MaxPayloadSize)
            return -1;

        return alignOffset;
    }

    /// <summary>PopEvents transport walk must decode Join without stream overread (Server relay hot path).</summary>
    public static bool PopEventsParsesJoinRelayApp(in BotRelayPacket wire)
    {
        if (!TryUnpackPopEventsListenFrame(in wire, out var app) || app.IsEmpty)
            return false;

        return TryReadRelayMessageTypeHeader(in app, out int type) &&
               type == (int)NetworkRelayMessageType.Join;
    }

    /// <summary>
    /// Bot listen inject uses a single ushort-framed relay app (NetworkServer PopEvents offset 0).
    /// </summary>
    public static bool TryUnpackPopEventsListenFrame(in BotRelayPacket wire, out BotRelayPacket app)
    {
        app = default;
        if (wire.IsEmpty || LooksLikeConnectHandshake(in wire) || LooksLikePipelineWire(in wire))
            return false;

        if (wire.length < sizeof(ushort) + 1)
            return false;

        int size = wire.GetByte(0) | (wire.GetByte(1) << 8);
        if (size < 1 || sizeof(ushort) + size != wire.length)
            return false;

        __CopyAppFromOffset(in wire, sizeof(ushort), size, out app);
        return !app.IsEmpty;
    }

    /// <summary>
    /// Mirrors <see cref="NetworkServerPopEventsJob{T}"/> ushort loop — false when any frame would over-read.
    /// </summary>
    public static bool TryValidatePopEventsTransportStream(in BotRelayPacket wire)
    {
        if (wire.IsEmpty)
            return false;

        int pos = 0;
        while (pos + sizeof(ushort) <= wire.length)
        {
            int size = wire.GetByte(pos) | (wire.GetByte(pos + 1) << 8);
            pos += sizeof(ushort);
            if (size < 1 || size > wire.length - pos)
                return false;

            pos += size;
        }

        return pos == wire.length;
    }

    /// <summary>
    /// Secondary guard on RelayOutbound listen inject. Primary link-ACK fix is swallow in __TryInjectLinkAck.
    /// Pipeline shells and composed UTP outbounds are valid; bare PopEvents / short connect poison PopEvents.
    /// </summary>
    public static bool ShouldSkipListenInjectForPopEvents(in BotRelayPacket wire)
    {
        if (wire.IsEmpty)
            return true;

        if (LooksLikePipelineWire(in wire))
            return WouldPoisonPopEventsListenInject(in wire);

        if (LooksLikeConnectHandshake(in wire) && wire.length > MinOutboundTransportWireBytes)
            return false;

        if (LooksLikeConnectHandshake(in wire))
            return true;

        if (TryUnpackPopEventsListenFrame(in wire, out _))
            return true;

        return !TryValidatePopEventsTransportStream(in wire);
    }

    /// <summary>
    /// Join outbound shell: captured SquadJoin wire, link ACK, or catalog scan — never Connect client handshake wire.
    /// </summary>
    public static int ResolveJoinOutboundWireIndex(ref BotRelayCatalogBlob catalog)
    {
        int joinIndex = catalog.templateWireIndex[(int)BotRelayCatalogPacketSlot.SquadJoinOutbound];
        ref var joinShell = ref GetWire(ref catalog, joinIndex);
        if (TryCopyWireToPacket(ref joinShell, out var joinPacket) &&
            IsJoinCompatibleOutboundShell(ref catalog, in joinPacket))
            return joinIndex;

        for (int i = 0; i < catalog.linkAckWireIndices.Length; ++i)
        {
            int ackIndex = catalog.linkAckWireIndices[i];
            ref var ackShell = ref GetWire(ref catalog, ackIndex);
            if (!TryCopyWireToPacket(ref ackShell, out var ackPacket))
                continue;

            if (IsJoinCompatibleOutboundShell(ref catalog, in ackPacket))
                return ackIndex;
        }

        return __FindBestJoinShellUpgradeIndex(ref catalog);
    }

    public static bool IsCatalogOutboundTemplateWire(ref BotRelayCatalogBlob catalog, in BotRelayPacket wire)
    {
        if (wire.IsEmpty)
            return false;

        ref var statusOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.StatusOutbound);
        if (!IsEmpty(ref statusOut) && Equals(ref statusOut, in wire))
            return true;

        ref var matchOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.MatchOutbound);
        if (!IsEmpty(ref matchOut) && Equals(ref matchOut, in wire))
            return true;

        ref var joinOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.SquadJoinOutbound);
        if (!IsEmpty(ref joinOut) && Equals(ref joinOut, in wire))
            return true;

        ref var applyOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.ApplyMatchOutbound);
        if (!IsEmpty(ref applyOut) && Equals(ref applyOut, in wire))
            return true;

        ref var leaveOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.SquadLeaveOutbound);
        return !IsEmpty(ref leaveOut) && Equals(ref leaveOut, in wire);
    }

    public static bool IsConnectClientCapturedWire(ref BotRelayCatalogBlob catalog, in BotRelayPacket wire)
    {
        if (IsCatalogOutboundTemplateWire(ref catalog, in wire))
            return false;

        for (int i = 0; i < catalog.connectClientWires.Length; ++i)
        {
            int wireIndex = catalog.connectClientWires[i].wireIndex;
            if (wireIndex <= EmptyWireIndex)
                continue;

            ref var captured = ref GetWire(ref catalog, wireIndex);
            if (Equals(ref captured, in wire))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the wire's primary relay app decodes as Reply Invite (inbound listen shell).
    /// Must never be copied as Join outbound — trailing FixedString512 bytes poison real clients.
    /// </summary>
    public static bool ShellCarriesInviteRelayApp(
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket wire) =>
        ShellCarriesInviteRelayApp(catalog.inboundAppPayloadOffset, in wire);

    /// <summary>
    /// Builder-safe overload used before the catalog BlobAssetReference exists. Passing the scalar
    /// offset avoids constructing a Blob root struct outside BlobBuilder storage (Entities EA0002).
    /// </summary>
    public static bool ShellCarriesInviteRelayApp(
        int inboundAppPayloadOffset,
        in BotRelayPacket wire)
    {
        if (wire.IsEmpty)
            return false;

        if (inboundAppPayloadOffset >= 0 &&
            inboundAppPayloadOffset < wire.length)
        {
            int payloadLength = wire.length - inboundAppPayloadOffset;
            if (__TryGetRelayAppTypeAtOffset(
                    in wire,
                    inboundAppPayloadOffset,
                    payloadLength,
                    StreamCompressionModel.Default,
                    out int type))
                return type == (int)ReplyMessageType.Invite;
        }

        if (TryExtractFramedApp(in wire, out var app) &&
            TryReadRelayMessageTypeHeader(in app, out int framedType))
            return framedType == (int)ReplyMessageType.Invite;

        return false;
    }

    /// <summary>
    /// Upgrade bare Join app to connect-client / captured Join transport shell — never Invite/Status/Match shells.
    /// </summary>
    public static void TryUpgradeJoinOutboundShell(
        ref BotRelayCatalogBlob catalog,
        ref BotRelayPacket wirePacket,
        in BotRelayPacket liveHello)
    {
        if (IsJoinCompatibleOutboundShell(ref catalog, in wirePacket))
            return;

        int shellIndex = ResolveJoinOutboundWireIndex(ref catalog);
        if (shellIndex > EmptyWireIndex &&
            TryCopyWireToPacket(ref GetWire(ref catalog, shellIndex), out var resolvedShell) &&
            IsJoinCompatibleOutboundShell(ref catalog, in resolvedShell))
        {
            wirePacket = resolvedShell;
            return;
        }

        shellIndex = __FindBestJoinShellUpgradeIndex(ref catalog);
        if (shellIndex > EmptyWireIndex &&
            TryCopyWireToPacket(ref GetWire(ref catalog, shellIndex), out resolvedShell) &&
            IsJoinCompatibleOutboundShell(ref catalog, in resolvedShell))
        {
            wirePacket = resolvedShell;
        }
    }

    private static int __FindBestJoinShellUpgradeIndex(ref BotRelayCatalogBlob catalog)
    {
        int bestIndex = EmptyWireIndex;
        int bestLen = 0;

        for (int i = 1; i < catalog.wires.Length; ++i)
        {
            ref var candidate = ref GetWire(ref catalog, i);
            if (candidate.length <= bestLen)
                continue;

            if (!TryCopyWireToPacket(ref candidate, out var packet))
                continue;

            if (!IsJoinCompatibleOutboundShell(ref catalog, in packet))
                continue;

            bestLen = candidate.length;
            bestIndex = i;
        }

        return bestIndex;
    }

    /// <summary>Link-ACK templates are pipeline-only and poison PopEvents if injected to listen.</summary>
    public static bool IsLinkAckCatalogTemplateWire(ref BotRelayCatalogBlob catalog, in BotRelayPacket wire)
    {
        if (wire.IsEmpty)
            return false;

        for (int i = 0; i < catalog.linkAckWireIndices.Length; ++i)
        {
            int wireIndex = catalog.linkAckWireIndices[i];
            if (wireIndex <= EmptyWireIndex)
                continue;

            ref var candidate = ref GetWire(ref catalog, wireIndex);
            if (Equals(ref candidate, in wire))
                return true;
        }

        return false;
    }

    private static int __FindBestOutboundShellIndex(ref BotRelayCatalogBlob catalog, int fallbackIndex)
    {
        int bestIndex = fallbackIndex;
        int bestLen = GetWire(ref catalog, fallbackIndex).length;

        for (int i = 0; i < catalog.linkAckWireIndices.Length; ++i)
            __ConsiderOutboundShell(ref catalog, catalog.linkAckWireIndices[i], ref bestIndex, ref bestLen);

        for (int i = 0; i < catalog.connectClientWires.Length; ++i)
            __ConsiderOutboundShell(ref catalog, catalog.connectClientWires[i].wireIndex, ref bestIndex, ref bestLen);

        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
            __ConsiderOutboundShell(ref catalog, catalog.inboundMappings[i].wireIndex, ref bestIndex, ref bestLen);

        return bestIndex;
    }

    private static int __FindBestJoinOutboundShellIndex(ref BotRelayCatalogBlob catalog, int fallbackIndex)
    {
        int bestIndex = fallbackIndex;
        int bestLen = GetWire(ref catalog, fallbackIndex).length;

        if (bestIndex > EmptyWireIndex &&
            TryCopyWireToPacket(ref GetWire(ref catalog, bestIndex), out var fallbackPacket) &&
            ShellCarriesInviteRelayApp(ref catalog, in fallbackPacket))
        {
            bestIndex = EmptyWireIndex;
            bestLen = 0;
        }

        ref var statusOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.StatusOutbound);
        ref var matchOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.MatchOutbound);

        for (int i = 0; i < catalog.linkAckWireIndices.Length; ++i)
            __ConsiderJoinOutboundShell(ref catalog, ref statusOut, ref matchOut, catalog.linkAckWireIndices[i], ref bestIndex, ref bestLen);

        for (int i = 0; i < catalog.connectClientWires.Length; ++i)
            __ConsiderJoinOutboundShell(ref catalog, ref statusOut, ref matchOut, catalog.connectClientWires[i].wireIndex, ref bestIndex, ref bestLen);

        return bestIndex;
    }

    private static void __ConsiderOutboundShell(
        ref BotRelayCatalogBlob catalog,
        int candidateIndex,
        ref int bestIndex,
        ref int bestLen)
    {
        if (candidateIndex <= EmptyWireIndex)
            return;

        ref var candidate = ref GetWire(ref catalog, candidateIndex);
        if (candidate.length <= bestLen)
            return;

        if (!TryCopyWireToPacket(ref candidate, out var packet))
            return;

        if (LooksLikeConnectHandshake(in packet))
            return;

        bestLen = candidate.length;
        bestIndex = candidateIndex;
    }

    private static void __ConsiderJoinOutboundShell(
        ref BotRelayCatalogBlob catalog,
        ref BotRelayWireBlob statusOut,
        ref BotRelayWireBlob matchOut,
        int candidateIndex,
        ref int bestIndex,
        ref int bestLen)
    {
        if (candidateIndex <= EmptyWireIndex)
            return;

        ref var candidate = ref GetWire(ref catalog, candidateIndex);
        if (candidate.length <= bestLen)
            return;

        if (!TryCopyWireToPacket(ref candidate, out var packet))
            return;

        if (LooksLikeConnectHandshake(in packet) ||
            Equals(ref statusOut, in packet) ||
            Equals(ref matchOut, in packet) ||
            ShellCarriesInviteRelayApp(ref catalog, in packet) ||
            packet.length <= MinRelayTransportShellBytes)
            return;

        bestLen = candidate.length;
        bestIndex = candidateIndex;
    }

    public static int GetWireScratchOffset(int agentIndex) =>
        agentIndex * BotRelayPacket.MaxPayloadSize;

    public const int PipelinePrefixBytes = 8;

    /// <summary>
    /// PopEvents ushort-framed stream start: prefer the first frame whose payload is strict SendRelay
    /// Invite; otherwise fall back to the last structurally valid ushort frame (10B capture envelopes).
    /// </summary>
    public static int ResolvePopEventsStreamStart(in BotRelayPacket wire)
    {
        if (wire.IsEmpty)
        {
            return PipelinePrefixBytes;
        }

        int lastValid = PipelinePrefixBytes;
        int bestStrictStage = -1;
        int bestStrictStart = -1;
        int maxStart = math.min(10, wire.length - sizeof(ushort) - 1);
        for (int candidate = PipelinePrefixBytes; candidate <= maxStart; ++candidate)
        {
            if (!__HasPopEventsFrameAt(in wire, candidate))
            {
                continue;
            }

            lastValid = candidate;
            int size = wire.GetByte(candidate) | (wire.GetByte(candidate + 1) << 8);
            int payloadStart = candidate + sizeof(ushort);
            if (size < 1 || payloadStart + size > wire.length)
            {
                continue;
            }

            BotRelayWireBytes.CopyAppFromOffset(in wire, payloadStart, size, out var rawPayload);
            if (BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in rawPayload, out var strictInvite))
            {
                if (strictInvite.stage > bestStrictStage ||
                    (strictInvite.stage == bestStrictStage &&
                     (bestStrictStart < 0 || candidate < bestStrictStart)))
                {
                    bestStrictStage = strictInvite.stage;
                    bestStrictStart = candidate;
                }
            }
        }

        if (bestStrictStart >= 0)
        {
            return bestStrictStart;
        }

        return lastValid;
    }

    private static bool __HasPopEventsFrameAt(in BotRelayPacket wire, int pos)
    {
        if (pos < 0 || pos + sizeof(ushort) >= wire.length)
        {
            return false;
        }

        int size = wire.GetByte(pos) | (wire.GetByte(pos + 1) << 8);
        return size >= 1 && pos + sizeof(ushort) + size <= wire.length;
    }

    public const int PipelineEnvelopeScanBytes = 24;

    /// <summary>Max skip inside a PopEvents payload before relay app (live Invite chunk ≈ 6B).</summary>
    public const int PopEventsPayloadInnerScanBytes = 24;

    /// <summary>Invite-only inner scan (live MCP payload may need skip up to 6B UTP prefix).</summary>
    public const int InvitePopEventsPayloadInnerScanBytes = 64;

    /// <summary>Live Play invite PopEvents payload skips 6B before Reply-shaped body.</summary>
    public const int InviteReplyPayloadMinInnerOffset = 6;

    /// <summary>Live pipeline invite PopEvents payloads: 6B junk + 2B packed Invite type before relay channel.</summary>
    public const int InvitePipelineJunkAnchoredInnerOffset = 8;

    /// <summary>Worst-case bytes a StreamCompressionModel packed int consumes (huffman prefix + up to 32 raw bits); used to zero-pad probe buffers so short candidate slices never overread.</summary>
    private const int MaxPackedIntBytes = 8;

    /// <summary>
    /// PopEvents transport framing: ushort size + payload (type inside payload).
    /// </summary>
    public static bool TryExtractAppViaTransportFraming(in BotRelayPacket wire, out BotRelayPacket app)
    {
        app = default;
        if (wire.IsEmpty)
            return false;

        if (BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out app) &&
            !app.IsEmpty)
            return true;

        if (BotRelayPopEventsWalkOps.TryExtractFirstServerToBotApp(in wire, out app) &&
            !app.IsEmpty)
            return true;

        if (!LooksLikePipelineWire(in wire) && !LooksLikeConnectHandshake(in wire))
            return __TryPopEventsWalkRelayApp(in wire, 0, out app);

        return false;
    }

    public static bool TryExtractFramedApp(in BotRelayPacket wire, out BotRelayPacket app)
    {
        app = default;
        if (wire.length < 3)
            return false;

        var model = StreamCompressionModel.Default;
        for (int start = 0; start <= wire.length - 3; ++start)
        {
            int payloadLen = wire.GetByte(start) | (wire.GetByte(start + 1) << 8);
            if (payloadLen < 2 || payloadLen > BotRelayPacket.MaxPayloadSize)
                continue;

            int frameOffset = start + 2;
            int actualLen = __ResolveFramedPayloadLength(payloadLen, frameOffset, wire.length);
            if (actualLen < 2)
                continue;

            if (!__TryValidateRelayAppAtOffset(in wire, frameOffset, actualLen, model))
                continue;

            __CopyAppFromOffset(in wire, frameOffset, actualLen, out app);
            return !app.IsEmpty;
        }

        return false;
    }

    /// <summary>
    /// Server relay payloads arrive inside UTP wire without ushort length prefix.
    /// Scan for a valid relay app header (Invite, Create, Match, …) embedded in the packet.
    /// </summary>
    public static bool LooksLikePipelineWire(in BotRelayPacket wire) =>
        wire.length > 8 &&
        wire.GetByte(0) == 0x01 &&
        !LooksLikeConnectHandshake(in wire);

    /// <summary>
    /// Bots receive over the null pipeline (see <see cref="BotRelayPipeline"/>), so every
    /// server→bot datagram carries the same fixed transport framing. Inbound decode is fully
    /// deterministic — no per-message heuristic scanning:
    /// <list type="number">
    /// <item>strip one measured offset (<paramref name="catalog"/>.inboundAppPayloadOffset,
    ///   captured once from a known reference message) when the app sits contiguously there;</item>
    /// <item>otherwise walk the protocol-fixed PopEvents ushort framing
    ///   (<see cref="TryExtractAppViaTransportFraming"/>), which is independent of the pipeline.</item>
    /// </list>
    /// Anything that validates as neither (host LevelPlayerProperty, in-level gameplay the bot
    /// ignores, handshake noise) returns false and is dropped by the caller.
    /// <para><paramref name="scratch"/>/<paramref name="scratchOffset"/> are retained for call-site
    /// compatibility; neither decode path uses them.</para>
    /// </summary>
    public static bool TryExtractRelayAppPayload(
        in BotRelayPacket wire,
        NativeArray<byte> scratch,
        int scratchOffset,
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket app)
    {
        app = default;
        if (wire.IsEmpty)
            return false;

        int knownPayloadOffset = catalog.IsValid ? catalog.inboundAppPayloadOffset : -1;
        if (__TryExtractCatalogInboundAppAtOffset(in wire, knownPayloadOffset, out app))
            return true;

        return TryExtractAppViaTransportFraming(in wire, out app) && !app.IsEmpty;
    }

    /// <inheritdoc cref="TryExtractRelayAppPayload(in BotRelayPacket, NativeArray{byte}, int, ref BotRelayCatalogBlob, out BotRelayPacket)"/>
    public static bool TryExtractRelayAppPayload(
        in BotRelayPacket wire,
        NativeArray<byte> scratch,
        int scratchOffset,
        in BotRelayPacket serverHelloWire,
        int inboundAppPayloadOffset,
        out BotRelayPacket app)
    {
        app = default;
        if (wire.IsEmpty)
            return false;

        if (__TryExtractCatalogInboundAppAtOffset(in wire, inboundAppPayloadOffset, out app))
            return true;

        return TryExtractAppViaTransportFraming(in wire, out app) && !app.IsEmpty;
    }

    private const int MinInviteRelayAppBytes = 85;

    private static int __ResolveFramedPayloadLength(int declaredLen, int payloadOffset, int wireLength)
    {
        if (declaredLen < 2 || payloadOffset < 0 || payloadOffset >= wireLength)
            return 0;

        int available = wireLength - payloadOffset;
        if (available < 2)
            return 0;

        return declaredLen <= available ? declaredLen : available;
    }

    /// <summary>
    /// Null-pipeline server→bot datagram: strip one measured offset and return the contiguous relay app.
    /// </summary>
    public static bool TryExtractCatalogInboundApp(
        in BotRelayPacket wire,
        int inboundAppPayloadOffset,
        out BotRelayPacket app) =>
        __TryExtractCatalogInboundAppAtOffset(in wire, inboundAppPayloadOffset, out app);

    private static bool __TryExtractCatalogInboundAppAtOffset(
        in BotRelayPacket wire,
        int inboundAppPayloadOffset,
        out BotRelayPacket app)
    {
        app = default;
        if (inboundAppPayloadOffset < 0 || inboundAppPayloadOffset >= wire.length)
            return false;

        int payloadLength = wire.length - inboundAppPayloadOffset;
        bool pipelineWire = LooksLikePipelineWire(in wire);
        if (!__TryValidateCatalogInboundApp(in wire, inboundAppPayloadOffset, payloadLength, pipelineWire))
            return false;

        __CopyAppFromOffset(in wire, inboundAppPayloadOffset, payloadLength, out app);
        return !app.IsEmpty;
    }

    private static bool __TryValidateCatalogInboundApp(
        in BotRelayPacket wire,
        int offset,
        int length,
        bool pipelineWire)
    {
        if (offset < 0 || length < BotRelayCodec.MinWorldInviteRelayAppPrefixBytes ||
            offset + length > wire.length)
            return false;

        var slice = default(BotRelayPacket);
        slice.length = length;
        for (int i = 0; i < length; ++i)
            slice.SetByte(i, wire.GetByte(offset + i));

        if (BotRelayCodec.TryParseWorldInviteRelayApp(in slice, out _))
            return true;

        if (pipelineWire && length >= 80 && length <= 160)
            return false;

        var model = StreamCompressionModel.Default;
        if (pipelineWire && wire.length > 32)
        {
            if (!__TryValidateReplyOrInviteAppAtOffset(in wire, offset, length, model))
                return false;

            if (!__TryGetRelayAppTypeAtOffset(in wire, offset, length, model, out int type))
                return false;

            if (type == (int)ReplyMessageType.Invite && length < MinInviteRelayAppBytes)
                return false;

            return true;
        }

        return __TryValidateRelayAppAtOffset(in wire, offset, length, model);
    }

    private static bool __IsReplyOrInviteRelayApp(int type) =>
        type == (int)ReplyMessageType.Invite ||
        (type >= (int)ReplyMessageType.Chat && type <= (int)ReplyMessageType.PlayerProperty);

    private static bool __TryValidateReplyOrInviteAppAtOffset(
        in BotRelayPacket wire,
        int offset,
        int length,
        StreamCompressionModel model)
    {
        if (!__TryValidateRelayAppAtOffset(in wire, offset, length, model))
            return false;

        return __TryGetRelayAppTypeAtOffset(in wire, offset, length, model, out int type) &&
               __IsReplyOrInviteRelayApp(type);
    }

    private static bool __TryGetRelayAppTypeAtOffset(
        in BotRelayPacket wire,
        int offset,
        int length,
        StreamCompressionModel model,
        out int type)
    {
        type = -1;
        if (offset < 0 || length < 1 || offset + length > wire.length)
            return false;

        var slice = default(BotRelayPacket);
        slice.length = length;
        for (int i = 0; i < length; ++i)
            slice.SetByte(i, wire.GetByte(offset + i));

        if (!__TryReadRelayAppType(in slice, model, out type))
            return false;

        return true;
    }

    /// <summary>
    /// Walk ushort-framed transport chunks (same as BotRelayConnection.EnqueueTransportPayload).
    /// </summary>
    private static bool __TryPopEventsWalkRelayApp(
        in BotRelayPacket wire,
        int streamStart,
        out BotRelayPacket app)
    {
        app = default;
        if (streamStart < 0 || streamStart >= wire.length)
            return false;

        int pos = streamStart;

        while (pos + sizeof(ushort) <= wire.length)
        {
            int size = wire.GetByte(pos) | (wire.GetByte(pos + 1) << 8);
            pos += sizeof(ushort);

            if (size < 1 || size > wire.length - pos)
                return false;

            __CopyAppFromOffset(in wire, pos, size, out var rawPayload);
            if (!rawPayload.IsEmpty)
            {
                var model = StreamCompressionModel.Default;
                if (__TryReadRelayAppType(in rawPayload, model, out _))
                {
                    app = rawPayload;
                    return true;
                }

                if (__TryNormalizePopEventsPayload(in rawPayload, out app))
                    return true;
            }

            pos += size;
        }

        return false;
    }

    /// <summary>
    /// PopEvents payload may include a short inner envelope before Huffman app bytes (live Invite ≈ 6B).
    /// </summary>
    public static bool TryNormalizePopEventsPayload(in BotRelayPacket raw, out BotRelayPacket app) =>
        __TryNormalizePopEventsPayload(in raw, out app);

    private static bool __TryNormalizePopEventsPayload(in BotRelayPacket raw, out BotRelayPacket app)
    {
        app = default;
        if (raw.IsEmpty)
        {
            return false;
        }

        int maxInner = math.min(PopEventsPayloadInnerScanBytes, raw.length - 2);
        bool preferInviteBestInner = __PayloadMayContainInviteApp(in raw, maxInner);
        int bestInner = -1;
        int bestScore = int.MinValue;
        for (int inner = 0; inner <= maxInner; ++inner)
        {
            int length = raw.length - inner;
            if (length < 1)
            {
                break;
            }

            if (!__TryValidateNormalizedRelayApp(in raw, inner, length))
            {
                continue;
            }

            if (!preferInviteBestInner)
            {
                __CopyAppFromOffset(in raw, inner, length, out app);
                return !app.IsEmpty;
            }

            int score;
            BotRelayWireBytes.CopyAppFromOffset(in raw, inner, length, out var candidate);
            if (BotRelayMessageUtility.TryReadInviteFromPacket(
                    in candidate,
                    out var invite,
                    BotRelayMessageUtility.InviteReadOptions.Default) &&
                invite.channel >= 0 &&
                invite.stage >= 0)
            {
                if (BotRelayInviteDecodeOps.IsPlausibleInvite(in invite))
                {
                    score = 1000 + invite.channel;
                }
                else if (invite.channel >= 0 && invite.stage >= 0)
                {
                    score = 100 + inner;
                }
                else
                {
                    score = inner;
                }
            }
            else
            {
                score = -inner;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestInner = inner;
            }
        }

        if (bestInner < 0)
        {
            return false;
        }

        __CopyAppFromOffset(in raw, bestInner, raw.length - bestInner, out app);
        return !app.IsEmpty;
    }

    private static bool __PayloadMayContainInviteApp(in BotRelayPacket raw, int maxInner)
    {
        for (int inner = 0; inner <= maxInner; ++inner)
        {
            int length = raw.length - inner;
            if (length < 1)
            {
                break;
            }

            if (!__TryValidateNormalizedRelayApp(in raw, inner, length))
            {
                continue;
            }

            BotRelayWireBytes.CopyAppFromOffset(in raw, inner, length, out var candidate);
            if (BotRelayMessageUtility.TryReadInviteFromPacket(
                    in candidate,
                    out var invite,
                    BotRelayMessageUtility.InviteReadOptions.Default) &&
                invite.channel >= 0 &&
                invite.stage >= 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryValidateSendRelayInviteApp(in BotRelayPacket app) =>
        BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out _);

    private static bool __TryValidateNormalizedRelayApp(in BotRelayPacket raw, int offset, int length)
    {
        if (offset < 0 || length < 1 || offset + length > raw.length)
            return false;

        var model = StreamCompressionModel.Default;
        if (!__TryGetRelayAppTypeAtOffset(in raw, offset, length, model, out int type))
            return false;

        if (type == (int)ReplyMessageType.Invite)
        {
            __CopyAppFromOffset(in raw, offset, length, out var inviteApp);
            return BotRelayInviteDecodeOps.IsValidInviteApp(in inviteApp);
        }

        return __TryValidateRelayAppAtOffset(in raw, offset, length, model);
    }

    public static bool LooksLikeLinkAckCandidate(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog,
        NativeArray<byte> scratch,
        int scratchOffset)
    {
        if (wire.IsEmpty || wire.length > 64 || LooksLikeConnectHandshake(in wire))
            return false;

        if (TryExtractRelayAppPayload(
                in wire,
                scratch,
                scratchOffset,
                ref catalog,
                out var app) &&
            !app.IsEmpty)
            return false;

        if (TryNormalizePopEventsPayload(in wire, out var normalized) && !normalized.IsEmpty)
            return false;

        ref var statusOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.StatusOutbound);
        ref var matchOut = ref GetTemplate(ref catalog, BotRelayCatalogPacketSlot.MatchOutbound);
        if (Equals(ref statusOut, in wire) || Equals(ref matchOut, in wire))
            return false;

        if (wire.length >= 3 &&
            wire.GetByte(0) == (byte)'U' &&
            wire.GetByte(1) == (byte)'T' &&
            wire.GetByte(2) == (byte)'P')
            return true;

        return wire.length <= 24;
    }

    /// <summary>
    /// Connect-time inbound calibration only (WireCatalog bootstrap). Runtime outbound embed must use
    /// catalog frame offsets (<see cref="TryResolveCatalogFrameEmbedOffset"/>).
    /// </summary>
    public static bool TryResolveInboundAppPayloadOffset(
        in BotRelayPacket wire,
        in BotRelayPacket appFrame,
        out int offset)
    {
        offset = -1;
        if (wire.IsEmpty || appFrame.IsEmpty || wire.length <= appFrame.length)
            return false;

        int limit = wire.length - appFrame.length;
        for (int i = 0; i <= limit; ++i)
        {
            bool match = true;
            for (int j = 0; j < appFrame.length; ++j)
            {
                if (wire.GetByte(i + j) != appFrame.GetByte(j))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                offset = i;
                return true;
            }
        }

        return false;
    }

    private static void __CopyAppFromOffset(
        in BotRelayPacket wire,
        int offset,
        int length,
        out BotRelayPacket app)
    {
        app = default;
        if (offset < 0 || length < 1 || offset + length > wire.length)
            return;

        if (length > BotRelayPacket.MaxPayloadSize)
            length = BotRelayPacket.MaxPayloadSize;

        app.length = length;
        for (int i = 0; i < length; ++i)
            app.SetByte(i, wire.GetByte(offset + i));
    }

    internal static void CopyAppFromOffset(
        in BotRelayPacket wire,
        int offset,
        int length,
        out BotRelayPacket app) =>
        __CopyAppFromOffset(in wire, offset, length, out app);

    private static bool __IsRelayAppMessageType(int type)
    {
        if (type >= 0 && type <= (int)NetworkRelayMessageType.Query)
            return true;

        if (type == (int)ReplyMessageType.Invite)
            return true;

        if (type > (int)NetworkRelayMessageType.Query + 1 && type <= (int)ReplyMessageType.PlayerProperty)
            return true;

        return false;
    }

    private static bool __TryValidateRelayAppAtOffset(
        in BotRelayPacket wire,
        int offset,
        int length,
        StreamCompressionModel model)
    {
        if (offset < 0 || length < 1 || offset + length > wire.length)
            return false;

        var slice = default(BotRelayPacket);
        slice.length = length;
        for (int i = 0; i < length; ++i)
            slice.SetByte(i, wire.GetByte(offset + i));

        return __TryValidateRelayAppPacket(in slice, model);
    }

    private static bool __TryValidateRelayAppPacket(in BotRelayPacket packet, StreamCompressionModel model) =>
        __TryReadRelayAppType(in packet, model, out _);

    public static bool TryReadRelayMessageType(in BotRelayPacket packet, out int type) =>
        TryReadRelayMessageType(in packet, StreamCompressionModel.Default, out type);

    public static bool TryReadRelayMessageType(
        in BotRelayPacket packet,
        StreamCompressionModel model,
        out int type) =>
        __TryReadRelayAppType(in packet, model, out type);

    /// <summary>Reads the first packed relay message type without validating trailing payload shape (outbound apps).</summary>
    public static bool TryReadRelayMessageTypeHeader(in BotRelayPacket packet, out int type) =>
        TryReadRelayMessageTypeHeader(in packet, StreamCompressionModel.Default, out type);

    public static bool TryReadRelayMessageTypeHeader(
        in BotRelayPacket packet,
        StreamCompressionModel model,
        out int type)
    {
        return __TryReadPackedRelayType(in packet, model, out type, out _) &&
               __IsRelayAppMessageType(type);
    }

    public static bool TryReadPackedUIntAtOffset(
        in BotRelayPacket packet,
        int offset,
        out uint value) =>
        TryReadPackedUIntAtOffset(in packet, offset, StreamCompressionModel.Default, out value);

    public static bool TryReadPackedIntAtOffset(
        in BotRelayPacket packet,
        int offset,
        out int value) =>
        TryReadPackedIntAtOffset(in packet, offset, StreamCompressionModel.Default, out value);

    public static bool TryReadPackedIntAtOffset(
        in BotRelayPacket packet,
        int offset,
        StreamCompressionModel model,
        out int value)
    {
        value = 0;
        if (offset < 0 || offset >= packet.length)
        {
            return false;
        }

        unsafe
        {
            int sliceLength = packet.length - offset;
            int capacity = sliceLength + MaxPackedIntBytes;
            byte* buffer = stackalloc byte[capacity];
            for (int i = 0; i < sliceLength; ++i)
            {
                buffer[i] = packet.GetByte(offset + i);
            }

            for (int i = sliceLength; i < capacity; ++i)
            {
                buffer[i] = 0;
            }

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                capacity,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref bytes, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            value = reader.ReadPackedInt(model);
            if (reader.HasFailedReads)
            {
                value = 0;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Reads the host Create relay channel at the fixed pipeline inner offset used by live invite shells.
    /// </summary>
    public static bool TryReadAuthoritativePipelineHostChannel(in BotRelayPacket app, out int channel)
    {
        channel = BotRelayInviteChannelAuthority.UnsetChannel;
        int scanEnd = math.min(app.length - 1, InvitePipelineJunkAnchoredInnerOffset + 4);
        for (int offset = 0; offset <= scanEnd; ++offset)
        {
            if (__TryReadPipelineAnchorChannelAtOffset(in app, offset, out channel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool __TryReadPipelineAnchorChannelAtOffset(
        in BotRelayPacket app,
        int offset,
        out int channel)
    {
        channel = BotRelayInviteChannelAuthority.UnsetChannel;
        if (offset < 0 || offset + 1 >= app.length)
        {
            return false;
        }

        byte b0 = app.GetByte(offset);
        byte b1 = app.GetByte(offset + 1);
        if (b0 == 0xA2 && b1 == 0x01)
        {
            channel = 1;
            return true;
        }

        if (b0 == 0x0C && b1 == 0x00)
        {
            channel = 1;
            return true;
        }

        return false;
    }

    private static bool __TryReadPipelineHostChannelAtOffset(
        in BotRelayPacket app,
        int offset,
        out int channel)
    {
        channel = BotRelayInviteChannelAuthority.UnsetChannel;
        if (__TryReadPipelineAnchorChannelAtOffset(in app, offset, out channel))
        {
            return true;
        }

        if (!TryReadPackedIntAtOffset(in app, offset, out int candidate))
        {
            return false;
        }

        if (candidate < 0 || candidate > BotRelayInviteChannelAuthority.MaxServerChannel)
        {
            return false;
        }

        channel = candidate;
        return true;
    }

    public static bool TryReadRelayCreateOrJoinChannelAtOffset(
        in BotRelayPacket packet,
        int offset,
        out int channel)
    {
        channel = BotRelayInviteChannelAuthority.UnsetChannel;
        if (offset < 0 || offset >= packet.length)
        {
            return false;
        }

        unsafe
        {
            int sliceLength = packet.length - offset;
            int capacity = sliceLength + MaxPackedIntBytes * 3;
            byte* buffer = stackalloc byte[capacity];
            for (int i = 0; i < sliceLength; ++i)
            {
                buffer[i] = packet.GetByte(offset + i);
            }

            for (int i = sliceLength; i < capacity; ++i)
            {
                buffer[i] = 0;
            }

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                capacity,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref bytes, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            int type = reader.ReadPackedInt(model);
            if (reader.HasFailedReads)
            {
                return false;
            }

            if ((NetworkRelayMessageType)type != NetworkRelayMessageType.Create &&
                (NetworkRelayMessageType)type != NetworkRelayMessageType.Join)
            {
                return false;
            }

            channel = reader.ReadPackedInt(model);
            if (reader.HasFailedReads ||
                channel < 0 ||
                channel > BotRelayInviteChannelAuthority.MaxServerChannel)
            {
                channel = BotRelayInviteChannelAuthority.UnsetChannel;
                return false;
            }

            return true;
        }
    }

    public static bool TryScanRelayCreateOrJoinChannel(in BotRelayPacket packet, out int channel)
    {
        channel = BotRelayInviteChannelAuthority.UnsetChannel;
        if (packet.IsEmpty)
        {
            return false;
        }

        int scanEnd = math.min(packet.length - 2, 64);
        int bestChannel = BotRelayInviteChannelAuthority.UnsetChannel;
        for (int offset = 0; offset <= scanEnd; ++offset)
        {
            if (!TryReadRelayCreateOrJoinChannelAtOffset(in packet, offset, out int candidate))
            {
                continue;
            }

            if (bestChannel < 0)
            {
                bestChannel = candidate;
            }
        }

        if (bestChannel < 0)
        {
            return false;
        }

        channel = bestChannel;
        return true;
    }

    /// <summary>
    /// Live pipeline invite payloads may embed the host Create relay channel via <c>04 B9</c> prefix.
    /// Does not run generic Create/Join scan on misaligned junk (false channel 0 positives).
    /// </summary>
    public static bool TryScanPipelineEmbeddedHostCreateChannel(in BotRelayPacket app, out int channel)
    {
        channel = BotRelayInviteChannelAuthority.UnsetChannel;
        if (app.IsEmpty)
        {
            return false;
        }

        int scanEnd = math.min(app.length - 4, 64);
        for (int offset = 0; offset <= scanEnd; ++offset)
        {
            if (app.GetByte(offset) != 0x04 ||
                app.GetByte(offset + 1) != 0xB9 ||
                !TryReadPackedIntAtOffset(in app, offset + 2, out int packedChannel) ||
                packedChannel < 0 ||
                packedChannel > BotRelayInviteChannelAuthority.MaxServerChannel)
            {
                continue;
            }

            channel = packedChannel;
            return true;
        }

        return false;
    }

    public static bool TryReadPackedUIntAtOffset(
        in BotRelayPacket packet,
        int offset,
        StreamCompressionModel model,
        out uint value)
    {
        value = 0;
        if (offset < 0 || offset >= packet.length)
            return false;

        unsafe
        {
            int sliceLength = packet.length - offset;
            int capacity = sliceLength + MaxPackedIntBytes;
            byte* buffer = stackalloc byte[capacity];
            for (int i = 0; i < sliceLength; ++i)
                buffer[i] = packet.GetByte(offset + i);
            for (int i = sliceLength; i < capacity; ++i)
                buffer[i] = 0;

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                capacity,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref bytes, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            value = reader.ReadPackedUInt(model);
            if (reader.HasFailedReads)
            {
                value = 0;
                return false;
            }

            return true;
        }
    }

    private static bool __TryReadRelayAppType(
        in BotRelayPacket packet,
        StreamCompressionModel model,
        out int type)
    {
        if (!__TryReadPackedRelayType(in packet, model, out type, out int bytesRead))
            return false;

        return __IsRelayAppMessageType(type) &&
               __HasMinimumRelayPayload(type, packet.length - bytesRead);
    }

    /// <summary>
    /// Reads the leading packed relay type over a zero-padded buffer so <see cref="DataStreamReader"/> never
    /// overreads while the inbound framing walker probes many short candidate offsets. Returns false — without
    /// emitting a stream-overread error — when the packed int would run past the real packet bytes.
    /// </summary>
    private static bool __TryReadPackedRelayType(
        in BotRelayPacket packet,
        StreamCompressionModel model,
        out int type,
        out int bytesRead)
    {
        type = -1;
        bytesRead = 0;
        if (packet.length < 1)
            return false;

        unsafe
        {
            int capacity = packet.length + MaxPackedIntBytes;
            byte* buffer = stackalloc byte[capacity];
            for (int i = 0; i < packet.length; ++i)
                buffer[i] = packet.GetByte(i);
            for (int i = packet.length; i < capacity; ++i)
                buffer[i] = 0;

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                capacity,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref bytes, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            type = reader.ReadPackedInt(model);
            if (reader.HasFailedReads)
            {
                type = -1;
                return false;
            }

            bytesRead = reader.GetBytesRead();

            // Reject candidates whose packed type ran into the zero padding (real slice too short).
            if (bytesRead > packet.length)
            {
                type = -1;
                bytesRead = 0;
                return false;
            }

            return true;
        }
    }

    private static bool __HasMinimumRelayPayload(int type, int remaining)
    {
        if (remaining < 0)
            return false;

        switch ((NetworkRelayMessageType)type)
        {
            case NetworkRelayMessageType.Create:
            case NetworkRelayMessageType.Leave:
            case NetworkRelayMessageType.Drop:
            case NetworkRelayMessageType.Status:
            case NetworkRelayMessageType.Add:
            case NetworkRelayMessageType.Remove:
                return remaining >= 1;
            case NetworkRelayMessageType.Join:
                return remaining >= 1;
            case NetworkRelayMessageType.Match:
                return remaining >= 2;
            case NetworkRelayMessageType.Mismatch:
            case NetworkRelayMessageType.Matching:
                return true;
            default:
                if (type == (int)ReplyMessageType.Invite)
                    return remaining >= BotRelayCodec.MinWorldInviteRelayAppPrefixBytes - 1;

                return remaining >= 1;
        }
    }
}
