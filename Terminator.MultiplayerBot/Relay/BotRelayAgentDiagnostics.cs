using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Agent / inbox diagnostic logging. Use Burst-compatible <see cref="Debug.Log"/> /
/// <c>$""</c> (no <see cref="BurstDiscardAttribute"/>) so arguments stay evaluated in Burst jobs.
/// </summary>
internal static class BotRelayAgentDiagnostics
{
    public static void LogMatchRejected(uint userID) =>
        Debug.Log($"[BotAgent:{userID}] Match rejected/failed.");

    public static void LogLeftSquad(uint userID) =>
        Debug.Log($"[BotAgent:{userID}] Left squad.");

    public static void LogLeftSquadReadyForRematch(uint userID) =>
        Debug.Log($"[BotAgent:{userID}] Left squad; rematch deferred (Status/Mismatch cleanup).");

    public static void LogRemoteLeftLevel(uint userID) =>
        Debug.Log($"[BotAgent:{userID}] Remote left level, sending SquadLeave.");

    public static void LogRemoteOfflineTimeout(
        uint userID,
        uint remoteUserID,
        double timeoutSeconds,
        int state,
        int remoteChannelStatus) =>
        Debug.Log($"[BotAgent:{userID}] Remote {remoteUserID} offline for {timeoutSeconds}s (state={state}, remoteStatus={remoteChannelStatus}); sending SquadLeave.");

    public static void LogJoinStall(
        uint userID,
        int state,
        bool hasPendingInvite,
        uint squadInviteID,
        int agentFlags,
        int channel,
        int remotePlayerCount,
        int remoteChannelStatus,
        bool remoteOnline,
        int matchID,
        uint levelStartStageID,
        int inboundDrops,
        int eventDrops) =>
        Debug.LogWarning(
            $"[BotAgent:{userID}] JOIN-STALL state={state} pendingInvite={hasPendingInvite} squadInviteID={squadInviteID} flags=0x{agentFlags:X2} channel={channel} matchID={matchID} levelStartStage={levelStartStageID} remotePlayers={remotePlayerCount} remoteStatus={remoteChannelStatus} remoteOnline={remoteOnline} inboundDrops={inboundDrops} eventDrops={eventDrops}");

    public static void LogPendingInvite(uint userID, uint squadInviteID, float delaySeconds) =>
        Debug.Log($"[BotAgent:{userID}] Pending invite {squadInviteID} in {delaySeconds}s");

    public static void LogJoinedSquad(uint userID, uint squadInviteID) =>
        Debug.Log($"[BotAgent:{userID}] Joined squad {squadInviteID}");

    public static void LogSquadHandshakeStatus(uint userID, uint userStageID) =>
        Debug.Log($"[BotAgent:{userID}] Squad handshake Status injected (userStageID={userStageID}).");

    public static void LogLevelLoginPropertyArmed(
        uint userID,
        uint userStageID,
        int matchID,
        in FixedString32Bytes sceneName) =>
        Debug.Log($"[BotAgent:{userID}] Level login PlayerProperty armed userStage={userStageID} matchID={matchID} replayScene={sceneName}; Status remains 0 until the next entry tick.");

    public static void LogSentApplyMatch(uint userID) =>
        Debug.Log($"[BotAgent:{userID}] Sent ApplyMatch.");

    public static void LogMatchSuccess(uint userID, int matchID, int level) =>
        Debug.Log($"[BotAgent:{userID}] Match success id={matchID} level={level}");

    public static void LogMatchStartAccepted(
        uint userID,
        int matchID,
        uint userStageID,
        uint levelID,
        int stage,
        in FixedString32Bytes sceneName) =>
        Debug.Log($"[BotAgent:{userID}] MatchStart accepted matchID={matchID} userStage={userStageID} level={levelID}_{stage} scene={sceneName}.");

    public static void LogRemotePlayerProperty(uint userID, uint remoteUserID) =>
        Debug.Log($"[BotAgent:{userID}] Remote PlayerProperty from {remoteUserID} (diagnostic only).");

    public static void LogSentSquadJoin(uint userID, uint squadInviteID) =>
        Debug.Log($"[BotAgent:{userID}] Queued SquadJoin {squadInviteID} for server inject.");

    public static void LogSquadJoinFailed(uint userID, uint squadChannel, int serverMatchId) =>
        Debug.LogWarning(
            $"[BotAgent:{userID}] SquadJoin failed for channel {squadChannel} (server JoinFailed, match={serverMatchId}).");

    public static void LogSquadJoinTimedOut(
        uint userID,
        uint squadChannel,
        double timeoutSeconds) =>
        Debug.LogWarning($"[BotAgent:{userID}] SquadJoin {squadChannel} received no Join/JoinFailed for {timeoutSeconds}s; cancelling this invitation and releasing its farm lease.");

    public static void LogUnexpectedSquadJoinRejected(
        uint userID,
        uint squadChannel,
        int state,
        uint expectedPendingSquadChannel) =>
        Debug.LogWarning($"[BotAgent:{userID}] Rejected SquadJoin {squadChannel} outside its active generation (state={state}, expectedPending={expectedPendingSquadChannel}).");

    public static void LogSentMatch(uint userID, int matchLevel) =>
        Debug.Log($"[BotAgent:{userID}] Sent MatchToSend level={matchLevel}.");

    public static void LogLevelEntryArmed(
        uint userID,
        uint userStageID,
        int matchID,
        in Unity.Collections.FixedString32Bytes sceneName,
        int remoteChannelStatus) =>
        Debug.Log($"[BotAgent:{userID}] Match level entry armed userStage={userStageID} matchID={matchID} replayScene={sceneName} remoteStatus={remoteChannelStatus}.");

    public static void LogIdleMatchArmed(uint userID) =>
        Debug.Log($"[BotAgent:{userID}] Idle match armed (relay ready).");

    public static void LogBotVsBotRejected(uint userID, uint remoteUserID) =>
        Debug.LogWarning(
            $"[BotAgent:{userID}] Bot-vs-Bot rejected (remote={remoteUserID}); leaving match/squad.");

    public static void LogSquadInvite(
        uint senderID,
        int relayType,
        uint squadInviteID,
        uint levelID,
        int stage,
        uint botUserID) =>
        Debug.Log(
            $"[BotRelaySlotInbox] Invite from {senderID}, relay={relayType}, squad={squadInviteID}, level={levelID}, stage={stage}, bot={botUserID}");

    /// <summary>
    /// Invite wire reached the bot inbox. relayReady=false strongly correlates with Server not
    /// forwarding All-broadcast Invites (path A in invite diagnostics).
    /// </summary>
    public static void LogInviteReceived(
        uint botUserID,
        uint senderID,
        uint squadInviteID,
        bool relayReady,
        int botState,
        int sessionChannel,
        ushort virtualPort) =>
        Debug.Log(
            $"[BotRelaySlotInbox] Invite received squad={squadInviteID} from={senderID} bot={botUserID} relayReady={relayReady} state={botState} channel={sessionChannel} vport={virtualPort}");

    public static void LogInviteParseFailed(uint botUserID, NativeArray<byte> payload, int payloadBytes)
    {
        var hex = __FormatPayloadHex(payload, payloadBytes, 64);
        FixedString512Bytes msg = default;
        msg.Append((FixedString128Bytes)"[BotRelaySlotInbox] Invite parse failed bot=");
        msg.Append(botUserID);
        msg.Append((FixedString32Bytes)" payloadBytes=");
        msg.Append(payloadBytes);
        msg.Append((FixedString32Bytes)" hex=");
        msg.Append(hex);
        Debug.LogWarning(msg);
    }

    public static void LogInviteIgnored(
        uint userID,
        in FixedString64Bytes reason,
        int botState,
        uint inviteSquadID,
        int sessionChannel)
    {
        FixedString512Bytes msg = default;
        msg.Append((FixedString64Bytes)"[BotAgent:");
        msg.Append(userID);
        msg.Append((FixedString64Bytes)"] Invite ignored: ");
        msg.Append(reason);
        msg.Append((FixedString32Bytes)" inviteSquad=");
        msg.Append(inviteSquadID);
        msg.Append((FixedString32Bytes)" state=");
        msg.Append(botState);
        msg.Append((FixedString32Bytes)" channel=");
        msg.Append(sessionChannel);
        Debug.LogWarning(msg);
    }

    public static void LogRelayServerStatusInjected(uint userID) =>
        Debug.Log(
            $"[BotAgent:{userID}] Relay server Status injected (app-layer). Server should log NetworkRelayServerRead type=2 for this bot.");

    public static void LogRelayHandshake(uint userID, in FixedString64Bytes stage, ushort virtualPort)
    {
        FixedString128Bytes msg = default;
        msg.Append((FixedString32Bytes)"[BotRelay] Bot ");
        msg.Append(userID);
        msg.Append((FixedString32Bytes)" handshake: ");
        msg.Append(stage);
        msg.Append((FixedString32Bytes)" vport=");
        msg.Append(virtualPort);
        Debug.Log(msg);
    }

    public static void LogSquadLeaveWhileIdle(uint userID, int botState, int sessionChannel) =>
        Debug.LogWarning(
            $"[BotAgent:{userID}] SquadLeave while not in squad state={botState} channel={sessionChannel}");

    public static void LogRouteSendPeeled(int relayType, int wireLength, int appLength)
    {
        if (!BotRelayDefines.VerboseTelemetry)
        {
            return;
        }

        Debug.Log($"[BotRelay] RouteSend peeled relay type={relayType} wire={wireLength} app={appLength}");
    }

    public static void LogReplayCompleted(
        uint userID,
        int catalogIndex,
        int frameCount,
        int advancedThisTick,
        int injectedThisTick,
        double deltaTime,
        double playbackTime) =>
        Debug.Log($"[BotAgent:{userID}] Replay completed catalog={catalogIndex} frames={frameCount} finalTickAdvanced={advancedThisTick} finalTickInjected={injectedThisTick} delta={deltaTime}s playback={playbackTime}s.");

    public static void LogRouteSendEnqueued(ushort fromPort, ushort toPort, int length)
    {
        if (!BotRelayDefines.VerboseTelemetry)
        {
            return;
        }

        FixedString128Bytes msg = default;
        msg.Append((FixedString64Bytes)"[BotRelay] RouteSend ");
        msg.Append(fromPort);
        msg.Append('>');
        msg.Append(toPort);
        msg.Append((FixedString32Bytes)" len=");
        msg.Append(length);
        Debug.Log(msg);
    }

    public static void LogRouteWirePreview(ushort fromPort, ushort toPort, int length, in BotRelayPacket packet)
    {
        if (!BotRelayDefines.VerboseTelemetry)
        {
            return;
        }

        if (length < 80 || length > 160)
        {
            return;
        }

        var hex = __HexPreview(in packet, length, 80);
        FixedString512Bytes msg = default;
        msg.Append((FixedString64Bytes)"[BotRelay] RouteWirePreview ");
        msg.Append(fromPort);
        msg.Append('>');
        msg.Append(toPort);
        msg.Append((FixedString32Bytes)" len=");
        msg.Append(length);
        msg.Append((FixedString32Bytes)" hex=");
        msg.Append(hex);
        Debug.Log(msg);
    }

    public static void LogRouteSendDropped(ushort fromPort, ushort toPort, int length)
    {
        FixedString128Bytes msg = default;
        msg.Append((FixedString64Bytes)"[BotRelay] RouteSend dropped (unknown port) ");
        msg.Append(fromPort);
        msg.Append('>');
        msg.Append(toPort);
        msg.Append((FixedString32Bytes)" len=");
        msg.Append(length);
        Debug.LogWarning(msg);
    }

    public static void LogInboundDequeued(uint botUserID, int wireLength, int appLength)
    {
        if (!BotRelayDefines.VerboseTelemetry)
        {
            return;
        }

        Debug.Log($"[BotRelay] Bot {botUserID} dequeued wire={wireLength} app={appLength}");
    }

    public static void LogLinkAckInjected(uint botUserID, int wireLength, int firstByte) =>
        Debug.LogWarning(
            $"[BotRelay] Bot {botUserID} link-ack injected wire len={wireLength} b0=0x{firstByte:X2}");

    public static void LogUnmappedInboundWire(uint botUserID, int wireLength) =>
        Debug.LogWarning($"[BotRelay] Bot {botUserID} inbound wire unmapped (len={wireLength})");

    private static FixedString512Bytes __HexPreview(in BotRelayPacket packet, int wireLength, int maxBytes)
    {
        FixedString512Bytes hex = default;
        if (packet.IsEmpty || wireLength <= 0)
        {
            return hex;
        }

        int count = math.min(math.min(packet.length, wireLength), maxBytes);
        for (int i = 0; i < count; ++i)
        {
            if (i > 0)
            {
                hex.Append(' ');
            }

            byte value = packet.GetByte(i);
            hex.Append(__HexNibbleChar(value >> 4));
            hex.Append(__HexNibbleChar(value & 0xF));
        }

        return hex;
    }

    private static FixedString512Bytes __FormatPayloadHex(NativeArray<byte> payload, int payloadBytes, int maxBytes)
    {
        FixedString512Bytes hex = default;
        if (!payload.IsCreated || payloadBytes <= 0)
        {
            return hex;
        }

        int count = math.min(math.min(payloadBytes, payload.Length), maxBytes);
        for (int i = 0; i < count; ++i)
        {
            if (i > 0)
            {
                hex.Append(' ');
            }

            byte value = payload[i];
            hex.Append(__HexNibbleChar(value >> 4));
            hex.Append(__HexNibbleChar(value & 0xF));
        }

        return hex;
    }

    private static char __HexNibbleChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
}
