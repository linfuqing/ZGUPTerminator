using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;

public struct BotRelayWorldTag : IComponentData
{
}

/// <summary>Init 已 Publish WireCatalog 至 Host；不含模板字节（见 Docs §5.1）。</summary>
public struct BotRelayWireCatalogReadyTag : IComponentData
{
}

public struct BotRelayHubState : IComponentData
{
    public ushort listenPort;
    public bool driverAdded;
    public Entity serverEntity;
}

public struct BotRelayClientResources : IComponentData
{
    public NetworkDriver driver;
    public NetworkPipeline pipeline;
    public JobHandle driverDependency;
}

public struct BotRelayFarmConfig : IComponentData
{
    public float inviteTimeoutMin;
    public float inviteTimeoutMax;
    public float remoteOfflineLeaveTimeout;
}

[System.Flags]
public enum BotAgentRuntimeFlags : byte
{
    None = 0,
    ClockInitialized = 1 << 0,
    PlayHandled = 1 << 1,
    PendingPlay = 1 << 2,
    MatchStartReceived = 1 << 3,
    JoinDispatched = 1 << 4,
    JoinMismatchQueued = 1 << 5,
    JoinMismatchAcked = 1 << 6,

    // While InLevel, the remote peer is present-in-stage but not consuming our stream (offline /
    // disconnected during the reconnect grace, or has just dropped the stage). Set by
    // BotAgentLogic.__TickChannelTracker to
    // pause replay streaming (BotRelayReplayTickSystem skips the inject) so we stop saturating the
    // departed/offline human's reliable send window (NetworkSendMessage -5) — without leaving the
    // level during the grace, so a reconnecting peer resumes seeing the bot.
    RemoteAbsentSuppressed = 1 << 7,
}
