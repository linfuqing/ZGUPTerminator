using ZG;

/// <summary>
/// Events parsed by the production fixed-slot inbox and committed by <see cref="BotAgentLogic"/>.
/// Level entry has one event only: <see cref="MatchStart"/>.
/// </summary>
public enum BotRelayEventType
{
    None,
    SquadInvite,
    SquadJoin,
    SquadJoinFail,
    SquadLeave,
    SquadDrop,
    ApplyMatch,
    ApplyMatchFail,
    RejectMatch,
    Match,
    Mismatch,
    MatchStart,
    RemotePlayerProperty
}

public struct BotRelayEvent
{
    public BotRelayEventType type;
    public ClientHeader header;
    public ClientMessageSquadInviteToRead squadInvite;
    public ClientMessageSquadJoinToRead squadJoin;
    public ClientMessageMatchToRead match;
    public ClientMessageMatchStart matchStart;

    // Channel messages are decoded into an immutable event snapshot. SlotInbox must not publish
    // membership into BotSessionState before BotAgentLogic has accepted the active generation.
    public NetworkRelayMessageType channelMessageType;
    public bool channelHasRemotePayload;
    public bool channelHasRemoteHeader;
    public uint channelRemoteUserID;
    public ClientHeader channelRemoteHeader;
}
