using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ZG;

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
        ref var serverHello = ref BotRelayWireBytes.GetServerHello(ref catalog);

        int drainBudget = BotRelayJobLimits.MaxBotPacketsPerDrainJob;
        while (drainBudget-- > 0 && manager.TryDequeuePeeledAppBurst(virtualPort, out var peeledApp))
        {
            if (peeledApp.IsEmpty)
                continue;

            BotRelaySlotOps.TryEnqueueInbound(
                index, farm.inbound, farm.inboundCount, in peeledApp, ref manager);
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

            if (awaitingHello)
            {
                if (BotRelayWireBytes.LooksLikeCurrentServerHello(
                        in wirePacket,
                        ref serverHello))
                {
                    __StoreLiveHello(index, ref farm, in wirePacket);
                }
                else
                {
                    BotRelayAgentDiagnostics.LogUnmappedInboundWire(
                        transport.header.userID, wirePacket.length);
                    ++manager.diagInboundUnmapped;
                }
                continue;
            }

            if ((transport.flags & BotRelayTransportFlags.Connected) != 0)
            {
                var liveHello = farm.wireLiveServerHello[index];
                if (__TryInjectLinkAck(
                        virtualPort,
                        ref manager,
                        ref catalog,
                        in wirePacket,
                        in liveHello,
                        transport.header.userID))
                    continue;
            }

            BotRelayAgentDiagnostics.LogUnmappedInboundWire(
                transport.header.userID, wirePacket.length);
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
        if ((uint)index >= (uint)catalog.agentConnectClientWireRanges.Length)
        {
            __FailConnect(index, ref farm);
            return;
        }

        var agentRange = catalog.agentConnectClientWireRanges[index];
        if (agentRange.count <= 0 ||
            agentRange.offset < 0 ||
            agentRange.offset + agentRange.count > catalog.agentConnectClientWires.Length)
        {
            __FailConnect(index, ref farm);
            return;
        }

        for (int i = 0; i < agentRange.count; ++i)
        {
            var entry = catalog.agentConnectClientWires[agentRange.offset + i];
            ref var wire = ref BotRelayWireBytes.GetWire(ref catalog, entry.wireIndex);
            if (BotRelayWireBytes.IsEmpty(ref wire))
            {
                __FailConnect(index, ref farm);
                return;
            }
        }

        for (int i = 0; i < agentRange.count; ++i)
        {
            var entry = catalog.agentConnectClientWires[agentRange.offset + i];
            ref var wire = ref BotRelayWireBytes.GetWire(ref catalog, entry.wireIndex);
            BotRelayWireBytes.TryCopyWireToPacket(ref wire, out var wirePacket);

            manager.EnqueueConnectLinkToListenFromPacketBurst(
                virtualPort,
                in wirePacket);
        }

        state.flags &= ~BotRelayTransportFlags.ConnectPending;
        state.flags |= BotRelayTransportFlags.ConnectRequested;
        state.flags &= ~BotRelayTransportFlags.Connected;
        farm.wireLiveServerHello[index] = default;
        BotRelayAgentDiagnostics.LogRelayHandshake(state.header.userID, "connect-injected", virtualPort);
    }

    private static void __FailConnect(int index, ref BotRelayFarmNative farm)
    {
        ref var state = ref farm.transport.ElementAt(index);
        state.flags &= ~(BotRelayTransportFlags.ConnectPending |
                         BotRelayTransportFlags.ConnectRequested |
                         BotRelayTransportFlags.Connected);
        farm.wireLiveServerHello[index] = default;
        BotRelayAgentDiagnostics.LogRelayHandshake(
            state.header.userID,
            "connect-wire-missing",
            state.virtualPort);
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
        int ackBudget = BotRelayJobLimits.MaxLinkAckPacketsPerPump;

        while (ackBudget-- > 0 &&
               manager.TryDequeueToBotBurst(virtualPort, out var wirePacket))
        {
            if (wirePacket.IsEmpty)
                continue;

            if (__TryInjectLinkAck(
                    virtualPort,
                    ref manager,
                    ref catalog,
                    in wirePacket,
                    in liveHello,
                    transport.header.userID))
                continue;

            BotRelayAgentDiagnostics.LogUnmappedInboundWire(
                transport.header.userID, wirePacket.length);
            ++manager.diagInboundUnmapped;
        }
    }

    private static bool __TryInjectLinkAck(
        ushort virtualPort,
        ref BotRelayManager manager,
        ref BotRelayCatalogBlob catalog,
        in BotRelayPacket inboundWire,
        in BotRelayPacket liveHello,
        uint botUserID)
    {
        if (!BotRelayWireBytes.LooksLikeLinkAckCandidate(
                in inboundWire,
                ref catalog))
            return false;

        if (catalog.connectClientWires.Length == 0)
            return false;

        if (BotRelayWireBytes.LooksLikeConnectHandshake(in inboundWire))
            return false;

        ref var connectTemplate = ref BotRelayWireBytes.GetWire(
            ref catalog,
            catalog.connectClientWires[0].wireIndex);
        ref var capturedHello = ref BotRelayWireBytes.GetServerHello(ref catalog);
        if (catalog.linkAckWireIndices.Length == 0)
            return false;

        int ackWireIndex = BotRelayWireBytes.EmptyWireIndex;
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

        if (ackWireIndex == BotRelayWireBytes.EmptyWireIndex)
            return false;

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

        manager.EnqueueConnectLinkToListenFromPacketBurst(virtualPort, in ackWire);
        ++manager.diagLinkAckInjected;
        BotRelayAgentDiagnostics.LogLinkAckInjected(
            botUserID,
            inboundWire.length,
            inboundWire.length > 0 ? inboundWire.GetByte(0) : 0);
        return true;
    }

    private static void __StoreLiveHello(int index, ref BotRelayFarmNative farm, in BotRelayPacket wirePacket)
    {
        if (wirePacket.IsEmpty)
            return;

        bool wasEmpty = farm.wireLiveServerHello[index].IsEmpty;
        farm.wireLiveServerHello[index] = wirePacket;
        ref var transport = ref farm.transport.ElementAt(index);
        transport.flags |= BotRelayTransportFlags.Connected;
        if (wasEmpty)
        {
            BotRelayAgentDiagnostics.LogRelayHandshake(
                transport.header.userID,
                "hello-live",
                transport.virtualPort);
        }
    }

}
