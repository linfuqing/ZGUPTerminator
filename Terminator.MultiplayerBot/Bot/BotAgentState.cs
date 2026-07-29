using ZG;

public enum BotMatchLoginPhase : byte
{
    None,
    PendingPlayerProperty,
    PlayerPropertySent,
    PlayerPropertyFailed
}

public struct BotAgentState
{
    public BotState state;
    public uint userID;
    /// <summary>BotProfile.matchLevel — sent as ClientMessageMatchToSend.level.</summary>
    public int matchLevel;
    public double stateEnterTime;

    public ClientMessageSquadInviteToRead pendingInvite;
    public bool hasPendingInvite;
    public double inviteJoinTime;

    public uint targetUserStageID;
    public uint playLevelID;
    public int playStageIndex;

    // Earliest time we may (re)process a Play message. Prevents a host/server that
    // re-broadcasts the Join/Play bundle (or drops+re-adds us on send-queue overflow)
    // from making us re-inject PlayerProperty/Status and restart replay every frame —
    // the amplification that overflows the server send queue (NetworkSendMessage -5).
    public double nextPlayHandleTime;

    // While InLevel, the time the remote (human) co-op peer's *stage* was first observed gone
    // (remoteChannelStatus == 0); -1 means the peer is still in the stage (present or merely
    // disconnected). Used to debounce the host's spurious per-frame Join/Leave re-broadcast flap so
    // only a *sustained* genuine leave ends the level — see BotAgentLogic.__TickChannelTracker.
    public double remoteAbsentSince;

    // Unified lobby / in-level disconnect grace. A negative value means the remote human is online
    // (or no authoritative disconnect has been observed). Disconnect preserves the last stage status;
    // only this timer decides when an offline peer has occupied the bot for too long.
    public double remoteOfflineSince;

    // While InLevel, latched true once we have POSITIVELY observed the remote peer present
    // (remoteOnline && remoteChannelStatus != 0). Streaming suppression / leave detection must only
    // act on an absence we saw *after* confirming presence — before that (entry window, stale session
    // defaults) we default to STREAMING so the bot is visible. Reset on InLevel entry.
    public bool remotePresenceSeen;

    // One-shot guard so the join-stall watchdog logs at most once per state entry. Reset on every
    // state transition. Diagnostic only — see BotAgentLogic join-stall watchdog.
    public bool joinStallLogged;

    // Set only by the authoritative MatchToRead event. Create/Join are channel membership only.
    public bool matchPaired;

    // MatchToRead starts a new stage-generation boundary. Any non-zero stage embedded in the
    // pre-made squad's earlier Join/Status belongs to the lobby/previous level and must not arm
    // ranked entry. Cleared by the matching ApplyStart descriptor (or fresh relay status used only
    // for diagnostics); replay entry still requires the descriptor flag.
    public bool awaitingFreshMatchStage;

    // Human sent PlayerProperty on the squad channel (diagnostic).
    public bool remotePlayerPropertySeen;

    // Ranked Match starts LoginManager.ApplyStart on both clients. The dedicated MatchStart
    // descriptor supplies the exact server Stage and Play tuple. Replay loading first publishes
    // PlayerProperty at Status=0, then publishes the non-zero Stage on a later tick so the human
    // Host can accept either packet order without depending on a matching Play broadcast.
    public BotMatchLoginPhase matchLoginPhase;

    /// <summary>Squad invite: Host Play(21) captured; required before arming InLevel replay.</summary>
    public bool squadHostPlaySeen;

    // Last Query inject time for match remote-status fallback (retry until stage known).
    public double lastChannelQueryTime;

    public void ResetSession()
    {
        targetUserStageID = 0;
        playLevelID = 0;
        playStageIndex = 0;
        hasPendingInvite = false;
        pendingInvite = default;
        inviteJoinTime = 0;
        matchPaired = false;
        awaitingFreshMatchStage = false;
        remotePlayerPropertySeen = false;
        matchLoginPhase = BotMatchLoginPhase.None;
        squadHostPlaySeen = false;
        lastChannelQueryTime = 0;
        remoteOfflineSince = -1.0;
    }
}
