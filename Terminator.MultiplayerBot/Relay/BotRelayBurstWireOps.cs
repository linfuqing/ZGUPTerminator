using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ZG;

internal enum BotRelayOutboundWireKind : byte
{
    None = 0,
    Status,
    Match,
    ApplyMatch,
    Leave,
    Join,
    Mismatch,
}

[BurstCompile]
internal static class BotRelayBurstWireOps
{
    public static void PreTickAgent(
        int index,
        ref BotRelayFarmNative farm,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if ((transport.flags & BotRelayTransportFlags.ConnectPending) != 0)
            __RequestConnect(index, ref farm, ref manager, ref catalog);

        __MaybeQueueInitialStatus(index, ref farm);
    }

    public static void DrainInbound(
        int index,
        ref BotRelayFarmNative farm,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if (transport.virtualPort == 0)
            return;

        ushort virtualPort = transport.virtualPort;
        ref var serverHello = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.ServerHello);
        int scratchBase = BotRelayWireBytes.GetWireScratchOffset(index);

        int drainBudget = BotRelayJobLimits.MaxBotPacketsPerDrainJob;
        while (drainBudget-- > 0 && manager.TryDequeuePeeledAppBurst(virtualPort, out var peeledApp))
        {
            if (peeledApp.IsEmpty)
                continue;

            transport.flags |= BotRelayTransportFlags.Connected;
            BotRelaySlotOps.TryEnqueueInbound(
                index, farm.inbound, farm.inboundCount, in peeledApp, ref manager);
            __LogInviteShellDrainIfNeeded(
                transport.header.userID,
                peeledApp.length,
                "peeledQueue",
                true,
                in peeledApp,
                in peeledApp);
        }

        while (drainBudget-- > 0 && manager.TryDequeueToBotBurst(virtualPort, out var wirePacket))
        {
            ++manager.diagDrainPacketsBudget;
            if (wirePacket.IsEmpty)
                continue;

            BotRelayAgentDiagnostics.LogInboundDequeued(
                transport.header.userID, wirePacket.length, 0);

            bool awaitingHello = farm.wireLiveServerHello[index].IsEmpty &&
                (transport.flags & BotRelayTransportFlags.ConnectRequested) != 0;

            BotRelayPacket appPacket;

            if (awaitingHello)
            {
                if (__TryResolveRelayAppFromWire(
                        in wirePacket,
                        ref catalog,
                        farm.wireScratch,
                        scratchBase,
                        out appPacket) &&
                    !appPacket.IsEmpty)
                {
                    transport.flags |= BotRelayTransportFlags.Connected;
                    BotRelaySlotOps.TryEnqueueInbound(
                        index, farm.inbound, farm.inboundCount, in appPacket, ref manager);
                    continue;
                }

                transport.flags |= BotRelayTransportFlags.Connected;
                __StoreLiveHello(index, ref farm, in wirePacket);
                __MaybeQueueInitialStatus(index, ref farm);
                continue;
            }

            if (BotRelayPopEventsWalkOps.TryWalkServerToBotAppsAndEnqueueInbound(
                    in wirePacket,
                    index,
                    ref farm,
                    ref manager))
            {
                transport.flags |= BotRelayTransportFlags.Connected;
                int inboundQueued = farm.inboundCount[index];
                BotRelayAgentDiagnostics.LogInboundDequeued(
                    transport.header.userID, wirePacket.length, inboundQueued);
                __LogInviteShellDrainIfNeeded(
                    transport.header.userID,
                    wirePacket.length,
                    "popEventsWalk",
                    inboundQueued > 0,
                    default,
                    in wirePacket);
                if (wirePacket.length > BotRelayWireBytes.MinOutboundTransportWireBytes)
                    __StoreJoinListenShell(index, ref farm, in wirePacket);
                continue;
            }

            if (__TryUnwrapInbound(in wirePacket, ref catalog, farm.wireScratch, scratchBase, out appPacket) &&
                !appPacket.IsEmpty)
            {
                if (BotRelayInviteDecodeOps.LooksLikeInviteShell(in wirePacket) && virtualPort != 0)
                {
                    BotRelayInviteShellCacheStore.Store(in wirePacket, virtualPort);
                }

                transport.flags |= BotRelayTransportFlags.Connected;
                BotRelayAgentDiagnostics.LogInboundDequeued(
                    transport.header.userID, wirePacket.length, appPacket.length);
                __LogInviteShellDrainIfNeeded(
                    transport.header.userID,
                    wirePacket.length,
                    "unwrapInbound",
                    true,
                    in appPacket,
                    in wirePacket);
                if (wirePacket.length > BotRelayWireBytes.MinOutboundTransportWireBytes)
                    __StoreJoinListenShell(index, ref farm, in wirePacket);
                BotRelaySlotOps.TryEnqueueInbound(
                    index, farm.inbound, farm.inboundCount, in appPacket, ref manager);
                continue;
            }

            if (__TryResolveRelayAppFromWire(
                    in wirePacket,
                    ref catalog,
                    farm.wireScratch,
                    scratchBase,
                    out appPacket) &&
                !appPacket.IsEmpty)
            {
                transport.flags |= BotRelayTransportFlags.Connected;
                BotRelayAgentDiagnostics.LogInboundDequeued(
                    transport.header.userID, wirePacket.length, appPacket.length);
                BotRelayAgentDiagnostics.LogInboundAppExtracted(
                    transport.header.userID,
                    wirePacket.length,
                    appPacket.length,
                    appPacket.length > 0 ? appPacket.GetByte(0) : 0);
                __LogInviteShellDrainIfNeeded(
                    transport.header.userID,
                    wirePacket.length,
                    "resolveRelayApp",
                    true,
                    in appPacket,
                    in wirePacket);
                if (wirePacket.length > BotRelayWireBytes.MinOutboundTransportWireBytes)
                    __StoreJoinListenShell(index, ref farm, in wirePacket);
                BotRelaySlotOps.TryEnqueueInbound(
                    index, farm.inbound, farm.inboundCount, in appPacket, ref manager);
                continue;
            }

            if (__TryUnwrapInbound(in wirePacket, ref catalog, farm.wireScratch, scratchBase, out appPacket) &&
                appPacket.IsEmpty &&
                wirePacket.length <= 32)
            {
                transport.flags |= BotRelayTransportFlags.Connected;
                if (!BotRelayWireBytes.IsEmpty(ref serverHello) &&
                    BotRelayWireBytes.Equals(ref serverHello, in wirePacket))
                {
                    __StoreLiveHello(index, ref farm, in wirePacket);
                    __MaybeQueueInitialStatus(index, ref farm);
                    continue;
                }
            }

            if (wirePacket.length <= 48 &&
                (BotRelayWireBytes.Equals(ref serverHello, in wirePacket) ||
                 __MatchesAnyServerTemplate(in wirePacket, ref catalog)))
            {
                if (__TryResolveRelayAppFromWire(
                        in wirePacket,
                        ref catalog,
                        farm.wireScratch,
                        scratchBase,
                        out appPacket) &&
                    !appPacket.IsEmpty)
                {
                    transport.flags |= BotRelayTransportFlags.Connected;
                    BotRelaySlotOps.TryEnqueueInbound(
                        index, farm.inbound, farm.inboundCount, in appPacket, ref manager);
                    continue;
                }

                transport.flags |= BotRelayTransportFlags.Connected;
                __StoreLiveHello(index, ref farm, in wirePacket);
                __MaybeQueueInitialStatus(index, ref farm);
                continue;
            }

            if ((transport.flags & BotRelayTransportFlags.Connected) != 0)
            {
                var liveHello = farm.wireLiveServerHello[index];
                if (__TryResolveRelayAppFromWire(
                        in wirePacket,
                        ref catalog,
                        farm.wireScratch,
                        scratchBase,
                        out appPacket) &&
                    !appPacket.IsEmpty)
                {
                    BotRelaySlotOps.TryEnqueueInbound(
                        index, farm.inbound, farm.inboundCount, in appPacket, ref manager);
                    continue;
                }

                if (__TryInjectLinkAck(
                        virtualPort,
                        ref manager,
                        ref catalog,
                        in wirePacket,
                        in liveHello,
                        farm.wireScratch,
                        scratchBase,
                        transport.header.userID))
                    continue;
            }

            BotRelayAgentDiagnostics.LogUnmappedInboundWire(
                transport.header.userID, wirePacket.length);
            __LogInviteShellDrainIfNeeded(
                transport.header.userID,
                wirePacket.length,
                "unmappedDrop",
                false,
                default,
                in wirePacket);
            ++manager.diagInboundUnmapped;
        }

        if (drainBudget < 0 && manager.HasInboundForBotBurst(virtualPort))
            ++manager.diagDrainBudgetExhausted;

        __PumpLinkLayerAcks(index, ref farm, ref manager, ref catalog);
    }

    public static void PumpLinkLayerAcks(
        int index,
        ref BotRelayFarmNative farm,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog) =>
        __PumpLinkLayerAcks(index, ref farm, ref manager, ref catalog);

    public static void FlushOutbound(
        int index,
        ref BotRelayFarmNative farm,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if (transport.virtualPort == 0)
            return;

        if ((transport.flags & BotRelayTransportFlags.Connected) == 0)
            return;

        var liveHello = farm.wireLiveServerHello[index];
        ref var capturedHello = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.ServerHello);
        if (liveHello.IsEmpty || BotRelayWireBytes.IsEmpty(ref capturedHello))
            return;

        if (catalog.connectClientWires.Length == 0)
            return;

        ref var connectTemplate = ref BotRelayWireBytes.GetWire(
            ref catalog,
            catalog.connectClientWires[0].wireIndex);

        int baseIndex = index * BotRelayFarmNative.MaxOutboundPerAgent;
        int count = farm.outboundCount[index];
        if (count <= 0)
            return;

        int scratchBase = index * BotRelayPacket.MaxPayloadSize;
        int scratchFrameBase = scratchBase + BotRelayPacket.MaxPayloadSize / 2;

        bool sentAny = false;
        int sentIndex = -1;
        for (int i = 0; i < count; ++i)
        {
            var appPacket = farm.outbound[baseIndex + i];
            if (appPacket.IsEmpty)
                continue;

            if (!__TryResolveOutboundWire(in appPacket, ref catalog, out var wireKind, out int wireIndex))
                continue;

            if (wireKind == BotRelayOutboundWireKind.Join)
            {
                int resolvedJoinIndex = BotRelayWireBytes.ResolveJoinOutboundWireIndex(ref catalog);
                if (resolvedJoinIndex > BotRelayWireBytes.EmptyWireIndex)
                    wireIndex = resolvedJoinIndex;
            }

            ref var wireTemplate = ref BotRelayWireBytes.GetWire(ref catalog, wireIndex);
            if (!BotRelayWireBytes.TryCopyWireToPacket(ref wireTemplate, out var wirePacket))
                continue;

            if (wireKind == BotRelayOutboundWireKind.Join)
            {
                BotRelayWireBytes.TryUpgradeJoinOutboundShell(
                    ref catalog,
                    ref wirePacket,
                    in liveHello);
            }
            else if (BotRelayWireBytes.IsConnectClientCapturedWire(ref catalog, in wirePacket))
                continue;

            int embedPayloadOffset = -1;
            if (wireKind == BotRelayOutboundWireKind.Join)
            {
                if (!BotRelayWireBytes.TryResolveJoinEmbedPayloadOffset(ref catalog, in wirePacket, out embedPayloadOffset))
                {
                    BotRelayAgentDiagnostics.LogJoinWireFlushFailed(
                        transport.header.userID,
                        appPacket.length,
                        embedPayloadOffset,
                        wirePacket.length);
                    continue;
                }
            }
            else if (wireKind == BotRelayOutboundWireKind.Mismatch)
            {
                if (!BotRelayWireBytes.TryResolveMismatchEmbedPayloadOffset(ref catalog, in wirePacket, out embedPayloadOffset))
                    continue;
            }
            else if (wireKind == BotRelayOutboundWireKind.Match)
            {
                if (!BotRelayWireBytes.TryResolveMatchEmbedPayloadOffset(ref catalog, in wirePacket, out embedPayloadOffset))
                    continue;
            }

            if (BotRelayWireBytes.ShouldComposePipelineListenUtpForOutboundFlush(in wirePacket) &&
                !BotRelayWireBytes.TryComposePipelineListenUtpWire(ref catalog, ref wirePacket, ref embedPayloadOffset))
            {
                if (wireKind == BotRelayOutboundWireKind.Join)
                {
                    BotRelayAgentDiagnostics.LogJoinWireFlushFailed(
                        transport.header.userID,
                        appPacket.length,
                        embedPayloadOffset,
                        wirePacket.length);
                }

                continue;
            }

            if (!BotRelayWireBytes.Equals(ref capturedHello, in liveHello))
            {
                if (!BotRelayWireBytes.TryApplySessionPatch(
                        ref wirePacket,
                        ref connectTemplate,
                        ref capturedHello,
                        in liveHello))
                    continue;
            }

            if (wireKind == BotRelayOutboundWireKind.Join)
            {
                ref var capturedJoinApp = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.SquadJoinApp);

                if (!BotRelayWireBytes.TryEmbedLiveApp(
                        ref wirePacket,
                        embedPayloadOffset,
                        in appPacket,
                        ref capturedJoinApp,
                        farm.wireScratch,
                        scratchFrameBase))
                {
                    BotRelayAgentDiagnostics.LogJoinWireFlushFailed(
                        transport.header.userID,
                        appPacket.length,
                        embedPayloadOffset,
                        wirePacket.length);
                    continue;
                }

                if (!BotRelayWireBytes.TryReadFramedJoinRelayAppAtOffset(in wirePacket, embedPayloadOffset, out _))
                {
                    BotRelayAgentDiagnostics.LogJoinWireFlushFailed(
                        transport.header.userID,
                        appPacket.length,
                        embedPayloadOffset,
                        wirePacket.length);
                    continue;
                }
            }
            else if (wireKind == BotRelayOutboundWireKind.Mismatch)
            {
                ref var statusApp = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.StatusApp);
                if (!BotRelayWireBytes.TryEmbedLiveApp(
                        ref wirePacket,
                        embedPayloadOffset,
                        in appPacket,
                        ref statusApp,
                        farm.wireScratch,
                        scratchFrameBase))
                    continue;

                if (!BotRelayWireBytes.TryReadFramedRelayAppAtOffset(
                        in wirePacket,
                        embedPayloadOffset,
                        (int)NetworkRelayMessageType.Mismatch,
                        out _))
                    continue;
            }
            else if (wireKind == BotRelayOutboundWireKind.Match)
            {
                ref var matchApp = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.MatchApp);
                if (!BotRelayWireBytes.TryEmbedLiveApp(
                        ref wirePacket,
                        embedPayloadOffset,
                        in appPacket,
                        ref matchApp,
                        farm.wireScratch,
                        scratchFrameBase))
                    continue;

                if (!BotRelayWireBytes.TryReadFramedRelayAppAtOffset(
                        in wirePacket,
                        embedPayloadOffset,
                        (int)NetworkRelayMessageType.Match,
                        out _))
                    continue;
            }

            if (!__TryEnqueueUtpListenWire(
                    in wirePacket,
                    wireKind,
                    transport.virtualPort,
                    transport.header.userID,
                    ref manager,
                    ref catalog))
            {
                if (wireKind == BotRelayOutboundWireKind.Join)
                {
                    BotRelayAgentDiagnostics.LogJoinWireFlushFailed(
                        transport.header.userID,
                        appPacket.length,
                        embedPayloadOffset,
                        wirePacket.length);
                }

                continue;
            }

            sentAny = true;
            sentIndex = i;
            if (wireKind == BotRelayOutboundWireKind.Status && farm.inboxSentInitialStatus[index] != 0)
            {
                BotRelayAgentDiagnostics.LogRelayReadyOnce(
                    index,
                    transport.header.userID,
                    transport.virtualPort);
            }

            break;
        }

        if (!sentAny)
            return;

        for (int j = sentIndex + 1; j < count; ++j)
            farm.outbound[baseIndex + j - 1] = farm.outbound[baseIndex + j];
        farm.outboundCount[index] = count - 1;
    }

    private static void __RequestConnect(
        int index,
        ref BotRelayFarmNative farm,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog)
    {
        ref var state = ref farm.transport.ElementAt(index);
        if ((state.flags & BotRelayTransportFlags.ConnectPending) == 0)
            return;

        if (!catalog.IsValid)
            return;

        ushort virtualPort = state.virtualPort;
        if (virtualPort == 0)
            return;

        // Connect is the only Bot->Server UTP path. Its payload carries ClientHeader, so every
        // agent must inject the handshake captured with its own identity; business frames remain
        // server-side injections and never reuse this transport wire.
        int wireOffset = 0;
        int injectCount = 0;
        if ((uint)index < (uint)catalog.agentConnectClientWireRanges.Length)
        {
            var agentRange = catalog.agentConnectClientWireRanges[index];
            if (agentRange.count > 0 && agentRange.offset >= 0 &&
                agentRange.offset + agentRange.count <= catalog.agentConnectClientWires.Length)
            {
                wireOffset = agentRange.offset;
                injectCount = agentRange.count;
            }
        }

        bool usePerAgentWire = injectCount > 0;
        if (!usePerAgentWire)
        {
            injectCount = catalog.connectClientWireCount > 0
                ? catalog.connectClientWireCount
                : 1;
            injectCount = math.min(injectCount, catalog.connectClientWires.Length);
        }

        for (int i = 0; i < injectCount; ++i)
        {
            var entry = usePerAgentWire
                ? catalog.agentConnectClientWires[wireOffset + i]
                : catalog.connectClientWires[i];
            ref var wire = ref BotRelayWireBytes.GetWire(ref catalog, entry.wireIndex);
            if (BotRelayWireBytes.IsEmpty(ref wire))
                continue;

            if (!BotRelayWireBytes.TryCopyWireToPacket(ref wire, out var wirePacket))
                continue;

            manager.EnqueueToListenFromPacketBurst(
                virtualPort,
                in wirePacket,
                BotRelayListenInjectClass.ConnectHandshake);
        }

        state.flags &= ~BotRelayTransportFlags.ConnectPending;
        state.flags |= BotRelayTransportFlags.ConnectRequested;
        state.flags |= BotRelayTransportFlags.Connected;
        farm.wireLiveServerHello[index] = default;

        ref var capturedHello = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.ServerHello);
        if (BotRelayWireBytes.TryCopyWireToPacket(ref capturedHello, out var provisionalHello))
            farm.wireLiveServerHello[index] = provisionalHello;

        __MaybeQueueInitialStatus(index, ref farm);
        BotRelayAgentDiagnostics.LogRelayHandshake(state.header.userID, "connect-injected", virtualPort);
    }

    public static void MaybeQueueInitialStatus(int index, ref BotRelayFarmNative farm) =>
        __MaybeQueueInitialStatus(index, ref farm);

    private static void __MaybeQueueInitialStatus(int index, ref BotRelayFarmNative farm)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if ((transport.flags & BotRelayTransportFlags.Connected) == 0)
            return;

        if (farm.wireLiveServerHello[index].IsEmpty)
            return;

        if (farm.inboxSentInitialStatus[index] != 0)
            return;

        // Connection is the sole bot→server UTP concern. Mark the slot ready here; PostTick then
        // injects Status(0) through NetworkRelayServerHandler before any Match/Join can start.
        // Do not enqueue a synthetic business packet into the legacy outbound UTP buffer.
        farm.inboxSentInitialStatus[index] = 1;
    }

    private static void __PumpLinkLayerAcks(
        int index,
        ref BotRelayFarmNative farm,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if (transport.virtualPort == 0)
            return;

        if ((transport.flags & BotRelayTransportFlags.Connected) == 0)
            return;

        if (farm.wireLiveServerHello[index].IsEmpty)
            return;

        ushort virtualPort = transport.virtualPort;
        var liveHello = farm.wireLiveServerHello[index];
        int scratchBase = BotRelayWireBytes.GetWireScratchOffset(index);
        int ackBudget = BotRelayJobLimits.MaxLinkAckPacketsPerPump;

        while (ackBudget-- > 0 &&
               manager.TryDequeueToBotBurst(virtualPort, out var wirePacket))
        {
            if (wirePacket.IsEmpty)
                continue;

            if (__TryUnwrapInbound(in wirePacket, ref catalog, farm.wireScratch, scratchBase, out var appPacket) && !appPacket.IsEmpty)
            {
                BotRelaySlotOps.TryEnqueueInbound(
                    index, farm.inbound, farm.inboundCount, in appPacket, ref manager);
                continue;
            }

            if (BotRelayWireBytes.TryNormalizePopEventsPayload(in wirePacket, out appPacket) &&
                !appPacket.IsEmpty)
            {
                BotRelaySlotOps.TryEnqueueInbound(
                    index, farm.inbound, farm.inboundCount, in appPacket, ref manager);
                continue;
            }

            if (BotRelayWireBytes.TryExtractRelayAppPayload(
                    in wirePacket,
                    farm.wireScratch,
                    scratchBase,
                    ref catalog,
                    out appPacket) &&
                !appPacket.IsEmpty)
            {
                BotRelaySlotOps.TryEnqueueInbound(
                    index, farm.inbound, farm.inboundCount, in appPacket, ref manager);
                continue;
            }

            if (__TryInjectLinkAck(
                    virtualPort,
                    ref manager,
                    ref catalog,
                    in wirePacket,
                    in liveHello,
                    farm.wireScratch,
                    scratchBase,
                    transport.header.userID))
                continue;

            BotRelayAgentDiagnostics.LogUnmappedInboundWire(
                transport.header.userID, wirePacket.length);
            __LogInviteShellDrainIfNeeded(
                transport.header.userID,
                wirePacket.length,
                "unmappedDrop",
                false,
                default,
                in wirePacket);
            ++manager.diagInboundUnmapped;
        }
    }

    private static bool __TryInjectLinkAck(
        ushort virtualPort,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket inboundWire,
        in BotRelayPacket liveHello,
        NativeArray<byte> scratch,
        int scratchOffset,
        uint botUserID)
    {
        if (!BotRelayWireBytes.LooksLikeLinkAckCandidate(
                in inboundWire,
                ref catalog,
                scratch,
                scratchOffset))
            return false;

        if (catalog.connectClientWires.Length == 0)
            return false;

        if (BotRelayWireBytes.LooksLikeConnectHandshake(in inboundWire))
            return false;

        ref var connectTemplate = ref BotRelayWireBytes.GetWire(
            ref catalog,
            catalog.connectClientWires[0].wireIndex);
        ref var capturedHello = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.ServerHello);
        int ackWireIndex;

        if (catalog.linkAckWireIndices.Length == 0)
        {
            ackWireIndex = catalog.templateWireIndex[(int)BotRelayCatalogPacketSlot.StatusOutbound];
            if (ackWireIndex < 0)
                return false;
        }
        else
        {
            ackWireIndex = catalog.linkAckWireIndices[0];

            for (int i = 0; i < catalog.linkAckWireIndices.Length; ++i)
            {
                int candidateIndex = catalog.linkAckWireIndices[i];
                ref var candidate = ref BotRelayWireBytes.GetWire(ref catalog, candidateIndex);
                if (candidate.length == inboundWire.length)
                {
                    ackWireIndex = candidateIndex;
                    break;
                }
            }
        }

        ref var ackTemplate = ref BotRelayWireBytes.GetWire(ref catalog, ackWireIndex);
        if (BotRelayWireBytes.IsEmpty(ref ackTemplate))
            return false;

        if (!BotRelayWireBytes.TryCopyWireToPacket(ref ackTemplate, out var ackWire))
            return false;

        if (!BotRelayWireBytes.IsEmpty(ref capturedHello) && !liveHello.IsEmpty &&
            !BotRelayWireBytes.Equals(ref capturedHello, in liveHello))
        {
            if (!BotRelayWireBytes.TryApplySessionPatch(
                    ref ackWire,
                    ref connectTemplate,
                    ref capturedHello,
                    in liveHello))
                return false;
        }

        ++manager.diagLinkAckSwallowed;
        BotRelayAgentDiagnostics.LogLinkAckSwallowed(
            botUserID,
            inboundWire.length,
            inboundWire.length > 0 ? inboundWire.GetByte(0) : 0);
        return true;
    }

    private static bool __IsInviteRelayApp(in BotRelayPacket app) =>
        BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type) &&
        type == (int)ReplyMessageType.Invite;

    private static void __LogInviteShellDrainIfNeeded(
        uint botUserID,
        int wireLength,
        in FixedString64Bytes decodePath,
        bool enqueued,
        in BotRelayPacket appPacket,
        in BotRelayPacket wirePacket)
    {
        if (wireLength < 80 || wireLength > 160)
            return;

        int relayType = -1;
        int appLength = 0;
        if (!appPacket.IsEmpty)
        {
            appLength = appPacket.length;
            if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in appPacket, out int type))
                relayType = type;
        }

        BotRelayAgentDiagnostics.LogInviteShellDrain(
            botUserID,
            wireLength,
            in decodePath,
            enqueued,
            relayType,
            appLength,
            in appPacket);
    }

    private static bool __TryUnwrapInbound(
        in BotRelayPacket wirePacket,
        ref BotRelayCatalogBlob catalog,
        NativeArray<byte> scratch,
        int scratchOffset,
        out BotRelayPacket appPacket)
    {
        appPacket = default;
        if (wirePacket.IsEmpty)
            return false;

        if (BotRelayInviteDecodeOps.LooksLikeInviteShell(in wirePacket))
        {
            return BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                in wirePacket,
                ref catalog,
                out appPacket,
                out _) && !appPacket.IsEmpty;
        }

        if (BotRelayWireBytes.TryNormalizePopEventsPayload(in wirePacket, out appPacket) &&
            !appPacket.IsEmpty)
            return true;

        // Relay app packets (Invite, etc.) are larger than link-layer keepalives; extract before
        // catalog template equality that can return true with an empty app frame.
        if (wirePacket.length > 32 &&
            BotRelayWireBytes.TryExtractRelayAppPayload(
                in wirePacket,
                scratch,
                scratchOffset,
                ref catalog,
                out appPacket) &&
            !appPacket.IsEmpty)
            return true;

        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
        {
            var mapping = catalog.inboundMappings[i];
            ref var wire = ref BotRelayWireBytes.GetWire(ref catalog, mapping.wireIndex);
            if (BotRelayWireBytes.Equals(ref wire, in wirePacket))
            {
                ref var appWire = ref BotRelayWireBytes.GetWire(ref catalog, mapping.appIndex);
                BotRelayWireBytes.TryCopyWireToPacket(ref appWire, out appPacket);
                if (!appPacket.IsEmpty)
                    return true;
            }
        }

        ref var serverHello = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.ServerHello);
        if (!BotRelayWireBytes.IsEmpty(ref serverHello) && BotRelayWireBytes.Equals(ref serverHello, in wirePacket))
            return true;

        if (wirePacket.length <= 48 && __MatchesAnyServerTemplate(in wirePacket, ref catalog))
            return true;

        return BotRelayWireBytes.TryExtractRelayAppPayload(
            in wirePacket,
            scratch,
            scratchOffset,
            ref catalog,
            out appPacket);
    }

    private static bool __TryResolveRelayAppFromWire(
        in BotRelayPacket wirePacket,
        ref BotRelayCatalogBlob catalog,
        NativeArray<byte> scratch,
        int scratchOffset,
        out BotRelayPacket appPacket)
    {
        appPacket = default;
        if (wirePacket.IsEmpty)
            return false;

        if (BotRelayWireBytes.TryNormalizePopEventsPayload(in wirePacket, out appPacket) &&
            !appPacket.IsEmpty)
            return true;

        if (BotRelayWireBytes.TryExtractRelayAppPayload(
                in wirePacket,
                scratch,
                scratchOffset,
                ref catalog,
                out appPacket) &&
            !appPacket.IsEmpty)
            return true;

        return false;
    }

    private static bool __MatchesAnyServerTemplate(in BotRelayPacket wirePacket, ref BotRelayCatalogBlob catalog)
    {
        for (int i = 0; i < catalog.connectServerWires.Length; ++i)
        {
            var entry = catalog.connectServerWires[i];
            ref var wire = ref BotRelayWireBytes.GetWire(ref catalog, entry.wireIndex);
            if (BotRelayWireBytes.Equals(ref wire, in wirePacket))
                return true;
        }

        return false;
    }

    private static bool __TryResolveOutboundWire(
        in BotRelayPacket appPacket,
        ref BotRelayCatalogBlob catalog,
        out BotRelayOutboundWireKind wireKind,
        out int wireIndex)
    {
        wireKind = BotRelayOutboundWireKind.None;
        wireIndex = -1;
        if (appPacket.IsEmpty)
            return false;

        ref var statusApp = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.StatusApp);
        var statusWireIndex = catalog.templateWireIndex[(int)BotRelayCatalogPacketSlot.StatusOutbound];
        if (BotRelayWireBytes.AppMatches(in appPacket, ref statusApp) && statusWireIndex >= 0)
        {
            wireIndex = statusWireIndex;
            wireKind = BotRelayOutboundWireKind.Status;
            return true;
        }

        ref var matchApp = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.MatchApp);
        var matchWireIndex = catalog.templateWireIndex[(int)BotRelayCatalogPacketSlot.MatchOutbound];
        if (BotRelayWireBytes.AppMatches(in appPacket, ref matchApp) && matchWireIndex >= 0)
        {
            wireIndex = matchWireIndex;
            wireKind = BotRelayOutboundWireKind.Match;
            return true;
        }

        ref var applyApp = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.ApplyMatchApp);
        var applyWireIndex = catalog.templateWireIndex[(int)BotRelayCatalogPacketSlot.ApplyMatchOutbound];
        if (BotRelayWireBytes.AppMatches(in appPacket, ref applyApp) && applyWireIndex >= 0)
        {
            wireIndex = applyWireIndex;
            wireKind = BotRelayOutboundWireKind.ApplyMatch;
            return true;
        }

        ref var leaveApp = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.SquadLeaveApp);
        var leaveWireIndex = catalog.templateWireIndex[(int)BotRelayCatalogPacketSlot.SquadLeaveOutbound];
        if (BotRelayWireBytes.AppMatches(in appPacket, ref leaveApp) && leaveWireIndex >= 0)
        {
            wireIndex = leaveWireIndex;
            wireKind = BotRelayOutboundWireKind.Leave;
            return true;
        }

        if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in appPacket, out int relayType) &&
            relayType == (int)NetworkRelayMessageType.Join)
        {
            wireIndex = catalog.templateWireIndex[(int)BotRelayCatalogPacketSlot.SquadJoinOutbound];
            wireKind = BotRelayOutboundWireKind.Join;
            return true;
        }

        if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in appPacket, out int bareRelayType) &&
            bareRelayType == (int)NetworkRelayMessageType.Mismatch &&
            statusWireIndex > BotRelayWireBytes.EmptyWireIndex)
        {
            wireIndex = statusWireIndex;
            wireKind = BotRelayOutboundWireKind.Mismatch;
            return true;
        }

        if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in appPacket, out int matchRelayType) &&
            matchRelayType == (int)NetworkRelayMessageType.Match &&
            matchWireIndex > BotRelayWireBytes.EmptyWireIndex)
        {
            wireIndex = matchWireIndex;
            wireKind = BotRelayOutboundWireKind.Match;
            return true;
        }

        return false;
    }

    private static void __StoreLiveHello(int index, ref BotRelayFarmNative farm, in BotRelayPacket wirePacket)
    {
        if (wirePacket.IsEmpty)
            return;

        bool wasEmpty = farm.wireLiveServerHello[index].IsEmpty;
        farm.wireLiveServerHello[index] = wirePacket;
        if (wasEmpty)
        {
            BotRelayAgentDiagnostics.LogRelayHandshake(
                farm.transport[index].header.userID,
                "hello-live",
                farm.transport[index].virtualPort);
        }
    }

    private static void __StoreJoinListenShell(int index, ref BotRelayFarmNative farm, in BotRelayPacket wirePacket)
    {
        if (wirePacket.IsEmpty)
            return;

        farm.wireLiveJoinListenShell[index] = wirePacket;
    }

    private static bool __TryBuildPopEventsListenPacketBurst(
        in BotRelayPacket app,
        NativeArray<byte> scratch,
        int scratchOffset,
        out BotRelayPacket framed)
    {
        framed = default;
        if (app.IsEmpty || !scratch.IsCreated || scratchOffset < 0)
            return false;

        int payloadOffset = scratchOffset + sizeof(ushort);
        if (payloadOffset + app.length > scratch.Length)
            return false;

        if (!app.TryCopyBytesTo(scratch, payloadOffset))
            return false;

        scratch[scratchOffset] = (byte)(app.length & 0xff);
        scratch[scratchOffset + 1] = (byte)(app.length >> 8);

        int framedLength = sizeof(ushort) + app.length;
        framed.length = framedLength;
        for (int i = 0; i < framedLength; ++i)
            framed.SetByte(i, scratch[scratchOffset + i]);
        return true;
    }

    private static bool __TryEnqueueUtpListenWire(
        in BotRelayPacket utpWire,
        BotRelayOutboundWireKind wireKind,
        ushort virtualPort,
        uint userId,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog)
    {
        if (utpWire.IsEmpty)
            return false;

        if (BotRelayWireBytes.IsLinkAckCatalogTemplateWire(ref catalog, in utpWire) ||
            BotRelayWireBytes.WouldPoisonPopEventsListenInject(in utpWire))
        {
            ++manager.diagListenInjectSkippedPopEvents;
            return false;
        }

        manager.EnqueueToListenFromPacketBurst(virtualPort, in utpWire);
        switch (wireKind)
        {
            case BotRelayOutboundWireKind.Join:
                ++manager.diagJoinWireInjected;
                manager.RecordJoinListenInjectBurst(virtualPort, in utpWire);
                BotRelayAgentDiagnostics.LogJoinWireFlushed(userId, utpWire.length, virtualPort);
                break;
            case BotRelayOutboundWireKind.Mismatch:
                ++manager.diagMismatchWireInjected;
                break;
            case BotRelayOutboundWireKind.Match:
                ++manager.diagMatchWireInjected;
                break;
        }

        return true;
    }
}
