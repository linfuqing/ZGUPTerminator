using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using ZG;

public struct BotRelayFarmTickConfig
{
    public float inviteTimeoutMin;
    public float inviteTimeoutMax;
    public float matchTimeoutMin;
    public float matchTimeoutMax;
    public float remoteOfflineLeaveTimeout;
    public double elapsedTime;
    public uint frameSeed;

    // Set by the production systems when the disk replay catalog is empty. Keep the default at
    // zero so pure logic tests can construct a tick config without depending on local assets.
    // A live farm with no recording must not advertise a bot that will enter a level but never
    // produce gameplay frames.
    public byte replayCatalogMissing;

    // App-layer relay inject: when injectEnabled != 0, decoded relay messages are pushed
    // straight into NetworkRelayServerHandler.Read (bypassing the UTP wire). Wired up by
    // BotRelayPostTickSystem from the NetworkRelayServer singleton.
    public byte injectEnabled;
    public NativeQueue<BotRelayInject>.ParallelWriter injectWriter;
}

[BurstCompile]
public static class BotAgentLogic
{
    public const double DefaultRemoteOfflineLeaveTimeoutSeconds = 5.0;

    public static void Execute(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        while (BotRelaySlotInbox.TryDequeueEvent(index, ref farm, out var evt))
            __HandleEvent(index, ref farm, in config, in evt);

        __TickState(index, ref farm, in config);
        __TickDescriptorLevelEntry(index, ref farm, in config);
        __TickChannelTracker(index, ref farm, in config);
        __TickBotVsBotGuard(index, ref farm, in config);
        __TickJoinWatchdog(index, ref farm, in config);

        BotRelayInboundProbe.RecordBotState((int)farm.agentStates[index].state);
    }

    /// <summary>
    /// Farm-wide reactive matching: observe non-bot entries in <see cref="NetworkRelayServer.ReadOnly.matchIDs"/>,
    /// wait <c>matchTimeout*</c>, then dispatch the closest Idle bot. Serial; call after PostAgent.
    /// </summary>
    public static void TickMatchDispatch(
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in NetworkRelayServer.ReadOnly server)
    {
        if (!farm.matchObserveUserID.IsCreated ||
            !farm.matchObserveGeneration.IsCreated ||
            !farm.matchObserveDispatchTime.IsCreated ||
            !farm.matchingSlotOwner.IsCreated)
        {
            return;
        }

        if (farm.matchingSlotOwner[0] != BotMatchGuard.NoMatchingSlotOwner)
            return;

        if (!__TryPickObservedHuman(in farm, in server, out uint humanUserID, out int humanDistance))
        {
            farm.matchObserveUserID[0] = 0;
            farm.matchObserveDispatchTime[0] = double.PositiveInfinity;
            return;
        }

        if (farm.matchObserveUserID[0] != humanUserID)
        {
            farm.matchObserveUserID[0] = humanUserID;
            farm.matchObserveGeneration[0] += 1u;
            var random = Unity.Mathematics.Random.CreateFromIndex(
                config.frameSeed ^ humanUserID ^ 0x4D415443u);
            farm.matchObserveDispatchTime[0] =
                config.elapsedTime + __RandomMatchTimeout(in config, ref random);
        }

        if (config.elapsedTime < farm.matchObserveDispatchTime[0])
            return;

        if (!__TryPickClosestIdleBot(in farm, humanDistance, out int agentIndex))
            return;

        __StartMatching(agentIndex, ref farm, in config);
        if (farm.agentStates[agentIndex].state == BotState.Matching)
        {
            farm.matchObserveUserID[0] = 0;
            farm.matchObserveDispatchTime[0] = double.PositiveInfinity;
        }
    }

    private static bool __TryPickObservedHuman(
        in BotRelayFarmNative farm,
        in NetworkRelayServer.ReadOnly server,
        out uint humanUserID,
        out int humanDistance)
    {
        humanUserID = 0;
        humanDistance = 0;
        double bestStart = double.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < server.matchIDs.Length; ++i)
        {
            uint userID = server.matchIDs[i];
            if (userID == 0 || BotMatchGuard.IsFarmBotUser(in farm, userID))
                continue;

            if (!server.sendBuffer.GetConnection(userID, out int connectionIndex, out int identityIndex, out _) ||
                connectionIndex < 0 ||
                identityIndex < 0 ||
                identityIndex >= server.identities.Length ||
                identityIndex >= server.matches.Length)
            {
                continue;
            }

            var identity = server.identities[identityIndex];
            if (!identity.canMatch || identity.match == 0)
                continue;

            var match = server.matches[identityIndex];
            if (!found || match.startTime < bestStart)
            {
                found = true;
                bestStart = match.startTime;
                humanUserID = userID;
                humanDistance = match.value.distance;
            }
        }

        return found;
    }

    private static bool __TryPickClosestIdleBot(
        in BotRelayFarmNative farm,
        int humanDistance,
        out int agentIndex)
    {
        agentIndex = -1;
        int bestDelta = int.MaxValue;
        int tieCount = 0;

        for (int i = 0; i < farm.agentCount; ++i)
        {
            ref readonly var state = ref farm.agentStates.ElementAt(i);
            if (state.state != BotState.Idle)
                continue;

            ref readonly var session = ref farm.sessions.ElementAt(i);
            if (!session.IsIdleForMatch)
                continue;

            if ((farm.transport[i].flags & BotRelayTransportFlags.Connected) == 0)
                continue;

            int delta = state.matchLevel - humanDistance;
            if (delta < 0)
                delta = -delta;

            if (delta < bestDelta)
            {
                bestDelta = delta;
                agentIndex = i;
                tieCount = 1;
            }
            else if (delta == bestDelta)
            {
                tieCount++;
            }
        }

        if (agentIndex < 0 || tieCount <= 1)
            return agentIndex >= 0;

        // Among closest Idle bots: deterministic per observe episode (userID ^ generation),
        // so the same human re-entering matchIDs can land on a different peer.
        var tieRandom = Unity.Mathematics.Random.CreateFromIndex(
            farm.matchObserveUserID[0] ^ farm.matchObserveGeneration[0] ^ 0x54494533u);
        int pickOrdinal = tieRandom.NextInt(0, tieCount);
        int seen = 0;
        for (int i = 0; i < farm.agentCount; ++i)
        {
            ref readonly var state = ref farm.agentStates.ElementAt(i);
            if (state.state != BotState.Idle)
                continue;

            ref readonly var session = ref farm.sessions.ElementAt(i);
            if (!session.IsIdleForMatch)
                continue;

            if ((farm.transport[i].flags & BotRelayTransportFlags.Connected) == 0)
                continue;

            int delta = state.matchLevel - humanDistance;
            if (delta < 0)
                delta = -delta;

            if (delta != bestDelta)
                continue;

            if (seen == pickOrdinal)
            {
                agentIndex = i;
                return true;
            }

            seen++;
        }

        return agentIndex >= 0;
    }

    private static void __HandleEvent(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in BotRelayEvent evt)
    {
        ref var state = ref farm.agentStates.ElementAt(index);

        switch (evt.type)
        {
            case BotRelayEventType.SquadInvite:
                __OnSquadInvite(index, ref farm, in config, in evt.header, in evt.squadInvite);
                break;
            case BotRelayEventType.SquadJoin:
                __OnSquadJoin(index, ref farm, in config, in evt);
                break;
            case BotRelayEventType.SquadJoinFail:
                __OnSquadJoinFail(index, ref farm, in config, in evt.squadJoin);
                break;
            case BotRelayEventType.SquadLeave:
                __OnSquadLeave(
                    index,
                    ref farm,
                    in config,
                    wasDropped: false,
                    hasRemotePayload: evt.channelHasRemotePayload);
                break;
            case BotRelayEventType.SquadDrop:
                __OnSquadLeave(
                    index,
                    ref farm,
                    in config,
                    wasDropped: true,
                    hasRemotePayload: evt.channelHasRemotePayload);
                break;
            case BotRelayEventType.ApplyMatch:
                __OnApplyMatch(index, ref farm, in config, in evt.header);
                break;
            case BotRelayEventType.ApplyMatchFail:
            case BotRelayEventType.RejectMatch:
                BotRelayAgentDiagnostics.LogMatchRejected(state.userID);
                break;
            case BotRelayEventType.Match:
                __OnMatch(index, ref farm, in config, in evt.match);
                break;
            case BotRelayEventType.Mismatch:
                __OnMismatch(
                    index,
                    ref farm,
                    in evt.header,
                    in evt.match,
                    config.elapsedTime);
                break;
            case BotRelayEventType.MatchStart:
                __OnMatchStart(index, ref farm, in evt.header, in evt.matchStart);
                break;
            case BotRelayEventType.RemotePlayerProperty:
                __OnRemotePlayerProperty(index, ref farm, in evt.header);
                break;
        }
    }

    private static void __TickState(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);

        switch (state.state)
        {
            case BotState.Idle:
                // Register Status once Connected. Production MatchToSend is reactive via
                // TickMatchDispatch (non-bot matchIDs + matchTimeout). A finite nextIdleMatchTime is
                // only a tests/harness force gate — spawn keeps +Infinity.
                if ((farm.transport[index].flags & BotRelayTransportFlags.Connected) != 0)
                    __TryRegisterRelayServerPresence(index, ref farm, in config);

                if (!double.IsPositiveInfinity(farm.nextIdleMatchTime[index]) &&
                    config.elapsedTime >= farm.nextIdleMatchTime[index])
                {
                    __StartMatching(index, ref farm, in config);
                    farm.nextIdleMatchTime[index] = double.PositiveInfinity;
                }
                break;

            case BotState.PendingInvite:
                // Invite handling queues Leave/Mismatch/Status(0) while draining events. Join is
                // deliberately not allowed in that same agent tick: Server applies Mismatch after
                // reading the inject batch, so a same-batch Join would still see match != 0.
                if (config.elapsedTime >= state.inviteJoinTime &&
                    config.elapsedTime > state.stateEnterTime)
                {
                    if ((farm.agentFlags[index] & BotAgentRuntimeFlags.JoinDispatched) != 0)
                        __ExpirePendingSquadJoin(index, ref farm, in config);
                    else
                        __SendSquadJoin(index, ref farm, in config);
                }
                break;

            case BotState.Matching:
                break;

            case BotState.InSquad:
                if (!__IsInSquad(ref farm, index))
                {
                    __TransitionTo(index, ref farm, BotState.Idle, config.elapsedTime);
                }
                break;

            case BotState.InLevel:
                // Replay exhaustion only means the bot has no more recorded actions. It does not
                // mean the human left the level. Stay committed until __TickChannelTracker observes
                // the live remote stage transition to zero.
                break;

            case BotState.Leaving:
                // Business traffic has one path: Server injection. Deliver the ordered
                // Leave + Mismatch + Status(0) cleanup before releasing local state. Mismatch also
                // clears a completed/failed match generation; otherwise the local slot may look
                // Idle while the Server still refuses a new match.
                __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Leave, 0, false);
                __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Mismatch, 0, false);
                __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Status, 0, true);
                farm.relayServerStatusInjected[index] = 1;
                ref var leavingSession = ref farm.sessions.ElementAt(index);
                leavingSession.ResetChannel();
                BotRelayInboundProbe.RecordLevelStartCleared(1);
                __ClearLevelStartGeneration(index, ref farm);
                __TransitionTo(index, ref farm, BotState.Idle, config.elapsedTime);
                farm.nextIdleMatchTime[index] = double.PositiveInfinity;
                BotRelayAgentDiagnostics.LogLeftSquad(state.userID);
                break;
        }
    }

    private static void __TickChannelTracker(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref readonly var session = ref farm.sessions.ElementAt(index);

        // Unified squad disconnect policy: preserve membership during a short reconnect window,
        // then release the Bot so an offline human cannot occupy it indefinitely. Disconnect only
        // changes remoteOnline; the last authoritative remoteChannelStatus remains intact.
        if (__TickRemoteOfflineTimeout(index, ref farm, in config, ref state, in session))
        {
            farm.prevRemoteChannelStatus[index] = session.remoteChannelStatus;
            return;
        }

        // InSquad lobby: honour a sustained remote stage-leave only when level entry is not armed.
        // When the host starts a level, remoteChannelStatus often flaps non-zero → 0 while the scene
        // loads; the bot is still InSquad until replay load promotes to InLevel. Treating that flap
        // as leave evicts the bot before it streams (invisible bot) — same class of bug as the InLevel
        // debounce below.
        if (state.state == BotState.InSquad)
        {
            bool levelStartHandshake =
                (farm.agentFlags[index] &
                 (BotAgentRuntimeFlags.PendingLevelStart | BotAgentRuntimeFlags.LevelStartHandled)) != 0;
            if (!levelStartHandshake &&
                farm.prevRemoteChannelStatus[index] != 0 &&
                session.remoteChannelStatus == 0)
            {
                BotRelayAgentDiagnostics.LogRemoteLeftLevel(state.userID);
                __TransitionTo(index, ref farm, BotState.Leaving, config.elapsedTime);
            }

            farm.prevRemoteChannelStatus[index] = session.remoteChannelStatus;
            return;
        }

        // InLevel: distinguish the two orthogonal remote-presence signals (see
        // BotSessionState.remoteOnline):
        //
        //   * remoteChannelStatus (channelFlag >> ShiftToStatus) == 0  => the human GENUINELY left
        //     the stage (returned to lobby). This is the original design's leave trigger
        //     ("channelStatus=0 才退队"). Debounced against the host's per-frame Create/Join/Leave
        //     re-broadcast flap so a single spurious 0 frame doesn't end the level.
        //   * remoteOnline == false while remoteChannelStatus != 0  => the human DISCONNECTED but is
        //     still in the stage during the unified reconnect grace. Pause streaming immediately;
        //     __TickRemoteOfflineTimeout releases the Bot only after the grace expires.
        //
        // So: whenever the peer is not actively present (offline OR status 0) we pause streaming
        // immediately via RemoteAbsentSuppressed (stops -5 at once, resumes on reconnect); we only
        // transition to Leaving when the stage is genuinely gone (status 0, debounce) or the shared
        // offline timeout expires.
        if (state.state == BotState.InLevel)
        {
            bool present = session.remoteOnline && session.remoteChannelStatus != 0;
            if (present)
            {
                // Peer confirmed here and consuming: stream normally, and latch that we have a
                // presence baseline so a subsequent absence is trustworthy.
                state.remotePresenceSeen = true;
                state.remoteAbsentSince = -1.0;
                farm.agentFlags[index] &= ~BotAgentRuntimeFlags.RemoteAbsentSuppressed;
            }
            else if (!state.remotePresenceSeen)
            {
                // We have not yet positively observed the peer present this level (entry window, or
                // the session's remoteOnline/remoteChannelStatus simply haven't been refreshed yet).
                // Default to STREAMING so the bot is visible — never suppress or leave on an absence we
                // never actually saw the peer transition into. (Pre-suppression behaviour: sticky.)
                state.remoteAbsentSince = -1.0;
                farm.agentFlags[index] &= ~BotAgentRuntimeFlags.RemoteAbsentSuppressed;
                BotRelayInboundProbe.RecordRemotePresence(0);
            }
            else
            {
                // We saw the peer present, and now they are not consuming (offline or left the stage):
                // pause streaming so we stop flooding the non-ACKing peer (NetworkSendMessage -5).
                farm.agentFlags[index] |= BotAgentRuntimeFlags.RemoteAbsentSuppressed;
                BotRelayInboundProbe.RecordRemotePresence(2);

                if (session.remoteChannelStatus == 0)
                {
                    // Genuine stage leave: debounce, then end the level.
                    if (state.remoteAbsentSince < 0.0)
                    {
                        state.remoteAbsentSince = config.elapsedTime;
                    }
                    else if (config.elapsedTime - state.stateEnterTime > InLevelReplayArmGraceSeconds &&
                             config.elapsedTime - state.remoteAbsentSince >= RemoteLeaveWhileInLevelDebounceSeconds)
                    {
                        BotRelayAgentDiagnostics.LogRemoteLeftLevel(state.userID);
                        __TransitionTo(index, ref farm, BotState.Leaving, config.elapsedTime);
                    }
                }
                else
                {
                    // Disconnected but still in-stage: hold during the shared reconnect grace.
                    state.remoteAbsentSince = -1.0;
                }
            }

            if (present)
                BotRelayInboundProbe.RecordRemotePresence(1);

            farm.prevRemoteChannelStatus[index] = session.remoteChannelStatus;
            return;
        }

        // Any other state (Idle/PendingInvite/Matching/Leaving): just track presence, take no action.
        farm.prevRemoteChannelStatus[index] = session.remoteChannelStatus;
    }

    private static bool __TickRemoteOfflineTimeout(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        ref BotAgentState state,
        in BotSessionState session)
    {
        bool tracksSquad = state.state == BotState.InSquad || state.state == BotState.InLevel;
        bool hasRemotePeer = session.remoteUserID != 0 || session.remotePlayerCount > 0;
        if (!tracksSquad || !session.IsInSquad || !hasRemotePeer)
        {
            state.remoteOfflineSince = -1.0;
            return false;
        }

        if (session.remoteOnline)
        {
            state.remoteOfflineSince = -1.0;
            return false;
        }

        if (state.remoteOfflineSince < 0.0)
        {
            state.remoteOfflineSince = config.elapsedTime;
            return false;
        }

        double timeout = config.remoteOfflineLeaveTimeout > 0.0f
            ? config.remoteOfflineLeaveTimeout
            : DefaultRemoteOfflineLeaveTimeoutSeconds;
        if (config.elapsedTime - state.remoteOfflineSince < timeout)
            return false;

        BotRelayAgentDiagnostics.LogRemoteOfflineTimeout(
            state.userID,
            session.remoteUserID,
            timeout,
            (int)state.state,
            session.remoteChannelStatus);
        __TransitionTo(index, ref farm, BotState.Leaving, config.elapsedTime);
        return true;
    }

    // Diagnostic-only (option A). When the bot is connected but a should-be-transient step toward the
    // level stalls (invite seen but never joins, or joined squad but never enters), emit a one-shot
    // snapshot of exactly where it is stuck. Turns the intermittent "邀请后没反应" into a precise line
    // instead of guesswork. Fires at most once per state entry and only on the rare stall, so it adds
    // no per-frame latency and won't mask the timing (unlike VerboseTelemetry).
    private static void __TickJoinWatchdog(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        if (state.joinStallLogged)
            return;

        if ((farm.transport[index].flags & BotRelayTransportFlags.Connected) == 0)
            return;

        if (state.state != BotState.PendingInvite &&
            state.state != BotState.Matching &&
            state.state != BotState.InSquad)
            return;

        if (config.elapsedTime - state.stateEnterTime < JoinStallWatchdogSeconds)
            return;

        ref readonly var session = ref farm.sessions.ElementAt(index);
        BotRelayAgentDiagnostics.LogJoinStall(
            state.userID,
            (int)state.state,
            state.HasPendingInvite,
            state.pendingInvite.squadInviteID,
            (int)farm.agentFlags[index],
            session.channel,
            session.remotePlayerCount,
            session.remoteChannelStatus,
            session.remoteOnline,
            session.matchID,
            farm.levelStartMessages[index].userStageID,
            BotRelayInboundProbe.InboundDrops,
            BotRelayInboundProbe.EventDrops);
        state.joinStallLogged = true;
    }

    private static void __OnSquadInvite(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientHeader header,
        in ClientMessageSquadInviteToRead invite)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        int sessionChannel = session.channel;
        uint userID = state.userID;
        uint inviteSquadID = invite.squadInviteID;

        // Give a newly delivered invitation a fair chance at the exact timeout boundary. Without
        // this pre-check, the old silent Join would still look Pending while events are drained and
        // the fresh Invite would be discarded before the state tick released the stale lease.
        if (__IsPendingSquadJoinExpired(index, ref farm, config.elapsedTime))
        {
            __ExpirePendingSquadJoin(index, ref farm, in config);
            sessionChannel = session.channel;
        }

        // A player may invite a bot through world broadcast (Public) or by its concrete userID
        // (Private). Both represent an external squad offer. A Channel/Squad relay is internal
        // team traffic, not a fresh invitation for an idle bot.
        if (invite.channel != ClientChannel.Public && invite.channel != ClientChannel.Private)
        {
            BotRelayAgentDiagnostics.LogInviteIgnored(
                userID, "squad-channel", (int)state.state, inviteSquadID, sessionChannel);
            return;
        }

        // Recordings are the bot's playable behaviour, not optional cosmetics. Refuse the offer
        // before claiming the farm squad lease when the production catalog is empty; otherwise a
        // player can successfully form a team and only discover a motionless bot after entering.
        if (config.replayCatalogMissing != 0)
        {
            BotRelayAgentDiagnostics.LogInviteIgnored(
                userID, "no-replay-catalog", (int)state.state, inviteSquadID, sessionChannel);
            return;
        }

        // Busy commit (design §3.1 single squad/level): state is the primary authority. Session and
        // replay guards cover one-frame state lag and replay-drained bots that are still in-game.
        if (state.state == BotState.InLevel ||
            state.state == BotState.Leaving ||
            state.state == BotState.InSquad ||
            session.IsInSquad ||
            session.channelStatus != 0 ||
            session.remoteChannelStatus != 0 ||
            farm.levelStartMessages[index].userStageID != 0 ||
            (farm.replayRuntime[index].flags & BotReplayRuntimeFlags.Playing) != 0)
        {
            BotRelayAgentDiagnostics.LogInviteIgnored(
                userID, "busy", (int)state.state, inviteSquadID, sessionChannel);
            return;
        }

        if (state.state == BotState.PendingInvite)
        {
            BotRelayAgentDiagnostics.LogInviteIgnored(
                userID, "pending-invite", (int)state.state, inviteSquadID, sessionChannel);
            return;
        }

        // A world Invite reaches every bot. Atomically pick one representative for this squad
        // before it leaves Matching / starts Join prep, so a broadcast never makes the farm join
        // the same squad several times. A different squad may select another available Bot.
        // JoinFail releases this lease; a later Invite event (even same channel) remains eligible.
        if (!BotMatchGuard.TryClaimSquadSlot(ref farm, index, inviteSquadID))
        {
            BotRelayAgentDiagnostics.LogInviteIgnored(
                userID, "farm-squad-claimed", (int)state.state, inviteSquadID, sessionChannel);
            return;
        }

        if (state.state == BotState.Matching)
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);

        farm.agentFlags[index] &= ~BotAgentRuntimeFlags.JoinDispatched;

        __ClearLevelStartGeneration(index, ref farm);
        state.pendingInviteHostUserID = header.userID;
        farm.nextIdleMatchTime[index] = double.PositiveInfinity;

        state.pendingInvite = invite;

        var random = Unity.Mathematics.Random.CreateFromIndex(config.frameSeed ^ (uint)index ^ state.userID);
        var delay = __RandomInviteDelay(in config, ref random);
        state.inviteJoinTime = config.elapsedTime + delay;
        __InjectSquadJoinPrep(index, ref farm, in config);
        __TransitionTo(index, ref farm, BotState.PendingInvite, config.elapsedTime);
        BotRelayAgentDiagnostics.LogPendingInvite(state.userID, invite.squadInviteID, delay);
    }

    private static void __OnSquadJoin(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in BotRelayEvent evt)
    {
        var join = evt.squadJoin;
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        bool supportedChannelMessage =
            evt.channelMessageType == NetworkRelayMessageType.Create ||
            evt.channelMessageType == NetworkRelayMessageType.Join;
        bool validRemoteSnapshot =
            !evt.channelHasRemotePayload ||
            (evt.channelHasRemoteHeader &&
             evt.channelRemoteUserID != 0 &&
             evt.channelRemoteHeader.userID == evt.channelRemoteUserID);
        bool fromSquadInvite =
            state.state == BotState.PendingInvite &&
            state.pendingInvite.squadInviteID == join.squadInviteID &&
            (farm.agentFlags[index] & BotAgentRuntimeFlags.JoinDispatched) != 0 &&
            evt.channelMessageType == NetworkRelayMessageType.Join &&
            !evt.channelHasRemotePayload;
        // Solo matchmaking may deliver temp-squad Create/Join before MatchToRead. Design §3.2:
        // channel membership may commit first with matchID still 0; only MatchToRead binds the
        // ranked generation. Requiring matchID!=0 here caused Leave teardown of a live pairing.
        bool fromMatchingChannel = state.state == BotState.Matching;
        bool duplicateCurrentSquad =
            (state.state == BotState.InSquad || state.state == BotState.InLevel) &&
            session.IsInSquad &&
            session.channel == (int)join.squadInviteID;

        // Join is consumed only by the generation that is already active. PendingInvite requires
        // the exact dispatched self-Join acknowledgement; Matching may accept temp-squad
        // Create/Join before MatchToRead; Idle/Leaving reject every Join.
        if (!supportedChannelMessage ||
            !validRemoteSnapshot ||
            (!fromSquadInvite && !fromMatchingChannel && !duplicateCurrentSquad))
        {
            BotRelayAgentDiagnostics.LogUnexpectedSquadJoinRejected(
                state.userID,
                join.squadInviteID,
                (int)state.state,
                state.HasPendingInvite ? state.pendingInvite.squadInviteID : 0);

            // A foreign generation must not tear down the current committed squad/level. For states
            // that have no accepted membership, fail closed both remotely and locally.
            if (state.state == BotState.InSquad || state.state == BotState.InLevel)
                return;

            __InjectUnexpectedSquadJoinCleanup(index, ref farm, in config);
            session.ResetChannel();

            if (state.state == BotState.PendingInvite)
            {
                // Keep the current, different invitation, but do not send its Join in this same
                // cleanup tick.
                __TransitionTo(index, ref farm, BotState.PendingInvite, config.elapsedTime);
            }
            else if (state.state == BotState.Idle)
            {
                farm.nextIdleMatchTime[index] = double.PositiveInfinity;
            }

            return;
        }

        if (duplicateCurrentSquad)
        {
            // Host LoginManager re-broadcasts Join every frame while waiting for stable presence.
            // Refresh only the side represented by this snapshot; never recreate membership.
            __RefreshCurrentChannelSnapshot(ref session, in evt);
            return;
        }

        if (!BotMatchGuard.TryClaimSquadSlot(ref farm, index, join.squadInviteID))
        {
            // A stale / concurrent server Join may still arrive after another bot committed.
            // Do not let this bot coexist in that squad; Leaving delivers server-side cleanup.
            __TransitionTo(index, ref farm, BotState.Leaving, config.elapsedTime);
            return;
        }

        uint invitedHostUserID = fromSquadInvite ? state.pendingInviteHostUserID : 0;
        int matchID = session.matchID;
        session.ResetChannel();
        session.matchID = matchID;
        session.channel = (int)join.squadInviteID;
        session.isHost = fromSquadInvite
            ? false
            : evt.channelHasRemotePayload
                ? evt.channelMessageType == NetworkRelayMessageType.Join
                : evt.channelMessageType == NetworkRelayMessageType.Create;
        session.channelStatus = evt.channelHasRemotePayload
            ? 0
            : BotRelayMessageUtility.DecodeChannelStatus((int)join.playerStatus.flag);

        if (evt.channelHasRemotePayload)
        {
            __CommitRemoteChannelSnapshot(ref session, in evt, ignoreZeroStageRefresh: false);
        }
        else if (invitedHostUserID != 0)
        {
            // A self Join acknowledgement carries no peer payload. The exact accepted Invite is the
            // peer authority for an ordinary squad generation.
            session.remoteUserID = invitedHostUserID;
            session.remotePlayerCount = 1;
            session.remoteOnline = true;
        }

        __ClearCommittedLevelStart(index, ref farm);
        BotRelayInboundProbe.RecordLevelStartCleared(2);
        farm.agentFlags[index] &= ~BotAgentRuntimeFlags.JoinDispatched;

        __TransitionTo(index, ref farm, BotState.InSquad, config.elapsedTime);
        // Create/Join only proves channel membership. It is not ranked-match success: for a
        // pre-made squad the exact same channel traffic exists before the captain asks ApplyMatch.
        // Only __OnMatch (ClientMessageMatchToRead) may set session.matchID; inferring it here lets a
        // squad-invited bot arm replay before the captain has sent MatchToSend / received Match.

        BotRelayAgentDiagnostics.LogJoinedSquad(state.userID, join.squadInviteID);

        if (__RejectBotVsBotIfNeeded(index, ref farm, in config, config.elapsedTime))
            return;
    }

    private static void __RefreshCurrentChannelSnapshot(
        ref BotSessionState session,
        in BotRelayEvent evt)
    {
        if (evt.channelHasRemotePayload)
        {
            __CommitRemoteChannelSnapshot(ref session, in evt, ignoreZeroStageRefresh: true);
            return;
        }

        session.isHost = evt.channelMessageType == NetworkRelayMessageType.Create;
        session.channelStatus =
            BotRelayMessageUtility.DecodeChannelStatus((int)evt.squadJoin.playerStatus.flag);
    }

    private static void __CommitRemoteChannelSnapshot(
        ref BotSessionState session,
        in BotRelayEvent evt,
        bool ignoreZeroStageRefresh)
    {
        session.remoteUserID = evt.channelRemoteUserID;
        session.remotePlayerCount = 1;

        int remoteStage =
            BotRelayMessageUtility.DecodeChannelStatus((int)evt.squadJoin.playerStatus.flag);
        if (!ignoreZeroStageRefresh || remoteStage != 0)
            session.remoteChannelStatus = remoteStage;

        session.remoteOnline = evt.squadJoin.playerStatus.isOnline;
    }

    private static void __OnRemotePlayerProperty(
        int index,
        ref BotRelayFarmNative farm,
        in ClientHeader header)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        if ((state.state != BotState.InSquad && state.state != BotState.InLevel) ||
            !session.IsInSquad ||
            header.userID == 0 ||
            session.remoteUserID == 0 ||
            header.userID != session.remoteUserID)
            return;

        session.remoteOnline = true;
        BotRelayAgentDiagnostics.LogRemotePlayerProperty(state.userID, header.userID);
    }

    private static void __OnSquadLeave(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        bool wasDropped,
        bool hasRemotePayload)
    {
        ref var state = ref farm.agentStates.ElementAt(index);

        // Sticky InLevel only against peer/channel Leave flaps. While the human host waits for
        // stable JOINed presence it re-broadcasts Create/Join/Leave with a remote payload every
        // frame (LoginManager.__Start); honouring those would Stop replay and flood -5.
        // Server Drop() maps Creator → Leave and non-Creator → Drop. The Creator self header is
        // bodyless (SendHeader to self). Matchmaking bots often Create first, so ignoring that
        // bodyless Leave left them InLevel forever after the human had already left.
        if (state.state == BotState.InLevel && !wasDropped && hasRemotePayload)
        {
            return;
        }

        // InSquad / mid-handshake Idle: host re-broadcast Leave must not clear an armed start.
        if (!wasDropped &&
            (farm.agentFlags[index] &
             (BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart)) != 0 &&
            (state.state == BotState.InSquad || state.state == BotState.Idle))
        {
            return;
        }

        ref var session = ref farm.sessions.ElementAt(index);

        // Already flattened after a prior Leave; ignore repeated host re-broadcast Leave frames.
        if (state.state == BotState.Idle && session.IsIdleForMatch)
        {
            return;
        }

        uint userID = state.userID;
        if (!session.IsInSquad && state.state == BotState.Idle)
        {
            BotRelayAgentDiagnostics.LogSquadLeaveWhileIdle(
                userID, (int)state.state, session.channel);
        }

        if (state.state == BotState.Matching)
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);

        // Server canMatch requires channelStatus==0 (NetworkRelayServerIdentity.canMatch). Squad
        // activity leaves non-zero status bits even after Drop; without Status(0) inject the bot's
        // next MatchToSend is rejected server-side. Defer rematch one tick so inject flattens first.
        __InjectRelayMessage(in config, userID, NetworkRelayMessageType.Mismatch, 0, false);
        __InjectRelayMessage(in config, userID, NetworkRelayMessageType.Status, 0, true);
        farm.relayServerStatusInjected[index] = 1;

        BotRelayInboundProbe.RecordLevelStartCleared(3);
        __ClearLevelStartGeneration(index, ref farm);
        session.ResetChannel();
        __TransitionTo(index, ref farm, BotState.Idle, config.elapsedTime);
        farm.nextIdleMatchTime[index] = double.PositiveInfinity;
        BotRelayAgentDiagnostics.LogLeftSquadReadyForRematch(userID);
    }

    private static void __OnApplyMatch(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientHeader header)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref readonly var session = ref farm.sessions.ElementAt(index);
        if (state.state != BotState.InSquad ||
            session.matchID != 0 ||
            !session.IsInSquad ||
            header.userID == 0 ||
            session.remoteUserID == 0 ||
            header.userID != session.remoteUserID)
            return;

        // Pre-made-team consent only. A solo-matched temporary squad has no ApplyMatch phase,
        // and a late ApplyMatch after MatchToRead must not restart that phase. Do not arm level
        // entry here; the captain sends MatchToSend after collecting this reply, and the common
        // ClientMessageMatchToRead event is authoritative for both flows.
        if (config.injectEnabled == 0)
            return;

        var injectWriter = config.injectWriter;
        BotRelayInjectOps.InjectApplyMatch(ref injectWriter, state.userID);
        BotRelayAgentDiagnostics.LogSentApplyMatch(state.userID);
    }

    private static void __OnMatch(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientMessageMatchToRead match)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        if (match.matchID == 0 ||
            (state.state != BotState.Matching && state.state != BotState.InSquad))
        {
            return;
        }

        uint userID = state.userID;
        ref var session = ref farm.sessions.ElementAt(index);
        if (session.matchID != 0)
        {
            // A Match result is immutable for the accepted generation. Reliable duplicates are
            // idempotent; a different non-zero ID is stale/foreign and must not replace either
            // the binding or an already accepted level descriptor.
            return;
        }

        __ClearCommittedLevelStart(index, ref farm);
        session.matchID = match.matchID;
        // MatchToRead means this Bot is no longer in the public matching pool, even if the
        // temporary squad Join is delivered on a later tick. Release the farm-wide lease now so
        // another idle Bot may start matching without ever being paired against this generation.
        if (state.state == BotState.Matching)
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);

        // A pre-made squad can carry the previous lobby/level Stage in its channel flag. Match is a
        // new level-entry generation: discard that diagnostic snapshot and wait for the human's
        // ApplyStart loop to publish the sole authoritative Stage/level/scene tuple.
        ref var matchSession = ref farm.sessions.ElementAt(index);
        matchSession.remoteChannelStatus = 0;
        farm.prevRemoteChannelStatus[index] = 0;

        BotRelayAgentDiagnostics.LogMatchSuccess(userID, match.matchID, match.level);
        if (__RejectBotVsBotIfNeeded(index, ref farm, in config, config.elapsedTime))
            return;

        // A temporary squad becomes real only when its Create/Join event is accepted. Match records
        // the generation ID but never promotes state from a pre-populated session snapshot.
    }

    private static void __OnMatchStart(
        int index,
        ref BotRelayFarmNative farm,
        in ClientHeader header,
        in ClientMessageMatchStart matchStart)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);

        if (matchStart.userStageID == 0 ||
            matchStart.levelID == 0 ||
            matchStart.stage < 0 ||
            matchStart.sceneName.IsEmpty)
        {
            return;
        }

        // MatchStart is deliberately not reorder-buffered. Join/Match must already have committed
        // the current squad generation, and only its canonical human peer may authorize entry.
        if (state.state != BotState.InSquad ||
            !session.IsInSquad ||
            header.userID == 0 ||
            session.remoteUserID == 0 ||
            header.userID != session.remoteUserID)
            return;

        if (matchStart.matchID == 0)
        {
            if (session.isHost || session.matchID != 0)
                return;
        }
        else
        {
            if (session.matchID <= 0 ||
                matchStart.matchID != session.matchID)
                return;
        }

        // An accepted descriptor is immutable for its generation. Reliable duplicates are no-ops;
        // a conflicting tuple must not move the Bot to a different Stage midway through entry.
        if (farm.levelStartMessages[index].userStageID != 0)
            return;

        farm.levelStartMessages[index] = matchStart;
        state.levelLoginPhase = BotLevelLoginPhase.None;
        BotRelayAgentDiagnostics.LogMatchStartAccepted(
            state.userID,
            matchStart.matchID,
            matchStart.userStageID,
            matchStart.levelID,
            matchStart.stage,
            matchStart.sceneName);
    }

    private static void __OnMismatch(
        int index,
        ref BotRelayFarmNative farm,
        in ClientHeader header,
        in ClientMessageMatchToRead mismatch,
        double elapsedTime)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        if (mismatch.matchID <= 0)
            return;

        // While actively searching, only the Server's identity response may cancel this Bot's
        // single current request. No pending generation is cached before MatchToRead.
        if (state.state == BotState.Matching)
        {
            if (header.userID != state.userID)
                return;

            __ClearLevelStartGeneration(index, ref farm);
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);
            __TransitionTo(index, ref farm, BotState.Idle, elapsedTime);
            farm.nextIdleMatchTime[index] = double.PositiveInfinity;
            return;
        }

        // After MatchToRead, a cancellation is authoritative only for that immutable generation
        // and only when attributed to this identity or its committed human peer. Stale Mismatch
        // frames cannot clear a newer MatchStart descriptor or interrupt an active level.
        if (state.state != BotState.InSquad ||
            session.matchID <= 0 ||
            mismatch.matchID != session.matchID ||
            (header.userID != state.userID &&
             header.userID != session.remoteUserID))
        {
            return;
        }

        __ClearLevelStartGeneration(index, ref farm);
    }

    // After squad kick/leave, wait for Mismatch+Status(0) inject to flatten before re-entering pool.
    private const double PostSquadLeaveRematchDelaySeconds = 1.0;

    // Once Join has been injected, the Server normally answers in the next few ticks. Silence is
    // not success: after this deadline cancel the one invitation, release its farm lease, and stay
    // eligible for a newly delivered Invite. The old Join itself is never retried.
    public const double SquadJoinResponseTimeoutSeconds = 10.0;

    // Join-stall watchdog (diagnostic only): how long a connected bot may sit in a should-be-transient
    // step (PendingInvite / Matching / InSquad) before we log a one-shot stall snapshot.
    private const double JoinStallWatchdogSeconds = 12.0;

    // Grace after entering InLevel before the replay-finished exit may fire. Begin() (which arms
    // Playing) is scheduled in the load job that runs after the agent-logic system, so Playing is
    // not observable on the entry tick; without this window the bot would leave immediately.
    private const double InLevelReplayArmGraceSeconds = 3.0;

    // How long remoteChannelStatus must stay 0 (the genuine stage-leave signal) before the bot ends
    // the level. Debounces the host's per-frame Create/Join/Leave re-broadcast flap so one spurious 0
    // frame doesn't evict the bot. Streaming is already paused the instant the peer stops consuming
    // (RemoteAbsentSuppressed), so this only delays the formal SquadLeave, not the -5 mitigation.
    // Offline duration is tracked separately and uniformly in __TickRemoteOfflineTimeout.
    private const double RemoteLeaveWhileInLevelDebounceSeconds = 2.0;

    /// <summary>
    /// Descriptor-driven level entry for both ordinary squads and matchmaking. MatchStart is the
    /// only Bot level-entry authority; Play, ChapterStage, Status and Query never arm this path.
    /// LoginManager.__Start publishes <see cref="ClientMessageMatchStart"/> only after the human
    /// side has established Waiting. The Bot then uses the same two-tick login handshake in both
    /// modes: PlayerProperty at Status=0, followed by PlayerProperty + non-zero Status + replay.
    /// </summary>
    private static void __TickDescriptorLevelEntry(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref readonly var session = ref farm.sessions.ElementAt(index);

        if (state.state != BotState.InSquad)
            return;

        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.LevelStartHandled) != 0)
            return;

        if (session.remotePlayerCount < 1 && session.remoteUserID == 0)
            return;

        var levelStart = farm.levelStartMessages[index];
        if (levelStart.userStageID == 0 ||
            levelStart.levelID == 0 ||
            levelStart.stage < 0 ||
            levelStart.sceneName.IsEmpty)
            return;

        switch (state.levelLoginPhase)
        {
            case BotLevelLoginPhase.PendingPlayerProperty:
            case BotLevelLoginPhase.PlayerPropertyFailed:
                return;
            case BotLevelLoginPhase.None:
                // Split property and status across scheduler ticks. The Host-side login loop also
                // accepts the same Stage when Status races ahead of PlayerProperty.
                state.levelLoginPhase = BotLevelLoginPhase.PendingPlayerProperty;
                farm.agentFlags[index] |= BotAgentRuntimeFlags.PendingLevelStart;
                BotRelayReplayRuntimeGate.AddPendingLevelStart();
                BotRelayAgentDiagnostics.LogLevelLoginPropertyArmed(
                    state.userID,
                    levelStart.userStageID,
                    session.matchID,
                    levelStart.sceneName);
                return;
            case BotLevelLoginPhase.PlayerPropertySent:
                if (__ArmCommittedLevelStart(index, ref farm))
                {
                    BotRelayAgentDiagnostics.LogLevelEntryArmed(
                        state.userID,
                        levelStart.userStageID,
                        session.matchID,
                        levelStart.sceneName,
                        session.remoteChannelStatus);
                }
                return;
        }
    }

    private static bool __ArmCommittedLevelStart(int index, ref BotRelayFarmNative farm)
    {
        ref readonly var state = ref farm.agentStates.ElementAt(index);
        if (state.state != BotState.InSquad ||
            (farm.agentFlags[index] & BotAgentRuntimeFlags.LevelStartHandled) != 0)
        {
            return false;
        }

        var levelStart = farm.levelStartMessages[index];
        if (levelStart.userStageID == 0 ||
            levelStart.levelID == 0 ||
            levelStart.stage < 0 ||
            levelStart.sceneName.IsEmpty)
        {
            return false;
        }

        farm.agentFlags[index] |=
            BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart;
        BotRelayInboundProbe.RecordLevelStartHandled();
        BotRelayReplayRuntimeGate.AddPendingLevelStart();
        return true;
    }

    private static void __SendSquadJoin(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        double elapsedTime = config.elapsedTime;

        ref var state = ref farm.agentStates.ElementAt(index);
        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.JoinDispatched) != 0)
            return;

        if (config.injectEnabled == 0)
        {
            __ReleasePendingSquadInvite(
                index,
                ref farm,
                elapsedTime,
                PostSquadLeaveRematchDelaySeconds);
            return;
        }

        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Join,
            (int)state.pendingInvite.squadInviteID, true);

        farm.agentFlags[index] |= BotAgentRuntimeFlags.JoinDispatched;
        state.inviteJoinTime = elapsedTime + SquadJoinResponseTimeoutSeconds;

        BotRelayAgentDiagnostics.LogSentSquadJoin(state.userID, state.pendingInvite.squadInviteID);
    }

    private static bool __IsPendingSquadJoinExpired(
        int index,
        ref BotRelayFarmNative farm,
        double elapsedTime)
    {
        ref readonly var state = ref farm.agentStates.ElementAt(index);
        return state.state == BotState.PendingInvite &&
               (farm.agentFlags[index] & BotAgentRuntimeFlags.JoinDispatched) != 0 &&
               elapsedTime >= state.inviteJoinTime;
    }

    private static void __ExpirePendingSquadJoin(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        if (!__IsPendingSquadJoinExpired(index, ref farm, config.elapsedTime))
            return;

        ref var state = ref farm.agentStates.ElementAt(index);
        uint expiredSquadID = state.pendingInvite.squadInviteID;
        BotRelayAgentDiagnostics.LogSquadJoinTimedOut(
            state.userID,
            expiredSquadID,
            SquadJoinResponseTimeoutSeconds);

        // A rejected/invalid Join can be a Server-side no-op with no JoinFailed reply. Ordered
        // app-layer cleanup makes both "not joined" and "joined but no usable acknowledgement"
        // safe before the local lease is freed; this is a semantic timeout, not packet-loss retry.
        __InjectSquadJoinPrep(index, ref farm, in config);
        farm.sessions.ElementAt(index).ResetChannel();

        __ReleasePendingSquadInvite(
            index,
            ref farm,
            config.elapsedTime,
            PostSquadLeaveRematchDelaySeconds);
    }

    private static void __ReleasePendingSquadInvite(
        int index,
        ref BotRelayFarmNative farm,
        double elapsedTime,
        double rematchDelay)
    {
        __ClearLevelStartGeneration(index, ref farm);
        __TransitionTo(index, ref farm, BotState.Idle, elapsedTime);
        // rematchDelay retained for call-site compatibility; reactive matching ignores it.
        farm.nextIdleMatchTime[index] = double.PositiveInfinity;
        _ = rematchDelay;
    }

    private static void __InjectUnexpectedSquadJoinCleanup(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Leave, 0, false);
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Mismatch, 0, false);
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Status, 0, true);
        farm.relayServerStatusInjected[index] = 1;
    }

    /// <summary>
    /// App-layer relay inject: encode [packedInt type][packedInt arg?] exactly as the real
    /// client (see ClientData.cs) and push it into the NetworkRelayServer inject queue so the
    /// server processes it via handler.Read, without any UTP wire / reliable sequence.
    /// </summary>
    private static void __InjectRelayMessage(
        in BotRelayFarmTickConfig config,
        uint userID,
        NetworkRelayMessageType type,
        int arg,
        bool hasArg)
    {
        if (config.injectEnabled == 0)
            return;

        var injectWriter = config.injectWriter;
        BotRelayInjectOps.InjectControlMessage(ref injectWriter, userID, (int)type, arg, hasArg);
    }

    private static unsafe bool __InjectMatchToSend(in BotRelayFarmTickConfig config, uint userID, int level)
    {
        if (config.injectEnabled == 0)
            return false;

        var model = StreamCompressionModel.Default;
        byte* scratch = stackalloc byte[32];
        var writer = new DataStreamWriter(scratch, 32);
        NetworkRelayMatch match;
        match.playerCount = 2;
        match.distance = level;
        match.distanceTime = 3.0f;
        BotRelayMessageUtility.WriteClientMatchToSend(ref writer, in match, in model);

        if (writer.HasFailedWrites)
            return false;

        BotRelayInject inject;
        inject.id = userID;
        inject.packet = default;
        inject.packet.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            inject.packet.SetByte(i, scratch[i]);

        config.injectWriter.Enqueue(inject);
        return true;
    }

    private static void __OnSquadJoinFail(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientMessageSquadJoinToRead joinFail)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        BotRelayAgentDiagnostics.LogSquadJoinFailed(
            state.userID,
            joinFail.squadInviteID,
            farm.sessions[index].matchID);

        if (state.state != BotState.PendingInvite ||
            state.pendingInvite.squadInviteID != joinFail.squadInviteID)
            return;

        // A JoinFailed is definitive for the current invitation (full/dissolved/invalid squad).
        // Clear the pending event and return to Idle: this prevents a retry of this Join while
        // keeping the bot eligible for a later, newly delivered SquadInvite event (even if that
        // invite addresses the same squad channel).
        __ReleasePendingSquadInvite(
            index,
            ref farm,
            config.elapsedTime,
            PostSquadLeaveRematchDelaySeconds);
    }

    /// <summary>
    /// Server <c>NetworkRelayServerIdentity.__CreateOrJoin</c> rejects Join while <c>match != 0</c>.
    /// Always queue Mismatch before SquadJoin so the server match gate is cleared first.
    /// </summary>


    /// <summary>
    /// Ordered app-layer Leave + Mismatch + Status(0) and local match-gate reset.
    /// </summary>
    private static void __InjectSquadJoinPrep(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        ref var session = ref farm.sessions.ElementAt(index);
        session.matchID = 0;
        farm.nextIdleMatchTime[index] = double.PositiveInfinity;

        if (config.injectEnabled == 0)
            return;

        ref var state = ref farm.agentStates.ElementAt(index);
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Leave, 0, false);
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Mismatch, 0, false);

        // The initial boot Status is intentionally one-shot, but it can race the link-ready
        // transition. Reassert Status(0) in the same ordered inject batch as Join preparation so
        // Server has (or refreshes) this bot's identity before it consumes the later Join.
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Status, 0, true);
        farm.relayServerStatusInjected[index] = 1;

    }

    private static void __TryRegisterRelayServerPresence(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        if (farm.relayServerStatusInjected[index] != 0)
            return;

        if ((farm.transport[index].flags & BotRelayTransportFlags.Connected) == 0)
            return;

        ref var state = ref farm.agentStates.ElementAt(index);
        if (config.injectEnabled == 0)
            return;

        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Status, 0, true);
        farm.relayServerStatusInjected[index] = 1;
        BotRelayAgentDiagnostics.LogRelayServerStatusInjected(state.userID);
    }

    private static void __StartMatching(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        if (config.replayCatalogMissing != 0)
            return;

        ref var session = ref farm.sessions.ElementAt(index);
        ref var state = ref farm.agentStates.ElementAt(index);
        if (!session.IsIdleForMatch)
            return;

        if ((farm.transport[index].flags & BotRelayTransportFlags.Connected) == 0)
            return;

        if (!BotMatchGuard.TryClaimMatchingSlot(ref farm, index))
            return;

        __ClearLevelStartGeneration(index, ref farm);
        bool queued = __InjectMatchToSend(in config, state.userID, state.matchLevel);

        if (!queued)
        {
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);
            return;
        }

        __TransitionTo(index, ref farm, BotState.Matching, config.elapsedTime);
        BotRelayAgentDiagnostics.LogSentMatch(state.userID, state.matchLevel);
    }

    private static void __ClearLevelStartGeneration(int index, ref BotRelayFarmNative farm)
    {
        __ClearCommittedLevelStart(index, ref farm);
        farm.sessions.ElementAt(index).matchID = 0;
    }

    private static void __ClearCommittedLevelStart(int index, ref BotRelayFarmNative farm)
    {
        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.PendingLevelStart) != 0)
            BotRelayReplayRuntimeGate.RemovePendingLevelStart();

        farm.levelStartMessages[index] = default;
        farm.agentFlags[index] &= ~(BotAgentRuntimeFlags.LevelStartHandled |
                                    BotAgentRuntimeFlags.PendingLevelStart);

        ref var runtime = ref farm.replayRuntime.ElementAt(index);
        BotReplayLogic.Stop(ref runtime);

        ref var state = ref farm.agentStates.ElementAt(index);
        state.levelLoginPhase = BotLevelLoginPhase.None;
    }

    private static void __TickBotVsBotGuard(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        if (state.state != BotState.Matching && state.state != BotState.InSquad)
            return;

        __RejectBotVsBotIfNeeded(index, ref farm, in config, config.elapsedTime);
    }

    private static bool __RejectBotVsBotIfNeeded(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        double elapsedTime)
    {
        ref var session = ref farm.sessions.ElementAt(index);
        if (!BotMatchGuard.IsFarmBotUser(in farm, session.remoteUserID))
            return false;

        ref var state = ref farm.agentStates.ElementAt(index);
        BotRelayAgentDiagnostics.LogBotVsBotRejected(state.userID, session.remoteUserID);

        if (state.state == BotState.Matching)
            __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Mismatch, 0, false);

        if (session.IsInSquad || state.state == BotState.InSquad)
            __TransitionTo(index, ref farm, BotState.Leaving, elapsedTime);
        else
            __TransitionTo(index, ref farm, BotState.Idle, elapsedTime);

        return true;
    }

    private static bool __IsInSquad(ref BotRelayFarmNative farm, int index) => farm.sessions[index].IsInSquad;

    private static float __RandomInviteDelay(in BotRelayFarmTickConfig config, ref Unity.Mathematics.Random random)
    {
        var min = config.inviteTimeoutMin;
        var max = config.inviteTimeoutMax;
        if (max < min)
            max = min;
        return random.NextFloat(min, max);
    }

    private static float __RandomMatchTimeout(in BotRelayFarmTickConfig config, ref Unity.Mathematics.Random random)
    {
        var min = config.matchTimeoutMin;
        var max = config.matchTimeoutMax;
        if (max < min)
            max = min;
        return random.NextFloat(min, max);
    }

    private static void __TransitionTo(int index, ref BotRelayFarmNative farm, BotState botState, double elapsedTime)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        var previous = state.state;
        if (state.state == BotState.Matching && botState != BotState.Matching)
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);

        if (botState == BotState.Idle)
        {
            BotMatchGuard.ReleaseSquadSlot(ref farm, index);
            state.remoteOfflineSince = -1.0;
        }

        if (previous == BotState.PendingInvite && botState != BotState.PendingInvite)
        {
            state.pendingInvite = default;
            state.pendingInviteHostUserID = 0;
            state.inviteJoinTime = 0.0;
            farm.agentFlags[index] &= ~BotAgentRuntimeFlags.JoinDispatched;
        }

        if (previous == BotState.InLevel && botState != BotState.InLevel)
            BotRelayInboundProbe.RecordInLevelExit((int)botState, (int)((elapsedTime - state.stateEnterTime) * 1000.0));

        state.state = botState;
        state.stateEnterTime = elapsedTime;
        state.joinStallLogged = false;
        if (previous != BotState.InLevel && botState == BotState.InLevel)
            __ResetInLevelPresenceWindow(index, ref farm, ref state);
        else if (botState == BotState.InSquad)
        {
            state.remoteAbsentSince = -1.0;
            state.remotePresenceSeen = false;
            if (previous != BotState.InSquad && previous != BotState.InLevel)
                state.remoteOfflineSince = -1.0;
            farm.agentFlags[index] &= ~BotAgentRuntimeFlags.RemoteAbsentSuppressed;
        }

        __UpdateReplayInLevelGate(previous, botState);
    }

    /// <summary>
    /// Promote to InLevel from the replay load job (after PostTick). Mirrors __TransitionTo's InLevel
    /// entry reset so stale remotePresenceSeen from a prior level cannot suppress streaming on entry.
    /// </summary>
    internal static void ApplyInLevelEntryFromReplayLoad(int index, ref BotRelayFarmNative farm, double elapsedTime)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        var previous = state.state;
        state.state = BotState.InLevel;
        state.stateEnterTime = elapsedTime;
        state.joinStallLogged = false;
        __ResetInLevelPresenceWindow(index, ref farm, ref state);
        __UpdateReplayInLevelGate(previous, BotState.InLevel);
    }

    /// <summary>
    /// A replay descriptor is not a successful level login until an exact recording and its
    /// startup PlayerProperty are both available. Hand the failure to the existing Leaving state
    /// so the next Agent tick reliably sends Leave/Mismatch/Status(0), releases the squad lease,
    /// and returns this Bot to the pool instead of occupying a squad indefinitely.
    /// </summary>
    internal static void ApplyReplayLoadFailure(int index, ref BotRelayFarmNative farm, double elapsedTime)
    {
        ref var runtime = ref farm.replayRuntime.ElementAt(index);
        BotReplayLogic.Stop(ref runtime);

        ref var state = ref farm.agentStates.ElementAt(index);
        if (state.levelLoginPhase != BotLevelLoginPhase.None)
            state.levelLoginPhase = BotLevelLoginPhase.PlayerPropertyFailed;

        __TransitionTo(index, ref farm, BotState.Leaving, elapsedTime);
    }

    static void __ResetInLevelPresenceWindow(int index, ref BotRelayFarmNative farm, ref BotAgentState state)
    {
        state.remoteAbsentSince = -1.0;
        state.remotePresenceSeen = false;
        farm.agentFlags[index] &= ~BotAgentRuntimeFlags.RemoteAbsentSuppressed;
    }

    static void __UpdateReplayInLevelGate(BotState previous, BotState next)
    {
        if (previous == BotState.InLevel && next != BotState.InLevel)
            BotRelayReplayRuntimeGate.RemoveInLevel();
        else if (previous != BotState.InLevel && next == BotState.InLevel)
            BotRelayReplayRuntimeGate.AddInLevel();
    }
}
