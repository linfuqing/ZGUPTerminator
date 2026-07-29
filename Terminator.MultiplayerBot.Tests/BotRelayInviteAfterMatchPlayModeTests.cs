using System.Collections;

using NUnit.Framework;

using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

using ZG;

/// <summary>
/// [Explicit·手动·ManualHost] PlayMode: invite after boot idle MatchToSend — catches server
/// <c>match != 0</c> Join gate. Uses the real-socket BotRelayHostTestClient which is unstable across
/// repeated EnterPlayMode sessions (DisconnectReason:3 / ConnectionDataMap 越界), so kept manual-only and
/// out of the automated category=PlayMode gate. Automated Join-reply verification is covered by
/// BotRelayPlayModeTests.ServerScene_CoreBotGate_InSingleSession.
/// 注意：category 名不含 "PlayMode" 前缀，避免 MCP category 子串匹配把手动用例带进 PlayMode 门禁。
/// MCP(手动): mode=EditMode + category=ManualHost + test_names=BotRelayInviteAfterMatchPlayModeTests.*
/// </summary>
[Category("ManualHost")]
[Timeout(120000)]
public class BotRelayInviteAfterMatchPlayModeTests
{
    private bool __previousRunInBackground;
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        __previousRunInBackground = UnityEngine.Application.runInBackground;
        UnityEngine.Application.runInBackground = true;

        // In the MCP editor session there may be no audio output device. FMOD can emit its
        // one-time device-reset error while Unity enters Play Mode, before this test has a
        // chance to install its normal relay assertions. Keep that environmental exception
        // scoped to the transition only; all bot/network errors after the scene is loaded
        // still fail the test as usual.
        bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            yield return new EnterPlayMode();
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
        }

        BotRelayDefines.VerboseTelemetry = true;
        yield return BotRelayPlayModeHarness.LoadServerSceneSingle();
        yield return BotRelayPlayModeHarness.WaitForBotFarmReadyPreserveBootMatch();
    }

[TearDown]
    public void TearDown()
    {
        BotRelayDefines.VerboseTelemetry = false;
        BotRelayHostTestClient.TeardownHostClient();
        UnityEngine.Application.runInBackground = __previousRunInBackground;
#if UNITY_EDITOR
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
#endif
    }

    [UnityTest]
    [Explicit]
    public IEnumerator Live117Invite_AfterBootMatch_MismatchThenJoin_ServerAcceptsJoin()
    {
        using var logGuard = BotRelayPlayModeLogAsserts.BeginStreamOverreadScope();
        using var hostDisconnectGuard = BotRelayPlayModeLogAsserts.BeginHostRelayDisconnectScope();
        try
        {
            yield return BotRelayPlayModeAcceptance.RunInviteAfterBootMatchServerJoinAcceptance(agentIndex: 0);

            logGuard.AssertNoViolations(
                "Invite after boot Match: inbound PopEvents must not overread while bot injects Mismatch+Join.");
        }
        finally
        {
            hostDisconnectGuard.Dispose();
            logGuard.Dispose();
        }
    }

    [UnityTest]
    [Explicit]
    public IEnumerator Live117Invite_AfterBootMatch_EndToEnd_InSquad()
    {
        using var logGuard = BotRelayPlayModeLogAsserts.BeginStreamOverreadScope();
        using var hostDisconnectGuard = BotRelayPlayModeLogAsserts.BeginHostRelayDisconnectScope();
        try
        {
            yield return BotRelayPlayModeAcceptance.RunInviteAfterBootMatchEndToEndAcceptance(agentIndex: 0);

            logGuard.AssertNoViolations(
                "Invite after boot Match E2E: PopEvents clean through Join ack simulation.");

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
