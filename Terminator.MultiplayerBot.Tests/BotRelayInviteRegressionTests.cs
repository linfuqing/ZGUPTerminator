using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Current Invite protocol contract. Fixtures are produced by the same writer used by the bot's
/// Server.SendRelay probe. Alternate framing and historical payload layouts are unsupported.
/// </summary>
public class BotRelayInviteRegressionTests
{
    [Test]
    [Category("Regression")]
    public void CanonicalCurrentInvite_FullSendRelayFrame_ParsesExactFields()
    {
        const uint senderId = 8737;
        const uint levelId = 12;
        const int stage = 1;
        const int squadChannel = 0;
        var text = new FixedString512Bytes("current invite");
        var senderHeader = BotRelayWireTestFixtures.BuildSenderHeader(senderId);

        Assert.IsTrue(BotRelayCodec.TryBuildWorldInviteRelayApp(
            in senderHeader,
            NetworkRelayType.All,
            squadChannel,
            levelId,
            stage,
            in text,
            out var app));

        Assert.IsTrue(
            BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out var invite));
        Assert.AreEqual(NetworkRelayType.All, invite.type);
        Assert.AreEqual(senderId, invite.id);
        Assert.AreEqual(levelId, invite.levelID);
        Assert.AreEqual(stage, invite.stage);
        Assert.AreEqual(squadChannel, invite.channel);
        Assert.AreEqual(text.ToString(), invite.text.ToString());
    }

    [TestCase(1u)]
    [TestCase(31u)]
    [TestCase(8737u)]
    [Category("Regression")]
    public void CanonicalCurrentInvite_CompactSenderId_IsAccepted(uint senderId)
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            senderId,
            0,
            0,
            0,
            out var app));

        Assert.IsTrue(BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out var invite));
        Assert.AreEqual(senderId, invite.id);
    }

    [Test]
    [Category("Regression")]
    public void CanonicalCurrentInvite_ChannelZero_RemainsAuthoritative()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            0,
            12,
            1,
            out var app));
        Assert.IsTrue(BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out var invite));
        Assert.AreEqual(0, invite.channel);
    }

    [Test]
    [Category("Regression")]
    public void CanonicalCurrentInvite_MeasuredNullPipelineFrame_PeelsExactly()
    {
        const uint senderId = 8737;
        const int squadChannel = 11;
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            senderId,
            squadChannel,
            0,
            0,
            out var inviteApp));

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref catalog,
                in inviteApp,
                out var wire));
            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsTrue(BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                in wire,
                ref catalog,
                ref batch,
                out bool fullyConsumed));
            Assert.IsTrue(fullyConsumed);
            Assert.AreEqual(1, batch.appCount);
            var peeledApp = batch.GetApp(0);
            Assert.IsTrue(BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(
                in peeledApp,
                out var invite));
            Assert.AreEqual(senderId, invite.id);
            Assert.AreEqual(squadChannel, invite.channel);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void CanonicalCurrentInvite_ReachesPendingInviteWithCompactSender()
    {
        const uint senderId = 8737;
        const int squadChannel = 11;
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            senderId,
            squadChannel,
            0,
            0,
            out var app));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            ref var transport = ref farm.transport.ElementAt(0);
            transport.flags |= BotRelayTransportFlags.Connected;
            farm.nextIdleMatchTime[0] = double.PositiveInfinity;

            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());
            BotAgentLogic.Execute(
                0,
                ref farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.AreEqual(
                (uint)squadChannel,
                farm.agentStates[0].pendingInvite.squadInviteID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void CanonicalCurrentInvite_TruncatedHeader_IsRejected()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            0,
            0,
            0,
            out var canonical));

        var truncated = Slice(in canonical, 0, canonical.length - 1);
        Assert.IsFalse(BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in truncated, out _));
    }

    [Test]
    [Category("Regression")]
    public void CanonicalCurrentInvite_TrailingBytes_AreRejected()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            0,
            0,
            0,
            out var canonical));

        var extended = canonical;
        extended.SetByte(extended.length++, 0x7F);
        Assert.IsFalse(BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in extended, out _));
    }

    [Test]
    [Category("Regression")]
    public void CanonicalCurrentInvite_PrefixedOrShiftedPayload_IsRejected()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            0,
            0,
            0,
            out var canonical));

        var shifted = default(BotRelayPacket);
        shifted.length = canonical.length + 1;
        shifted.SetByte(0, 0x0B);
        for (int i = 0; i < canonical.length; ++i)
            shifted.SetByte(i + 1, canonical.GetByte(i));

        Assert.IsFalse(BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in shifted, out _));
        Assert.IsFalse(BotRelayRoutePeelOps.IsPeelCandidateApp(in shifted));
    }

    private static BotRelayPacket Slice(
        in BotRelayPacket source,
        int offset,
        int length)
    {
        var result = default(BotRelayPacket);
        result.length = length;
        for (int i = 0; i < length; ++i)
            result.SetByte(i, source.GetByte(offset + i));
        return result;
    }
}
