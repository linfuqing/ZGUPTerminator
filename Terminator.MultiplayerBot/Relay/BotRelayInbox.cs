using Unity.Collections;
using ZG;

public enum BotRelayEventType
{
    None,
    Connected,
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
    ChapterStage,
    MatchStart,
    Play,
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
    public ClientMessagePlay play;
    public uint chapterStageID;
}

public struct BotRelayInbox
{
    private NativeQueue<BotRelayEvent> __events;
    public BotSessionState session;
    private bool __sentInitialStatus;

    public static BotRelayInbox Create()
    {
        return new BotRelayInbox
        {
            __events = new NativeQueue<BotRelayEvent>(Allocator.Persistent),
            session = new BotSessionState { channel = ReplyMessageShared.CHANNEL_NULL },
            __sentInitialStatus = false
        };
    }

    public void Dispose()
    {
        if (__events.IsCreated)
            __events.Dispose();
    }

    public bool TryDequeue(out BotRelayEvent evt)
    {
        if (!__events.IsCreated || !__events.TryDequeue(out evt))
        {
            evt = default;
            return false;
        }

        return evt.type != BotRelayEventType.None;
    }

    public void Pump(ref BotRelayConnection connection)
    {
        if (!connection.IsCreated)
            return;

        var selfHeader = connection.Header;
        session.userID = selfHeader.userID;

        while (connection.TryDequeueInbound(out var packet))
            BotRelayMessageParser.ParseTransportPayload(in packet, ref this, ref connection, in selfHeader, ref __sentInitialStatus);
    }

    public void NotifyConnected(ref BotRelayConnection connection, in ClientHeader selfHeader, ref bool sentInitialStatus)
    {
        if (!sentInitialStatus)
        {
            connection.TryWriteStatus(0);
            sentInitialStatus = true;
        }

        Enqueue(new BotRelayEvent { type = BotRelayEventType.Connected, header = selfHeader });
    }

    public void ParseRelayFrame(NativeArray<byte> frame, in ClientHeader selfHeader)
    {
        if (!frame.IsCreated || frame.Length < 2)
            return;

        var reader = new DataStreamReader(frame);
        ParseDataPayload(ref reader, in selfHeader);
    }

    private void Enqueue(in BotRelayEvent evt) => __events.Enqueue(evt);

    private void ParseDataPayload(ref DataStreamReader reader, in ClientHeader selfHeader)
    {
        var streamCompressionModel = StreamCompressionModel.Default;
        if (reader.Length == 0)
            return;

        int type = reader.ReadPackedInt(streamCompressionModel);
        switch ((NetworkRelayMessageType)type)
        {
            case NetworkRelayMessageType.Matching:
                session.matchID = 0;
                break;
            case NetworkRelayMessageType.Mismatch:
                session.matchID = 0;
                Enqueue(new BotRelayEvent { type = BotRelayEventType.Mismatch, header = selfHeader });
                break;
            case NetworkRelayMessageType.Match:
            {
                int matchID = reader.ReadPackedInt(streamCompressionModel);
                int level = reader.ReadPackedInt(streamCompressionModel);
                session.matchID = matchID;
                ClientMessageMatchToRead match;
                match.matchID = matchID;
                match.level = level;
                Enqueue(new BotRelayEvent
                {
                    type = BotRelayEventType.Match,
                    header = selfHeader,
                    match = match
                });
                break;
            }
            case NetworkRelayMessageType.Create:
            case NetworkRelayMessageType.Join:
            case NetworkRelayMessageType.Leave:
            case NetworkRelayMessageType.Drop:
                ParseChannelMessage(type, ref reader, streamCompressionModel, in selfHeader);
                break;
            case NetworkRelayMessageType.JoinFailed:
            {
                if (!HasRemaining(ref reader, 1))
                    return;

                int failedChannel = reader.ReadPackedInt(streamCompressionModel);
                ClientMessageSquadJoinToRead joinFail;
                joinFail.playerStatus.flag = 0;
                joinFail.squadInviteID = (uint)failedChannel;
                Enqueue(new BotRelayEvent
                {
                    type = BotRelayEventType.SquadJoinFail,
                    header = selfHeader,
                    squadJoin = joinFail
                });
                break;
            }
            case NetworkRelayMessageType.Query:
                ParseRelayQueryMessage(ref reader, streamCompressionModel);
                break;
            case NetworkRelayMessageType.Connect:
            case NetworkRelayMessageType.Status:
            case NetworkRelayMessageType.Add:
                if (!HasRemaining(ref reader, 2))
                    return;

                ParseRelayStatusMessage(type, ref reader, streamCompressionModel, in selfHeader);
                break;
            case NetworkRelayMessageType.Disconnect:
            case NetworkRelayMessageType.Remove:
                if (!HasRemaining(ref reader, 1))
                    return;

                ParseRelayStatusMessage(type, ref reader, streamCompressionModel, in selfHeader);
                break;
            default:
                if (type == (int)ReplyMessageType.Invite ||
                    (type >= (int)ReplyMessageType.Chat && type <= (int)ReplyMessageType.PlayerProperty))
                    ParseReplyPayloadMessage(type, ref reader, streamCompressionModel, in selfHeader);
                else if (type >= (int)NetworkRelayMessageType.Query + 2 && type < (int)ReplyMessageType.Chat)
                    ParseReplyHeaderClientMessage(type, ref reader, in selfHeader);
                break;
        }
    }

    private void ParseRelayStatusMessage(
        int type,
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        in ClientHeader selfHeader)
    {
        var messageType = (NetworkRelayMessageType)type;
        int channelFlag;
        switch (messageType)
        {
            case NetworkRelayMessageType.Disconnect:
            case NetworkRelayMessageType.Remove:
                channelFlag = 0;
                break;
            default:
                if (!HasRemaining(ref reader, 1))
                    return;

                channelFlag = reader.ReadPackedInt(streamCompressionModel);
                break;
        }

        if (!HasRemaining(ref reader, 2))
            return;

        var remoteUserID = reader.ReadPackedUInt(streamCompressionModel);
        if (remoteUserID != 0 && remoteUserID != selfHeader.userID)
        {
            session.remoteUserID = remoteUserID;
            if (messageType == NetworkRelayMessageType.Disconnect)
            {
                session.remoteOnline = false;
            }
            else
            {
                session.remoteChannelStatus = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
                session.remoteOnline = ((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Online) != 0;
            }

            if (session.remotePlayerCount == 0)
                session.remotePlayerCount = 1;
        }
    }

    private void ParseRelayQueryMessage(
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel)
    {
        if (!HasRemaining(ref reader, 2))
            return;

        int queryChannel = reader.ReadPackedInt(streamCompressionModel);
        int channelFlag = reader.ReadPackedInt(streamCompressionModel);
        if (queryChannel != session.channel)
            return;

        int remoteStage = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
        if (remoteStage <= 0)
            return;

        session.remoteChannelStatus = remoteStage;
        session.remoteOnline = ((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Online) != 0;
        if (session.remotePlayerCount == 0)
            session.remotePlayerCount = 1;
    }

    private static bool HasRemaining(ref DataStreamReader reader, int minimumBytes) =>
        reader.Length - reader.GetBytesRead() >= minimumBytes;

    private void ParseChannelMessage(
        int type,
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        in ClientHeader selfHeader)
    {
        int channel = reader.ReadPackedInt(streamCompressionModel);
        int channelFlag = reader.ReadPackedInt(streamCompressionModel);

        if ((NetworkRelayMessageType)type == NetworkRelayMessageType.Leave)
        {
            session.ResetChannel();
            Enqueue(new BotRelayEvent { type = BotRelayEventType.SquadLeave, header = selfHeader });
            return;
        }

        if ((NetworkRelayMessageType)type == NetworkRelayMessageType.Drop)
        {
            session.ResetChannel();
            Enqueue(new BotRelayEvent { type = BotRelayEventType.SquadDrop, header = selfHeader });
            return;
        }

        BotRelayInboundProbe.RecordChannelJoin();

        // Align with ReplyMessage.cs:
        // - Create (self) => isHost=true
        // - Join with remote payload => peer joined our channel; keep self isHost
        // - Join without remote payload => self join ack; isHost=false
        bool hasRemotePayload = reader.GetBytesRead() < reader.Length;
        if ((NetworkRelayMessageType)type == NetworkRelayMessageType.Create)
        {
            session.isHost = true;
            session.channel = channel;
            session.channelStatus = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
            if (!hasRemotePayload)
                session.remotePlayerCount = 0;
        }
        else
        {
            session.channel = channel;
            if (!hasRemotePayload)
            {
                session.isHost = false;
                session.channelStatus = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
                session.remotePlayerCount = 0;
            }
        }

        if (hasRemotePayload)
        {
            if (session.channel != 0 && channel != session.channel)
                return;

            var remoteUserID = reader.ReadPackedUInt(streamCompressionModel);
            session.remoteUserID = remoteUserID;
            if (BotRelayMessageUtility.TryReadOptionalRemoteClientHeader(
                    remoteUserID,
                    ref reader,
                    streamCompressionModel,
                    out var remoteHeader))
            {
                session.remoteHeader = remoteHeader.ToLevelPlayerHeader();
            }

            session.remotePlayerCount = 1;
            session.remoteChannelStatus = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
            session.remoteOnline = ((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Online) != 0;
        }

        ClientMessageSquadJoinToRead join;
        join.playerStatus.flag = (ClientRemotePlayerFlag)channelFlag;
        join.squadInviteID = (uint)channel;
        Enqueue(new BotRelayEvent
        {
            type = BotRelayEventType.SquadJoin,
            header = selfHeader,
            squadJoin = join
        });
    }

    private void ParseReplyPayloadMessage(
        int type,
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        in ClientHeader selfHeader)
    {
        int channel = reader.ReadPackedInt(streamCompressionModel);
        uint senderID = reader.ReadPackedUInt(streamCompressionModel);

        if ((ReplyMessageType)type == ReplyMessageType.Invite)
        {
            var invite = new ReplyMessages.Invite((NetworkRelayType)channel, senderID, ref reader, streamCompressionModel);
            if (invite.channel < 0)
                return;

            ClientMessageSquadInviteToRead squadInvite = new ClientMessageSquadInviteToRead(invite, out var header);
            if (invite.stage >= 0)
            {
                squadInvite.stage = invite.stage;
            }

            UnityEngine.Debug.Log(
                $"[BotRelayInbox] Invite from {senderID}, relay={(NetworkRelayType)channel}, squad={squadInvite.squadInviteID}, bot={selfHeader.userID}");
            Enqueue(new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                header = header,
                squadInvite = squadInvite
            });
        }
    }

    private void ParseReplyHeaderClientMessage(int type, ref DataStreamReader reader, in ClientHeader selfHeader)
    {
        reader.ReadReplyHeader(out NetworkRelayType relayType, out uint id);

        switch ((ClientMessageType)type)
        {
            case ClientMessageType.ApplyMatch:
                Enqueue(new BotRelayEvent { type = BotRelayEventType.ApplyMatch, header = selfHeader });
                break;
            case ClientMessageType.ApplyMatchFail:
                Enqueue(new BotRelayEvent { type = BotRelayEventType.ApplyMatchFail, header = selfHeader });
                break;
            case ClientMessageType.RejectMatch:
                Enqueue(new BotRelayEvent { type = BotRelayEventType.RejectMatch, header = selfHeader });
                break;
            case ClientMessageType.ChapterStage:
            {
                var chapterStage = new ClientMessageChapterStage(ref reader, StreamCompressionModel.Default);
                Enqueue(new BotRelayEvent
                {
                    type = BotRelayEventType.ChapterStage,
                    header = selfHeader,
                    chapterStageID = chapterStage.userStageID
                });
                break;
            }
            case ClientMessageType.MatchStart:
            {
                var matchStart = new ClientMessageMatchStart(ref reader);
                Enqueue(new BotRelayEvent
                {
                    type = BotRelayEventType.MatchStart,
                    header = selfHeader,
                    matchStart = matchStart
                });
                break;
            }
            case ClientMessageType.Play:
            {
                var play = new ClientMessagePlay(ref reader);
                Enqueue(new BotRelayEvent
                {
                    type = BotRelayEventType.Play,
                    header = selfHeader,
                    play = play
                });
                break;
            }
        }
    }
}
