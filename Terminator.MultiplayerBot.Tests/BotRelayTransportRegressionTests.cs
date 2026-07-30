using NUnit.Framework;

using Unity.Entities;
using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Regression for L2 transport framing (PopEvents ushort + payload) — not app field parsing.
/// </summary>
public class BotRelayTransportRegressionTests
{

[Test]
    [Category("Regression")]
    public void PerAgentConnectCatalog_PreservesDedicatedHandshake()
    {
        var catalog = default(BotRelayWireCatalogState);
        var dedicated = new BotRelayConnectWireState[2];
        BlobAssetReference<BotRelayCatalogBlob> blob = default;
        try
        {
            catalog.connectWire.clientPackets = new NativeList<BotRelayConnectWirePacket>(1, Allocator.Temp);
            catalog.connectWire.clientPackets.Add(new BotRelayConnectWirePacket
            {
                wire = BotRelayWireTestFixtures.FromBytes(new byte[] { 0xC0, 0x13 })
            });
            catalog.connectClientWireCount = 1;

            dedicated[1].clientPackets = new NativeList<BotRelayConnectWirePacket>(1, Allocator.Temp);
            dedicated[1].clientPackets.Add(new BotRelayConnectWirePacket
            {
                wire = BotRelayWireTestFixtures.FromBytes(new byte[] { 0xC0, 0x14 })
            });

            blob = BotRelayCatalogBlobBuilder.Create(in catalog, dedicated);
            Assert.IsTrue(blob.IsCreated);
            ref var blobCatalog = ref blob.Value;
            Assert.AreEqual(2, blobCatalog.agentConnectClientWireRanges.Length);

            var firstRange = blobCatalog.agentConnectClientWireRanges[0];
            var secondRange = blobCatalog.agentConnectClientWireRanges[1];
            Assert.AreEqual(1, firstRange.count);
            Assert.AreEqual(1, secondRange.count);

            ref var firstWire = ref BotRelayWireBytes.GetWire(
                ref blobCatalog,
                blobCatalog.agentConnectClientWires[firstRange.offset].wireIndex);
            ref var secondWire = ref BotRelayWireBytes.GetWire(
                ref blobCatalog,
                blobCatalog.agentConnectClientWires[secondRange.offset].wireIndex);
            Assert.AreEqual(0x13, firstWire.bytes[1]);
            Assert.AreEqual(0x14, secondWire.bytes[1]);
        }
        finally
        {
            if (blob.IsCreated)
                blob.Dispose();

            catalog.Dispose();
            dedicated[1].Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void CanonicalInviteWire_PopEventsFraming_ExtractsNormalizedInviteApp()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        using var logs = BotRelayWireTestAsserts.LogScope.Begin();
        Assert.IsTrue(BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var app));
        BotRelayWireTestAsserts.AssertNoStreamOverreadErrors(logs, "TryExtractAppViaTransportFraming canonical invite");

        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out var squadId));
        Assert.AreEqual(1u, squadId);
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            BotRelayWireTestFixtures.RealPlayerUserId,
            1,
            0,
            0,
            out var canonicalApp));
        Assert.AreEqual(canonicalApp.length, app.length);
    }

    [Test]
    [Category("Regression")]
    public void CanonicalInviteWire_PopEventsFraming_ReadsInviteTypeFromPayload()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        Assert.IsTrue(BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var app));

        using var bytes = new NativeArray<byte>(app.length, Allocator.Temp);
        app.TryCopyTo(bytes);
        var reader = new DataStreamReader(bytes);
        int type = reader.ReadPackedInt(StreamCompressionModel.Default);
        Assert.AreEqual((int)ReplyMessageType.Invite, type);
    }

    [Test]
    [Category("Regression")]
    public void RelayEntry_WithoutOffset_FallsBackToDeterministicFraming()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);

        // The transport-framing helper recovers the app on its own.
        Assert.IsTrue(BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var viaFraming));
        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in viaFraming, out var squadId));
        Assert.AreEqual(1u, squadId);

        // Null-pipeline design (see BotRelayPipeline): the runtime relay decode strips a single
        // fixed offset, then falls back to the protocol-fixed PopEvents framing walk — never the
        // retired per-message heuristic cascade. With no offset it must still recover the invite
        // through framing (not scan for it), and the result must match the framing helper exactly.
        Assert.IsTrue(
            BotRelayWireBytes.TryExtractRelayAppPayload(in wire, scratch, 0, default, -1, out var viaRelay),
            "TryExtractRelayAppPayload must recover the app via deterministic framing when no offset is known.");
        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in viaRelay, out var relaySquadId));
        Assert.AreEqual(squadId, relaySquadId);
        Assert.AreEqual(viaFraming.length, viaRelay.length);
    }

    [Test]
    [Category("Regression")]
    public void CanonicalInviteWire_PopEventsFraming_ParsesSquadId1()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        using var logs = BotRelayWireTestAsserts.LogScope.Begin();

        Assert.IsTrue(BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var app));
        BotRelayWireTestAsserts.AssertNoStreamOverreadErrors(logs, "Canonical invite transport framing");

        Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out var squadId));
        Assert.AreEqual(1u, squadId);
    }

    [Test]
    [Category("Regression")]
    public void FramedJoinApp_PopEventsLoop_ReadsType6()
    {
        Assert.IsTrue(
            BotRelayWireTestFixtures.TryBuildOutboundSquadJoinApp(1u, out var joinApp),
            "Join app fixture required.");

        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        Assert.IsTrue(
            BotRelayCodec.TryBuildPopEventsListenPacket(in joinApp, scratch, 0, out var framed),
            "TryBuildPopEventsListenPacket failed.");
        Assert.IsTrue(
            BotRelayWireBytes.TryUnpackPopEventsListenFrame(in framed, out var app),
            "Framed listen wire must unpack as PopEvents ushort frame.");
        Assert.IsTrue(BotRelayWireBytes.PopEventsParsesJoinRelayApp(in framed));

        using var payload = new NativeArray<byte>(framed.length, Allocator.Temp);
        framed.TryCopyTo(payload);
        var reader = new DataStreamReader(payload);
        int messageSize = reader.ReadUShort();
        using var bytes = new NativeArray<byte>(messageSize, Allocator.Temp);
        reader.ReadBytes(bytes);
        var stream = new DataStreamReader(bytes);
        int type = stream.ReadPackedInt(StreamCompressionModel.Default);
        Assert.AreEqual((int)NetworkRelayMessageType.Join, type);
    }

    [Test]
    [Category("Regression")]
    public void PipelinePrefix_IsEightBytesOnCanonicalInviteWire()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        Assert.IsTrue(BotRelayWireBytes.LooksLikePipelineWire(in wire));
        Assert.AreEqual(0x01, wire.GetByte(0));
        Assert.AreEqual(BotRelayWireBytes.PipelinePrefixBytes, 8);
    }

    [Test]
    [Category("Regression")]
    public void PopEventsWalk_TwoFramedApps_ExtractsBoth()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            const int stageId = 213;
            int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                              (int)NetworkRelayChannelFlag.Online;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayChapterStageApp(
                BotRelayFlowTestFixtures.RealPlayerUserId,
                stageId,
                out var chapterApp));
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayStatusApp(
                channelFlag,
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var statusApp));
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedMultiAppInboundWire(
                ref session.catalogBlob.Value,
                in chapterApp,
                in statusApp,
                out var wire));

            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsTrue(BotRelayPopEventsWalkOps.TryWalkServerToBotApps(
                in wire,
                ref batch,
                out bool fullyConsumed));
            Assert.AreEqual(2, batch.appCount);
            Assert.IsTrue(fullyConsumed);
            var firstApp = batch.GetApp(0);
            var secondApp = batch.GetApp(1);
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageType(in firstApp, out int firstType));
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageType(in secondApp, out int secondType));
            Assert.AreEqual((int)ClientMessageType.ChapterStage, firstType);
            Assert.AreEqual((int)NetworkRelayMessageType.Status, secondType);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_MultiFrameConnectShaped_FullyConsumed_SkipsRawQueue()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            const int stageId = 213;
            int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                              (int)NetworkRelayChannelFlag.Online;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayChapterStageApp(
                BotRelayFlowTestFixtures.RealPlayerUserId,
                stageId,
                out var chapterApp));
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayStatusApp(
                channelFlag,
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var statusApp));
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedMultiAppInboundWire(
                ref session.catalogBlob.Value,
                in chapterApp,
                in statusApp,
                out var wire));

            ref var manager = ref BotRelayManager.Instance;
            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            Assert.IsFalse(
                manager.HasInboundForBotBurst(BotRelayIntegrationSession.BotVirtualPort),
                "Fully consumed peel must not leave raw wire in m_Queue.");
            Assert.Greater(manager.diagRouteSendPeeled, 0);

            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            Assert.GreaterOrEqual(session.farm.inboundCount[0], 2);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_NullFramedInvite_WithPeelCatalog_PeesType104_ReachesPendingInvite()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            var wireBytes = BotRelayIntegrationTestFixtures.BuildNullFramedInviteWireBytes(ref session);
            Assert.GreaterOrEqual(wireBytes.Length, 80);
            Assert.LessOrEqual(wireBytes.Length, 160);

            BotRelayIntegrationTestFixtures.RouteServerToBot(wireBytes);

            ref var manager = ref BotRelayManager.Instance;
            Assert.Greater(manager.diagRouteSendPeeled, 0, "Null-framed invite must peel via catalog offset, not spurious PopEvents walk.");
            Assert.IsFalse(
                manager.HasInboundForBotBurst(BotRelayIntegrationSession.BotVirtualPort),
                "Fully consumed invite peel must not leave raw wire in m_Queue.");

            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            Assert.Greater(session.farm.inboundCount[0], 0);

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

    /// <summary>Live Play 103B invite shell decodes through unified <see cref="BotRelayInviteDecodeOps"/>.</summary>
    [Test]
    [Category("Regression")]
    public void LivePlayPipelineInviteShell103_ExtractsType104AndSquadChannel1()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite103WirePacket();
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                    in wire,
                    ref catalog,
                    out var app,
                    out int decodePath),
                "103B live Play invite shell must decode.");
            Assert.AreEqual(BotRelayInviteDecodeOps.DecodePathPipeline, decodePath);
            Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out var squadId));
            Assert.AreEqual(1u, squadId, "Live Play host Create on server channel 1 must round-trip for Join.");
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void LivePlayPipelineInviteShell_ExtractsType104AndSquadChannel1()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite104WirePacket();
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                    in wire,
                    ref catalog,
                    out var app,
                    out int decodePath),
                "Live Play pipeline invite shell must decode via unified decoder.");
            Assert.AreEqual(BotRelayInviteDecodeOps.DecodePathPipeline, decodePath);
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int relayType));
            Assert.AreEqual((int)ReplyMessageType.Invite, relayType);
            Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out var squadId));
            Assert.AreEqual(1u, squadId, "Live Play host Create on server channel 1 must round-trip for Join.");
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void McpCapturedInvite104Wire_DecodesViaUnifiedInviteDecoder()
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
                    out var app,
                    out int decodePath),
                "MCP-captured 104B invite shell must decode.");
            Assert.IsTrue(
                decodePath == BotRelayInviteDecodeOps.DecodePathPipeline ||
                decodePath == BotRelayInviteDecodeOps.DecodePathMappingExact ||
                decodePath == BotRelayInviteDecodeOps.DecodePathMappingOffset ||
                decodePath == BotRelayInviteDecodeOps.DecodePathCatalogOffset);
            Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out var squadId));
            Assert.AreEqual(0u, squadId, "MCP session invite shell uses host squad channel 0 after Create.");
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void TargetedInviteFlushBoundary_DecodesActualServerRelayShape()
    {
        var wire = BotRelayWireTestFixtures.BuildTargetedInviteFlushBoundaryWirePacket();
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                    in wire,
                    ref catalog,
                    out var app,
                    out int decodePath),
                "Targeted Invite copied from a flushed ReplyMessages body must decode.");
            Assert.AreEqual(BotRelayInviteDecodeOps.DecodePathPipeline, decodePath);
            Assert.IsTrue(BotRelayCodec.TryParseWorldInviteRelayApp(in app, out uint squadChannel));
            Assert.AreEqual(5u, squadChannel);
        }
        finally
        {
            session.Dispose();
        }
    }


    [Test]
    [Category("Regression")]
    public void RoutePeel_InviteStructureDecodeFail_DeferredNotPopEventsPoison()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite103WirePacket();
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayInviteDecodeOps.ShouldBlockGenericShellWalk(in wire),
                "Live invite shell must block generic PopEventsWalk on decode failure.");
            Assert.IsFalse(
                BotRelayInviteDecodeOps.ShouldFallThroughToGenericShellWalk(in wire),
                "Structural invite must not fall through to generic PopEventsWalk.");

            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsTrue(
                BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                    in wire,
                    ref catalog,
                    ref batch,
                    out bool fullyConsumed,
                    out int peelPath),
                "Invite shell must peel via unified decoder.");
            Assert.AreEqual(BotRelayRoutePeelOps.PeelPathPipelineInvite, peelPath);
            Assert.IsTrue(fullyConsumed);
            Assert.AreEqual(1, batch.appCount);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_JoinShell_DefersRawWireForDrainPopEventsWalk()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            const uint squadChannel = 0;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundJoinApp(
                squadChannel,
                BotRelayWireTestFixtures.RealPlayerUserId,
                out var joinApp));
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref session.catalogBlob.Value,
                in joinApp,
                out var wire));
            if (BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire))
            {
                Assert.IsTrue(
                    BotRelayInviteDecodeOps.ShouldFallThroughToGenericShellWalk(in wire),
                    "Invite-sized Join shell must not block generic walk.");
            }

            ref var catalog = ref session.catalogBlob.Value;
            var batch = default(BotRelayPopEventsWalkBatch);
            bool peeled = BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                in wire,
                ref catalog,
                ref batch,
                out bool fullyConsumed,
                out int peelPath);
            if (peeled)
            {
                Assert.IsTrue(fullyConsumed, "Join shell peel must consume the PopEvents stream.");
                Assert.Greater(batch.appCount, 0);
                var peeledJoinApp = batch.GetApp(0);
                Assert.IsTrue(
                    BotRelayWireBytes.TryReadRelayMessageTypeHeader(in peeledJoinApp, out int relayType));
                Assert.AreEqual((int)NetworkRelayMessageType.Join, relayType);
            }

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref session.farm, out var evt));
            Assert.AreEqual(BotRelayEventType.SquadJoin, evt.type);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_MatchHostPlayWithRankText_RemainsPlayInsteadOfFalseInvite()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundPlayApp(
                22,
                0,
                1126954459u,
                new FixedString32Bytes("青铜III"),
                new FixedString32Bytes("Scenes/Level4-1.scene"),
                out var playApp));
            var wire = BotRelayWireTestFixtures.WrapPipelineFramedApp(
                BotRelayWireTestFixtures.PipelineHeader,
                in playApp);

            ref var catalog = ref session.catalogBlob.Value;
            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsTrue(BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                in wire,
                ref catalog,
                ref batch,
                out bool fullyConsumed,
                out _));
            Assert.IsTrue(fullyConsumed);
            Assert.AreEqual(1, batch.appCount);

            var peeled = batch.GetApp(0);
            Assert.LessOrEqual(
                peeled.length,
                wire.length,
                "A normalized app cannot be larger than its source wire; the old false-Invite path rebuilt 55B Play as a 97B Invite.");
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(in peeled, out int relayType));
            Assert.AreEqual((int)ClientMessageType.Play, relayType);

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref session.farm, out var evt));
            Assert.AreEqual(BotRelayEventType.Play, evt.type);
            Assert.AreEqual(22u, evt.play.levelID);
            Assert.AreEqual("青铜III", evt.play.levelName.ToString());
            Assert.AreEqual("Scenes/Level4-1.scene", evt.play.sceneName.ToString());
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_MatchStart_PreservesServerStageAndApplyStartTuple()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMatchStartApp(
                9,
                210,
                22,
                0,
                4655,
                new FixedString32Bytes("青铜III"),
                new FixedString32Bytes("Scenes/Level3-1.scene"),
                out var matchStartApp));
            Assert.IsTrue(
                BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                    ref session.catalogBlob.Value,
                    in matchStartApp,
                    out var matchStartWire),
                "MatchStart regression must use the captured server pipeline shell, not a zero prefix.");
            var peelBatch = default(BotRelayPopEventsWalkBatch);
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(
                BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                    in matchStartWire,
                    ref catalog,
                    ref peelBatch,
                    out bool fullyConsumed,
                    out int peelPath),
                $"MatchStart pipeline shell must peel (wire={matchStartWire.length}, path={peelPath}, fullyConsumed={fullyConsumed}).");
            Assert.Greater(peelBatch.appCount, 0);
            var peeledMatchStartApp = peelBatch.GetApp(0);
            Assert.IsTrue(
                BotRelayWireBytes.TryReadRelayMessageTypeHeader(in peeledMatchStartApp, out int peeledType),
                $"Peeled MatchStart must retain a readable header (path={peelPath}).");
            Assert.AreEqual(
                (int)ClientMessageType.MatchStart,
                peeledType,
                $"Pipeline normalization selected the wrong app (path={peelPath}, appLen={peeledMatchStartApp.length}).");
            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayWireTestFixtures.ToBytes(in matchStartWire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            BotRelayEvent evt = default;
            bool foundMatchStart = false;
            while (BotRelaySlotInbox.TryDequeueEvent(0, ref session.farm, out evt))
            {
                if (evt.type == BotRelayEventType.MatchStart)
                {
                    foundMatchStart = true;
                    break;
                }
            }

            Assert.IsTrue(foundMatchStart, "The routed app queue must contain MatchStart.");
            Assert.AreEqual(9, evt.matchStart.matchID);
            Assert.AreEqual(210u, evt.matchStart.userStageID);
            Assert.AreEqual(22u, evt.matchStart.levelID);
            Assert.AreEqual("青铜III", evt.matchStart.levelName.ToString());
            Assert.AreEqual("Scenes/Level3-1.scene", evt.matchStart.sceneName.ToString());
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_QueryHeaderShell_SelectsExactFrameAndReleasesFreshMatchStage()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            const int matchChannel = 5;
            const int stageId = 210;
            int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                              (int)NetworkRelayChannelFlag.Online |
                              (int)NetworkRelayChannelFlag.Creator;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayQueryHeaderApp(
                matchChannel,
                channelFlag,
                BotRelayWireTestFixtures.RealPlayerUserId,
                out var queryApp));
            var wire = BotRelayWireTestFixtures.WrapPipelineFramedApp(
                BotRelayWireTestFixtures.PipelineHeader,
                in queryApp);
            Assert.IsTrue(
                BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire),
                "The live Query + ClientHeader response shares the 80..160B pipeline-shell range.");

            ref var state = ref session.farm.agentStates.ElementAt(0);
            state.matchPaired = true;
            state.awaitingFreshMatchStage = true;
            state.targetUserStageID = 0;
            var relaySession = session.farm.sessions[0];
            relaySession.channel = matchChannel;
            relaySession.remoteChannelStatus = 0;
            session.farm.sessions[0] = relaySession;

            ref var catalog = ref session.catalogBlob.Value;
            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsTrue(BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                in wire,
                ref catalog,
                ref batch,
                out bool fullyConsumed,
                out int peelPath));
            Assert.IsTrue(fullyConsumed, "The selected PopEvents stream must consume the Query shell exactly.");
            Assert.AreEqual(BotRelayRoutePeelOps.PeelPathPopEventsWalk, peelPath);
            Assert.AreEqual(1, batch.appCount);
            var peeledQueryApp = batch.GetApp(0);
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(
                in peeledQueryApp,
                out int relayType));
            Assert.AreEqual((int)NetworkRelayMessageType.Query, relayType);

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.AreEqual(stageId, session.farm.sessions[0].remoteChannelStatus);
            Assert.IsFalse(session.farm.agentStates[0].awaitingFreshMatchStage);
            Assert.AreEqual((uint)stageId, session.farm.agentStates[0].targetUserStageID);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void LivePlayInvite103_PopEventsWalk_ReadsHostChannel1()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite103WirePacket();
        var batch = default(BotRelayPopEventsWalkBatch);
        Assert.IsTrue(
            BotRelayPopEventsWalkOps.TryWalkServerToBotApps(in wire, ref batch, out bool fullyConsumed),
            "103B live wire must walk via PopEvents framing.");
        Assert.IsTrue(fullyConsumed);
        Assert.Greater(batch.appCount, 0);
        var app = batch.GetApp(0);
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadInviteFromPacket(
                in app,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default));
        Assert.AreEqual(1, invite.channel, "Host Create on server channel 1 must round-trip through L2+L1.");
    }

    [Test]
    [Category("Regression")]
    public void LivePlayInvite104_JunkEnvelope_PopEventsWalk_SelectsInviteStreamStart()
    {
        var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite104WirePacket();
        var batch = default(BotRelayPopEventsWalkBatch);
        Assert.IsTrue(
            BotRelayPopEventsWalkOps.TryWalkServerToBotApps(in wire, ref batch, out bool fullyConsumed),
            "104B live wire with junk envelope at offset 8 must walk.");
        Assert.IsTrue(fullyConsumed);
        Assert.IsTrue(
            BotRelayPopEventsWalkOps.BatchContainsInvite(in batch),
            "Stream start must resolve to Invite(104), not junk type=1 at offset 8.");
        Assert.Greater(batch.appCount, 0);
        var app = batch.GetApp(0);
        Assert.IsTrue(
            BotRelayMessageUtility.TryReadInviteFromPacket(
                in app,
                out var invite,
                BotRelayMessageUtility.InviteReadOptions.Default));
        Assert.AreEqual(1, invite.channel, "104B junk-anchored reply prefix carries server squad index 1.");
    }

    [Test]
    [Category("Regression")]
    public void LivePlayPipelineInviteShell_RoutePeel_ReachesPendingInvite()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            var wire = BotRelayWireTestFixtures.BuildLatestPlayCaptureInvite104WirePacket();

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
}
