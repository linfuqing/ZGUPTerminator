using Unity.Entities;

public struct BotRelayWorldTag : IComponentData
{
}

/// <summary>
/// Init 已将仅含 Connect/link 校准数据的 WireCatalog 发布至 Host；不含任何业务协议模板。
/// </summary>
public struct BotRelayWireCatalogReadyTag : IComponentData
{
}

public struct BotRelayHubState : IComponentData
{
    public Entity serverEntity;
}

public struct BotRelayFarmConfig : IComponentData
{
    public float inviteTimeoutMin;
    public float inviteTimeoutMax;
    public float matchTimeoutMin;
    public float matchTimeoutMax;
    public float remoteOfflineLeaveTimeout;
}

[System.Flags]
public enum BotAgentRuntimeFlags : byte
{
    None = 0,
    ClockInitialized = 1 << 0,
    LevelStartHandled = 1 << 1,
    PendingLevelStart = 1 << 2,
    JoinDispatched = 1 << 3,

    // While InLevel, the remote peer is present-in-stage but not consuming our stream (offline /
    // disconnected during the reconnect grace, or has just dropped the stage). Set by
    // BotAgentLogic.__TickChannelTracker to
    // pause replay streaming (BotRelayReplayTickSystem skips the inject) so we stop saturating the
    // departed/offline human's reliable send window (NetworkSendMessage -5) — without leaving the
    // level during the grace, so a reconnecting peer resumes seeing the bot.
    RemoteAbsentSuppressed = 1 << 4,
}
