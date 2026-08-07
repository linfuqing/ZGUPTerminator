using System;
using Unity.Collections;
using Unity.Entities;
using ZG;

[System.Flags]
public enum BotRelayTransportFlags : byte
{
    None = 0,
    ConnectPending = 1 << 0,
    ConnectRequested = 1 << 1,
    Connected = 1 << 2,
}

public struct BotRelayTransportState
{
    public ClientHeader header;
    public ushort virtualPort;
    public BotRelayTransportFlags flags;
}

public struct BotRelayFarmNative : IComponentData, IDisposable
{
    public const int MaxInboundPerAgent = 64;
    public const int MaxEventsPerAgent = 32;

    public int agentCount;
    /// <summary>
    /// Length-1; [0] = agent index owning the sole outstanding MatchToSend request, or
    /// <see cref="BotMatchGuard.NoMatchingSlotOwner"/>. MatchToRead releases it even when the
    /// temporary squad Join arrives on a later tick.
    /// </summary>
    public NativeArray<int> matchingSlotOwner;
    /// <summary>Length-1 spin gate protecting <see cref="squadSlotSquadKeys"/> across parallel agents.</summary>
    public NativeArray<int> squadSlotLock;
    /// <summary>
    /// One entry per agent. Zero means no squad lease; otherwise the value is
    /// <c>squadInviteID + 1</c>. Different squads may be served concurrently, while the guard
    /// rejects duplicate keys so a broadcast for one squad still selects exactly one Bot.
    /// </summary>
    public NativeArray<long> squadSlotSquadKeys;
    public NativeArray<BotRelayTransportState> transport;
    public NativeArray<BotRelayPacket> inbound;
    public NativeArray<int> inboundCount;
    public NativeArray<BotRelayEvent> events;
    public NativeArray<int> eventCount;
    public NativeArray<BotSessionState> sessions;
    public NativeArray<BotAgentState> agentStates;
    public NativeArray<BotAgentRuntimeFlags> agentFlags;
    /// <summary>
    /// Per-agent force / harness gate for MatchToSend. Production keeps <c>+Infinity</c> (reactive
    /// dispatch owns matching). Finite values are only set by tests/harness to request an immediate
    /// MatchToSend while Idle.
    /// </summary>
    public NativeArray<double> nextIdleMatchTime;
    /// <summary>Length-1: non-bot <c>matchIDs</c> user currently observed for matchTimeout dispatch, or 0.</summary>
    public NativeArray<uint> matchObserveUserID;
    /// <summary>
    /// Length-1: bumps each time a new observe episode starts (human enters/re-enters matchIDs).
    /// Seeds closest-Idle tie breaks so the same user can get a different bot on re-queue while
    /// a single episode stays deterministic.
    /// </summary>
    public NativeArray<uint> matchObserveGeneration;
    /// <summary>Length-1: elapsed time when farm may dispatch a closest Idle bot for <see cref="matchObserveUserID"/>.</summary>
    public NativeArray<double> matchObserveDispatchTime;
    public NativeArray<int> prevRemoteChannelStatus;
    /// <summary>The committed descriptor for the current joined squad/match generation.</summary>
    public NativeArray<ClientMessageMatchStart> levelStartMessages;
    /// <summary>One-shot: app-layer Status(0) injected so NetworkRelayServer registers the bot for All-broadcast.</summary>
    public NativeArray<byte> relayServerStatusInjected;
    public NativeArray<BotRelayPacket> wireLiveServerHello;
    public NativeArray<BotReplayRuntimeState> replayRuntime;
    public NativeArray<byte> wireScratch;

    public static BotRelayFarmNative Create(int agentCount, Allocator allocator)
    {
        if (agentCount < 1)
            throw new ArgumentOutOfRangeException(nameof(agentCount));

        var farm = new BotRelayFarmNative
        {
            agentCount = agentCount,
        };
        farm.matchingSlotOwner = new NativeArray<int>(1, allocator);
        farm.matchingSlotOwner[0] = BotMatchGuard.NoMatchingSlotOwner;
        farm.squadSlotLock = new NativeArray<int>(1, allocator);
        farm.squadSlotSquadKeys = new NativeArray<long>(agentCount, allocator);
        farm.transport = new NativeArray<BotRelayTransportState>(agentCount, allocator);
        farm.inbound = new NativeArray<BotRelayPacket>(agentCount * MaxInboundPerAgent, allocator);
        farm.inboundCount = new NativeArray<int>(agentCount, allocator);
        farm.events = new NativeArray<BotRelayEvent>(agentCount * MaxEventsPerAgent, allocator);
        farm.eventCount = new NativeArray<int>(agentCount, allocator);
        farm.sessions = new NativeArray<BotSessionState>(agentCount, allocator);
        farm.agentStates = new NativeArray<BotAgentState>(agentCount, allocator);
        farm.agentFlags = new NativeArray<BotAgentRuntimeFlags>(agentCount, allocator);
        farm.nextIdleMatchTime = new NativeArray<double>(agentCount, allocator);
        farm.matchObserveUserID = new NativeArray<uint>(1, allocator);
        farm.matchObserveGeneration = new NativeArray<uint>(1, allocator);
        farm.matchObserveDispatchTime = new NativeArray<double>(1, allocator);
        farm.matchObserveDispatchTime[0] = double.PositiveInfinity;
        farm.prevRemoteChannelStatus = new NativeArray<int>(agentCount, allocator);
        farm.levelStartMessages = new NativeArray<ClientMessageMatchStart>(agentCount, allocator);
        farm.relayServerStatusInjected = new NativeArray<byte>(agentCount, allocator);
        farm.wireLiveServerHello = new NativeArray<BotRelayPacket>(agentCount, allocator);
        farm.replayRuntime = new NativeArray<BotReplayRuntimeState>(agentCount, allocator);
        farm.wireScratch = new NativeArray<byte>(agentCount * BotRelayPacket.MaxPayloadSize, allocator);

        for (int i = 0; i < agentCount; ++i)
        {
            farm.sessions[i] = new BotSessionState
            {
                channel = ReplyMessageShared.CHANNEL_NULL,
            };
            farm.replayRuntime[i] = new BotReplayRuntimeState
            {
                catalogIndex = BotReplayRuntimeState.InvalidCatalogIndex
            };
        }

        return farm;
    }

    public void Dispose()
    {
        if (matchingSlotOwner.IsCreated)
            matchingSlotOwner.Dispose();
        if (squadSlotLock.IsCreated)
            squadSlotLock.Dispose();
        if (squadSlotSquadKeys.IsCreated)
            squadSlotSquadKeys.Dispose();
        if (replayRuntime.IsCreated)
            replayRuntime.Dispose();
        if (transport.IsCreated)
            transport.Dispose();
        if (inbound.IsCreated)
            inbound.Dispose();
        if (inboundCount.IsCreated)
            inboundCount.Dispose();
        if (events.IsCreated)
            events.Dispose();
        if (eventCount.IsCreated)
            eventCount.Dispose();
        if (sessions.IsCreated)
            sessions.Dispose();
        if (agentStates.IsCreated)
            agentStates.Dispose();
        if (agentFlags.IsCreated)
            agentFlags.Dispose();
        if (nextIdleMatchTime.IsCreated)
            nextIdleMatchTime.Dispose();
        if (matchObserveUserID.IsCreated)
            matchObserveUserID.Dispose();
        if (matchObserveGeneration.IsCreated)
            matchObserveGeneration.Dispose();
        if (matchObserveDispatchTime.IsCreated)
            matchObserveDispatchTime.Dispose();
        if (prevRemoteChannelStatus.IsCreated)
            prevRemoteChannelStatus.Dispose();
        if (levelStartMessages.IsCreated)
            levelStartMessages.Dispose();
        if (relayServerStatusInjected.IsCreated)
            relayServerStatusInjected.Dispose();
        if (wireLiveServerHello.IsCreated)
            wireLiveServerHello.Dispose();
        if (wireScratch.IsCreated)
            wireScratch.Dispose();

        agentCount = 0;
    }
}
