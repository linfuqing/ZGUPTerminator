using Unity.Burst;
using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

[BurstCompile]
internal static class BotRelaySlotInbox
{
    public static void ParseTransportPayload(
        in BotRelayPacket packet,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader,
        bool sentInitialStatus)
    {
        if (packet.IsEmpty)
        {
            NotifyConnected(agentIndex, ref farm, in selfHeader, sentInitialStatus);
            return;
        }

        if (packet.length <= 0)
            return;

        int scratchBase = agentIndex * BotRelayPacket.MaxPayloadSize;
        if (!packet.TryCopyBytesTo(farm.wireScratch, scratchBase))
            return;

        var payload = farm.wireScratch.GetSubArray(scratchBase, packet.length);
        ParseDataPayload(payload, agentIndex, ref farm, in selfHeader);
    }

    public static void NotifyConnected(
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader,
        bool sentInitialStatus)
    {
        if (!sentInitialStatus)
        {
            // Initial status is injected by BotAgentLogic after the Connect/link path marks the
            // slot ready. Do not place a business Status packet in the deprecated UTP outbound slot.
            farm.inboxSentInitialStatus[agentIndex] = 1;
        }

        TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
        {
            type = BotRelayEventType.Connected,
            header = selfHeader
        });
    }

    public static bool TryDequeueEvent(int agentIndex, ref BotRelayFarmNative farm, out BotRelayEvent evt)
    {
        evt = default;
        int count = farm.eventCount[agentIndex];
        if (count <= 0)
            return false;

        int baseIndex = agentIndex * BotRelayFarmNative.MaxEventsPerAgent;
        while (count > 0)
        {
            evt = farm.events[baseIndex];
            for (int i = 1; i < count; ++i)
                farm.events[baseIndex + i - 1] = farm.events[baseIndex + i];

            --count;
            if (evt.type != BotRelayEventType.None)
            {
                farm.eventCount[agentIndex] = count;
                return true;
            }

            evt = default;
        }

        farm.eventCount[agentIndex] = 0;
        return false;
    }

    private static void TryEnqueueEvent(int agentIndex, ref BotRelayFarmNative farm, in BotRelayEvent evt)
    {
        int count = farm.eventCount[agentIndex];
        if (count >= BotRelayFarmNative.MaxEventsPerAgent)
        {
            BotRelayInboundProbe.RecordEventDrop();
            return;
        }

        farm.events[agentIndex * BotRelayFarmNative.MaxEventsPerAgent + count] = evt;
        farm.eventCount[agentIndex] = count + 1;
    }

    private static void ParseDataPayload(
        NativeArray<byte> payload,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        var streamCompressionModel = StreamCompressionModel.Default;
        if (payload.Length == 0)
            return;

        var reader = new DataStreamReader(payload);
        ref var session = ref farm.sessions.ElementAt(agentIndex);

        int type = reader.ReadPackedInt(streamCompressionModel);
        BotRelayInboundProbe.RecordInbound(type);
        switch ((NetworkRelayMessageType)type)
        {
            case NetworkRelayMessageType.Matching:
                session.matchID = 0;
                break;
            case NetworkRelayMessageType.Mismatch:
                session.matchID = 0;
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.Mismatch,
                    header = selfHeader
                });
                break;
            case NetworkRelayMessageType.Match:
            {
                if (!BotRelayMessageUtility.TryReadServerMatchNotification(
                        ref reader, in streamCompressionModel, out int matchID, out int level))
                    return;

                session.matchID = matchID;
                ClientMessageMatchToRead match;
                match.matchID = matchID;
                match.level = level;
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
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
                ParseChannelMessage(type, ref reader, streamCompressionModel, agentIndex, ref farm, in selfHeader);
                break;
            case NetworkRelayMessageType.JoinFailed:
            {
                if (!HasRemaining(ref reader, 1))
                    return;

                int failedChannel = reader.ReadPackedInt(streamCompressionModel);
                ClientMessageSquadJoinToRead joinFail;
                joinFail.playerStatus.flag = 0;
                joinFail.squadInviteID = (uint)failedChannel;
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadJoinFail,
                    header = selfHeader,
                    squadJoin = joinFail
                });
                break;
            }
            case NetworkRelayMessageType.Query:
                ParseRelayQueryMessage(ref reader, streamCompressionModel, agentIndex, ref farm);
                break;
            case NetworkRelayMessageType.Connect:
                __ParseWrappedRelayPayload(payload, agentIndex, ref farm, in selfHeader);
                break;
            case NetworkRelayMessageType.Status:
            case NetworkRelayMessageType.Add:
            {
                // Server __WriteStatus is always [channelFlag][userID]. Do not treat a short remainder
                // as a bare channelStatus — that would store (stage<<3|flags) and break stage lookup.
                if (!HasRemaining(ref reader, 2))
                    return;

                ParseRelayStatusMessage(type, ref reader, streamCompressionModel, agentIndex, ref farm, in selfHeader);
                break;
            }
            case NetworkRelayMessageType.Disconnect:
            case NetworkRelayMessageType.Remove:
                if (!HasRemaining(ref reader, 1))
                    return;

                ParseRelayStatusMessage(type, ref reader, streamCompressionModel, agentIndex, ref farm, in selfHeader);
                break;
            default:
                if (type == (int)ReplyMessageType.Invite)
                    ParseInviteSendRelayMessage(payload, agentIndex, ref farm, in selfHeader);
                else if (type >= (int)ReplyMessageType.Chat && type <= (int)ReplyMessageType.PlayerProperty)
                    ParseReplyPayloadMessage(type, ref reader, streamCompressionModel, agentIndex, ref farm, in selfHeader);
                else if (type >= (int)NetworkRelayMessageType.Query + 2 && type < (int)ReplyMessageType.Chat)
                    ParseReplyHeaderClientMessage(type, ref reader, agentIndex, ref farm, in selfHeader);
                break;
        }
    }

    private static void __ParseWrappedRelayPayload(
        NativeArray<byte> payload,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        if (payload.Length <= 0)
            return;

        var wire = default(BotRelayPacket);
        wire.length = payload.Length;
        for (int i = 0; i < payload.Length; ++i)
            wire.SetByte(i, payload[i]);

        if (BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var inner) &&
            !inner.IsEmpty &&
            __TryParseResolvedApp(in inner, agentIndex, ref farm, in selfHeader))
            return;

        if (BotRelayWireBytes.TryNormalizePopEventsPayload(in wire, out inner) &&
            !inner.IsEmpty &&
            __TryParseResolvedApp(in inner, agentIndex, ref farm, in selfHeader))
            return;
    }

    private static bool __TryParseResolvedApp(
        in BotRelayPacket app,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        if (app.IsEmpty)
            return false;

        if (!BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type))
            return false;

        if (type == (int)NetworkRelayMessageType.Connect)
            return false;

        int scratchBase = agentIndex * BotRelayPacket.MaxPayloadSize;
        if (!app.TryCopyBytesTo(farm.wireScratch, scratchBase))
            return false;

        var payload = farm.wireScratch.GetSubArray(scratchBase, app.length);
        ParseDataPayload(payload, agentIndex, ref farm, in selfHeader);
        return true;
    }

    private static void ParseInviteSendRelayMessage(
        NativeArray<byte> payload,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        var packet = default(BotRelayPacket);
        packet.length = payload.Length;
        for (int i = 0; i < payload.Length; ++i)
        {
            packet.SetByte(i, payload[i]);
        }

        var options = BotRelayMessageUtility.InviteReadOptions.Default;
        if (!BotRelayMessageUtility.TryReadInviteFromPacket(in packet, out var invite, options))
        {
            BotRelayAgentDiagnostics.LogInviteParseFailed(selfHeader.userID, payload, payload.Length);
            return;
        }

        ref var session = ref farm.sessions.ElementAt(agentIndex);
        ref readonly var transport = ref farm.transport.ElementAt(agentIndex);
        var shellWire = default(BotRelayPacket);
        BotRelayInviteShellCacheStore.TryGet(transport.virtualPort, out shellWire);
        BotRelayInviteChannelAuthority.TryResolveSquadChannel(
            ref invite,
            in packet,
            in shellWire,
            in session);

        ClientMessageSquadInviteToRead squadInvite = new ClientMessageSquadInviteToRead(invite, out var header);
        if (invite.stage >= 0)
        {
            squadInvite.stage = invite.stage;
        }

        ref var agentState = ref farm.agentStates.ElementAt(agentIndex);
        if (invite.stage > 0)
        {
            agentState.playStageIndex = invite.stage;
        }

        BotRelayAgentDiagnostics.LogSquadInvite(
            invite.id,
            (int)invite.type,
            squadInvite.squadInviteID,
            invite.levelID,
            invite.stage,
            selfHeader.userID);

        ref readonly var sessionReadonly = ref farm.sessions.ElementAt(agentIndex);
        bool relayReady = farm.inboxSentInitialStatus[agentIndex] != 0 &&
                          (transport.flags & BotRelayTransportFlags.Connected) != 0;
        BotRelayAgentDiagnostics.LogInviteReceived(
            selfHeader.userID,
            invite.id,
            squadInvite.squadInviteID,
            relayReady,
            (int)agentState.state,
            sessionReadonly.channel,
            transport.virtualPort);

        TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
        {
            type = BotRelayEventType.SquadInvite,
            header = header,
            squadInvite = squadInvite
        });
    }

    private static void ParseRelayStatusMessage(
        int type,
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        ref var session = ref farm.sessions.ElementAt(agentIndex);

        var messageType = (NetworkRelayMessageType)type;
        bool carriesChannelFlag = messageType != NetworkRelayMessageType.Disconnect &&
                                  messageType != NetworkRelayMessageType.Remove;
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
                // Disconnect carries only userID. It clears transport presence, not the authoritative
                // stage status retained by the Server for reconnect. Treating the absent flag as
                // Status(0) makes the Bot confuse a short drop with a genuine level exit.
                session.remoteOnline = false;
            }
            else
            {
                session.remoteChannelStatus = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
                session.remoteOnline = ((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Online) != 0;
            }

            if (session.remotePlayerCount == 0)
                session.remotePlayerCount = 1;

            if (carriesChannelFlag && session.remoteChannelStatus > 0)
            {
                ref var state = ref farm.agentStates.ElementAt(agentIndex);
                if (state.matchPaired)
                {
                    state.awaitingFreshMatchStage = false;
                    state.targetUserStageID = (uint)session.remoteChannelStatus;
                    farm.lastSeenChapterStage[agentIndex] = (uint)session.remoteChannelStatus;
                }
            }
        }
    }

    /// <summary>
    /// Server SendHeader(Query): [channel][channelFlag][optional payload]. Used after bot Query inject.
    /// </summary>
    private static void ParseRelayQueryMessage(
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        int agentIndex,
        ref BotRelayFarmNative farm)
    {
        if (!HasRemaining(ref reader, 2))
            return;

        int queryChannel = reader.ReadPackedInt(streamCompressionModel);
        int channelFlag = reader.ReadPackedInt(streamCompressionModel);
        ref var session = ref farm.sessions.ElementAt(agentIndex);
        if (queryChannel != session.channel)
            return;

        int remoteStage = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
        if (remoteStage <= 0)
            return;

        session.remoteChannelStatus = remoteStage;
        session.remoteOnline = ((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Online) != 0;
        if (session.remotePlayerCount == 0)
            session.remotePlayerCount = 1;

        ref var state = ref farm.agentStates.ElementAt(agentIndex);
        if (state.matchPaired)
        {
            state.awaitingFreshMatchStage = false;
            state.targetUserStageID = (uint)remoteStage;
            farm.lastSeenChapterStage[agentIndex] = (uint)remoteStage;
        }
    }

    private static bool HasRemaining(ref DataStreamReader reader, int minimumBytes) =>
        reader.Length - reader.GetBytesRead() >= minimumBytes;

    private static void ParseChannelMessage(
        int type,
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        ref var session = ref farm.sessions.ElementAt(agentIndex);

        int channel = reader.ReadPackedInt(streamCompressionModel);
        int channelFlag = reader.ReadPackedInt(streamCompressionModel);

        if ((NetworkRelayMessageType)type == NetworkRelayMessageType.Leave)
        {
            BotRelayInboundProbe.RecordChannelLeave();
            session.ResetChannel();
            TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadLeave,
                header = selfHeader
            });
            return;
        }

        if ((NetworkRelayMessageType)type == NetworkRelayMessageType.Drop)
        {
            BotRelayInboundProbe.RecordChannelLeave();
            session.ResetChannel();
            TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadDrop,
                header = selfHeader
            });
            return;
        }

        BotRelayInboundProbe.RecordChannelJoin();

        // Align with ReplyMessage.cs:
        // - Create (self) => isHost=true
        // - Join with remote payload => peer joined our channel; keep self isHost
        // - Join without remote payload => self join ack; isHost=false
        bool wasIdle = !session.IsInSquad;
        bool hasRemotePayload = reader.GetBytesRead() < reader.Length;
        if (hasRemotePayload &&
            wasIdle &&
            (NetworkRelayMessageType)type == NetworkRelayMessageType.Create)
        {
            if (!HasRemaining(ref reader, 1))
            {
                return;
            }

            var remoteUserID = reader.ReadPackedUInt(streamCompressionModel);
            BotRelayInviteChannelAuthority.RecordRemoteHostCreateOrJoin(
                ref session,
                channel,
                remoteUserID);
            return;
        }

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
            if (session.IsInSquad && (int)channel != session.channel)
            {
                return;
            }

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
            // Peer Join/Create relay embeds the *peer's* channelFlag.
            session.remoteChannelStatus = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
            session.remoteOnline = ((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Online) != 0;
        }

        ClientMessageSquadJoinToRead join;
        join.playerStatus.flag = (ClientRemotePlayerFlag)channelFlag;
        join.squadInviteID = (uint)channel;
        TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
        {
            type = BotRelayEventType.SquadJoin,
            header = selfHeader,
            squadJoin = join
        });
    }

    /// <summary>
    /// Reads the Server.SendRelay envelope. The server appends the original client body as raw
    /// bytes after its packed [type][relay][sender] fields, so the body begins on the next byte
    /// boundary. This must mirror NetworkingUtility.ReadReplyHeader before reading Play.
    /// </summary>
    private static bool TryReadRelayHeader(
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        out NetworkRelayType relayType,
        out uint senderId)
    {
        relayType = default;
        senderId = 0;
        if (!HasRemaining(ref reader, 1))
            return false;

        relayType = (NetworkRelayType)reader.ReadPackedInt(streamCompressionModel);
        if (!HasRemaining(ref reader, 1))
            return false;

        senderId = reader.ReadPackedUInt(streamCompressionModel);
        reader.Flush();
        return true;
    }

    private static void ParseReplyPayloadMessage(
        int type,
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        if (type == (int)ReplyMessageType.PlayerProperty)
        {
            if (!HasRemaining(ref reader, 2))
                return;

            var relayType = (NetworkRelayType)reader.ReadPackedInt(streamCompressionModel);
            if (relayType != NetworkRelayType.Channel)
                return;

            uint senderID = reader.ReadPackedUInt(streamCompressionModel);

            if (senderID == 0 || senderID == selfHeader.userID)
                return;

            ref var session = ref farm.sessions.ElementAt(agentIndex);
            session.remoteUserID = senderID;
            session.remotePlayerCount = 1;
            session.remoteOnline = true;
            TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.RemotePlayerProperty,
                header = new ClientHeader(senderID, default)
            });
            return;
        }

        if (!HasRemaining(ref reader, 2))
            return;

        int channel = reader.ReadPackedInt(streamCompressionModel);
        uint legacySenderID = reader.ReadPackedUInt(streamCompressionModel);

        if ((ReplyMessageType)type == ReplyMessageType.Invite)
            return;
    }

    private static void ParseReplyHeaderClientMessage(
        int type,
        ref DataStreamReader reader,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        var streamCompressionModel = StreamCompressionModel.Default;
        if (!TryReadRelayHeader(ref reader, streamCompressionModel, out _, out uint senderId))
            return;

        if (senderId != 0 && senderId != selfHeader.userID)
        {
            ref var session = ref farm.sessions.ElementAt(agentIndex);
            session.remoteUserID = senderId;
            if (session.remotePlayerCount == 0)
                session.remotePlayerCount = 1;
            session.remoteOnline = true;
        }

        BotRelayInboundProbe.RecordClientMessage(type);
        switch ((ClientMessageType)type)
        {
            case ClientMessageType.ApplyMatch:
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.ApplyMatch,
                    header = selfHeader
                });
                break;
            case ClientMessageType.ApplyMatchFail:
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.ApplyMatchFail,
                    header = selfHeader
                });
                break;
            case ClientMessageType.RejectMatch:
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.RejectMatch,
                    header = selfHeader
                });
                break;
            case ClientMessageType.ChapterStage:
            {
                var chapterStage = new ClientMessageChapterStage(ref reader, StreamCompressionModel.Default);
                BotRelayInboundProbe.RecordChapterStage();
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
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
                if (reader.HasFailedReads)
                    return;
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.MatchStart,
                    header = selfHeader,
                    matchStart = matchStart
                });
                break;
            }
            case ClientMessageType.Play:
            {
                // The Server.SendRelay envelope was byte-aligned by TryReadRelayHeader above.
                // Decode the body with the protocol's own reader so its packed-bit cursor and
                // FixedString alignment stay identical to the real client implementation.
                var play = new ClientMessagePlay(ref reader);
                if (reader.HasFailedReads)
                    return;
                BotRelayInboundProbe.RecordPlaySeen();
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
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
