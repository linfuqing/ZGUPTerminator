using System.Collections;

using NUnit.Framework;

using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

using ZG;

/// <summary>
/// PlayMode delivery gate for the bot Invite→Join flow on Server.unity (World.Update → PopEvents + Relay).
///
/// Design: bot→server messages (Join/Mismatch) use app-layer inject (NetworkRelayServer.injects → InjectJob
/// → handler.Read) inside the single driver, so no real socket/port is needed. Verification is state-based
/// (inject queue drain, bot Join-reply counter, local state), not UTP wire shape or Burst-thread logs.
///
/// Core acceptance goal: after a live invite, does the bot actually issue a Join reply? Covered by
/// the JoinDispatched step inside the single-session gate. The genuine real-socket full end-to-end (foreign squad channel join)
/// is unstable across repeated EnterPlayMode sessions and lives in BotRelayPlayModeManualTests
/// (category=PlayModeManual, [Explicit]) for manual verification only.
///
/// EditMode Regression is a fast pre-check. The checks below deliberately run as one UnityTest so
/// Unity 2022.3 does not repeatedly tear down/re-enter Play Mode between cases and leak a stale
/// NetworkRelayServer singleton into the next ECS world.
/// See Documentation~/MultiplayerBot/联机机器人-架构约束.md §6.1.
/// </summary>
[Category("PlayMode")]
[Timeout(120000)]
public class BotRelayPlayModeTests
{
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode();
        yield return BotRelayPlayModeHarness.LoadServerSceneSingle();
        yield return BotRelayPlayModeHarness.WaitForBotFarmReady();
    }

    [TearDown]
    public void TearDown()
    {
        BotRelayHostTestClient.TeardownHostClient();
#if UNITY_EDITOR
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
#endif
    }

    [UnityTest]
    public IEnumerator ServerScene_CoreBotGate_InSingleSession()
    {
        yield return __ServerScene_BotFarmBecomesReady();

        yield return BotRelayIdleMatchPlayModeChecks.AssertExclusiveIdleMatch();
        yield return __ResetAgentZeroForNextScenario();

        yield return __InjectMechanism_ProducerQueueJoin_DrainsThroughFlatten();
        yield return __ResetAgentZeroForNextScenario();

        yield return __Live117Invite_ReachesPendingInvite();
        yield return __ResetAgentZeroForNextScenario();

        yield return __Live117Invite_BotSendsJoinReply();
        yield return __ResetAgentZeroForNextScenario();

        yield return __Invite_BotEntersInSquad_Deterministic();
    }

    private static IEnumerator __ResetAgentZeroForNextScenario()
    {
        BotRelayPlayModeHarness.ResetAgentForTestIsolation(0);
        yield return BotRelayPlayModeHarness.WaitForInjectQueueDrained();
        yield return BotRelayPlayModeHarness.WaitForRouteSendSettled(
            stableSeconds: 0.5f,
            timeoutSeconds: 8f);
    }

    /// <summary>Server.unity boots and the single-process bot farm becomes ready.</summary>
    private static IEnumerator __ServerScene_BotFarmBecomesReady()
    {
        Assert.IsTrue(BotRelayPlayModeHarness.TryGetBotVirtualPort(0, out var vport));
        Assert.Greater(vport, (ushort)0);
        yield return null;
    }

    /// <summary>
    /// 机制：把 Join 写入 Bot 的生产 inject queue，验证 Flatten → Server tick 后队列从 1 变为 0。
    /// 这里仅覆盖生产者/flatten 调度；Handler 接受 Join 的语义由 ManualHost 端到端用例验证，不能把「队列清空」伪称为入队成功。
    /// </summary>
    private static IEnumerator __InjectMechanism_ProducerQueueJoin_DrainsThroughFlatten()
    {
        Assert.IsTrue(BotRelayPlayModeHarness.TryGetBotUserId(0, out var botUserId), "bot user id required");

        bool registered = BotRelayPlayModeServerProbe.TryGetIdentity(botUserId, out var match, out var channel);

        bool enqueued = BotRelayPlayModeServerProbe.TryEnqueueInjectJoin(botUserId, channel < 0 ? 0 : channel);
        int countAfterEnqueue = BotRelayPlayModeServerProbe.GetInjectCount();

        for (int i = 0; i < 60; ++i)
            yield return null;

        int countAfterFrames = BotRelayPlayModeServerProbe.GetInjectCount();

        Assert.IsTrue(registered, $"bot {botUserId} not registered on server");
        Assert.IsTrue(enqueued, "inject enqueue failed");
        Assert.AreEqual(1, countAfterEnqueue, "inject not visible in live queue after enqueue");
        Assert.AreEqual(
            0,
            countAfterFrames,
            $"Bot inject producer queue did not drain through flatten/server scheduling: match={match} channel={channel} botId={botUserId}");
    }

    /// <summary>入站：真人房主 WorldInvite → bot 收到并进入 PendingInvite（携带 squadInviteID）。</summary>
    private static IEnumerator __Live117Invite_ReachesPendingInvite()
    {
        BotRelayPlayModeHarness.RouteLiveInvite117ToBot(0);

        yield return BotRelayPlayModeHarness.WaitForAgentState(
            0,
            BotState.PendingInvite,
            timeoutSeconds: 8f);

        Assert.IsTrue(BotRelayPlayModeHarness.TryGetPendingInviteId(0, out var squadId));
        Assert.AreEqual(1u, squadId);
    }

    /// <summary>
    /// 交付门禁（本用例核心目标）：真人邀请 → bot 进入 PendingInvite → bot 主动做出 Join 答复
    /// （__SendSquadJoin：Mismatch→defer→Join，app 层 inject 到 NetworkRelayServer；JoinDispatched 置位）。
    /// 验证"邀请后 bot 到底有没有发起 Join 答复"，单 bot、确定性、无真实 socket。
    /// </summary>
    private static IEnumerator __Live117Invite_BotSendsJoinReply()
    {
        BotRelayPlayModeHarness.RouteLiveInvite117ToBot(0);

        yield return BotRelayPlayModeHarness.WaitForAgentState(
            0,
            BotState.PendingInvite,
            timeoutSeconds: 8f);

        Assert.IsTrue(BotRelayPlayModeHarness.TryGetPendingInviteId(0, out var squadId));
        Assert.AreEqual(1u, squadId);

        // Join dispatch defers behind Mismatch + ack-fallback + invite delay, so allow a generous window.
        float joinTimeout = BotRelayPlayModeHarness.GetInviteTimeoutMaxSeconds() + 30f;
        yield return BotRelayPlayModeHarness.WaitForJoinDispatched(0, joinTimeout);

        Assert.IsTrue(
            BotRelayPlayModeHarness.TryGetJoinDispatched(0, out var dispatched) && dispatched,
            "Bot did not queue the app-layer Join inject after the invite.");
    }

    /// <summary>
    /// 端到端门禁（bot 本地 InSquad，确定性）：真人邀请 → bot 进入 PendingInvite → 模拟 Relay 下行 SquadJoin →
    /// bot 本地状态进入 InSquad。不依赖真实 socket host。
    /// </summary>
    private static IEnumerator __Invite_BotEntersInSquad_Deterministic()
    {
        BotRelayPlayModeHarness.RouteLiveInvite117ToBot(0);

        yield return BotRelayPlayModeHarness.WaitForAgentState(
            0,
            BotState.PendingInvite,
            timeoutSeconds: 8f);

        Assert.IsTrue(BotRelayPlayModeHarness.TryGetPendingInviteId(0, out var squadId));
        BotRelayPlayModeAcceptance.SimulateServerJoinAckToBot(0, squadId);

        for (int i = 0; i < 30; ++i)
            yield return null;

        yield return BotRelayPlayModeAcceptance.WaitForInSquad(0, timeoutSeconds: 20f);

        Assert.IsTrue(BotRelayPlayModeHarness.TryGetAgentState(0, out var finalState));
        Assert.AreEqual(BotState.InSquad, finalState);
    }
}
