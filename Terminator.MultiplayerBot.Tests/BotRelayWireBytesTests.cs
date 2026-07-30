using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

public class BotRelayWireBytesTests
{
    [Test]
    public void LooksLikePipelineWire_StatusOutbound_ReturnsTrue()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.StatusOutboundWire);
        Assert.IsTrue(BotRelayWireBytes.LooksLikePipelineWire(in wire));
    }

    [Test]
    public void LooksLikePipelineWire_PipelineHeaderOnly_ReturnsFalse()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.PipelineHeader);
        Assert.IsFalse(BotRelayWireBytes.LooksLikePipelineWire(in wire),
            "A pipeline prefix without an application payload is not a wire message.");
        Assert.IsFalse(BotRelayWireBytes.LooksLikeConnectHandshake(in wire));
    }

    [Test]
    public void TryBuildInviteApp_ProducesNonEmptyPayload()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            BotRelayWireTestFixtures.RealPlayerUserId,
            1,
            0,
            0,
            out var app));
        Assert.Greater(app.length, 8);
    }

    [Test]
    public void InviteType_PackedPrefix_StartsWithExpectedBytes()
    {
        var prefix = BotRelayWireTestFixtures.BuildInviteTypePrefix();
        Assert.AreEqual(2, prefix.Length);
        Assert.AreEqual((int)ReplyMessageType.Invite, ReadPackedInt(prefix));
    }

    [Test]
    public void TryExtract_PipelineRawInviteApp_ExtractsInvite()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            BotRelayWireTestFixtures.RealPlayerUserId,
            1,
            0,
            0,
            out var app));

        var wire = BotRelayWireTestFixtures.WrapPipelineRawApp(BotRelayWireTestFixtures.PipelineHeader, in app);

        Assert.IsTrue(BotRelayWireTestFixtures.TryExtract(in wire, 8, out var extracted));
        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in extracted, out var squadId));
        Assert.AreEqual(1u, squadId);
        Assert.AreEqual(app.length, extracted.length);
    }

    [Test]
    public void TryExtract_PipelineFramedInviteApp_ExtractsInvite()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            BotRelayWireTestFixtures.RealPlayerUserId,
            1,
            0,
            0,
            out var app));

        var wire = BotRelayWireTestFixtures.WrapPipelineFramedApp(BotRelayWireTestFixtures.PipelineHeader, in app);

        Assert.IsTrue(BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var extracted));
        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in extracted, out var squadId));
        Assert.AreEqual(1u, squadId);
        Assert.AreEqual(app.length, extracted.length);
    }

    [Test]
    public void CanonicalInviteWire_PopEventsExtract_ParsesSquadId()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.BuildPipelineInviteWireBytes());

        Assert.IsTrue(
            BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var app),
            BotRelayWireTestFixtures.ToHex(in wire, 80));

        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out var squadId));
        Assert.AreEqual(1u, squadId);
        Assert.LessOrEqual(app.length, wire.length);
    }

    [Test]
    public void CanonicalInviteApp_UtilityRead_MatchesZgupFields()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            BotRelayWireTestFixtures.RealPlayerUserId,
            1,
            0,
            0,
            out var app));

        using var bytes = new NativeArray<byte>(app.length, Allocator.Temp);
        Assert.IsTrue(app.TryCopyTo(bytes));
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadSendRelayInviteFromBytes(
                bytes,
                StreamCompressionModel.Default,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default));
        Assert.AreEqual(BotRelayWireTestFixtures.RealPlayerUserId, invite.id);
        Assert.AreEqual(1, invite.channel);
    }

    [Test]
    public void ParseTransportPayload_CanonicalInviteApp_EnqueuesSquadInviteEvent()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            BotRelayWireTestFixtures.RealPlayerUserId,
            1,
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
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out var evt));
            Assert.AreEqual(BotRelayEventType.SquadInvite, evt.type);
            Assert.AreEqual(1u, evt.squadInvite.squadInviteID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void ParseTransportPayload_PipelineFramedInvite_EnqueuesSquadInviteEvent()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        Assert.IsTrue(BotRelayWireTestFixtures.TryExtractLiveInviteWire(in wire, out var app));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out var evt));
            Assert.AreEqual(BotRelayEventType.SquadInvite, evt.type);
            Assert.AreEqual(1u, evt.squadInvite.squadInviteID);
            Assert.AreEqual(BotRelayWireTestFixtures.RealPlayerUserId, evt.header.userID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void ParseTransportPayload_DriverShapeFramedInvite_EnqueuesSquadInviteEvent()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(
            BotRelayIntegrationTestFixtures.BuildDriverShapeInviteWireBytes());
        Assert.IsTrue(
            BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var extracted));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelaySlotInbox.ParseTransportPayload(
                in extracted,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out var evt));
            Assert.AreEqual(BotRelayEventType.SquadInvite, evt.type);
            Assert.AreEqual(1u, evt.squadInvite.squadInviteID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void BuildMatchApp_ProducesNonEmptyPayload()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMatchApp(
            9,
            BotRelayFlowTestFixtures.MatchLevel,
            out var app));
        Assert.IsFalse(app.IsEmpty, BotRelayWireTestFixtures.ToHex(in app));
    }

    [Test]
    public void ParseTransportPayload_TwoByteFirstMatchWithZeroLevel_EnqueuesMatchEvent()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMatchApp(
            1,
            0,
            out var app));
        Assert.AreEqual(2, app.length,
            "The live first Match notification is bit-packed into two bytes.");

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out var evt), "no event dequeued");
            Assert.AreEqual(BotRelayEventType.Match, evt.type);
            Assert.AreEqual(1, evt.match.matchID);
            Assert.AreEqual(0, evt.match.level);
        }
        finally
        {
            farm.Dispose();
        }
    }

    private static int ReadPackedInt(byte[] bytes)
    {
        var scratch = new NativeArray<byte>(bytes.Length, Allocator.Temp);
        try
        {
            for (int i = 0; i < bytes.Length; ++i)
                scratch[i] = bytes[i];

            var reader = new DataStreamReader(scratch);
            return reader.ReadPackedInt(StreamCompressionModel.Default);
        }
        finally
        {
            scratch.Dispose();
        }
    }

    private static uint ReadPackedUInt(byte[] bytes)
    {
        var scratch = new NativeArray<byte>(bytes.Length, Allocator.Temp);
        try
        {
            for (int i = 0; i < bytes.Length; ++i)
                scratch[i] = bytes[i];

            var reader = new DataStreamReader(scratch);
            return reader.ReadPackedUInt(StreamCompressionModel.Default);
        }
        finally
        {
            scratch.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void LatestPlay104_InviteTypePrefix_MatchesCodec()
    {
        var prefix = BotRelayWireTestFixtures.BuildInviteTypePrefix();
        Assert.GreaterOrEqual(prefix.Length, 2);
        Assert.AreEqual(0x04, BotRelayWireTestFixtures.McpLatestPlayInvite104SendRelayTail[0]);
        Assert.AreEqual(
            0xD5,
            BotRelayWireTestFixtures.McpLatestPlayInvite104SendRelayTail[1],
            "live Play session packs Invite(104) as 04 D5");
    }

    [Test]
    [Category("Regression")]
    public void LatestPlay104_PayloadInner8_ReadsReplyInvite()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildMcpCapturedInvite104JunkPayload(out var payload),
            "payload");
        var app = default(BotRelayPacket);
        const int inner = BotRelayWireBytes.InvitePipelineJunkAnchoredInnerOffset;
        app.length = payload.length - inner;
        for (int i = 0; i < app.length; ++i)
        {
            app.SetByte(i, payload.GetByte(inner + i));
        }

        Assert.IsTrue(
            BotRelayMessageUtility.TryReadInviteFromPacket(
                in app,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default),
            "reply payload read");
        Assert.AreEqual(1, invite.channel);
    }

}
