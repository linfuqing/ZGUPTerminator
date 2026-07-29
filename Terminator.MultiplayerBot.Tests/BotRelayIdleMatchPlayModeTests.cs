using System.Collections;

using NUnit.Framework;

using ZG;

/// <summary>
/// Idle-match check used by the single-session PlayMode delivery gate.
/// </summary>
internal static class BotRelayIdleMatchPlayModeChecks
{
    public static IEnumerator AssertExclusiveIdleMatch()
    {
        using var logGuard = BotRelayPlayModeLogAsserts.BeginStreamOverreadScope();
        try
        {
            BotRelayPlayModeHarness.ScheduleExclusiveIdleMatchNow(0);

            yield return BotRelayPlayModeHarness.WaitForAgentState(
                0,
                BotState.Matching,
                timeoutSeconds: 15f);

            Assert.IsTrue(BotRelayPlayModeHarness.TryGetMatchingSlotOwner(out var initialOwner));
            Assert.AreEqual(0, initialOwner, "The scheduled Bot must own the farm-wide matching lease.");

            for (int i = 0; i < 45; ++i)
                yield return null;

            Assert.IsTrue(BotRelayPlayModeHarness.TryGetAgentState(0, out var botState));
            Assert.IsTrue(
                botState == BotState.Idle || botState == BotState.Matching,
                $"A solo farm match must never leave the Bot paired with another farm Bot; state={botState}.");

            Assert.IsTrue(BotRelayPlayModeHarness.TryGetMatchingSlotOwner(out var finalOwner));
            Assert.IsTrue(
                finalOwner == BotMatchGuard.NoMatchingSlotOwner || finalOwner == 0,
                $"Only the scheduled Bot may hold the matching lease; owner={finalOwner}.");

            int agentCount = BotRelayPlayModeHarness.GetBotAgentCount();
            Assert.Greater(agentCount, 0);
            for (int i = 1; i < agentCount; ++i)
            {
                Assert.IsTrue(BotRelayPlayModeHarness.TryGetAgentState(i, out var otherState));
                Assert.AreEqual(BotState.Idle, otherState, $"Unexpected concurrent Bot activity in slot {i}: {otherState}.");
            }

            logGuard.AssertNoViolations(
                "Idle→Match: inbound NetworkServer PopEvents must not overread while bot matchmakes.");
        }
        finally
        {
            logGuard.Dispose();
        }
    }
}
