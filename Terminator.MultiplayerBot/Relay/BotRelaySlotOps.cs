using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using ZG;

[BurstCompile]
internal static class BotRelaySlotOps
{
    public struct SlotSendBuffer : BotRelayCodec.IClientSendBuffer
    {
        public int agentIndex;
        public NativeArray<BotRelayPacket> outbound;
        public NativeArray<int> outboundCount;
        public NativeArray<byte> codecScratch;

        public bool BeginWrite(out DataStreamWriter writer, ushort capacity = 1024)
        {
            if (!codecScratch.IsCreated || codecScratch.Length < capacity)
            {
                writer = default;
                return false;
            }

            writer = new DataStreamWriter(codecScratch.GetSubArray(0, capacity));
            return true;
        }

        public void EndWrite(in DataStreamWriter writer)
        {
            if (!codecScratch.IsCreated)
                return;

            int length = writer.Length;
            if (length > 0)
                TryEnqueueOutbound(agentIndex, outbound, outboundCount, codecScratch.GetSubArray(0, length));
        }
    }

    public static bool TryEnqueueOutbound(
        int agentIndex,
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        NativeArray<byte> payload)
    {
        if (!payload.IsCreated || payload.Length == 0)
            return false;

        int count = outboundCount[agentIndex];
        if (count >= BotRelayFarmNative.MaxOutboundPerAgent)
            return false;

        var packet = default(BotRelayPacket);
        if (!packet.TryWriteFrom(payload))
            return false;

        outbound[agentIndex * BotRelayFarmNative.MaxOutboundPerAgent + count] = packet;
        outboundCount[agentIndex] = count + 1;
        return true;
    }

    public static bool TryEnqueueInbound(
        int agentIndex,
        NativeArray<BotRelayPacket> inbound,
        NativeArray<int> inboundCount,
        in BotRelayPacket packet,
        ref BotRelayManager manager)
    {
        if (packet.IsEmpty)
            return false;

        int count = inboundCount[agentIndex];
        if (count >= BotRelayFarmNative.MaxInboundPerAgent)
        {
            BotRelayInboundProbe.RecordInboundDrop();
            return false;
        }

        inbound[agentIndex * BotRelayFarmNative.MaxInboundPerAgent + count] = packet;
        inboundCount[agentIndex] = count + 1;
        ++manager.diagInboundAppsEnqueued;
        return true;
    }

    public static bool TryEnqueueOutbound(
        int agentIndex,
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        in BotRelayPacket packet)
    {
        if (packet.IsEmpty)
            return false;

        int count = outboundCount[agentIndex];
        if (count >= BotRelayFarmNative.MaxOutboundPerAgent)
            return false;

        outbound[agentIndex * BotRelayFarmNative.MaxOutboundPerAgent + count] = packet;
        outboundCount[agentIndex] = count + 1;
        return true;
    }

    public static void ClearOutbound(int agentIndex, NativeArray<int> outboundCount) =>
        outboundCount[agentIndex] = 0;

    public static void PrepareAgent(
        ref BotRelayFarmNative farm,
        int index,
        in ClientHeader header,
        ushort listenPort,
        int matchLevel)
    {
        farm.transport[index] = new BotRelayTransportState
        {
            header = header,
            listenPort = listenPort,
            flags = BotRelayTransportFlags.Created | BotRelayTransportFlags.ConnectPending
        };

        farm.agentStates[index] = new BotAgentState
        {
            userID = header.userID,
            state = BotState.Idle,
            matchLevel = matchLevel,
            remoteOfflineSince = -1.0
        };

        // Server Join rejects while identity.match != 0; defer idle MatchToSend until relay Status
        // is queued (BotAgentLogic re-arms nextIdleMatchTime) or post-squad leave (+5s).
        farm.nextIdleMatchTime[index] = double.PositiveInfinity;
        farm.relayServerStatusInjected[index] = 0;
        BotRelayAgentDiagnostics.ResetRelayReadyLogged(index);
    }

    /// <summary>
    /// Assigns stable virtualPort per agent on the main thread before any Farm Tick Job runs.
    /// Required for parallel PreTick (no port-map writes in Burst jobs).
    /// </summary>
    public static void AssignVirtualPortsAtSpawn(ref BotRelayFarmNative farm, ref BotRelayManager manager, uint seed)
    {
        var rng = Unity.Mathematics.Random.CreateFromIndex(seed != 0 ? seed : 0xB07F4E00u);
        for (int i = 0; i < farm.agentCount; ++i)
        {
            ref var transport = ref farm.transport.ElementAt(i);
            if (transport.virtualPort != 0)
                continue;

            transport.virtualPort = manager.AllocateVirtualPortBurst(ref rng);
            manager.RegisterBotVirtualPortBurst(transport.header.userID, transport.virtualPort);
        }
    }

    public static void TryDisconnect(ref BotRelayFarmNative farm, int index, ref NetworkDriver driver)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if (transport.connection != default && driver.IsCreated)
            driver.Disconnect(transport.connection);

        transport.connection = default;
        transport.flags &= ~(BotRelayTransportFlags.Connected | BotRelayTransportFlags.ConnectRequested);
    }

    public static void TryDisconnect(ref BotRelayFarmNative farm, int index)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        transport.connection = default;
        transport.flags &= ~(BotRelayTransportFlags.Connected | BotRelayTransportFlags.ConnectRequested);
    }

    public static void ConnectBurst(
        int index,
        ref NativeArray<BotRelayTransportState> transport,
        ref NetworkDriver driver,
        NativeArray<byte> wireScratch)
    {
        var state = transport[index];
        if ((state.flags & BotRelayTransportFlags.Created) == 0 || !driver.IsCreated)
            return;

        if ((state.flags & BotRelayTransportFlags.ConnectPending) != 0 &&
            (state.flags & BotRelayTransportFlags.ConnectRequested) == 0 &&
            (state.flags & BotRelayTransportFlags.Connected) == 0)
        {
            int scratchBase = index * BotRelayPacket.MaxPayloadSize;
            if (!BotRelayCodec.TryBuildConnectPayload(in state.header, wireScratch, scratchBase, out int payloadLength))
                return;

            var payload = wireScratch.GetSubArray(scratchBase, payloadLength);
            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(state.listenPort);
            state.connection = driver.Connect(endpoint, payload);
            if (state.connection != default)
            {
                state.flags &= ~BotRelayTransportFlags.ConnectPending;
                state.flags |= BotRelayTransportFlags.ConnectRequested;
                BotRelayAgentDiagnostics.LogTransportConnecting(state.header.userID, state.listenPort);
            }
        }

        transport[index] = state;
    }

    public static void PreFlushMain(
        int index,
        ref NativeArray<BotRelayTransportState> transport,
        ref NetworkDriver driver,
        NetworkPipeline pipeline,
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        NativeArray<byte> wireScratch) =>
        PreFlushBurst(
            index,
            ref transport,
            driver.ToConcurrent(),
            pipeline,
            outbound,
            outboundCount,
            wireScratch);

    public static void PreFlushBurst(
        int index,
        ref NativeArray<BotRelayTransportState> transport,
        NetworkDriver.Concurrent driver,
        NetworkPipeline pipeline,
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        NativeArray<byte> wireScratch)
    {
        var state = transport[index];
        if ((state.flags & BotRelayTransportFlags.Created) == 0)
            return;

        if ((state.flags & BotRelayTransportFlags.Connected) == 0 || state.connection == default)
            return;

        if (!wireScratch.IsCreated)
            return;

        int scratchBase = index * BotRelayPacket.MaxPayloadSize;
        int baseIndex = index * BotRelayFarmNative.MaxOutboundPerAgent;
        int count = outboundCount[index];
        for (int i = 0; i < count; ++i)
        {
            var packet = outbound[baseIndex + i];
            if (packet.IsEmpty)
                continue;

            if (!packet.TryCopyBytesTo(wireScratch, scratchBase + sizeof(ushort)))
                continue;

            if (!BotRelayCodec.TryWriteTransportFramePrefix(
                    wireScratch,
                    scratchBase,
                    packet.length,
                    out int framedLength))
                continue;

            if (driver.BeginSend(pipeline, state.connection, out var writer) < 0)
                continue;

            writer.WriteBytes(wireScratch.GetSubArray(scratchBase, framedLength));
            driver.EndSend(writer);
        }

        outboundCount[index] = 0;
        transport[index] = state;
    }

    public static void PostReceiveMain(
        int index,
        ref NativeArray<BotRelayTransportState> transport,
        ref NetworkDriver driver,
        NativeArray<BotRelayPacket> inbound,
        NativeArray<int> inboundCount,
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        NativeArray<byte> sentInitialStatus,
        NativeArray<byte> wireScratch) =>
        PostReceiveBurst(
            index,
            ref transport,
            driver.ToConcurrent(),
            inbound,
            inboundCount,
            outbound,
            outboundCount,
            sentInitialStatus,
            wireScratch);

    public static void PostReceiveBurst(
        int index,
        ref NativeArray<BotRelayTransportState> transport,
        NetworkDriver.Concurrent driver,
        NativeArray<BotRelayPacket> inbound,
        NativeArray<int> inboundCount,
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        NativeArray<byte> sentInitialStatus,
        NativeArray<byte> wireScratch)
    {
        var state = transport[index];
        if ((state.flags & BotRelayTransportFlags.Created) == 0)
            return;

        if (state.connection == default)
            return;

        while (true)
        {
            var cmd = driver.PopEventForConnection(state.connection, out var stream, out _);
            if (cmd == NetworkEvent.Type.Empty)
                break;

            switch (cmd)
            {
                case NetworkEvent.Type.Connect:
                    state.flags |= BotRelayTransportFlags.Connected;
                    __TryWriteInitialStatus(index, outbound, outboundCount, sentInitialStatus, wireScratch);
                    BotRelayAgentDiagnostics.LogTransportConnected(state.header.userID);
                    break;
                case NetworkEvent.Type.Data:
                    state.flags |= BotRelayTransportFlags.Connected;
                    __TryWriteInitialStatus(index, outbound, outboundCount, sentInitialStatus, wireScratch);
                    __EnqueueTransportPayload(index, ref stream, inbound, inboundCount);
                    break;
                case NetworkEvent.Type.Disconnect:
                    state.flags &= ~BotRelayTransportFlags.Connected;
                    break;
            }
        }

        transport[index] = state;
    }

    public static bool TryWriteMatch(ref BotRelayFarmNative farm, int index, int level)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWriteMatchToSend(ref send, level);
    }

    public static bool TryWriteMismatch(ref BotRelayFarmNative farm, int index)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWriteMismatch(ref send);
    }

    public static bool TryWriteSquadJoin(ref BotRelayFarmNative farm, int index, uint squadInviteID)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWriteSquadJoin(ref send, squadInviteID);
    }

    public static bool TryWriteApplyMatch(ref BotRelayFarmNative farm, int index)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWriteApplyMatch(ref send);
    }

    public static bool TryWritePlay(ref BotRelayFarmNative farm, int index, in ClientMessagePlay play)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWritePlay(ref send, in play);
    }

    public static bool TryWriteSquadLeave(ref BotRelayFarmNative farm, int index)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWriteSquadLeave(ref send);
    }

    public static bool TryWriteStatus(ref BotRelayFarmNative farm, int index, int channelStatus)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWriteStatus(ref send, channelStatus);
    }

    public static bool TryWriteRelayPayload(ref BotRelayFarmNative farm, int index, NativeArray<byte> payload)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWriteRelayPayload(ref send, payload);
    }

    public static bool TryWriteRelayPayload(ref BotRelayFarmNative farm, int index, byte[] payload)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWriteRelayPayload(ref send, payload);
    }

    public static bool TryWritePlayerPropertyBody(ref BotRelayFarmNative farm, int index, byte[] recordedPayload)
    {
        var send = CreateSendBuffer(farm, index);
        return BotRelayCodec.TryWritePlayerPropertyBody(ref send, recordedPayload);
    }

    public static SlotSendBuffer CreateCaptureSendBuffer(
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        NativeArray<byte> codecScratch) =>
        new SlotSendBuffer
        {
            agentIndex = 0,
            outbound = outbound,
            outboundCount = outboundCount,
            codecScratch = codecScratch
        };

    public static SlotSendBuffer CreateSendBuffer(BotRelayFarmNative farm, int index)
    {
        int scratchBase = index * BotRelayPacket.MaxPayloadSize;
        return new SlotSendBuffer
        {
            agentIndex = index,
            outbound = farm.outbound,
            outboundCount = farm.outboundCount,
            codecScratch = farm.wireScratch.GetSubArray(scratchBase, BotRelayPacket.MaxPayloadSize)
        };
    }

    public static void DrainInboundFromServer(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayWireCatalogState catalog) =>
        BotRelayWireCatalog.DrainInbound(index, ref farm, in catalog);

    public static void FlushOutboundWire(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayWireCatalogState catalog) =>
        BotRelayWireCatalog.FlushOutbound(index, ref farm, in catalog);

    public static void TryWriteInitialStatus(int index, ref BotRelayFarmNative farm)
    {
        byte before = farm.inboxSentInitialStatus[index];
        __TryWriteInitialStatus(
            index,
            farm.outbound,
            farm.outboundCount,
            farm.inboxSentInitialStatus,
            farm.wireScratch);
        if (before == 0 && farm.inboxSentInitialStatus[index] != 0)
        {
            BotRelayAgentDiagnostics.LogRelayHandshake(
                farm.transport[index].header.userID,
                "status-queued",
                farm.transport[index].virtualPort);
        }
    }

    public static void TryWriteInitialStatus(
        int index,
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        NativeArray<byte> sentInitialStatus,
        NativeArray<byte> wireScratch) =>
        __TryWriteInitialStatus(index, outbound, outboundCount, sentInitialStatus, wireScratch);

    public static void EnqueueTransportPayloadFromStream(
        int agentIndex,
        ref DataStreamReader stream,
        NativeArray<BotRelayPacket> inbound,
        NativeArray<int> inboundCount) =>
        __EnqueueTransportPayload(agentIndex, ref stream, inbound, inboundCount);

    private static void __TryWriteInitialStatus(
        int index,
        NativeArray<BotRelayPacket> outbound,
        NativeArray<int> outboundCount,
        NativeArray<byte> sentInitialStatus,
        NativeArray<byte> wireScratch)
    {
        if (sentInitialStatus[index] != 0)
            return;

        if (!wireScratch.IsCreated)
            return;

        int scratchBase = index * BotRelayPacket.MaxPayloadSize;
        var send = new SlotSendBuffer
        {
            agentIndex = index,
            outbound = outbound,
            outboundCount = outboundCount,
            codecScratch = wireScratch.GetSubArray(scratchBase, BotRelayPacket.MaxPayloadSize)
        };

        if (BotRelayCodec.TryWriteStatus(ref send, 0))
            sentInitialStatus[index] = 1;
    }

    public static void PumpInboundBurst(
        int index,
        ref BotRelayFarmNative farm)
    {
        ref var session = ref farm.sessions.ElementAt(index);
        var header = farm.transport[index].header;
        session.userID = header.userID;

        int baseIndex = index * BotRelayFarmNative.MaxInboundPerAgent;
        int count = farm.inboundCount[index];
        for (int i = 0; i < count; ++i)
        {
            var packet = farm.inbound[baseIndex + i];
            BotRelaySlotInbox.ParseTransportPayload(
                in packet,
                index,
                ref farm,
                in header,
                farm.inboxSentInitialStatus[index] != 0);
        }

        farm.inboundCount[index] = 0;
    }

    private static void __EnqueueTransportPayload(
        int agentIndex,
        ref DataStreamReader stream,
        NativeArray<BotRelayPacket> inbound,
        NativeArray<int> inboundCount)
    {
        while (stream.GetBytesRead() < stream.Length)
        {
            int frameStart = stream.GetBytesRead();
            int remaining = stream.Length - frameStart;
            if (remaining <= 0)
                break;

            if (remaining < sizeof(ushort))
            {
                __EnqueueInnerBytes(agentIndex, ref stream, remaining, inbound, inboundCount);
                break;
            }

            ushort size = stream.ReadUShort();
            if (size == 0 || size > stream.Length - stream.GetBytesRead())
            {
                stream.SeekSet(frameStart);
                __EnqueueInnerBytes(agentIndex, ref stream, stream.Length - frameStart, inbound, inboundCount);
                break;
            }

            __EnqueueInnerBytes(agentIndex, ref stream, size, inbound, inboundCount);
        }
    }

    private static void __EnqueueInnerBytes(
        int agentIndex,
        ref DataStreamReader stream,
        int length,
        NativeArray<BotRelayPacket> inbound,
        NativeArray<int> inboundCount)
    {
        if (length <= 0)
            return;

        if (length > BotRelayPacket.MaxPayloadSize)
            length = BotRelayPacket.MaxPayloadSize;

        var packet = default(BotRelayPacket);
        if (!packet.TryReadBytesFrom(ref stream, length))
            return;

        TryEnqueueInbound(agentIndex, inbound, inboundCount, in packet, ref BotRelayManager.Instance);
    }
}
