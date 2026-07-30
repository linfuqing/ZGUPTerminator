using ZG;

public enum BotLevelLoginPhase : byte
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
    /// <summary>BotProfile.matchLevel, sent as ClientMessageMatchToSend.level.</summary>
    public int matchLevel;
    public double stateEnterTime;

    public ClientMessageSquadInviteToRead pendingInvite;
    public uint pendingInviteHostUserID;
    public double inviteJoinTime;
    public bool HasPendingInvite => state == BotState.PendingInvite;

    // While InLevel, the time the remote human's stage was first observed gone
    // (remoteChannelStatus == 0); -1 means the peer is still in the stage or merely disconnected.
    // A sustained transition ends the level; see BotAgentLogic.__TickChannelTracker.
    public double remoteAbsentSince;

    // Unified lobby/in-level disconnect grace. A negative value means the remote human is online
    // or no authoritative disconnect has been observed.
    public double remoteOfflineSince;

    // Latched after positively observing the remote peer in-stage. Absence handling only activates
    // after this baseline exists, so the entry window defaults to streaming.
    public bool remotePresenceSeen;

    // One-shot diagnostic guard, reset on every state transition.
    public bool joinStallLogged;

    // ApplyStart publishes MatchStart for both ordinary squads and matchmaking. Replay loading
    // first publishes PlayerProperty at Status=0, then publishes PlayerProperty + the non-zero
    // Stage on a later tick. Play, ChapterStage and remote Status never supply this descriptor.
    public BotLevelLoginPhase levelLoginPhase;

}
