using ZG;

public struct BotSessionState
{
    public uint userID;
    public int channel;
    public int matchID;
    public bool isHost;
    public int channelStatus;
    public int remoteChannelStatus;

    // Remote peer's live connection presence (NetworkRelayChannelFlag.Online bit). This is the
    // *disconnect* signal and is orthogonal to remoteChannelStatus (the ShiftToStatus stage value):
    // a network drop clears remoteOnline but keeps remoteChannelStatus non-zero during the configured
    // reconnect grace; only a genuine level exit drops remoteChannelStatus to 0.
    public bool remoteOnline;

    public uint remoteUserID;
    public int remotePlayerCount;
    public LevelPlayerHeader remoteHeader;

    /// <summary>Last remote host Create/Join relay channel observed while bot was idle (invite authority).</summary>
    public int pendingHostSquadChannel;
    public uint pendingHostUserId;

    public bool IsInSquad => channel != ReplyMessageShared.CHANNEL_NULL;

    public bool IsIdleForMatch => channel == ReplyMessageShared.CHANNEL_NULL && channelStatus == 0;

    public void ResetChannel()
    {
        channel = ReplyMessageShared.CHANNEL_NULL;
        matchID = 0;
        isHost = false;
        channelStatus = 0;
        remoteChannelStatus = 0;
        remoteOnline = false;
        remoteUserID = 0;
        remotePlayerCount = 0;
        remoteHeader = default;
        pendingHostSquadChannel = BotRelayInviteChannelAuthority.UnsetChannel;
        pendingHostUserId = 0;
    }
}
