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
        in ClientHeader selfHeader)
    {
        if (packet.IsEmpty ||
            !BotRelayWireBytes.TryAcceptCurrentInboundApp(in packet, out _))
            return;

        int scratchBase = agentIndex * BotRelayPacket.MaxPayloadSize;
        if (!packet.TryCopyBytesTo(farm.wireScratch, scratchBase))
            return;

        var payload = farm.wireScratch.GetSubArray(scratchBase, packet.length);
        ParseDataPayload(payload, agentIndex, ref farm, in selfHeader);
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
        int type = reader.ReadPackedInt(streamCompressionModel);
        if (reader.HasFailedReads)
            return;

        BotRelayInboundProbe.RecordInbound(type);
        switch ((NetworkRelayMessageType)type)
        {
            case NetworkRelayMessageType.Matching:
                // Validate the complete current notification, but do not cache a pending
                // generation. MatchToRead is the sole authority that commits matchID.
                BotRelayMessageUtility.TryReadServerMatchStateNotification(
                    ref reader,
                    in streamCompressionModel,
                    out _,
                    out _);
                break;
            case NetworkRelayMessageType.Mismatch:
            {
                if (!BotRelayMessageUtility.TryReadServerMatchStateNotification(
                        ref reader,
                        in streamCompressionModel,
                        out int mismatchID,
                        out uint sourceID))
                {
                    return;
                }

                ClientMessageMatchToRead mismatch;
                mismatch.matchID = mismatchID;
                mismatch.level = 0;
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.Mismatch,
                    header = sourceID == 0
                        ? selfHeader
                        : new ClientHeader(sourceID, default),
                    match = mismatch
                });
                break;
            }
            case NetworkRelayMessageType.Match:
            {
                if (!BotRelayMessageUtility.TryReadServerMatchNotification(
                        ref reader,
                        in streamCompressionModel,
                        out int matchID,
                        out int level) ||
                    matchID <= 0 ||
                    level < 0)
                {
                    return;
                }

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
                int failedChannel = reader.ReadPackedInt(streamCompressionModel);
                if (failedChannel < 0 ||
                    !BotRelayMessageUtility.IsAtEnd(ref reader))
                {
                    return;
                }

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
            case NetworkRelayMessageType.Status:
            case NetworkRelayMessageType.Add:
            {
                // Server __WriteStatus is always [channelFlag][userID]. Do not treat a short remainder
                // as a bare channelStatus — that would store (stage<<3|flags) and break stage lookup.
                ParseRelayStatusMessage(type, ref reader, streamCompressionModel, agentIndex, ref farm, in selfHeader);
                break;
            }
            case NetworkRelayMessageType.Disconnect:
            case NetworkRelayMessageType.Remove:
                ParseRelayStatusMessage(type, ref reader, streamCompressionModel, agentIndex, ref farm, in selfHeader);
                break;
            default:
                if (type == (int)ReplyMessageType.Invite)
                    ParseInviteSendRelayMessage(payload, agentIndex, ref farm, in selfHeader);
                else if (type == (int)ReplyMessageType.PlayerProperty)
                    ParseReplyPayloadMessage(type, ref reader, streamCompressionModel, agentIndex, ref farm, in selfHeader);
                else if (type >= (int)ClientMessageType.ApplyFriend &&
                         type <= (int)ClientMessageType.MatchStart)
                    ParseReplyHeaderClientMessage(type, ref reader, agentIndex, ref farm, in selfHeader);
                break;
        }
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

        // Defense in depth: RouteSend normally admits only validated Invite apps, but a bad
        // PopEvents offset must never be able to manufacture a PendingInvite if it reaches Inbox.
        if (!BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in packet, out var invite))
        {
            BotRelayAgentDiagnostics.LogInviteParseFailed(selfHeader.userID, payload, payload.Length);
            return;
        }

        ref readonly var transport = ref farm.transport.ElementAt(agentIndex);

        ClientMessageSquadInviteToRead squadInvite = new ClientMessageSquadInviteToRead(invite, out var header);
        if (invite.stage >= 0)
        {
            squadInvite.stage = invite.stage;
        }

        ref readonly var agentState = ref farm.agentStates.ElementAt(agentIndex);
        BotRelayAgentDiagnostics.LogSquadInvite(
            invite.id,
            (int)invite.type,
            squadInvite.squadInviteID,
            invite.levelID,
            invite.stage,
            selfHeader.userID);

        ref readonly var sessionReadonly = ref farm.sessions.ElementAt(agentIndex);
        bool relayReady = (transport.flags & BotRelayTransportFlags.Connected) != 0;
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
        int channelFlag;
        switch (messageType)
        {
            case NetworkRelayMessageType.Disconnect:
            case NetworkRelayMessageType.Remove:
                channelFlag = 0;
                break;
            default:
                channelFlag = reader.ReadPackedInt(streamCompressionModel);
                break;
        }

        var remoteUserID = reader.ReadPackedUInt(streamCompressionModel);
        if (channelFlag < 0 ||
            remoteUserID == 0 ||
            !BotRelayMessageUtility.IsAtEnd(ref reader))
        {
            return;
        }

        if (remoteUserID != selfHeader.userID)
        {
            // Status is live presence only. It must not establish a peer before Join or replace
            // the peer committed for the current squad generation.
            if (!session.IsInSquad ||
                session.remoteUserID == 0 ||
                session.remoteUserID != remoteUserID)
            {
                return;
            }

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
        int queryChannel = reader.ReadPackedInt(streamCompressionModel);
        int channelFlag = reader.ReadPackedInt(streamCompressionModel);
        if (reader.HasFailedReads || queryChannel < 0 || channelFlag < 0)
            return;

        // Query emits one response per member. The bodyless response is the Bot's own snapshot;
        // only the complete remote payload may update peer presence.
        if (BotRelayMessageUtility.IsAtEnd(ref reader))
            return;

        reader.Flush();
        uint remoteUserID = reader.ReadPackedUInt(streamCompressionModel);
        if (remoteUserID == 0 ||
            !BotRelayMessageUtility.TryReadOptionalRemoteClientHeader(
                remoteUserID,
                ref reader,
                streamCompressionModel,
                out _) ||
            !BotRelayMessageUtility.IsAtEnd(ref reader))
        {
            return;
        }

        ref var session = ref farm.sessions.ElementAt(agentIndex);
        if (!session.IsInSquad ||
            queryChannel != session.channel ||
            session.remoteUserID == 0 ||
            remoteUserID != session.remoteUserID)
        {
            return;
        }

        int remoteStage = BotRelayMessageUtility.DecodeChannelStatus(channelFlag);
        session.remoteChannelStatus = remoteStage;
        session.remoteOnline = ((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Online) != 0;
    }

    private static void ParseChannelMessage(
        int type,
        ref DataStreamReader reader,
        StreamCompressionModel streamCompressionModel,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        int channel = reader.ReadPackedInt(streamCompressionModel);
        int channelFlag = reader.ReadPackedInt(streamCompressionModel);
        if (reader.HasFailedReads || channel < 0 || channelFlag < 0)
            return;

        var channelMessageType = (NetworkRelayMessageType)type;
        bool hasRemotePayload = reader.GetBytesRead() < reader.Length;
        bool hasRemoteHeader = false;
        uint remoteUserID = 0;
        ClientHeader remoteHeader = default;
        if (hasRemotePayload)
        {
            // Current Server channel payloads mirror ClientData exactly:
            // [packed remoteUserID][fixed LevelPlayerHeader]. Truncated legacy variants are not
            // accepted, because an incomplete peer snapshot must never establish membership.
            reader.Flush();
            remoteUserID = reader.ReadPackedUInt(streamCompressionModel);
            if (reader.HasFailedReads ||
                remoteUserID == 0 ||
                !BotRelayMessageUtility.TryReadOptionalRemoteClientHeader(
                    remoteUserID,
                    ref reader,
                    streamCompressionModel,
                    out remoteHeader) ||
                BotRelayMessageUtility.RemainingBytes(ref reader) != 0)
            {
                return;
            }

            hasRemoteHeader = true;
        }

        ClientMessageSquadJoinToRead join;
        join.playerStatus.flag = (ClientRemotePlayerFlag)channelFlag;
        join.squadInviteID = (uint)channel;

        BotRelayEventType eventType;
        switch (channelMessageType)
        {
            case NetworkRelayMessageType.Leave:
                eventType = BotRelayEventType.SquadLeave;
                BotRelayInboundProbe.RecordChannelLeave();
                break;
            case NetworkRelayMessageType.Drop:
                eventType = BotRelayEventType.SquadDrop;
                BotRelayInboundProbe.RecordChannelLeave();
                break;
            case NetworkRelayMessageType.Create:
            case NetworkRelayMessageType.Join:
                eventType = BotRelayEventType.SquadJoin;
                BotRelayInboundProbe.RecordChannelJoin();
                break;
            default:
                return;
        }

        TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
        {
            type = eventType,
            header = selfHeader,
            squadJoin = join,
            channelMessageType = channelMessageType,
            channelHasRemotePayload = hasRemotePayload,
            channelHasRemoteHeader = hasRemoteHeader,
            channelRemoteUserID = remoteUserID,
            channelRemoteHeader = remoteHeader
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
        relayType = (NetworkRelayType)reader.ReadPackedInt(streamCompressionModel);
        senderId = reader.ReadPackedUInt(streamCompressionModel);
        if (reader.HasFailedReads)
            return false;

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
            if (!TryReadRelayHeader(
                    ref reader,
                    streamCompressionModel,
                    out NetworkRelayType relayType,
                    out uint senderID) ||
                relayType != NetworkRelayType.Channel ||
                senderID == 0 ||
                senderID == selfHeader.userID)
                return;

            ref readonly var session = ref farm.sessions.ElementAt(agentIndex);
            if (!session.IsInSquad ||
                session.remoteUserID == 0 ||
                session.remoteUserID != senderID)
                return;

            _ = new ClientMessagePlayerProperty(ref reader);
            if (!BotRelayMessageUtility.IsAtEnd(ref reader))
                return;

            TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.RemotePlayerProperty,
                header = new ClientHeader(senderID, default)
            });
            return;
        }

    }

    private static void ParseReplyHeaderClientMessage(
        int type,
        ref DataStreamReader reader,
        int agentIndex,
        ref BotRelayFarmNative farm,
        in ClientHeader selfHeader)
    {
        var streamCompressionModel = StreamCompressionModel.Default;
        if (!TryReadRelayHeader(
                ref reader,
                streamCompressionModel,
                out NetworkRelayType relayType,
                out uint senderId) ||
            relayType != NetworkRelayType.Channel ||
            senderId == 0 ||
            senderId == selfHeader.userID)
            return;

        ref readonly var knownSession = ref farm.sessions.ElementAt(agentIndex);
        // Once Join/Create has established the peer for this squad generation, an app envelope
        // attributed to a different sender is a framing false-positive or stale traffic.
        if (knownSession.remoteUserID != 0 && knownSession.remoteUserID != senderId)
            return;

        var senderHeader = new ClientHeader(senderId, default);

        BotRelayInboundProbe.RecordClientMessage(type);
        switch ((ClientMessageType)type)
        {
            case ClientMessageType.ApplyMatch:
                if (!BotRelayMessageUtility.IsAtEnd(ref reader))
                    return;

                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.ApplyMatch,
                    header = senderHeader
                });
                break;
            case ClientMessageType.ApplyMatchFail:
                if (!BotRelayMessageUtility.IsAtEnd(ref reader))
                    return;

                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.ApplyMatchFail,
                    header = senderHeader
                });
                break;
            case ClientMessageType.RejectMatch:
                if (!BotRelayMessageUtility.IsAtEnd(ref reader))
                    return;

                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.RejectMatch,
                    header = senderHeader
                });
                break;
            case ClientMessageType.ChapterStage:
            {
                // Human UI navigation metadata is not a Bot level-entry authority. Decode the
                // complete body so the packet cursor cannot be reused as another protocol, then
                // deliberately discard it without committing sender/session state.
                _ = new ClientMessageChapterStage(ref reader, StreamCompressionModel.Default);
                if (!BotRelayMessageUtility.IsAtEnd(ref reader))
                    return;
                break;
            }
            case ClientMessageType.MatchStart:
            {
                var matchStart = new ClientMessageMatchStart(ref reader);
                if (!BotRelayMessageUtility.IsAtEnd(ref reader))
                    return;

                // AgentLogic first verifies mode/generation and the expected Host/peer identity.
                // Committing here would let a stale/foreign early descriptor poison remoteUserID
                // and make the real Host fail the known-peer envelope gate.
                TryEnqueueEvent(agentIndex, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.MatchStart,
                    header = senderHeader,
                    matchStart = matchStart
                });
                break;
            }
            case ClientMessageType.Play:
            {
                // The Server.SendRelay envelope was byte-aligned by TryReadRelayHeader above.
                // Decode the full current human body, but never turn it into Bot entry state.
                // Keeping the full skip prevents arbitrary string bytes from being reinterpreted
                // as a shorter control application by an outer transport walk.
                _ = new ClientMessagePlay(ref reader);
                if (!BotRelayMessageUtility.IsAtEnd(ref reader))
                    return;
                break;
            }
        }
    }

}
