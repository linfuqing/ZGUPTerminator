using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG;

[BurstCompile]
internal static class BotRelaySlotOps
{
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

    public static void PrepareAgent(
        ref BotRelayFarmNative farm,
        int index,
        in ClientHeader header,
        int matchLevel)
    {
        farm.transport[index] = new BotRelayTransportState
        {
            header = header,
            flags = BotRelayTransportFlags.ConnectPending
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

    public static void TryDisconnect(ref BotRelayFarmNative farm, int index)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        transport.flags &= ~(BotRelayTransportFlags.Connected | BotRelayTransportFlags.ConnectRequested);
    }

    public static void PumpInboundBurst(
        int index,
        ref BotRelayFarmNative farm)
    {
        var header = farm.transport[index].header;

        int baseIndex = index * BotRelayFarmNative.MaxInboundPerAgent;
        int count = farm.inboundCount[index];
        for (int i = 0; i < count; ++i)
        {
            var packet = farm.inbound[baseIndex + i];
            BotRelaySlotInbox.ParseTransportPayload(
                in packet,
                index,
                ref farm,
                in header);
        }

        farm.inboundCount[index] = 0;
    }

}
