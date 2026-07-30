using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Contract tests for the strict Bot level-entry protocol.
/// ClientMessageMatchStart is the only level descriptor; it is accepted only after the current
/// squad/match generation and canonical human peer are already established.
/// </summary>
public class BotRelayStrictMatchStartTests
{
    private const int MatchId = 7;
    private const uint StageId = 10;
    private const uint LevelId = 1;
    private const int StageIndex = 0;
    private const string SceneName = "Scenes/Level1.scene";

    [SetUp]
    public void SetUp() => BotRelayReplayRuntimeGate.Reset();

    [TearDown]
    public void TearDown() => BotRelayReplayRuntimeGate.Reset();

    [TestCase(false)]
    [TestCase(true)]
    [Category("Regression")]
    public void MatchStart_AfterEstablishedGeneration_ArmsFirstLoginPhase(bool matched)
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, matched ? MatchId : 0);
            EnqueueMatchStart(ref farm, BuildDescriptor(matched ? MatchId : 0));

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());

            AssertDescriptor(farm.levelStartMessages[0], matched ? MatchId : 0);
            Assert.AreEqual(BotLevelLoginPhase.PendingPlayerProperty, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(BotAgentRuntimeFlags.PendingLevelStart, farm.agentFlags[0]);
            Assert.AreEqual(0, farm.sessions[0].channelStatus);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [TestCase(BotState.Idle, 1, BotRelayFlowTestFixtures.RealPlayerUserId, 0, 0)]
    [TestCase(BotState.Matching, 1, BotRelayFlowTestFixtures.RealPlayerUserId, 0, 0)]
    [TestCase(BotState.PendingInvite, 1, BotRelayFlowTestFixtures.RealPlayerUserId, 0, 0)]
    [TestCase(BotState.InSquad, -1, BotRelayFlowTestFixtures.RealPlayerUserId, 0, 0)]
    [TestCase(BotState.InSquad, 1, 0u, 0, 0)]
    [TestCase(BotState.InSquad, 1, 999999u, 0, 0)]
    [TestCase(BotState.InSquad, 1, BotRelayFlowTestFixtures.RealPlayerUserId, 0, MatchId)]
    [TestCase(BotState.InSquad, 1, BotRelayFlowTestFixtures.RealPlayerUserId, MatchId, 0)]
    [TestCase(BotState.InSquad, 1, BotRelayFlowTestFixtures.RealPlayerUserId, MatchId, MatchId + 1)]
    [Category("Regression")]
    public void MatchStart_InvalidEnvelope_IsRejectedWithoutState(
        BotState botState,
        int channel,
        uint senderUserId,
        int sessionMatchId,
        int descriptorMatchId)
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = botState;
            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = channel;
            session.matchID = sessionMatchId;
            session.remotePlayerCount = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remoteOnline = true;
            farm.nextIdleMatchTime[0] = double.PositiveInfinity;

            EnqueueMatchStart(ref farm, BuildDescriptor(descriptorMatchId), senderUserId);
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());

            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotLevelLoginPhase.None, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(0, farm.sessions[0].channelStatus);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [TestCase("stage")]
    [TestCase("level")]
    [TestCase("index")]
    [TestCase("scene")]
    [Category("Regression")]
    public void MatchStart_IncompleteDescriptor_IsRejected(string invalidField)
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, 0);
            var descriptor = BuildDescriptor(0);
            switch (invalidField)
            {
                case "stage":
                    descriptor.userStageID = 0;
                    break;
                case "level":
                    descriptor.levelID = 0;
                    break;
                case "index":
                    descriptor.stage = -1;
                    break;
                case "scene":
                    descriptor.sceneName = default;
                    break;
            }

            EnqueueMatchStart(ref farm, descriptor);
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());

            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotLevelLoginPhase.None, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void OrdinaryMatchStart_WhenBotIsSquadHost_IsRejected()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, 0);
            ref var session = ref farm.sessions.ElementAt(0);
            session.isHost = true;

            EnqueueMatchStart(ref farm, BuildDescriptor(0));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());

            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotLevelLoginPhase.None, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchStart_BeforeMatchAndJoin_IsDroppedWithoutReorderCache()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.Matching;
            ref var session = ref farm.sessions.ElementAt(0);
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            session.remoteOnline = true;
            farm.nextIdleMatchTime[0] = double.PositiveInfinity;

            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = MatchId, level = 0 }
            });
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
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
                    squadInviteID = BotRelayFlowTestFixtures.SquadInviteId,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = ClientRemotePlayerFlag.Online
                    }
                }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.0));

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(MatchId, farm.sessions[0].matchID);
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID,
                "Committing Match+Join later must not resurrect an early descriptor.");
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);

            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(2.0));
            AssertDescriptor(farm.levelStartMessages[0], MatchId);
            Assert.AreEqual(BotAgentRuntimeFlags.PendingLevelStart, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchStart_EarlierInSameBatch_IsDropped_ButCorrectOrderIsAccepted()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedMatchingWithKnownPeer(ref farm);
            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId));
            EnqueueMatchAndJoin(ref farm, MatchId);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }

        farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedMatchingWithKnownPeer(ref farm);
            EnqueueMatchAndJoin(ref farm, MatchId);
            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId));

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());
            AssertDescriptor(farm.levelStartMessages[0], MatchId);
            Assert.AreEqual(BotAgentRuntimeFlags.PendingLevelStart, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchStart_FirstCompleteDescriptor_IsImmutableForGeneration()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, MatchId);
            var first = BuildDescriptor(MatchId);
            EnqueueMatchStart(ref farm, first);
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());

            EnqueueMatchStart(ref farm, first);
            var conflict = BuildDescriptor(MatchId);
            conflict.userStageID = 99;
            conflict.levelID = 8;
            conflict.stage = 3;
            conflict.sceneName = new FixedString32Bytes("Scenes/Conflicting.scene");
            EnqueueMatchStart(ref farm, conflict);
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.0));

            AssertDescriptor(farm.levelStartMessages[0], MatchId);
            Assert.AreEqual(BotLevelLoginPhase.PendingPlayerProperty, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(BotAgentRuntimeFlags.PendingLevelStart, farm.agentFlags[0],
                "Reliable duplicates/conflicts must not re-arm another pending load.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchStart_WrongMatchedGeneration_CannotOverwriteAcceptedDescriptor()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, MatchId);
            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId + 1));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);

            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.0));
            AssertDescriptor(farm.levelStartMessages[0], MatchId);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ConflictingMatchResult_CannotReplaceCommittedGenerationOrDescriptor()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, MatchId);
            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());
            AssertDescriptor(farm.levelStartMessages[0], MatchId);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = MatchId + 1, level = 0 }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.0));

            Assert.AreEqual(MatchId, farm.sessions[0].matchID);
            AssertDescriptor(farm.levelStartMessages[0], MatchId);
            Assert.AreEqual(BotAgentRuntimeFlags.PendingLevelStart, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PlayAndChapterStage_AreFullyConsumedWithoutBotEventsOrLevelState()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundPlayApp(
            12,
            1,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            new FixedString32Bytes("Rank12"),
            new FixedString32Bytes("Scenes/Level12-2.scene"),
            out var playApp));
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayChapterStageApp(
            BotRelayFlowTestFixtures.RealPlayerUserId,
            129,
            out var chapterApp));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, 0);
            BotRelaySlotInbox.ParseTransportPayload(
                in playApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());
            BotRelaySlotInbox.ParseTransportPayload(
                in chapterApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.IsFalse(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out _));
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(0, farm.sessions[0].channelStatus);
            Assert.AreEqual(0, farm.sessions[0].remoteChannelStatus);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("Regression")]
    public void MatchStart_LoginHandshake_IsPropertyThenPropertyAndLiveStatus(bool matched)
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var entries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelayReplayRuntimeGate.Reset();
            SeedJoined(ref farm, matched ? MatchId : 0);
            EnqueueMatchStart(ref farm, BuildDescriptor(matched ? MatchId : 0));

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());
            Assert.AreEqual(BotLevelLoginPhase.PendingPlayerProperty, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(BotAgentRuntimeFlags.PendingLevelStart, farm.agentFlags[0]);
            Assert.IsTrue(BotRelayReplayRuntimeGate.HasPendingLevelStart);

            var catalog = new BotReplayCatalog { entries = entries };
            var writer = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingLevelStart(
                0,
                ref farm,
                in catalog,
                0.0,
                ref writer,
                1);

            Assert.IsFalse(BotRelayReplayRuntimeGate.HasPendingLevelStart);
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(BotLevelLoginPhase.PlayerPropertySent, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(0, farm.sessions[0].channelStatus);
            Assert.AreEqual(1, injects.Count);
            Assert.IsTrue(injects.TryDequeue(out var firstProperty));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in firstProperty, out int firstType));
            Assert.AreEqual((int)ReplyMessageType.PlayerProperty, firstType);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.0));
            Assert.AreEqual(
                BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart,
                farm.agentFlags[0]);
            Assert.IsTrue(BotRelayReplayRuntimeGate.HasPendingLevelStart);

            BotRelayReplayLoadOps.HandlePendingLevelStart(
                0,
                ref farm,
                in catalog,
                1.0,
                ref writer,
                1);

            Assert.IsFalse(BotRelayReplayRuntimeGate.HasPendingLevelStart);
            Assert.IsTrue(BotRelayReplayRuntimeGate.HasInLevel);
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.AreEqual((int)StageId, farm.sessions[0].channelStatus);
            Assert.AreEqual(BotAgentRuntimeFlags.LevelStartHandled, farm.agentFlags[0]);
            Assert.AreEqual(2, injects.Count);
            Assert.IsTrue(injects.TryDequeue(out var secondProperty));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in secondProperty, out int secondType));
            Assert.AreEqual((int)ReplyMessageType.PlayerProperty, secondType);
            Assert.IsTrue(injects.TryDequeue(out var status));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in status, out int statusType));
            Assert.AreEqual((int)NetworkRelayMessageType.Status, statusType);
            Assert.AreEqual(
                BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing,
                farm.replayRuntime[0].flags);
        }
        finally
        {
            BotRelayReplayRuntimeGate.Reset();
            farm.Dispose();
            injects.Dispose();
            entries.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
        }
    }

    [TestCase(BotRelayFlowTestFixtures.BotUserId)]
    [TestCase(BotRelayFlowTestFixtures.RealPlayerUserId)]
    [Category("Regression")]
    public void Mismatch_SameGeneration_ClearsAcceptedDescriptorAndLoginPhase(uint sourceId)
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, MatchId);
            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());
            Assert.AreNotEqual(0u, farm.levelStartMessages[0].userStageID);

            EnqueueMismatch(
                ref farm,
                MatchId,
                sourceId);
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.0));

            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotLevelLoginPhase.None, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(0, farm.sessions[0].matchID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [TestCase(MatchId - 1, BotRelayFlowTestFixtures.BotUserId)]
    [TestCase(MatchId, 999999u)]
    [Category("Regression")]
    public void Mismatch_StaleGenerationOrForeignSource_DoesNotClearCommittedEntry(
        int mismatchId,
        uint sourceId)
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            SeedJoined(ref farm, MatchId);
            EnqueueMatchStart(ref farm, BuildDescriptor(MatchId));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());
            AssertDescriptor(farm.levelStartMessages[0], MatchId);

            EnqueueMismatch(ref farm, mismatchId, sourceId);
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.0));

            AssertDescriptor(farm.levelStartMessages[0], MatchId);
            Assert.AreEqual(
                BotLevelLoginPhase.PendingPlayerProperty,
                farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(
                BotAgentRuntimeFlags.PendingLevelStart,
                farm.agentFlags[0]);
            Assert.AreEqual(MatchId, farm.sessions[0].matchID);
            Assert.AreEqual(
                BotRelayFlowTestFixtures.RealPlayerUserId,
                farm.sessions[0].remoteUserID);
            Assert.IsTrue(farm.sessions[0].remoteOnline);
        }
        finally
        {
            farm.Dispose();
        }
    }

    private static ClientMessageMatchStart BuildDescriptor(int matchId) =>
        new ClientMessageMatchStart
        {
            matchID = matchId,
            userStageID = StageId,
            levelID = LevelId,
            stage = StageIndex,
            levelName = new FixedString32Bytes("Level1"),
            sceneName = new FixedString32Bytes(SceneName)
        };

    private static void SeedJoined(ref BotRelayFarmNative farm, int sessionMatchId)
    {
        ref var state = ref farm.agentStates.ElementAt(0);
        state.state = BotState.InSquad;

        ref var session = ref farm.sessions.ElementAt(0);
        session.channel = 1;
        session.matchID = sessionMatchId;
        session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
        session.remotePlayerCount = 1;
        session.remoteOnline = true;
    }

    private static void SeedMatchingWithKnownPeer(ref BotRelayFarmNative farm)
    {
        ref var state = ref farm.agentStates.ElementAt(0);
        state.state = BotState.Matching;
        ref var session = ref farm.sessions.ElementAt(0);
        session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
        session.remotePlayerCount = 1;
        session.remoteOnline = true;
        farm.nextIdleMatchTime[0] = double.PositiveInfinity;
    }

    private static void EnqueueMatchAndJoin(ref BotRelayFarmNative farm, int matchId)
    {
        BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
        {
            type = BotRelayEventType.Match,
            match = new ClientMessageMatchToRead { matchID = matchId, level = 0 }
        });
        BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
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
                squadInviteID = BotRelayFlowTestFixtures.SquadInviteId,
                playerStatus = new ClientMessageRemotePlayerStatus
                {
                    flag = ClientRemotePlayerFlag.Online
                }
            }
        });
    }

    private static void EnqueueMatchStart(
        ref BotRelayFarmNative farm,
        in ClientMessageMatchStart descriptor,
        uint senderUserId = BotRelayFlowTestFixtures.RealPlayerUserId)
    {
        BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
        {
            type = BotRelayEventType.MatchStart,
            header = new ClientHeader { userID = senderUserId },
            matchStart = descriptor
        });
    }

    private static void EnqueueMismatch(
        ref BotRelayFarmNative farm,
        int matchId,
        uint sourceId)
    {
        BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
        {
            type = BotRelayEventType.Mismatch,
            header = new ClientHeader { userID = sourceId },
            match = new ClientMessageMatchToRead { matchID = matchId }
        });
    }

    private static void AssertDescriptor(ClientMessageMatchStart descriptor, int matchId)
    {
        Assert.AreEqual(matchId, descriptor.matchID);
        Assert.AreEqual(StageId, descriptor.userStageID);
        Assert.AreEqual(LevelId, descriptor.levelID);
        Assert.AreEqual(StageIndex, descriptor.stage);
        Assert.AreEqual("Level1", descriptor.levelName.ToString());
        Assert.AreEqual(SceneName, descriptor.sceneName.ToString());
    }
}
