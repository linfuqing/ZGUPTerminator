using NUnit.Framework;
using ZG;

public class BotRelayWireBytesTests
{
    [Test]
    public void CurrentFraming_MeasuredAppOffset_ResolvesExactStreamStart()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            0,
            0,
            0,
            out var app));
        var wire = BotRelayWireTestFixtures.WrapFramedAppAtPrefix(
            BotRelayWireTestFixtures.SyntheticTransportPrefix,
            in app);
        int appOffset =
            BotRelayWireTestFixtures.SyntheticTransportPrefix.Length + sizeof(ushort);

        Assert.IsTrue(BotRelayWireBytes.TryGetCurrentPopEventsStreamStart(
            in wire,
            appOffset,
            out int streamStart));
        Assert.AreEqual(
            BotRelayWireTestFixtures.SyntheticTransportPrefix.Length,
            streamStart);
    }

    [Test]
    public void CurrentFraming_WrongMeasuredOffset_IsRejected()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            0,
            0,
            0,
            out var app));
        var wire = BotRelayWireTestFixtures.WrapFramedAppAtPrefix(
            BotRelayWireTestFixtures.SyntheticTransportPrefix,
            in app);
        int correctOffset =
            BotRelayWireTestFixtures.SyntheticTransportPrefix.Length + sizeof(ushort);

        Assert.IsFalse(BotRelayWireBytes.TryGetCurrentPopEventsStreamStart(
            in wire,
            correctOffset + 1,
            out _));
    }

    [Test]
    public void CanonicalInviteApp_UtilityRead_MatchesCurrentFields()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            11,
            12,
            1,
            out var app));

        Assert.IsTrue(BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(
            in app,
            out var invite));
        Assert.AreEqual(8737u, invite.id);
        Assert.AreEqual(11, invite.channel);
        Assert.AreEqual(12u, invite.levelID);
        Assert.AreEqual(1, invite.stage);
    }

    [Test]
    public void CanonicalInvite_CurrentMeasuredFraming_ExtractsExactApp()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            11,
            0,
            0,
            out var expected));
        var wire = BotRelayWireTestFixtures.WrapFramedAppAtPrefix(
            BotRelayWireTestFixtures.SyntheticTransportPrefix,
            in expected);
        int appOffset =
            BotRelayWireTestFixtures.SyntheticTransportPrefix.Length + sizeof(ushort);

        Assert.IsTrue(BotRelayWireBytes.TryExtractCurrentFramedApp(
            in wire,
            appOffset,
            out var actual));
        Assert.IsTrue(BotRelayWireBytes.Equals(in expected, in actual));
    }

    [Test]
    public void ParseTransportPayload_CanonicalInvite_EnqueuesInviteEvent()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            11,
            0,
            0,
            out var app));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(
                0,
                ref farm,
                out var evt));
            Assert.AreEqual(BotRelayEventType.SquadInvite, evt.type);
            Assert.AreEqual(11u, evt.squadInvite.squadInviteID);
            Assert.AreEqual(8737u, evt.header.userID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void ParseTransportPayload_TwoByteFirstMatchWithZeroLevel_EnqueuesMatchEvent()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMatchApp(
            1,
            0,
            out var app));
        Assert.AreEqual(2, app.length);

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(
                0,
                ref farm,
                out var evt));
            Assert.AreEqual(BotRelayEventType.Match, evt.type);
            Assert.AreEqual(1, evt.match.matchID);
            Assert.AreEqual(0, evt.match.level);
        }
        finally
        {
            farm.Dispose();
        }
    }
}
