using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using ZG;

public struct BotRelayFarmTickConfig
{
    public float inviteTimeoutMin;
    public float inviteTimeoutMax;
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

        __TryCommitDeferredPlayAfterEvents(index, ref farm, in config);

        __TickState(index, ref farm, in config);
        __TickMatchChannelQuery(index, ref farm, in config);
        __TickMatchLevelEntry(index, ref farm, in config);
        __TickChannelTracker(index, ref farm, in config);
        __TickBotVsBotGuard(index, ref farm, in config);
        __TickJoinWatchdog(index, ref farm, in config);

        BotRelayInboundProbe.RecordBotState((int)farm.agentStates[index].state);
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
            case BotRelayEventType.Connected:
                BotRelayAgentDiagnostics.LogConnected(state.userID);
                break;
            case BotRelayEventType.SquadInvite:
                __OnSquadInvite(index, ref farm, in config, in evt.squadInvite);
                break;
            case BotRelayEventType.SquadJoin:
                __OnSquadJoin(index, ref farm, in config, in evt.squadJoin);
                break;
            case BotRelayEventType.SquadJoinFail:
                __OnSquadJoinFail(index, ref farm, in config, in evt.squadJoin);
                break;
            case BotRelayEventType.SquadLeave:
                __OnSquadLeave(index, ref farm, in config, wasDropped: false);
                break;
            case BotRelayEventType.SquadDrop:
                __OnSquadLeave(index, ref farm, in config, wasDropped: true);
                break;
            case BotRelayEventType.ApplyMatch:
                __OnApplyMatch(index, ref farm, in config);
                break;
            case BotRelayEventType.ApplyMatchFail:
            case BotRelayEventType.RejectMatch:
                BotRelayAgentDiagnostics.LogMatchRejected(state.userID);
                break;
            case BotRelayEventType.Match:
                __OnMatch(index, ref farm, in config, in evt.match);
                break;
            case BotRelayEventType.Mismatch:
                __OnMismatch(index, ref farm, config.elapsedTime);
                break;
            case BotRelayEventType.ChapterStage:
            {
                uint previousChapterStage = farm.lastSeenChapterStage[index];
                if (evt.chapterStageID != previousChapterStage)
                {
                    // Host may rebroadcast ChapterStage(userStageID=0) during the start coroutine;
                    // keep the last non-zero live stage until the session itself is cleared.
                    if (evt.chapterStageID != 0 || previousChapterStage == 0)
                    {
                        farm.lastSeenChapterStage[index] = evt.chapterStageID;
                        ref var session = ref farm.sessions.ElementAt(index);
                        bool keepMatchStartStage =
                            state.matchPaired &&
                            (farm.agentFlags[index] & BotAgentRuntimeFlags.MatchStartReceived) != 0 &&
                            !state.awaitingFreshMatchStage &&
                            state.targetUserStageID != 0;

                        if (!keepMatchStartStage)
                        {
                            state.targetUserStageID = evt.chapterStageID;
                        }
                        else if (evt.chapterStageID != 0 &&
                                 evt.chapterStageID != state.targetUserStageID)
                        {
                            BotRelayAgentDiagnostics.LogMatchChapterStageMismatch(
                                state.userID,
                                state.targetUserStageID,
                                evt.chapterStageID);
                        }

                        if (evt.chapterStageID != 0)
                        {
                            session.remoteChannelStatus = (int)evt.chapterStageID;
                            if (state.matchPaired)
                                state.awaitingFreshMatchStage = false;
                        }
                    }
                }

                uint effectiveStage = evt.chapterStageID != 0
                    ? evt.chapterStageID
                    : farm.lastSeenChapterStage[index];

                bool playHandled = (farm.agentFlags[index] & BotAgentRuntimeFlags.PlayHandled) != 0;

                // ChapterStage is also broadcast while a pre-made team is still in the ranked
                // lobby. It may supply the future stage, but only a previously observed Host Play
                // authorizes publishing Status and arming the ordinary squad replay.
                if (effectiveStage != 0 &&
                    !state.matchPaired &&
                    state.state == BotState.InSquad &&
                    state.squadHostPlaySeen &&
                    !playHandled)
                {
                    ref var farmRef = ref farm;
                    var tickConfig = config;
                    var pendingPlay = farm.pendingPlayMessage[index];
                    __TryCommitPendingPlayForLevelEntry(
                        index,
                        ref farmRef,
                        in tickConfig,
                        in pendingPlay);
                }

                break;
            }
            case BotRelayEventType.MatchStart:
                __OnMatchStart(index, ref farm, in evt.matchStart);
                break;
            case BotRelayEventType.Play:
            {
                // Matching clients now publish ClientMessageMatchStart from ApplyStart. A Play
                // received during match is either a legacy peer or a delayed lobby packet and must
                // not replace the authoritative Stage/level/scene tuple.
                if (state.matchPaired)
                    break;

                bool playHandled = (farm.agentFlags[index] & BotAgentRuntimeFlags.PlayHandled) != 0;
                bool retryAfterStageResolve = playHandled &&
                                              state.targetUserStageID == 0 &&
                                              farm.lastSeenChapterStage[index] != 0;
                if (!playHandled || retryAfterStageResolve)
                {
                    __OnPlay(index, ref farm, in config, in evt.play);
                }

                break;
            }
            case BotRelayEventType.RemotePlayerProperty:
                __OnRemotePlayerProperty(index, ref farm, in config, in evt.header);
                break;
        }
    }

    private static void __TickState(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);

        switch (state.state)
        {
            case BotState.Idle:
                if (__IsInSquad(ref farm, index))
                {
                    __TransitionTo(index, ref farm, BotState.InSquad, config.elapsedTime);
                    break;
                }

                // Spawn defers idle MatchToSend (nextIdleMatchTime=∞) so Join is not rejected while
                // server match!=0. Re-arm once relay Status is queued so boot bots enter the human pool.
                if (double.IsPositiveInfinity(farm.nextIdleMatchTime[index]) &&
                    farm.inboxSentInitialStatus[index] != 0)
                {
                    __TryRegisterRelayServerPresence(index, ref farm, in config);
                    farm.nextIdleMatchTime[index] = config.elapsedTime;
                    BotRelayAgentDiagnostics.LogIdleMatchArmed(state.userID);
                }

                if (config.elapsedTime >= farm.nextIdleMatchTime[index])
                {
                    __StartMatching(index, ref farm, in config);
                    farm.nextIdleMatchTime[index] = config.elapsedTime + 5.0;
                }
                break;

            case BotState.PendingInvite:
                if (config.elapsedTime >= state.inviteJoinTime)
                    __SendSquadJoin(index, ref farm, in config);
                break;

            case BotState.Matching:
                if (__IsInSquad(ref farm, index))
                    __TransitionTo(index, ref farm, BotState.InSquad, config.elapsedTime);
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
                if (config.injectEnabled == 0)
                {
                    BotRelaySlotOps.TryWriteSquadLeave(ref farm, index);
                    BotRelaySlotOps.TryWriteStatus(ref farm, index, 0);
                }
                // The outbound UTP wire is the unreliable, drop-prone path (the bot emits no
                // link-layer ACKs — the reason app-layer inject was introduced for Join/Status).
                // Sending the squad-leave only on that wire meant it never reached the server, so the
                // bot stayed in the squad after level completion / host offline ("不会离队"). Mirror
                // the Join handshake and deliver Leave + Status(0) over the reliable inject so the
                // server actually drops us from the channel: the host sees the bot leave and our slot
                // returns to the match pool.
                __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Leave, 0, false);
                __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Status, 0, true);
                ref var leavingSession = ref farm.sessions.ElementAt(index);
                leavingSession.ResetChannel();
                BotRelayInboundProbe.RecordPlayCleared(1);
                farm.agentFlags[index] &= ~(BotAgentRuntimeFlags.PlayHandled |
                                            BotAgentRuntimeFlags.PendingPlay |
                                            BotAgentRuntimeFlags.MatchStartReceived);
                BotRelayPlayStageOps.ClearPlayStageState(index, ref farm);
                state.matchPaired = false;
                state.remotePlayerPropertySeen = false;
                state.matchLoginPhase = BotMatchLoginPhase.None;
                __TransitionTo(index, ref farm, BotState.Idle, config.elapsedTime);
                farm.nextIdleMatchTime[index] = config.elapsedTime + 5.0;
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

        // InSquad lobby: honour a sustained remote stage-leave only when we are not mid-Play handshake.
        // When the host starts a level, remoteChannelStatus often flaps non-zero → 0 while the scene
        // loads; the bot is still InSquad until replay load promotes to InLevel. Treating that flap
        // as leave evicts the bot before it streams (invisible bot) — same class of bug as the InLevel
        // debounce below.
        if (state.state == BotState.InSquad)
        {
            bool playHandshake =
                (farm.agentFlags[index] & (BotAgentRuntimeFlags.PendingPlay | BotAgentRuntimeFlags.PlayHandled)) != 0;
            if (!playHandshake &&
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
            state.hasPendingInvite,
            state.pendingInvite.squadInviteID,
            (int)farm.agentFlags[index],
            session.channel,
            session.remotePlayerCount,
            session.remoteChannelStatus,
            session.remoteOnline,
            state.matchPaired,
            state.targetUserStageID,
            BotRelayInboundProbe.InboundDrops,
            BotRelayInboundProbe.EventDrops);
        state.joinStallLogged = true;
    }

    private static void __OnSquadInvite(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientMessageSquadInviteToRead invite)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        int sessionChannel = session.channel;
        uint userID = state.userID;
        uint inviteSquadID = invite.squadInviteID;

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
            state.targetUserStageID != 0 ||
            (farm.replayRuntime[index].flags & BotReplayRuntimeFlags.Playing) != 0)
        {
            BotRelayAgentDiagnostics.LogInviteIgnored(
                userID, "busy", (int)state.state, inviteSquadID, sessionChannel);
            return;
        }

        if (state.state == BotState.PendingInvite && state.hasPendingInvite)
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

        farm.agentFlags[index] &= ~(BotAgentRuntimeFlags.JoinDispatched |
                                    BotAgentRuntimeFlags.JoinMismatchQueued | BotAgentRuntimeFlags.JoinMismatchAcked);

        session.matchID = 0;
        farm.nextIdleMatchTime[index] = double.PositiveInfinity;

        state.pendingInvite = invite;
        state.hasPendingInvite = true;
        if (invite.levelID != 0)
        {
            state.playLevelID = invite.levelID;
        }

        if (invite.stage > 0)
        {
            state.playStageIndex = invite.stage;
        }

        var random = Unity.Mathematics.Random.CreateFromIndex(config.frameSeed ^ (uint)index ^ state.userID);
        var delay = __RandomInviteDelay(in config, ref random);
        state.inviteJoinTime = config.elapsedTime + delay;
        __InjectSquadJoinPrep(index, ref farm, in config, markMismatchQueued: true);
        __TransitionTo(index, ref farm, BotState.PendingInvite, config.elapsedTime);
        BotRelayAgentDiagnostics.LogPendingInvite(state.userID, invite.squadInviteID, delay);
    }

    private static void __OnSquadJoin(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientMessageSquadJoinToRead join)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        bool fromSquadInvite = state.hasPendingInvite &&
                               state.pendingInvite.squadInviteID == join.squadInviteID;

        // SlotInbox normally commits the channel before enqueuing this event. Keep the state
        // transition self-contained as well: direct/injected event delivery and one-frame parser
        // lag must not leave InSquad with CHANNEL_NULL, otherwise __TickState drops to Idle and a
        // repeated Join/Play bundle re-arms the lobby handshake every frame.
        if (!session.IsInSquad)
            session.channel = (int)join.squadInviteID;

        if (state.state == BotState.InSquad || state.state == BotState.InLevel)
        {
            // Host LoginManager re-broadcasts Join every frame while waiting for stable presence.
            // hasPendingInvite is already cleared on the first join, so fromSquadInvite is false here;
            // flipping matchPaired would arm idle-match auto-entry on PlayerProperty and break squad invite.
            state.hasPendingInvite = false;
            __RefreshRemoteFromJoin(index, ref farm, in join);
            return;
        }

        if (!BotMatchGuard.TryClaimSquadSlot(ref farm, index, join.squadInviteID))
        {
            // A stale / concurrent server Join may still arrive after another bot committed.
            // Do not let this bot coexist in that squad; Leaving delivers server-side cleanup.
            state.hasPendingInvite = false;
            __TransitionTo(index, ref farm, BotState.Leaving, config.elapsedTime);
            return;
        }

        state.hasPendingInvite = false;
        if (state.state == BotState.Matching || state.state == BotState.PendingInvite)
        {
            BotRelayInboundProbe.RecordPlayCleared(2);
            farm.agentFlags[index] &= ~(BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.JoinDispatched);
        }

        if (fromSquadInvite)
        {
            uint inviteLevelID = state.pendingInvite.levelID;
            int inviteStage = state.pendingInvite.stage > 0
                ? state.pendingInvite.stage
                : state.playStageIndex;
            BotRelayPlayStageOps.ClearPlayStageState(index, ref farm);
            if (inviteLevelID != 0)
            {
                state.playLevelID = inviteLevelID;
            }

            if (inviteStage > 0)
            {
                state.playStageIndex = inviteStage;
            }
        }

        if (!fromSquadInvite)
        {
            BotRelayPlayStageOps.ClearPlayStageState(index, ref farm);
        }

        __TransitionTo(index, ref farm, BotState.InSquad, farm.agentStates[index].stateEnterTime);
        // Create/Join only proves channel membership. It is not ranked-match success: for a
        // pre-made squad the exact same channel traffic exists before the captain asks ApplyMatch.
        // Only __OnMatch (ClientMessageMatchToRead) may set matchPaired; inferring it here lets a
        // squad-invited bot arm replay before the captain has sent MatchToSend / received Match.

        BotRelayAgentDiagnostics.LogJoinedSquad(state.userID, join.squadInviteID);

        __RefreshRemoteFromJoin(index, ref farm, in join);

        if (__RejectBotVsBotIfNeeded(index, ref farm, in config, farm.agentStates[index].stateEnterTime))
            return;
    }

    private static void __RefreshRemoteFromJoin(
        int index,
        ref BotRelayFarmNative farm,
        in ClientMessageSquadJoinToRead join)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        int remoteStage = BotRelayMessageUtility.DecodeChannelStatus((int)join.playerStatus.flag);
        if (state.matchPaired && state.awaitingFreshMatchStage)
        {
            // Match creates a new stage generation. Server may repeat the channel Join bundle with
            // the pre-match channelFlag; keep membership/online data but discard that embedded Stage.
            session.remoteChannelStatus = 0;
        }
        else if (remoteStage != 0)
        {
            session.remoteChannelStatus = remoteStage;
            if (farm.lastSeenChapterStage[index] == 0)
            {
                farm.lastSeenChapterStage[index] = (uint)remoteStage;
                state.targetUserStageID = (uint)remoteStage;
            }
        }

        session.remoteOnline = join.playerStatus.isOnline;
        if (session.remotePlayerCount < 1)
            session.remotePlayerCount = 1;
    }

    private static void __OnRemotePlayerProperty(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientHeader header)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        if (header.userID != 0 && header.userID != state.userID)
        {
            session.remoteUserID = header.userID;
            session.remotePlayerCount = 1;
            session.remoteOnline = true;
        }

        state.remotePlayerPropertySeen = true;
        BotRelayAgentDiagnostics.LogRemotePlayerProperty(state.userID, header.userID);
    }

    private static void __OnSquadLeave(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        bool wasDropped)
    {
        ref var state = ref farm.agentStates.ElementAt(index);

        // Sticky InLevel. While the host waits for our stable JOINed presence it re-broadcasts the
        // channel Create/Join/Leave bundle every frame (LoginManager.__Start). Honouring a Leave
        // here drops us back to Idle, which makes ReplayTickJob Stop the replay; the next honoured
        // Play then reloads and re-drains the recording in a single tick (~200 frames) →
        // NetworkSendMessage -5 flood and an invisible bot (never held in-level). Once committed to
        // the level, ignore squad Leave churn; level exit is driven by the live remote stage
        // returning to zero (__TickChannelTracker), never by replay exhaustion.
        // Drop is an explicit host removal, not the transient Leave rebroadcast that InLevel must
        // debounce. Honour it immediately so the old replay and farm-wide squad lease are cleared
        // before a one-shot replacement Invite from the same network batch is evaluated.
        if (state.state == BotState.InLevel && !wasDropped)
        {
            return;
        }

        // InSquad / mid-handshake Idle: host re-broadcast Leave must not clear PlayHandled or re-inject.
        if (!wasDropped &&
            (farm.agentFlags[index] & (BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay)) != 0 &&
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
        if (config.injectEnabled != 0)
        {
            __InjectRelayMessage(in config, userID, NetworkRelayMessageType.Mismatch, 0, false);
            __InjectRelayMessage(in config, userID, NetworkRelayMessageType.Status, 0, true);
        }
        else
        {
            BotRelaySlotOps.TryWriteMismatch(ref farm, index);
        }

        BotRelayInboundProbe.RecordPlayCleared(3);
        farm.agentFlags[index] &= ~(BotAgentRuntimeFlags.PlayHandled |
                                    BotAgentRuntimeFlags.PendingPlay |
                                    BotAgentRuntimeFlags.MatchStartReceived);
        state.matchPaired = false;
        state.remotePlayerPropertySeen = false;
        state.matchLoginPhase = BotMatchLoginPhase.None;
        BotRelayPlayStageOps.ClearPlayStageState(index, ref farm);
        session.ResetChannel();
        __TransitionTo(index, ref farm, BotState.Idle, config.elapsedTime);
        farm.nextIdleMatchTime[index] = config.elapsedTime + PostSquadLeaveRematchDelaySeconds;
        BotRelayAgentDiagnostics.LogLeftSquadReadyForRematch(userID);
    }

    private static void __OnApplyMatch(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        if (state.state != BotState.InSquad || state.matchPaired)
            return;

        // Pre-made-team consent only. A solo-matched temporary squad has no ApplyMatch phase,
        // and a late ApplyMatch after MatchToRead must not restart that phase. Do not arm level
        // entry here; the captain sends MatchToSend after collecting this reply, and the common
        // ClientMessageMatchToRead event is authoritative for both flows.
        if (config.injectEnabled != 0)
        {
            var injectWriter = config.injectWriter;
            BotRelayInjectOps.InjectApplyMatch(ref injectWriter, state.userID);
        }
        else
        {
            BotRelaySlotOps.TryWriteApplyMatch(ref farm, index);
        }
        BotRelayAgentDiagnostics.LogSentApplyMatch(state.userID);
    }

    private static void __OnMatch(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientMessageMatchToRead match)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        uint userID = state.userID;
        double stateEnterTime = state.stateEnterTime;
        if (match.matchID != 0)
        {
            ref var session = ref farm.sessions.ElementAt(index);
            session.matchID = match.matchID;
        }

        state.matchPaired = true;
        state.awaitingFreshMatchStage = true;
        state.remotePlayerPropertySeen = false;
        state.matchLoginPhase = BotMatchLoginPhase.None;
        farm.agentFlags[index] &= ~(BotAgentRuntimeFlags.PlayHandled |
                                    BotAgentRuntimeFlags.PendingPlay |
                                    BotAgentRuntimeFlags.MatchStartReceived);
        BotRelayPlayStageOps.ClearPlayStageState(index, ref farm);
        // A pre-made squad can carry the previous lobby/level Stage in its channel flag. Match is
        // a new stage generation: discard that snapshot and wait for the human's ApplyStart loop
        // to publish the authoritative Stage/level/scene tuple through MatchStart.
        ref var matchSession = ref farm.sessions.ElementAt(index);
        matchSession.remoteChannelStatus = 0;
        farm.prevRemoteChannelStatus[index] = 0;

        BotRelayAgentDiagnostics.LogMatchSuccess(userID, match.matchID, match.level);
        if (__RejectBotVsBotIfNeeded(index, ref farm, in config, stateEnterTime))
            return;

        if (!__IsInSquad(ref farm, index))
            __TransitionTo(index, ref farm, BotState.InSquad, stateEnterTime);
    }

    private static void __OnMatchStart(
        int index,
        ref BotRelayFarmNative farm,
        in ClientMessageMatchStart matchStart)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref readonly var session = ref farm.sessions.ElementAt(index);

        if (!state.matchPaired ||
            state.state != BotState.InSquad ||
            matchStart.matchID == 0 ||
            matchStart.matchID != session.matchID ||
            matchStart.userStageID == 0 ||
            matchStart.levelID == 0 ||
            matchStart.sceneName.IsEmpty)
        {
            return;
        }

        // ApplyStart owns the authoritative tuple: Register has already translated the client's
        // level/stage pair to the backend UserStage ID, while ApplyStart still has the exact scene.
        // Keep it as a ClientMessagePlay-shaped value so catalog lookup remains shared with squads.
        state.targetUserStageID = matchStart.userStageID;
        state.awaitingFreshMatchStage = false;
        state.playLevelID = matchStart.levelID;
        state.playStageIndex = matchStart.stage;
        farm.pendingPlayMessage[index] = new ClientMessagePlay
        {
            isRestart = matchStart.isRestart,
            levelID = matchStart.levelID,
            stage = matchStart.stage,
            levelName = matchStart.levelName,
            sceneName = matchStart.sceneName
        };
        farm.agentFlags[index] |= BotAgentRuntimeFlags.MatchStartReceived;
    }

    private static void __OnMismatch(int index, ref BotRelayFarmNative farm, double elapsedTime)
    {
        ref var session = ref farm.sessions.ElementAt(index);
        session.matchID = 0;

        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.JoinMismatchQueued) != 0)
            farm.agentFlags[index] |= BotAgentRuntimeFlags.JoinMismatchAcked;

        if (farm.agentStates[index].state == BotState.Matching)
        {
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);
            __TransitionTo(index, ref farm, BotState.Idle, elapsedTime);
            farm.nextIdleMatchTime[index] = elapsedTime + 5.0;
        }
    }

    // Minimum spacing between honoured Play messages. A single level entry only needs one
    // Play; anything faster is the host/server re-broadcast storm, which must not amplify.
    private const double PlayHandleCooldownSeconds = 1.0;

    // After squad kick/leave, wait for Mismatch+Status(0) inject to flatten before re-entering pool.
    private const double PostSquadLeaveRematchDelaySeconds = 1.0;

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

    private static void __OnPlay(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientMessagePlay play)
    {
        ref var state = ref farm.agentStates.ElementAt(index);

        // Play is the authorization boundary for an ordinary squad level. Join and ChapterStage
        // can both exist in a pre-made ranked lobby and must leave the bot's local Status at 0.
        state.squadHostPlaySeen = true;

        // LoginManager.__Start re-broadcasts Play while waiting for remote Status. If the live
        // stage is still unknown, capture Play and let the next ChapterStage finish arming.
        if (!state.matchPaired && farm.lastSeenChapterStage[index] == 0)
        {
            state.playLevelID = play.levelID;
            state.playStageIndex = play.stage;
            farm.pendingPlayMessage[index] = play;
            return;
        }

        ref var farmRef = ref farm;
        var tickConfig = config;
        var playCopy = play;
        __TryCommitPendingPlayForLevelEntry(index, ref farmRef, in tickConfig, in playCopy);
    }

    internal static bool TryCommitPendingPlayForLevelEntry(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientMessagePlay play)
    {
        ref var farmRef = ref farm;
        var tickConfig = config;
        var playCopy = play;
        return __TryCommitPendingPlayForLevelEntry(index, ref farmRef, in tickConfig, in playCopy);
    }

    private static bool __TryCommitPendingPlayForLevelEntry(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        in ClientMessagePlay play)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        if (state.state != BotState.InSquad)
        {
            return false;
        }

        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.PlayHandled) != 0)
        {
            return false;
        }

        if (!state.matchPaired && !state.squadHostPlaySeen)
        {
            return false;
        }

        if (!state.matchPaired && farm.lastSeenChapterStage[index] == 0)
        {
            return false;
        }

        var commitPlay = play;
        if (commitPlay.levelID == 0 && state.playLevelID != 0)
        {
            commitPlay.levelID = state.playLevelID;
            commitPlay.stage = state.playStageIndex;
        }

        // Re-handle cooldown: even if PlayHandled was cleared by a spurious Leave/Join churn,
        // don't rebuild the play/replay/inject state more than once per cooldown window.
        if (config.elapsedTime < state.nextPlayHandleTime)
        {
            return false;
        }

        state.nextPlayHandleTime = config.elapsedTime + PlayHandleCooldownSeconds;

        farm.agentFlags[index] |= BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay;
        farm.pendingPlayMessage[index] = commitPlay;
        state.playLevelID = commitPlay.levelID;
        state.playStageIndex = commitPlay.stage;
        BotRelayInboundProbe.RecordPlayHandled();
        BotRelayReplayRuntimeGate.AddPendingPlay();

        return true;
    }

    private static void __TryCommitDeferredPlayAfterEvents(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        ref readonly var state = ref farm.agentStates.ElementAt(index);
        if (state.matchPaired || state.state != BotState.InSquad)
        {
            return;
        }

        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.PlayHandled) != 0)
        {
            return;
        }

        if (farm.lastSeenChapterStage[index] == 0)
        {
            return;
        }

        var pendingPlay = farm.pendingPlayMessage[index];
        ref var farmRef = ref farm;
        var tickConfig = config;
        __TryCommitPendingPlayForLevelEntry(
            index,
            ref farmRef,
            in tickConfig,
            in pendingPlay);
    }

    /// <summary>
    /// Idle-match fallback: when Status/PlayerProperty UTP wires are misclassified as link ACK on the
    /// Null pipeline, inject Query so the server pushes peer SendHeader(channelFlag) to the bot inbox.
    /// </summary>
    private static void __TickMatchChannelQuery(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref readonly var session = ref farm.sessions.ElementAt(index);

        if (!state.matchPaired || state.state != BotState.InSquad)
            return;

        if (!state.awaitingFreshMatchStage &&
            (session.remoteChannelStatus > 0 ||
             state.targetUserStageID > 0 ||
             state.remotePlayerPropertySeen))
        {
            return;
        }

        if (session.channel < 0)
            return;

        if (config.elapsedTime - state.stateEnterTime < MatchChannelQueryDelaySeconds)
            return;

        if (state.lastChannelQueryTime > 0 &&
            config.elapsedTime - state.lastChannelQueryTime < MatchChannelQueryRetrySeconds)
            return;

        state.lastChannelQueryTime = config.elapsedTime;
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Query, session.channel, true);
        BotRelayAgentDiagnostics.LogMatchChannelQuery(state.userID, session.channel);
    }

    private const double MatchChannelQueryDelaySeconds = 2.0;
    private const double MatchChannelQueryRetrySeconds = 2.0;

    /// <summary>
    /// Match-path level entry (path B, <see cref="BotAgentState.matchPaired"/>).
    /// Humans call <c>LoginManager.Register</c> + <c>ApplyStart</c> themselves when
    /// <c>match!=0</c>. ApplyStart broadcasts <see cref="ClientMessageMatchStart"/>, which contains
    /// the backend UserStage ID and exact level/scene tuple. Both Bot roles use the same two-tick
    /// login handshake: PlayerProperty at Status=0, then non-zero Status + replay. Matching never
    /// depends on a ClientMessagePlay broadcast. Squad invite bots leave
    /// <c>matchPaired=false</c> and continue to wait for Host Play.
    /// </summary>
    private static void __TickMatchLevelEntry(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref readonly var session = ref farm.sessions.ElementAt(index);

        if (!state.matchPaired)
            return;

        if (state.state != BotState.InSquad)
            return;

        if (state.awaitingFreshMatchStage ||
            (farm.agentFlags[index] & BotAgentRuntimeFlags.MatchStartReceived) == 0)
            return;

        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.PlayHandled) != 0)
            return;

        if (session.remotePlayerCount < 1 && session.remoteUserID == 0)
            return;

        if (state.targetUserStageID == 0)
            return;

        var play = farm.pendingPlayMessage[index];
        if (play.levelID == 0 || play.sceneName.IsEmpty)
            return;

        switch (state.matchLoginPhase)
        {
            case BotMatchLoginPhase.PendingPlayerProperty:
            case BotMatchLoginPhase.PlayerPropertyFailed:
                return;
            case BotMatchLoginPhase.None:
                // Split property and status across scheduler ticks. The Host-side login loop also
                // accepts the same Stage when Status races ahead of PlayerProperty.
                state.matchLoginPhase = BotMatchLoginPhase.PendingPlayerProperty;
                farm.agentFlags[index] |= BotAgentRuntimeFlags.PendingPlay;
                BotRelayReplayRuntimeGate.AddPendingPlay();
                BotRelayAgentDiagnostics.LogMatchLoginPropertyArmed(
                    state.userID,
                    state.targetUserStageID,
                    session.matchID,
                    play.sceneName);
                return;
            case BotMatchLoginPhase.PlayerPropertySent:
                if (__TryCommitPendingPlayForLevelEntry(index, ref farm, in config, in play))
                {
                    BotRelayAgentDiagnostics.LogMatchLevelEntryArmed(
                        state.userID,
                        state.targetUserStageID,
                        session.matchID,
                        play.sceneName,
                        session.remoteChannelStatus);
                }
                return;
        }
    }

    private const double JoinMismatchDeferSeconds = 1.5;
    private const double JoinMismatchAckFallbackSeconds = 1.0;

    private static void __SendSquadJoin(int index, ref BotRelayFarmNative farm, in BotRelayFarmTickConfig config)
    {
        double elapsedTime = config.elapsedTime;

        ref var state = ref farm.agentStates.ElementAt(index);
        if (!state.hasPendingInvite)
        {
            __TransitionTo(index, ref farm, BotState.Idle, state.stateEnterTime);
            return;
        }

        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.JoinDispatched) != 0)
            return;

        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.JoinMismatchQueued) == 0)
        {
            farm.agentFlags[index] |= BotAgentRuntimeFlags.JoinMismatchQueued;
            if (state.state == BotState.Matching)
                BotMatchGuard.ReleaseMatchingSlot(ref farm, index);

            __InjectSquadJoinPrep(index, ref farm, in config, markMismatchQueued: false);
            state.inviteJoinTime = elapsedTime + JoinMismatchDeferSeconds;
            return;
        }

        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.JoinMismatchAcked) == 0 &&
            elapsedTime < state.inviteJoinTime + JoinMismatchAckFallbackSeconds)
            return;

        if (config.injectEnabled != 0)
        {
            __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Join,
                (int)state.pendingInvite.squadInviteID, true);
        }
        else if (!BotRelaySlotOps.TryWriteSquadJoin(ref farm, index, state.pendingInvite.squadInviteID))
        {
            return;
        }

        farm.agentFlags[index] |= BotAgentRuntimeFlags.JoinDispatched;
        state.inviteJoinTime = double.MaxValue;

        BotRelayAgentDiagnostics.LogSentSquadJoin(state.userID, state.pendingInvite.squadInviteID);
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
            !state.hasPendingInvite ||
            state.pendingInvite.squadInviteID != joinFail.squadInviteID)
            return;

        // A JoinFailed is definitive for the current invitation (full/dissolved/invalid squad).
        // Clear the pending event and return to Idle: this prevents a retry of this Join while
        // keeping the bot eligible for a later, newly delivered SquadInvite event (even if that
        // invite addresses the same squad channel).
        state.hasPendingInvite = false;
        farm.agentFlags[index] &= ~(BotAgentRuntimeFlags.JoinDispatched |
                                    BotAgentRuntimeFlags.JoinMismatchQueued |
                                    BotAgentRuntimeFlags.JoinMismatchAcked);
        __TransitionTo(index, ref farm, BotState.Idle, config.elapsedTime);
        farm.nextIdleMatchTime[index] = config.elapsedTime + PostSquadLeaveRematchDelaySeconds;
    }

    /// <summary>
    /// Server <c>NetworkRelayServerIdentity.__CreateOrJoin</c> rejects Join while <c>match != 0</c>.
    /// Always queue Mismatch before SquadJoin so the server match gate is cleared first.
    /// </summary>


    /// <summary>
    /// Reliable app-layer Leave + Mismatch (UTP wire alone is drop-prone) and local match gate reset.
    /// </summary>
    private static void __InjectSquadJoinPrep(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config,
        bool markMismatchQueued)
    {
        ref var session = ref farm.sessions.ElementAt(index);
        session.matchID = 0;
        farm.nextIdleMatchTime[index] = double.PositiveInfinity;

        if (config.injectEnabled == 0)
        {
            BotRelaySlotOps.TryWriteSquadLeave(ref farm, index);
            BotRelaySlotOps.TryWriteMismatch(ref farm, index);
            return;
        }

        ref var state = ref farm.agentStates.ElementAt(index);
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Leave, 0, false);
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Mismatch, 0, false);

        // The initial boot Status is intentionally one-shot, but it can race the link-ready
        // transition. Reassert Status(0) in the same ordered inject batch as Join preparation so
        // Server has (or refreshes) this bot's identity before it consumes the later Join.
        __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Status, 0, true);
        farm.relayServerStatusInjected[index] = 1;

        if (markMismatchQueued)
            farm.agentFlags[index] |= BotAgentRuntimeFlags.JoinMismatchQueued;
    }

    private static void __TryRegisterRelayServerPresence(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayFarmTickConfig config)
    {
        if (farm.relayServerStatusInjected[index] != 0)
            return;

        if (farm.inboxSentInitialStatus[index] == 0)
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
        if (!session.IsIdleForMatch || state.hasPendingInvite)
            return;

        if (farm.inboxSentInitialStatus[index] == 0)
            return;

        if (!BotMatchGuard.TryClaimMatchingSlot(ref farm, index))
            return;

        bool queued;
        if (config.injectEnabled != 0)
            queued = __InjectMatchToSend(in config, state.userID, state.matchLevel);
        else
            queued = BotRelaySlotOps.TryWriteMatch(ref farm, index, state.matchLevel);

        if (!queued)
        {
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);
            return;
        }

        __TransitionTo(index, ref farm, BotState.Matching, config.elapsedTime);
        BotRelayAgentDiagnostics.LogSentMatch(state.userID, state.matchLevel);
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
        {
            if (config.injectEnabled != 0)
                __InjectRelayMessage(in config, state.userID, NetworkRelayMessageType.Mismatch, 0, false);
            else
                BotRelaySlotOps.TryWriteMismatch(ref farm, index);
        }

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

    private static void __TransitionTo(int index, ref BotRelayFarmNative farm, BotState botState, double elapsedTime)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        if (state.state == BotState.Matching && botState != BotState.Matching)
            BotMatchGuard.ReleaseMatchingSlot(ref farm, index);

        if (botState == BotState.Idle)
        {
            BotMatchGuard.ReleaseSquadSlot(ref farm, index);
            state.remoteOfflineSince = -1.0;
        }

        var previous = state.state;
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
