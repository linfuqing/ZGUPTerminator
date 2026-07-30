using NUnit.Framework;

/// <summary>
/// Manager queue -> strict PopEvents peel -> inbox -> agent integration for the current frame.
/// </summary>
public class BotRelayInviteIntegrationTests
{
    [Test]
    [Category("Integration")]
    public void CanonicalMeasuredInvite_FullRoute_ReachesPendingInvite()
    {
        const uint senderId = 8737;
        const int squadChannel = 11;
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
                senderId,
                squadChannel,
                0,
                0,
                out var app));
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref catalog,
                in app,
                out var wire));

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayWireTestFixtures.ToBytes(in wire));

            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotState.PendingInvite, session.farm.agentStates[0].state);
            Assert.IsTrue(session.farm.agentStates[0].HasPendingInvite);
            Assert.AreEqual(
                (uint)squadChannel,
                session.farm.agentStates[0].pendingInvite.squadInviteID);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Integration")]
    public void WrongMeasuredFrameOffset_DoesNotReachPendingInvite()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
                8737,
                11,
                0,
                0,
                out var app));
            int measuredStreamStart =
                session.catalogBlob.Value.inboundAppPayloadOffset - sizeof(ushort);
            Assert.GreaterOrEqual(measuredStreamStart, 0);
            var wrongPrefix = new byte[measuredStreamStart + 1];
            var wire = BotRelayWireTestFixtures.WrapFramedAppAtPrefix(
                wrongPrefix,
                in app);

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotState.Idle, session.farm.agentStates[0].state);
            Assert.IsFalse(session.farm.agentStates[0].HasPendingInvite);
        }
        finally
        {
            session.Dispose();
        }
    }
}
