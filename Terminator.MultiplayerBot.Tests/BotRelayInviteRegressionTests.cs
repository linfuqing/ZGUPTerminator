using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Inbound relay regression: server→bot SendRelay Invite via PopEvents peel must reach PendingInvite.
/// Fixtures are built from <see cref="BotRelayCodec.TryBuildWorldInviteRelayApp"/> (ZGUP shape), not static hex.
/// </summary>
public class BotRelayInviteRegressionTests
{
    [Test]
    [Category("Regression")]
    public void CanonicalInviteWire_PopEventsExtract_ParsesSquadChannel()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.BuildPipelineInviteWireBytes());
        Assert.IsTrue(BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var app));
        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out var squadId));
        Assert.AreEqual(1u, squadId);
    }

    [Test]
    [Category("Regression")]
    public void CanonicalInviteApp_SendRelayUtility_MatchesZgupFields()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildInviteApp(
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
        Assert.AreEqual(NetworkRelayType.All, invite.type);
    }

    [Test]
    [Category("Regression")]
    public void WorldInvite_SquadChannelZero_ParsesAndReachesPendingInvite()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildInviteApp(
                BotRelayWireTestFixtures.RealPlayerUserId,
                0,
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
        Assert.AreEqual(0, invite.channel);
        Assert.IsTrue(BotRelayWireBytes.TryValidateSendRelayInviteApp(in app));
        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out var squadId));
        Assert.AreEqual(0u, squadId);

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            var wire = BotRelayIntegrationTestFixtures.BuildNullFramedRelayAppWireBytes(ref session, in app);
            BotRelayIntegrationTestFixtures.RouteServerToBot(wire);
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            Assert.Greater(session.farm.inboundCount[0], 0);

            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f, 0f));

            Assert.AreEqual(BotState.PendingInvite, session.farm.agentStates[0].state);
            Assert.AreEqual(0u, session.farm.agentStates[0].pendingInvite.squadInviteID);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SendRelayInvite_NormalizePopEvents_PreservesSquadChannelOne()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildInviteApp(
                BotRelayWireTestFixtures.RealPlayerUserId,
                1,
                100u,
                2,
                out var app));

        Assert.IsTrue(BotRelayWireBytes.TryNormalizePopEventsPayload(in app, out var normalized));
        using var bytes = new NativeArray<byte>(normalized.length, Allocator.Temp);
        Assert.IsTrue(normalized.TryCopyTo(bytes));
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadSendRelayInviteFromBytes(
                bytes,
                StreamCompressionModel.Default,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default));
        Assert.AreEqual(1, invite.channel);
        Assert.AreEqual(2, invite.stage);
        Assert.AreEqual(100u, invite.levelID);
    }

    [Test]
    [Category("Regression")]
    public void CanonicalInviteWire_Extract_DoesNotEmitStreamOverreadErrors()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);

        using var logs = BotRelayWireTestAsserts.LogScope.Begin();
        Assert.IsTrue(
            BotRelayWireBytes.TryExtractAppViaTransportFraming(
                in wire,
                out var app));
        Assert.IsFalse(app.IsEmpty);
        BotRelayWireTestAsserts.AssertNoStreamOverreadErrors(logs, "TryExtractAppViaTransportFraming on canonical invite wire");
    }

    [Test]
    [Category("Regression")]
    public void CanonicalInviteWire_BurstDrainInbound_EnqueuesAppLayer()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            using var logs = BotRelayWireTestAsserts.LogScope.Begin();
            BotRelayIntegrationTestFixtures.RouteNullFramedInviteToBot(ref session);
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);

            BotRelayWireTestAsserts.AssertNoStreamOverreadErrors(logs, "Burst DrainInbound on null-framed invite wire");
            Assert.Greater(
                session.farm.inboundCount[0],
                0,
                "Null-framed invite wire must decode at inboundAppPayloadOffset in Burst DrainInbound.");
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void CanonicalInviteWire_FullPipeline_ReachesPendingInvite()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.RouteNullFramedInviteToBot(ref session);
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            Assert.Greater(session.farm.inboundCount[0], 0);

            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            Assert.Greater(session.farm.eventCount[0], 0, "PumpInbound should enqueue SquadInvite event.");

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
    [Category("Regression")]
    public void LiveHostInviteApp_RoundTrip_ParsesSquadChannel()
    {
        var header = BotRelayWireTestFixtures.BuildSenderHeader(BotRelayWireTestFixtures.LiveHostUserId);
        var text = new FixedString512Bytes("776757715");
        Assert.IsTrue(
            BotRelayCodec.TryBuildWorldInviteRelayApp(
                in header,
                NetworkRelayType.All,
                1,
                0u,
                0,
                in text,
                out var app));

        using var bytes = new NativeArray<byte>(app.length, Allocator.Temp);
        Assert.IsTrue(app.TryCopyTo(bytes));
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadSendRelayInviteFromBytes(
                bytes,
                StreamCompressionModel.Default,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default));
        Assert.AreEqual(BotRelayWireTestFixtures.LiveHostUserId, invite.id);
        Assert.AreEqual(1, invite.channel, "squadInviteID must be host relay channel index, not level/stage");
        Assert.AreEqual(0u, invite.levelID);
        Assert.AreEqual(0, invite.stage);
        Assert.AreEqual("776757715", invite.text.ToString());
    }

    [Test]
    [Category("Regression")]
    public void LiveHostInviteApp_Stage691_DoesNotConfuseSquadChannel()
    {
        var header = BotRelayWireTestFixtures.BuildSenderHeader(BotRelayWireTestFixtures.LiveHostUserId);
        var text = new FixedString512Bytes("776757715");
        Assert.IsTrue(
            BotRelayCodec.TryBuildWorldInviteRelayApp(
                in header,
                NetworkRelayType.All,
                1,
                0u,
                691,
                in text,
                out var app));

        using var bytes = new NativeArray<byte>(app.length, Allocator.Temp);
        Assert.IsTrue(app.TryCopyTo(bytes));
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadSendRelayInviteFromBytes(
                bytes,
                StreamCompressionModel.Default,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default));
        Assert.AreEqual(691, invite.stage);
        Assert.AreEqual(1, invite.channel);
    }

    [Test]
    [Category("Regression")]
    public void LiveHostInviteApp_Level691_DoesNotConfuseSquadChannel()
    {
        var header = BotRelayWireTestFixtures.BuildSenderHeader(BotRelayWireTestFixtures.LiveHostUserId);
        var text = new FixedString512Bytes("776757715");
        Assert.IsTrue(
            BotRelayCodec.TryBuildWorldInviteRelayApp(
                in header,
                NetworkRelayType.All,
                1,
                691u,
                0,
                in text,
                out var app));

        using var bytes = new NativeArray<byte>(app.length, Allocator.Temp);
        Assert.IsTrue(app.TryCopyTo(bytes));
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadSendRelayInviteFromBytes(
                bytes,
                StreamCompressionModel.Default,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default));
        Assert.AreEqual(691u, invite.levelID);
        Assert.AreEqual(1, invite.channel);
    }

    [Test]
    [Category("Regression")]
    public void LiveHostInviteWire_Stage691_DrainPreservesStage()
    {
        var header = BotRelayWireTestFixtures.BuildSenderHeader(BotRelayWireTestFixtures.LiveHostUserId);
        var text = new FixedString512Bytes("776757715");
        Assert.IsTrue(
            BotRelayCodec.TryBuildWorldInviteRelayApp(
                in header,
                NetworkRelayType.All,
                1,
                0u,
                691,
                in text,
                out var app));

        var wire = BotRelayWireTestFixtures.WrapPipelineFramedApp(
            BotRelayWireTestFixtures.PipelineHeader,
            in app);

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out var rawShell),
                "extractShell");
            Assert.IsTrue(
                BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(
                    in rawShell,
                    out var rawInvite,
                    BotRelayMessageUtility.InviteReadOptions.Default),
                "rawShell parse");
            Assert.AreEqual(691, rawInvite.stage, "extractShell");

            Assert.IsTrue(
                BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                    in wire,
                    ref catalog,
                    out var decoded,
                    out _));
            Assert.IsTrue(
                BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(
                    in decoded,
                    out var decodedInvite,
                    BotRelayMessageUtility.InviteReadOptions.Default));
            Assert.AreEqual(691, decodedInvite.stage, "decodeWire");

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);

            Assert.Greater(session.farm.inboundCount[0], 0, "inbound after drain");
            var inbound = session.farm.inbound[0];
            Assert.IsTrue(
                BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(
                    in inbound,
                    out var inboundInvite,
                    BotRelayMessageUtility.InviteReadOptions.Default),
                "inbound invite parse");
            Assert.AreEqual(691, inboundInvite.stage, "inbound after drain");
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void LiveHostInviteWire_Stage691_FullPipeline_JoinsChannel1()
    {
        var header = BotRelayWireTestFixtures.BuildSenderHeader(BotRelayWireTestFixtures.LiveHostUserId);
        var text = new FixedString512Bytes("776757715");
        Assert.IsTrue(
            BotRelayCodec.TryBuildWorldInviteRelayApp(
                in header,
                NetworkRelayType.All,
                1,
                0u,
                691,
                in text,
                out var app));

        var wire = BotRelayWireTestFixtures.WrapPipelineFramedApp(
            BotRelayWireTestFixtures.PipelineHeader,
            in app);

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f, 0f));

            Assert.AreEqual(BotState.PendingInvite, session.farm.agentStates[0].state);
            Assert.AreEqual(1u, session.farm.agentStates[0].pendingInvite.squadInviteID);
            Assert.AreEqual(691, session.farm.agentStates[0].playStageIndex);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void McpCapturedInviteWire_DebugPipelineExtractSteps()
    {
        var wire = BotRelayWireTestFixtures.BuildMcpCapturedInvite104WirePacket();
        Assert.IsTrue(BotRelayWireBytes.LooksLikePipelineWire(in wire), "step=pipelineWire");
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryGetMcpCapturedInvitePayload(out var payload),
            "step=payload");
        Assert.IsTrue(
            BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out var raw),
            "step=extractShell");
        Assert.IsTrue(BotRelayInviteDecodeOps.IsValidInviteApp(in raw), "step=validApp");
        Assert.IsTrue(
            BotRelayMessageUtility.TryCanonicalizeInviteAppToSendRelay(in raw, out var canonical),
            "step=canonicalize");
        Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(in canonical, out int type));
        Assert.AreEqual((int)ReplyMessageType.Invite, type);
    }

    [Test]
    [Category("Regression")]
    public void McpCapturedInvitePayload_CanonicalizesToSendRelayChannel0()
    {
        var wire = BotRelayWireTestFixtures.BuildMcpCapturedInvite104WirePacket();
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                    in wire,
                    ref catalog,
                    out var sendRelay,
                    out _),
                "Live MCP shell must decode and canonicalize to SendRelay Invite.");
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(in sendRelay, out int type));
            Assert.AreEqual((int)ReplyMessageType.Invite, type);
            Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in sendRelay, out var squadId));
            Assert.AreEqual(0u, squadId, "Live Play host squad channel after Create.");
            Assert.IsTrue(
                BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(
                    in sendRelay,
                    out var invite,
                    BotRelayMessageUtility.InviteReadOptions.Default));
            Assert.AreEqual(BotRelayWireTestFixtures.LiveHostUserId, invite.id);
            Assert.AreEqual(NetworkRelayType.All, invite.type);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void LatestPlayCaptureInvite104_ReachesPendingInvite()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite104WirePacket();
        Assert.AreEqual(104, wire.length);
        Assert.IsTrue(
            BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out var raw),
            "extractShell");
        Assert.IsTrue(BotRelayInviteDecodeOps.IsValidInviteApp(in raw), "validRawApp");
        Assert.IsTrue(
            BotRelayMessageUtility.TryCanonicalizeInviteAppToSendRelay(in raw, in wire, out var canonical),
            "canonicalize");
        Assert.IsFalse(canonical.IsEmpty, "canonicalApp");
        Assert.IsTrue(BotRelayWireBytes.LooksLikePipelineWire(in wire), "pipelineWire");
        Assert.IsTrue(BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire), "inviteShell");

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                    in wire,
                    ref catalog,
                    out var decoded,
                    out int decodePath),
                "decodeWire");
            Assert.AreEqual(BotRelayInviteDecodeOps.DecodePathPipeline, decodePath);
            Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in decoded, out var squadId));
            Assert.AreEqual(1u, squadId, "Live Play host Create on server channel 1 must round-trip for Join.");

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

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
    [Category("Regression")]
    public void StrictPeeledInvite103_InboxPath_ReadsSquadChannel1()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite103WirePacket();
        Assert.IsTrue(
            BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out var raw),
            "extractShell");
        Assert.IsTrue(
            BotRelayMessageUtility.TryCanonicalizeInviteAppToSendRelay(in raw, in wire, out var canonical),
            "canonicalize");
        Assert.IsFalse(canonical.IsEmpty, "canonicalApp");
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadInviteFromPacket(in canonical, out var invite),
            "inboxRead");
        Assert.AreEqual(1, invite.channel, "103B inner+8 0C00 anchor must resolve squad channel 1 without shell.");
    }

    [Test]
    [Category("Regression")]
    public void LatestPlayCaptureInvite103_ReachesPendingInvite()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite103WirePacket();
        Assert.AreEqual(103, wire.length);
        Assert.IsTrue(
            BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out var raw),
            "extractShell");
        Assert.IsTrue(BotRelayInviteDecodeOps.IsValidInviteApp(in raw), "validRawApp");

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                    in wire,
                    ref catalog,
                    out var decoded,
                    out int decodePath),
                "decodeWire");
            Assert.AreEqual(BotRelayInviteDecodeOps.DecodePathPipeline, decodePath);
            Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in decoded, out var squadId));
            Assert.AreEqual(1u, squadId, "Live Play host Create on server channel 1 must round-trip for Join.");

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

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
    [Category("Regression")]
    public void InviteChannelAuthority_PipelineInnerOffset103_ReadsHostCreateChannel1()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite103WirePacket();
        Assert.IsTrue(
            BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out var raw),
            "extractShell");
        Assert.IsTrue(
            BotRelayWireBytes.TryReadAuthoritativePipelineHostChannel(in raw, out int hostChannel),
            "pipelineInner");
        Assert.AreEqual(1, hostChannel);

        Assert.IsTrue(
            BotRelayMessageUtility.TryCanonicalizeInviteAppToSendRelay(in raw, in wire, out var canonical),
            "canonicalize");
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadInviteFromPacket(in canonical, out var invite),
            "inboxRead");
        Assert.AreEqual(1, invite.channel);
    }

    [Test]
    [Category("Regression")]
    public void InviteChannelAuthority_SessionHostCreateBeforeInvite_ReachesPendingInviteChannel1()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildInboundHostCreateWithRemoteApp(
                1,
                0,
                BotRelayWireTestFixtures.RealPlayerUserId,
                out var createApp),
            "createApp");
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildInviteApp(
                BotRelayWireTestFixtures.RealPlayerUserId,
                0,
                0,
                0,
                out var inviteApp),
            "inviteApp with body channel 0");

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayIntegrationTestFixtures.BuildNullFramedRelayAppWireBytes(ref session, in createApp));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            ref var botSession = ref session.farm.sessions.ElementAt(0);
            Assert.AreEqual(1, botSession.pendingHostSquadChannel, "host Create must arm invite authority");
            Assert.AreEqual(BotState.Idle, session.farm.agentStates[0].state, "idle bot must not join from host Create alone");

            var inviteWire = BotRelayIntegrationTestFixtures.BuildNullFramedRelayAppWireBytes(ref session, in inviteApp);
            BotRelayIntegrationTestFixtures.RouteServerToBot(inviteWire);
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

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
    [Category("Regression")]
    public void InviteChannelAuthority_SessionHostCreateZero_KeepsInviteChannelZero()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildInboundHostCreateWithRemoteApp(
                0,
                0,
                BotRelayWireTestFixtures.RealPlayerUserId,
                out var createApp),
            "createApp");
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildInviteApp(
                BotRelayWireTestFixtures.RealPlayerUserId,
                0,
                0,
                0,
                out var inviteApp),
            "inviteApp");

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayIntegrationTestFixtures.BuildNullFramedRelayAppWireBytes(ref session, in createApp));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.AreEqual(0, session.farm.sessions.ElementAt(0).pendingHostSquadChannel);
            Assert.AreEqual(BotState.Idle, session.farm.agentStates[0].state);

            var inviteWire = BotRelayIntegrationTestFixtures.BuildNullFramedRelayAppWireBytes(ref session, in inviteApp);
            BotRelayIntegrationTestFixtures.RouteServerToBot(inviteWire);
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f, 0f));

            Assert.AreEqual(BotState.PendingInvite, session.farm.agentStates[0].state);
            Assert.AreEqual(0u, session.farm.agentStates[0].pendingInvite.squadInviteID);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InviteChannelAuthority_StructuredPositiveChannel_IsNotOverriddenByEmbeddedPrefix()
    {
        var payload = default(BotRelayPacket);
        byte[] bytes =
        {
            0x04, 0xB9, 0x08, 0x00, 0x00, 0x17, 0x59, 0x00, 0x00, 0x0C, 0x00,
            0xE6, 0x9D, 0x80, 0xE9, 0xB1, 0xBC, 0xE6, 0xA0, 0xBC, 0xE6, 0xA0, 0xBC
        };
        payload.length = bytes.Length;
        for (int i = 0; i < bytes.Length; ++i)
        {
            payload.SetByte(i, bytes[i]);
        }

        Assert.IsTrue(
            BotRelayWireBytes.TryScanPipelineEmbeddedHostCreateChannel(in payload, out int hostChannel),
            "embeddedCreate");
        Assert.AreEqual(0, hostChannel);

        var invite = default(ReplyMessages.Invite);
        invite.channel = 1;
        invite.id = BotRelayWireTestFixtures.RealPlayerUserId;
        BotRelayInviteChannelAuthority.TryResolveSquadChannel(ref invite, in payload, default);
        Assert.AreEqual(
            1,
            invite.channel,
            "A successfully decoded positive Invite channel is authoritative; the raw payload scan can match unrelated body bytes.");
    }

    [Test]
    [Category("Regression")]
    public void InviteChannelAuthority_ChannelZero_IgnoresJunkAnchorWhenEmbeddedCreatePresent()
    {
        var payload = default(BotRelayPacket);
        byte[] bytes =
        {
            0x04, 0xB9, 0x08, 0x00, 0x00, 0x17, 0x59, 0x00, 0x00, 0x0C, 0x00,
            0xE6, 0x9D, 0x80, 0xE9, 0xB1, 0xBC, 0xE6, 0xA0, 0xBC, 0xE6, 0xA0, 0xBC
        };
        payload.length = bytes.Length;
        for (int i = 0; i < bytes.Length; ++i)
        {
            payload.SetByte(i, bytes[i]);
        }

        var invite = default(ReplyMessages.Invite);
        invite.channel = 0;
        invite.id = BotRelayWireTestFixtures.RealPlayerUserId;
        BotRelayInviteChannelAuthority.TryResolveSquadChannel(ref invite, in payload, default);
        Assert.AreEqual(0, invite.channel, "embedded Create ch0 must not be upgraded by 0C00 junk anchor.");
    }

    [Test]
    [Category("Regression")]
    public void McpCapturedInvitePayload_RejectsInnerZeroMisalignedPrefix()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildMcpCapturedInviteJunkPayload(out var payload),
            "fixture payload");

        var misaligned = default(BotRelayPacket);
        misaligned.length = payload.length;
        for (int i = 0; i < payload.length; ++i)
        {
            misaligned.SetByte(i, payload.GetByte(i));
        }

        Assert.IsFalse(
            BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in misaligned, out _),
            "6-byte transport prefix before SendRelay header must not validate at inner=0.");
    }

    [Test]
    [Category("Regression")]
    public void MisalignedInviteSlice_FailsStrictSendRelayValidation()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildInviteApp(
                BotRelayWireTestFixtures.LiveHostUserId,
                1,
                691u,
                691,
                out var app));

        Assert.IsTrue(BotRelayWireBytes.TryValidateSendRelayInviteApp(in app));

        var shifted = default(BotRelayPacket);
        shifted.length = app.length;
        for (int i = 0; i < app.length - 1; ++i)
            shifted.SetByte(i, app.GetByte(i + 1));

        Assert.IsFalse(
            BotRelayWireBytes.TryValidateSendRelayInviteApp(in shifted),
            "Inner-offset false positives must fail once FixedString512 length is bounds-checked.");
    }

    [Test]
    [Category("Regression")]
    public void TryValidatePopEventsTransportStream_RejectsBogusUshortLength()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(new byte[] { 0x3F, 0x4C, 0x01, 0x02 });
        Assert.IsFalse(BotRelayWireBytes.TryValidatePopEventsTransportStream(in wire));
    }

    [Test]
    [Category("Regression")]
    public void BurstDrainInbound_RespectsPacketBudget_LeavesRemainderInQueue()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var manager = ref BotRelayManager.Instance;
            int exhaustBefore = manager.diagDrainBudgetExhausted;
            int processedBefore = manager.diagDrainPacketsBudget;

            var keepAlive = new byte[] { 0x01 };
            int total = BotRelayJobLimits.MaxBotPacketsPerDrainJob + 32;
            for (int i = 0; i < total; ++i)
                BotRelayIntegrationTestFixtures.RouteServerToBot(keepAlive);

            Assert.IsTrue(manager.HasInboundForBotBurst(BotRelayIntegrationSession.BotVirtualPort));

            BotRelayIntegrationTestFixtures.DrainInbound(ref session);

            int processed = manager.diagDrainPacketsBudget - processedBefore;
            Assert.LessOrEqual(processed, BotRelayJobLimits.MaxBotPacketsPerDrainJob);
            Assert.Greater(processed, 0);
            Assert.Greater(manager.diagDrainBudgetExhausted, exhaustBefore);
            Assert.IsTrue(
                manager.HasInboundForBotBurst(BotRelayIntegrationSession.BotVirtualPort),
                "DrainInbound must stop at budget and leave remainder for the next tick.");
        }
        finally
        {
            session.Dispose();
        }
    }
}
