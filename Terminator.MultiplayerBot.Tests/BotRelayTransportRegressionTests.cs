using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Current L2 contract: one capture-measured prefix plus NetworkSendBuffer ushort framing.
/// </summary>
public class BotRelayTransportRegressionTests
{
    [Test]
    [Category("Regression")]
    public void CurrentClientApplyMatch_EmptyStructStorage_RelaysBodylessAndAgentReplies()
    {
        using var savedStructBytes = new NativeArray<byte>(
            UnsafeUtility.SizeOf<ClientMessageApplyMatch>(),
            Allocator.Temp,
            NativeArrayOptions.ClearMemory);
        Assert.AreEqual(1, savedStructBytes.Length,
            "An empty unmanaged message still occupies one byte; that storage is not protocol payload.");

        using var clientBytes = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var clientWriter = new DataStreamWriter(clientBytes);
        ClientMessageRelayWire.WriteChannelMessage(
            ref clientWriter,
            ClientMessageType.ApplyMatch,
            in savedStructBytes);

        var model = StreamCompressionModel.Default;
        var clientReader = new DataStreamReader(
            clientBytes.GetSubArray(0, clientWriter.Length));
        int type = clientReader.ReadPackedInt(model);
        int relayType = clientReader.ReadPackedInt(model);
        Assert.AreEqual((int)ClientMessageType.ApplyMatch, type);
        Assert.AreEqual((int)NetworkRelayType.Channel, relayType);
        Assert.AreEqual(clientReader.Length, clientReader.GetBytesRead(),
            "ApplyMatch must have no client body after its relay header.");

        using var serverBytes = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var serverWriter = new DataStreamWriter(serverBytes);
        NetworkRelayServerIdentity.SendRelay(
            type,
            relayType,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            ref clientReader,
            ref serverWriter);

        var rawApp = default(BotRelayPacket);
        rawApp.length = serverWriter.Length;
        for (int i = 0; i < serverWriter.Length; ++i)
            rawApp.SetByte(i, serverBytes[i]);

        Assert.IsTrue(BotRelayWireBytes.TryAcceptCurrentInboundApp(
            in rawApp,
            out var acceptedApp));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            farm.agentStates[0] = state;

            var session = farm.sessions[0];
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            session.remoteOnline = true;
            farm.sessions[0] = session;

            BotRelaySlotInbox.ParseTransportPayload(
                in acceptedApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig();
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.IsTrue(injects.TryDequeue(out var applyMatchReply));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(
                in applyMatchReply,
                out int replyType));
            Assert.AreEqual((int)ClientMessageType.ApplyMatch, replyType);
            Assert.AreEqual(0, injects.Count);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void CurrentBodylessApplyMatch_SendRelayEnvelope_IsAccepted()
    {
        var expected = BotRelayFlowTestFixtures.BuildApplyMatchApp();

        Assert.IsTrue(BotRelayWireBytes.TryAcceptCurrentInboundApp(
            in expected,
            out var actual));
        Assert.IsTrue(BotRelayWireBytes.Equals(in expected, in actual));
    }

    [Test]
    [Category("Regression")]
    public void CurrentConnectApp_KnownRemotePresence_IsRestored()
    {
        const int stageId = 213;
        int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                          (int)NetworkRelayChannelFlag.Online;
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayConnectApp(
            channelFlag,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            out var connectApp));
        Assert.IsTrue(BotRelayWireBytes.TryAcceptCurrentInboundApp(
            in connectApp,
            out var accepted));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 12;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            session.remoteChannelStatus = 0;
            session.remoteOnline = false;
            farm.sessions[0] = session;

            BotRelaySlotInbox.ParseTransportPayload(
                in accepted,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.AreEqual(stageId, farm.sessions[0].remoteChannelStatus);
            Assert.IsTrue(farm.sessions[0].remoteOnline);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void CurrentStrictApps_TrailingGarbage_IsRejected()
    {
        var applyMatch = BotRelayFlowTestFixtures.BuildApplyMatchApp();
        __AssertTrailingGarbageRejected(in applyMatch, "ApplyMatch");

        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundPlayApp(
            22,
            0,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            new FixedString32Bytes("Bronze"),
            new FixedString32Bytes("Scenes/Level3-1.scene"),
            out var play));
        __AssertTrailingGarbageRejected(in play, "Play");

        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMatchStartApp(
            9,
            210,
            22,
            0,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            new FixedString32Bytes("Bronze"),
            new FixedString32Bytes("Scenes/Level3-1.scene"),
            out var matchStart));
        __AssertTrailingGarbageRejected(in matchStart, "MatchStart");

        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundJoinFailedApp(
            12,
            out var joinFailed));
        __AssertTrailingGarbageRejected(in joinFailed, "JoinFailed");

        int channelFlag = (213 << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                          (int)NetworkRelayChannelFlag.Online;
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayStatusApp(
            channelFlag,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            out var status));
        __AssertTrailingGarbageRejected(in status, "Status");
    }

    [Test]
    [Category("Regression")]
    public void CurrentPlayApp_NonChannelRelayType_IsRejected()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundPlayApp(
            22,
            0,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            NetworkRelayType.All,
            new FixedString32Bytes("Bronze"),
            new FixedString32Bytes("Scenes/Level3-1.scene"),
            out var play));

        Assert.IsFalse(BotRelayWireBytes.TryAcceptCurrentInboundApp(in play, out _));
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_UnframedCurrentInvite_IsRejected()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
            8737,
            0,
            0,
            0,
            out var inviteApp));

        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            ref var catalog = ref session.catalogBlob.Value;
            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsFalse(BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                in inviteApp,
                ref catalog,
                ref batch,
                out bool fullyConsumed));
            Assert.IsFalse(fullyConsumed);
            Assert.AreEqual(0, batch.appCount);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PopEventsWalk_TwoCanonicalFramedApps_ExtractsBoth()
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
                session.catalogBlob.Value.inboundAppPayloadOffset,
                ref batch,
                out bool fullyConsumed));
            Assert.IsTrue(fullyConsumed);
            Assert.AreEqual(2, batch.appCount);

            var first = batch.GetApp(0);
            var second = batch.GetApp(1);
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(
                in first,
                out int firstType));
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(
                in second,
                out int secondType));
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
    public void PopEventsWalk_UnexpectedPrefixLength_IsRejected()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInviteApp(
                8737,
                0,
                0,
                0,
                out var app));
            int measuredStreamStart =
                session.catalogBlob.Value.inboundAppPayloadOffset - sizeof(ushort);
            Assert.GreaterOrEqual(measuredStreamStart, 0);
            var unexpectedPrefix = new byte[measuredStreamStart + 1];
            var wire = BotRelayWireTestFixtures.WrapFramedAppAtPrefix(
                unexpectedPrefix,
                in app);

            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsFalse(BotRelayPopEventsWalkOps.TryWalkServerToBotApps(
                in wire,
                session.catalogBlob.Value.inboundAppPayloadOffset,
                ref batch,
                out bool fullyConsumed));
            Assert.IsFalse(fullyConsumed);
            Assert.AreEqual(0, batch.appCount);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_CanonicalJoinFrame_RemainsJoin()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundJoinApp(
                0,
                BotRelayWireTestFixtures.RealPlayerUserId,
                out var joinApp));
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref catalog,
                in joinApp,
                out var wire));
            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsTrue(BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                in wire,
                ref catalog,
                ref batch,
                out bool fullyConsumed));
            Assert.IsTrue(fullyConsumed);
            Assert.AreEqual(1, batch.appCount);
            var peeled = batch.GetApp(0);
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(
                in peeled,
                out int relayType));
            Assert.AreEqual((int)NetworkRelayMessageType.Join, relayType);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RoutePeel_CurrentHumanPlay_RemainsPlayAndIsDiscardedByBot()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundPlayApp(
                22,
                0,
                1126954459u,
                new FixedString32Bytes("BronzeIII"),
                new FixedString32Bytes("Scenes/Level4-1.scene"),
                out var playApp));
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref catalog,
                in playApp,
                out var wire));
            var batch = default(BotRelayPopEventsWalkBatch);
            Assert.IsTrue(BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
                in wire,
                ref catalog,
                ref batch,
                out bool fullyConsumed));
            Assert.IsTrue(fullyConsumed);
            Assert.AreEqual(1, batch.appCount);
            var peeled = batch.GetApp(0);
            Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(
                in peeled,
                out int relayType));
            Assert.AreEqual((int)ClientMessageType.Play, relayType);

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.IsFalse(BotRelaySlotInbox.TryDequeueEvent(
                0,
                ref session.farm,
                out _));
            Assert.AreEqual(0u, session.farm.levelStartMessages[0].userStageID);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ClientMessagePlay_CurrentHumanBody_RetainsParseContract()
    {
        using var scratch = new NativeArray<byte>(256, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WriteRawBits(0, 1);
        writer.WritePackedUInt(12, model);
        writer.WritePackedInt(0, model);
        writer.WriteFixedString32(new FixedString32Bytes("Rank12"));
        writer.WriteFixedString32(new FixedString32Bytes("Scenes/Level12-1.scene"));

        using var encoded = scratch.GetSubArray(0, writer.Length);
        var reader = new DataStreamReader(encoded);
        var decoded = new ClientMessagePlay(ref reader);

        Assert.IsFalse(reader.HasFailedReads);
        Assert.IsFalse(decoded.isRestart);
        Assert.AreEqual(12u, decoded.levelID);
        Assert.AreEqual(0, decoded.stage);
        Assert.AreEqual("Rank12", decoded.levelName.ToString());
        Assert.AreEqual("Scenes/Level12-1.scene", decoded.sceneName.ToString());
    }

    [Test]
    [Category("Regression")]
    public void MatchStartBeforeMatchAndJoin_IsDroppedAndNeverCached()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayReplayRuntimeGate.Reset();
            ref var state = ref session.farm.agentStates.ElementAt(0);
            state.state = BotState.Matching;
            session.farm.nextIdleMatchTime[0] = double.PositiveInfinity;

            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMatchStartApp(
                9,
                210,
                22,
                0,
                4655,
                new FixedString32Bytes("Bronze"),
                new FixedString32Bytes("Scenes/Level3-1.scene"),
                out var matchStartApp));
            ref var catalog = ref session.catalogBlob.Value;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref catalog,
                in matchStartApp,
                out var matchStartWire));

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayWireTestFixtures.ToBytes(in matchStartWire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(0u, session.farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.None, session.farm.agentFlags[0]);

            ref var knownPeer = ref session.farm.sessions.ElementAt(0);
            knownPeer.remoteUserID = 4655;
            knownPeer.remotePlayerCount = 1;
            knownPeer.remoteOnline = true;
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref session.farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = 9, level = 0 }
            });
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref session.farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                channelMessageType = NetworkRelayMessageType.Join,
                channelHasRemotePayload = true,
                channelHasRemoteHeader = true,
                channelRemoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId,
                channelRemoteHeader = new ClientHeader
                {
                    userID = BotRelayFlowTestFixtures.RealPlayerUserId
                },
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 6,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = ClientRemotePlayerFlag.Online
                    }
                }
            });
            BotAgentLogic.Execute(
                0,
                ref session.farm,
                BotRelayFlowTestFixtures.CreateTickConfig(1f));

            Assert.AreEqual(BotState.InSquad, session.farm.agentStates[0].state);
            Assert.AreEqual(0u, session.farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.None, session.farm.agentFlags[0]);
        }
        finally
        {
            BotRelayReplayRuntimeGate.Reset();
            session.Dispose();
        }
    }

    private static void __AssertTrailingGarbageRejected(
        in BotRelayPacket app,
        string messageType)
    {
        Assert.IsTrue(
            BotRelayWireBytes.TryAcceptCurrentInboundApp(in app, out _),
            $"{messageType} fixture must be a valid exact current app.");
        Assert.Less(app.length, BotRelayPacket.MaxPayloadSize);

        var extended = app;
        extended.SetByte(extended.length, 0x7F);
        ++extended.length;
        Assert.IsFalse(
            BotRelayWireBytes.TryAcceptCurrentInboundApp(in extended, out _),
            $"{messageType} with trailing garbage must be rejected.");
    }
}
