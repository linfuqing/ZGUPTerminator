using NUnit.Framework;
using Unity.Collections;
using ZG;

/// <summary>
/// Regression coverage for reliable-send amplification and replay pacing.
/// Current human Play/ChapterStage packets are consumed without entering the Bot state machine;
/// MatchStart is the sole immutable entry descriptor, while an already committed InLevel bot
/// remains stable through squad churn.
/// </summary>
public class BotRelayAntiAmplificationTests
{
    private const int StormTicks = 60;
    private const double TickDelta = 0.066;

    [Test]
    [Category("Regression")]
    public void ReplayDrained_StaysInLevelUntilRemoteStageExit_ThenEmitsReliableLeave()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.InLevel;
            state.stateEnterTime = 0.0;
            state.remotePresenceSeen = true;
            state.levelLoginPhase = BotLevelLoginPhase.PlayerPropertySent;

            farm.levelStartMessages[0] = BuildDescriptor(5);
            farm.agentFlags[0] = BotAgentRuntimeFlags.LevelStartHandled;

            ref var runtime = ref farm.replayRuntime.ElementAt(0);
            runtime.flags = BotReplayRuntimeFlags.Loaded;

            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            session.channelStatus = 5;
            session.remoteChannelStatus = 5;
            session.remoteOnline = true;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            farm.prevRemoteChannelStatus[0] = 5;

            var replayDrained = BotRelayFlowTestFixtures.CreateTickConfig(5.0);
            replayDrained.injectEnabled = 1;
            replayDrained.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in replayDrained);
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state,
                "Replay exhaustion is not a level-exit authority.");

            session.remoteChannelStatus = 0;
            session.remoteOnline = true;

            var firstAbsent = BotRelayFlowTestFixtures.CreateTickConfig(5.1);
            firstAbsent.injectEnabled = 1;
            firstAbsent.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in firstAbsent);
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);

            var debouncedExit = BotRelayFlowTestFixtures.CreateTickConfig(7.2);
            debouncedExit.injectEnabled = 1;
            debouncedExit.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in debouncedExit);
            Assert.AreEqual(BotState.Leaving, farm.agentStates[0].state);

            var leaveTick = BotRelayFlowTestFixtures.CreateTickConfig(7.3);
            leaveTick.injectEnabled = 1;
            leaveTick.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in leaveTick);
            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);

            bool sawLeave = false;
            while (injects.TryDequeue(out var inject))
            {
                if (BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type) &&
                    type == (int)NetworkRelayMessageType.Leave)
                {
                    Assert.AreEqual(BotRelayFlowTestFixtures.BotUserId, inject.id);
                    sawLeave = true;
                }
            }

            Assert.IsTrue(sawLeave);
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(
                BotAgentRuntimeFlags.None,
                farm.agentFlags[0] &
                (BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart));
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InLevelBot_IgnoresSquadAndRepeatedMatchStartChurn()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.InLevel;
            state.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite();
            state.levelLoginPhase = BotLevelLoginPhase.PlayerPropertySent;

            var descriptor = BuildDescriptor(5);
            farm.levelStartMessages[0] = descriptor;
            farm.agentFlags[0] = BotAgentRuntimeFlags.LevelStartHandled;

            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            session.channelStatus = 5;
            session.remoteChannelStatus = 5;
            session.remoteOnline = true;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            farm.prevRemoteChannelStatus[0] = 5;

            ref var runtime = ref farm.replayRuntime.ElementAt(0);
            runtime.flags = BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing;

            for (int i = 0; i < StormTicks; ++i)
            {
                var config = BotRelayFlowTestFixtures.CreateTickConfig(i * TickDelta);
                config.injectEnabled = 1;
                config.injectWriter = injects.AsParallelWriter();

                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadInvite,
                    squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadLeave
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadJoin,
                    channelMessageType = NetworkRelayMessageType.Join,
                    squadJoin = new ClientMessageSquadJoinToRead
                    {
                        squadInviteID = BotRelayFlowTestFixtures.SquadInviteId,
                        playerStatus = new ClientMessageRemotePlayerStatus
                        {
                            flag = ClientRemotePlayerFlag.Online
                        }
                    }
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.MatchStart,
                    header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId },
                    matchStart = descriptor
                });

                BotAgentLogic.Execute(0, ref farm, in config);

                Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
                Assert.AreEqual(5u, farm.levelStartMessages[0].userStageID);
                Assert.AreEqual(
                    BotAgentRuntimeFlags.None,
                    farm.agentFlags[0] & BotAgentRuntimeFlags.PendingLevelStart);
            }

            Assert.AreEqual(0, injects.Count,
                "Squad/descriptor rebroadcast churn must not restart the level handshake.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InLevelReplay_UnderHostChurn_PacesFrames_HoldsInLevel_SelectSkillOnce()
    {
        const float FrameSpacing = 0.1f;
        const int MoveFrames = 40;
        const double Dt = 1.0 / 30.0;

        var recording = BotRelayFlowTestFixtures.CreateInLevelReplayRecording(
            MoveFrames, FrameSpacing, out int whitelistedFrames, out float duration);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            BotRelayReplayRuntimeGate.Reset();
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.InSquad;
            state.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite();

            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            session.remoteChannelStatus = 10;
            session.remoteOnline = true;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            session.isHost = false;
            farm.prevRemoteChannelStatus[0] = 10;

            var descriptor = BuildDescriptor(10);
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.MatchStart,
                header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId },
                matchStart = descriptor
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig());

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingLevelStart(
                0, ref farm, in catalog, 0.0, ref injectWriter, 1);
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(0, farm.sessions[0].channelStatus);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0.1));
            BotRelayReplayLoadOps.HandlePendingLevelStart(
                0, ref farm, in catalog, 0.1, ref injectWriter, 1);
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);

            int drainTicks = 0;
            bool replayDone = false;
            double elapsed = 0.1;
            int totalTicks = (int)(duration / Dt) + 30;

            for (int tick = 0; tick < totalTicks; ++tick)
            {
                elapsed += Dt;
                BotReplayLogic.Tick(0, ref farm, catalogEntries, Dt, ref injectWriter, 1);

                bool playingBeforeExecute =
                    (farm.replayRuntime[0].flags & BotReplayRuntimeFlags.Playing) != 0;
                if (!replayDone)
                {
                    ++drainTicks;
                    if (!playingBeforeExecute)
                        replayDone = true;
                }

                var config = BotRelayFlowTestFixtures.CreateTickConfig(elapsed);
                config.injectEnabled = 1;
                config.injectWriter = injects.AsParallelWriter();

                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadInvite,
                    squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadLeave
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadJoin,
                    channelMessageType = NetworkRelayMessageType.Join,
                    squadJoin = new ClientMessageSquadJoinToRead
                    {
                        squadInviteID = BotRelayFlowTestFixtures.SquadInviteId,
                        playerStatus = new ClientMessageRemotePlayerStatus
                        {
                            flag = ClientRemotePlayerFlag.Online
                        }
                    }
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.MatchStart,
                    header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId },
                    matchStart = descriptor
                });

                BotAgentLogic.Execute(0, ref farm, in config);
                if (playingBeforeExecute)
                    Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            }

            int selectSkillInjects = 0;
            int gameplayInjects = 0;
            while (injects.TryDequeue(out var inject))
            {
                if (!BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type))
                    continue;
                if (type == (int)ReplyMessageType.Move ||
                    type == (int)ReplyMessageType.SelectSkill)
                {
                    ++gameplayInjects;
                }

                if (type == (int)ReplyMessageType.SelectSkill)
                    ++selectSkillInjects;
            }

            Assert.AreEqual(1, selectSkillInjects);
            Assert.AreEqual(whitelistedFrames, gameplayInjects,
                "Each recorded gameplay frame must be injected exactly once.");
            Assert.GreaterOrEqual(
                drainTicks,
                (int)(duration / Dt) - 5,
                "Replay must advance on its recorded timeline instead of dumping frames early.");
        }
        finally
        {
            BotRelayReplayRuntimeGate.Reset();
            injects.Dispose();
            farm.Dispose();
            catalogEntries.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ReplayTick_LowServerRate_EmitsEveryFrameEligibleOnRecordedTimeline()
    {
        const double LoadedServerDelta = 1.0;

        var recording = BotRelayFlowTestFixtures.CreateInLevelReplayRecording(
            20, 0.125f, out _, out _);
        var catalog = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var runtime = ref farm.replayRuntime.ElementAt(0);
            runtime.catalogIndex = 0;
            BotReplayLogic.PrepareRuntime(recording, ref runtime);
            BotReplayLogic.Begin(ref runtime);

            var injectWriter = injects.AsParallelWriter();
            BotReplayLogic.Tick(
                0,
                ref farm,
                catalog,
                LoadedServerDelta,
                ref injectWriter,
                1);

            Assert.AreEqual(
                LoadedServerDelta,
                farm.replayRuntime[0].playbackTime,
                0.0001);
            Assert.AreEqual(
                9,
                injects.Count,
                "Every frame eligible at the recorded time must be emitted; runtime no longer caps six per tick.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
            catalog.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ReplayTick_SameTimestampBatch_IsNotSplitAcrossTicks()
    {
        var recording = BotRelayFlowTestFixtures.CreateInLevelReplayRecording(
            20, 0f, out int gameplayFrameCount, out _);
        var catalog = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var runtime = ref farm.replayRuntime.ElementAt(0);
            runtime.catalogIndex = 0;
            BotReplayLogic.PrepareRuntime(recording, ref runtime);
            BotReplayLogic.Begin(ref runtime);

            var injectWriter = injects.AsParallelWriter();
            BotReplayLogic.Tick(0, ref farm, catalog, 0.0, ref injectWriter, 1);

            Assert.AreEqual(gameplayFrameCount, injects.Count);
            Assert.AreEqual(recording.Value.frames.Length, farm.replayRuntime[0].nextFrameIndex,
                "RecordingAudit rejects malformed batches; runtime must not split a valid timestamp batch.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
            catalog.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
        }
    }

    private static ClientMessageMatchStart BuildDescriptor(uint stageId) =>
        new ClientMessageMatchStart
        {
            matchID = 0,
            userStageID = stageId,
            levelID = 1,
            stage = 0,
            levelName = new FixedString32Bytes("Level1"),
            sceneName = new FixedString32Bytes("Scenes/Level1.scene")
        };
}
