using System.Collections;

using NUnit.Framework;

using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

using ZG;

/// <summary>
/// [Explicit·手动] 真实 socket host 全链路 PlayMode 用例（HostTestClient: ClientData → ReplyServer）。
///
/// 这些用例走真实 socket，验证真人房主建队+邀请 → bot 自动 inject Join → Server 接受 → bot 服务器侧加入
/// host squad channel 的完整端到端。真实 socket 在重复 EnterPlayMode 下不稳定（DisconnectReason:3 /
/// ConnectionDataMap 越界），因此归入独立 category=PlayModeManual 且 [Explicit]，不进自动化门禁，仅人工手动跑。
///
/// 自动化门禁（category=PlayMode）请见 BotRelayPlayModeTests。
/// 注意：category 名不含 "PlayMode" 前缀，避免 MCP category 子串匹配把手动用例带进 PlayMode 门禁。
/// </summary>
[Category("ManualHost")]
[Explicit]
[Timeout(120000)]
public class BotRelayPlayModeManualTests
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

    /// <summary>真实 socket host 端到端：房主建队+邀请 → bot 自动 inject Join → Server 接受 → bot 加入 host squad channel。</summary>
    [UnityTest]
    public IEnumerator Live117Invite_ServerAcceptsInjectedJoin_RealSocket()
    {
        using var logGuard = BotRelayPlayModeLogAsserts.BeginStreamOverreadScope();
        try
        {
            yield return BotRelayPlayModeAcceptance.RunInviteToJoinInjectAcceptance(agentIndex: 0);

            logGuard.AssertNoViolations(
                "Invite→Join inject: inbound NetworkServer PopEvents must not overread.");
        }
        finally
        {
            logGuard.Dispose();
        }
    }

    /// <summary>真实 socket host 端到端 + bot 本地 InSquad。</summary>
    [UnityTest]
    public IEnumerator Live117Invite_ServerAcceptsJoin_BotEntersInSquad_RealSocket()
    {
        using var logGuard = BotRelayPlayModeLogAsserts.BeginStreamOverreadScope();
        using var hostDisconnectGuard = BotRelayPlayModeLogAsserts.BeginHostRelayDisconnectScope();
        try
        {
            yield return BotRelayPlayModeAcceptance.RunInviteToJoinInjectAcceptance(agentIndex: 0);

            logGuard.AssertNoViolations(
                "Invite→Join inject: inbound NetworkServer PopEvents must not overread.");

            Assert.IsTrue(BotRelayPlayModeHarness.TryGetPendingInviteId(0, out var squadId));
            BotRelayPlayModeAcceptance.SimulateServerJoinAckToBot(0, squadId);

            for (int i = 0; i < 30; ++i)
                yield return null;

            yield return BotRelayPlayModeAcceptance.WaitForInSquad(0, timeoutSeconds: 20f);

            Assert.IsTrue(BotRelayPlayModeHarness.TryGetAgentState(0, out var finalState));
            Assert.AreEqual(BotState.InSquad, finalState);
        }
        finally
        {
            hostDisconnectGuard.Dispose();
            logGuard.Dispose();
        }
    }
}
