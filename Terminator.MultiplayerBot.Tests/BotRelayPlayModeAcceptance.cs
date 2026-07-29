using System.Collections;
using NUnit.Framework;
using ZG;

/// <summary>
/// PlayMode acceptance flows simulating real-player operations on Server.unity (World.Update + PopEvents).
///
/// Verification is state-based on the app-layer inject design: the bot enqueues decoded Join/Mismatch
/// into <see cref="NetworkRelayServer"/>.injects, the InjectJob drains them and calls handler.Read inside
/// the single-driver DOTS schedule. A successful Join (type=6) moves the bot's server-side identity into
/// the host squad channel, which is what these flows assert — not UTP wire byte shape, and not the
/// server's Burst-thread Debug.Log (which does not route through Application.logMessageReceived).
///
/// EditMode Regression tests are fast pre-checks; these flows are the delivery gate before manual Play.
/// See Documentation~/MultiplayerBot/联机机器人-架构约束.md §6.1.
/// </summary>
internal static class BotRelayPlayModeAcceptance
{
    /// <summary>
    /// 真人房主：HostTestClient（ClientData → ReplyServer）建队 → WorldInvite → bot 自动 inject Join →
    /// Server handler.Read(type=6) 接受 → bot 服务器侧 identity 加入房主 squad channel。
    /// </summary>
    public static IEnumerator RunInviteToJoinInjectAcceptance(
        int agentIndex = 0,
        float extraJoinWaitSeconds = 6f)
    {
        var hostScope = BotRelayHostTestClient.BeginScope();
        using var hostDisconnectGuard = BotRelayPlayModeLogAsserts.BeginHostRelayDisconnectScope();
        try
        {
            BotRelayHostTestClient.ResetSessionState();
            yield return __EstablishHostConfirmedServerSide();

            yield return BotRelayHostTestClient.SendWorldSquadInviteAndWaitForBotPending(
                agentIndex,
                timeoutSeconds: 20f,
                routeSettleSeconds: 1f);

            yield return __AssertServerAcceptsInjectedJoin(agentIndex, extraJoinWaitSeconds);
        }
        finally
        {
            hostScope.Dispose();
        }
    }

    /// <summary>
    /// Manual Play path: boot idle Match gate active → invite → bot injects Mismatch (clears match gate)
    /// → bot injects Join → Server accepts (type=6) → bot joins host squad channel server-side.
    /// </summary>
public static IEnumerator RunInviteAfterBootMatchServerJoinAcceptance(
        int agentIndex = 0,
        float extraJoinWaitSeconds = 6f)
    {
        var hostScope = BotRelayHostTestClient.BeginScope();
        using var hostDisconnectGuard = BotRelayPlayModeLogAsserts.BeginHostRelayDisconnectScope();
        try
        {
            BotRelayHostTestClient.ResetSessionState();
            yield return __EstablishHostConfirmedServerSide();

            // Keep the boot-match setup as a regression precondition. The host test client can
            // target a specific connected bot, so the acceptance path must not broadcast a world
            // invite to every virtual-port agent and make the selected recipient nondeterministic.
            BotRelayPlayModeHarness.ScheduleExclusiveIdleMatchNow(agentIndex);
            yield return BotRelayPlayModeHarness.WaitForAgentState(
                agentIndex,
                BotState.Matching,
                timeoutSeconds: 20f);

            yield return BotRelayPlayModeHarness.WaitForRouteSendSettled(
                stableSeconds: 0.5f,
                timeoutSeconds: 10f);

            yield return BotRelayHostTestClient.SendWorldSquadInviteAndWaitForBotPending(
                agentIndex,
                timeoutSeconds: 20f,
                routeSettleSeconds: 0.5f);

            yield return __AssertServerAcceptsInjectedJoin(
                agentIndex,
                extraJoinWaitSeconds);
        }
        finally
        {
            hostScope.Dispose();
        }
    }

    /// <summary>
    /// Boot-match server-join flow plus the bot's local InSquad transition. In solo PlayMode the server
    /// squad-join downlink to the bot is simulated (<see cref="SimulateServerJoinAckToBot"/>); the
    /// server-side acceptance itself is proven by <see cref="__AssertServerAcceptsInjectedJoin"/>.
    /// </summary>
    public static IEnumerator RunInviteAfterBootMatchEndToEndAcceptance(
        int agentIndex = 0,
        float extraJoinWaitSeconds = 6f)
    {
        yield return RunInviteAfterBootMatchServerJoinAcceptance(agentIndex, extraJoinWaitSeconds);

        // A real host socket may already have delivered SquadJoin while the server-acceptance
        // assertion was polling. In that case PendingInvite is correctly cleared and the Bot is
        // already InSquad; only the isolated no-downlink path needs the simulated Relay ack.
        Assert.IsTrue(BotRelayPlayModeHarness.TryGetAgentState(agentIndex, out var stateAfterServerJoin));
        if (stateAfterServerJoin != BotState.InSquad &&
            BotRelayPlayModeHarness.TryGetPendingInviteId(agentIndex, out var squadId))
        {
            SimulateServerJoinAckToBot(agentIndex, squadId);

            for (int i = 0; i < 60; ++i)
                yield return null;
        }

        yield return WaitForInSquad(agentIndex, timeoutSeconds: 20f);
    }

    /// <summary>
    /// Establish the real host client AND require the server to actually register its squad channel.
    /// The real-socket ClientData establishment is flaky across repeated EnterPlayMode sessions (client
    /// echoes channel=1 while the server never registered the host); retry until server truth confirms.
    /// </summary>
    static IEnumerator __EstablishHostConfirmedServerSide(int maxAttempts = 3, float perAttemptSeconds = 8f)
    {
        for (int attempt = 0; attempt < maxAttempts; ++attempt)
        {
            yield return BotRelayHostTestClient.EstablishAsSquadHost();

            float t = 0f;
            while (t < perAttemptSeconds)
            {
                BotRelayHostTestClient.SyncChannelFromServer();
                // The host's server-side identity id is NOT RealPlayerUserId (it connects with that header
                // but Creates with userID=0), so resolve via the client-echoed channel and confirm the
                // server actually registered a member there.
                if (BotRelayHostTestClient.TryGetEstablishedSquadChannel(out int echoChannel) &&
                    echoChannel >= 0 &&
                    BotRelayPlayModeServerProbe.ChannelHasAnyMember(echoChannel))
                    yield break;

                yield return null;
                t += UnityEngine.Time.unscaledDeltaTime;
            }
        }

        Assert.Fail(
            $"HostTestClient: server never registered host squad channel after {maxAttempts} attempts (real-socket flakiness).");
    }

    /// <summary>
    /// Core assertion: after the bot reaches PendingInvite it auto-injects Join; wait for the server to
    /// accept it (type=6) and place the bot identity into the host squad channel, then assert membership.
    /// </summary>
static IEnumerator __AssertServerAcceptsInjectedJoin(int agentIndex, float extraJoinWaitSeconds)
    {
        Assert.IsTrue(
            BotRelayPlayModeHarness.TryGetBotUserId(agentIndex, out var botUserId),
            "Bot user id required for server Join acceptance assert.");

        bool pendingInviteStillVisible =
            BotRelayPlayModeHarness.TryGetPendingInviteId(agentIndex, out var pendingSquadId);

        int invitedChannel;
        if (pendingInviteStillVisible)
        {
            invitedChannel = (int)pendingSquadId;
        }
        else
        {
            Assert.IsTrue(
                BotRelayHostTestClient.TryGetEstablishedSquadChannel(out invitedChannel) &&
                invitedChannel >= 0,
                "Invite channel was no longer pending and HostTestClient has no established fallback channel.");
        }

        Assert.GreaterOrEqual(
            invitedChannel,
            0,
            "Bot Join requires the non-null squad channel carried by its invite.");

        // Before this bot joins, a valid invite channel must already have a member (the inviting
        // player). This keeps the check on Server truth without relying on stale host SharedStatic.
        Assert.IsTrue(
            BotRelayPlayModeServerProbe.ChannelHasAnyMember(invitedChannel),
            "Invite channel must contain the inviting player before the bot Join is accepted.");

        float joinWait = BotRelayPlayModeHarness.GetInviteTimeoutMaxSeconds() + extraJoinWaitSeconds + 20f;
        float elapsed = 0f;
        bool joined = false;
        while (elapsed < joinWait)
        {
            if (BotRelayPlayModeHarness.TryGetBotServerChannel(botUserId, out int botChannel) &&
                botChannel == invitedChannel)
            {
                joined = true;
                break;
            }

            yield return null;
            elapsed += UnityEngine.Time.unscaledDeltaTime;
        }

        if (!joined)
        {
            BotRelayPlayModeHarness.TryGetAgentState(agentIndex, out var botState);
            string observedInvite = pendingInviteStillVisible ? pendingSquadId.ToString() : "<joined-before-observe>";
            Assert.Fail(
                $"Server bot {botUserId} did not join invite squad {invitedChannel} in {joinWait:F1}s. " +
                $"botState={botState} injectQueue={BotRelayPlayModeServerProbe.GetInjectCount()} " +
                $"pendingSquadId={observedInvite} " +
                $"gate=[{BotRelayPlayModeServerProbe.DescribeJoinGate(botUserId, invitedChannel)}] " +
                $"{BotRelayPlayModeServerProbe.DescribeIdentities()}");
        }
    }

    /// <summary>
    /// Solo PlayMode 无外部 host 客户端；模拟 Server 在 Accept Join 后 RouteSend SquadJoin→bot（非 UI 操作，是 Relay 下行）。
    /// </summary>
    public static void SimulateServerJoinAckToBot(int agentIndex, uint squadInviteId) =>
        BotRelayPlayModeHarness.RouteServerJoinConfirmationToBot(agentIndex, squadInviteId);

    public static IEnumerator WaitForInSquad(int agentIndex, float timeoutSeconds = 20f)
    {
        yield return BotRelayPlayModeHarness.WaitForAgentState(
            agentIndex,
            BotState.InSquad,
            timeoutSeconds);
    }
}
