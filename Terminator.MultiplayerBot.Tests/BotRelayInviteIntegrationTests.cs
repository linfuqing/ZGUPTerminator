using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.TestTools;
using ZG;

/// <summary>
/// Integration-style EditMode tests: WireCatalog driver capture + Manager queue + DrainInbound + agent.
/// Simulates RouteSend(1390→botVport) without full ClientData / NetworkRelayServerSystem.
/// </summary>
public class BotRelayInviteIntegrationTests
{
    [Test]
    public void RouteServerToBot_EnqueuesOnManagerChannel()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayIntegrationTestFixtures.BuildDriverShapeInviteWireBytes());

            Assert.IsTrue(
                BotRelayManager.Instance.TryDequeueToBot(
                    BotRelayIntegrationSession.BotVirtualPort,
                    out var wire));
            Assert.Greater(wire.length, 0);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    public void DriverShapeInviteWire_RouteQueue_DrainInbound_EnqueuesAppLayer()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayIntegrationTestFixtures.BuildDriverShapeInviteWireBytes());

            BotRelayIntegrationTestFixtures.DrainInbound(ref session);

            Assert.Greater(
                session.farm.inboundCount[0],
                0,
                "DrainInbound should extract SquadInvite app from driver-shaped pipeline wire.");
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    public void DriverShapeInviteWire_FullPipeline_ReachesPendingInviteState()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayIntegrationTestFixtures.BuildDriverShapeInviteWireBytes());

            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.Greater(session.farm.eventCount[0], 0, "PumpInbound should enqueue SquadInvite event.");

            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f, 0f));

            Assert.AreEqual(BotState.PendingInvite, session.farm.agentStates[0].state);
            Assert.IsTrue(session.farm.agentStates[0].hasPendingInvite);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    public void LiveCapturedInviteWire117_FullPipeline_ParsesValidSquadId()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayWireTestFixtures.CapturedLiveInviteWire117);
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            Assert.Greater(session.farm.inboundCount[0], 0);

            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.Greater(session.farm.eventCount[0], 0);

            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f, 0f));

            Assert.AreEqual(BotState.PendingInvite, session.farm.agentStates[0].state);
            Assert.AreEqual(1u, session.farm.agentStates[0].pendingInvite.squadInviteID);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    public void LiveCapturedInviteWire117_DrainInbound_ExtractsInvitePrefix()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            using var logs = BotRelayWireTestAsserts.LogScope.Begin();
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayWireTestFixtures.CapturedLiveInviteWire117);
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);

            BotRelayWireTestAsserts.AssertNoStreamOverreadErrors(logs, "DrainInbound live 117B");
            Assert.Greater(session.farm.inboundCount[0], 0, "Expected live invite app in inbound queue.");
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Integration")]
    public void WireCatalog_DriverCapture_ProducesValidCatalogForDrain()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession(captureWireCatalog: true);
        try
        {
            Assert.IsTrue(session.catalogBlob.IsCreated);
            Assert.IsTrue(session.catalogBlob.Value.IsValid);
            ref var catalog = ref session.catalogBlob.Value;
            Assert.GreaterOrEqual(
                catalog.inboundMappings.Length,
                2,
                "WireCatalog must record server hello + invite inbound mappings.");

            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayIntegrationTestFixtures.BuildDriverShapeInviteWireBytes());
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);

            Assert.Greater(session.farm.inboundCount[0], 0);
        }
        finally
        {
            session.Dispose();
        }
    }
}
