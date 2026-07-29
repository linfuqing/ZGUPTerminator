using NUnit.Framework;
using Unity.Collections;
using ZG;

/// <summary>
/// Regression coverage for the "NetworkSendMessage: -5" (server send-queue overflow) family.
///
/// Root cause reproduced here: while the host waits for our RemotePlayer.status==Joined it
/// re-broadcasts the channel Join/Play bundle every frame, and once the send queue overflows the
/// server drops+re-adds our connection (Leave then Join). If the bot re-processes Play on every
/// such churn it re-injects PlayerProperty/Status and restarts replay each frame — an
/// amplification loop whose outbound volume is what overflows the send queue in the first place.
///
/// These tests drive the exact churn at the BotAgentLogic level and assert the bot's app-layer
/// inject volume stays bounded (does NOT scale with the number of churn frames).
/// </summary>
public class BotRelayAntiAmplificationTests
{
    // 60 frames at ~15 fps ≈ 4s of churn. With the re-handle cooldown only a handful of Plays
    // are honored; without it every frame re-processes Play and re-injects (~60).
    private const int StormTicks = 60;
    private const double TickDelta = 0.066;

    [Test]
    [Category("Regression")]
    public void HostRebroadcastLeaveJoinPlayStorm_DoesNotAmplifyInjects()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.InSquad;
            state.targetUserStageID = 5;
            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = 1;

            for (int i = 0; i < StormTicks; ++i)
            {
                var config = BotRelayFlowTestFixtures.CreateTickConfig(i * TickDelta);
                config.injectEnabled = 1;
                config.injectWriter = injects.AsParallelWriter();

                // Server dropped us (send-queue overflow) → Leave, then re-added → Join,
                // and the host re-broadcasts ChapterStage + Play every frame.
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadLeave
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadJoin,
                    squadJoin = new ClientMessageSquadJoinToRead
                    {
                        squadInviteID = BotRelayFlowTestFixtures.SquadInviteId
                    }
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.ChapterStage,
                    chapterStageID = 5
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.Play,
                    play = new ClientMessagePlay { levelID = 1, stage = 0 }
                });

                BotAgentLogic.Execute(0, ref farm, in config);
            }

            int injectCount = injects.Count;
            Assert.LessOrEqual(
                injectCount,
                8,
                $"Bot amplified app-layer injects under the host Leave/Join/Play re-broadcast storm " +
                $"({injectCount} injects over {StormTicks} frames). This is the runaway volume that " +
                $"overflows the server send queue (NetworkSendMessage: -5).");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

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
            state.targetUserStageID = 5;
            state.remotePresenceSeen = true;

            var runtime = farm.replayRuntime[0];
            runtime.flags = BotReplayRuntimeFlags.Loaded;
            farm.replayRuntime[0] = runtime;

            var session = farm.sessions[0];
            session.channel = 1;
            session.channelStatus = 5;
            session.remoteChannelStatus = 5;
            session.remoteOnline = true;
            farm.sessions[0] = session;
            farm.prevRemoteChannelStatus[0] = 5;

            var replayDrained = BotRelayFlowTestFixtures.CreateTickConfig(5.0);
            replayDrained.injectEnabled = 1;
            replayDrained.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in replayDrained);
            Assert.AreEqual(
                BotState.InLevel,
                farm.agentStates[0].state,
                "Replay exhaustion must not make an in-game bot available for another invitation.");

            session = farm.sessions[0];
            session.remoteChannelStatus = 0;
            session.remoteOnline = true;
            farm.sessions[0] = session;

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

            Assert.IsTrue(
                sawLeave,
                "Remote stage exit must deliver SquadLeave over the reliable app-layer inject.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InLevelBot_IgnoresSquadLeaveJoinChurn_StaysInLevel()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.InLevel;
            state.targetUserStageID = 5;
            state.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite();

            // A committed co-op level: the human peer is present in the stage and online. This is the
            // real InLevel precondition; the channel-tracker must only end the level on a genuine
            // remote leave (status 0) / disconnect, not on the squad-event churn this test exercises.
            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = 1;
            session.remoteChannelStatus = 5;
            session.remoteOnline = true;
            farm.prevRemoteChannelStatus[0] = 5;

            // A committed level: replay armed (Playing) so the replay-finished exit stays dormant.
            ref var runtime = ref farm.replayRuntime.ElementAt(0);
            runtime.flags = BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing;
            farm.agentFlags[0] |= BotAgentRuntimeFlags.PlayHandled;

            for (int i = 0; i < StormTicks; ++i)
            {
                var config = BotRelayFlowTestFixtures.CreateTickConfig(i * TickDelta);
                config.injectEnabled = 1;
                config.injectWriter = injects.AsParallelWriter();

                // Host/server re-broadcast bundle every frame: Invite → Leave (drop) → Join (re-add) →
                // ChapterStage → Play. A committed in-level bot must ignore all of it.
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
                    squadJoin = new ClientMessageSquadJoinToRead
                    {
                        squadInviteID = BotRelayFlowTestFixtures.SquadInviteId
                    }
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.ChapterStage,
                    chapterStageID = 5
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.Play,
                    play = new ClientMessagePlay { levelID = 1, stage = 0 }
                });

                BotAgentLogic.Execute(0, ref farm, in config);

                Assert.AreEqual(
                    BotState.InLevel,
                    farm.agentStates[0].state,
                    $"Bot dropped out of InLevel on churn frame {i}. Each drop makes ReplayTickJob " +
                    $"stop the replay and the next Play reload dumps the whole recording in one tick " +
                    $"(NetworkSendMessage: -5) while leaving the bot invisible.");
            }

            int injectCount = injects.Count;
            Assert.LessOrEqual(
                injectCount,
                8,
                $"In-level bot amplified injects under Leave/Join/Play churn ({injectCount} over " +
                $"{StormTicks} frames); the sticky-InLevel guard is not holding.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    // Full in-level replay loop under the host re-broadcast churn. This is the self-verifying
    // reproduction for the live symptoms (NetworkSendMessage -5, repeated SelectSkill, invisible
    // bot): it enters the level exactly like the load job, then each server tick advances replay
    // pacing by a fixed delta, drives the replay inject, and feeds the host Leave/Join/ChapterStage/
    // Play bundle. Regressions in pacing (dump), stickiness (flap) or single-shot frames (repeat)
    // all fail here without any manual Play.
    [Test]
    [Category("Regression")]
    public void InLevelReplay_UnderHostChurn_PacesFrames_HoldsInLevel_SelectSkillOnce()
    {
        const float FrameSpacing = 0.1f;      // recording emits a frame every 100 ms
        const int MoveFrames = 40;            // ~4 s recording
        const double Dt = 1.0 / 30.0;         // 30 Hz server tick

        var recording = BotRelayFlowTestFixtures.CreateInLevelReplayRecording(
            MoveFrames, FrameSpacing, out int whitelistedFrames, out float duration);
        var catalog = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay | BotAgentRuntimeFlags.PlayHandled;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };
            var seed = farm.agentStates[0];
            seed.targetUserStageID = 10;
            // Mirror the real invite->join->play flow: the invite the bot acted on is retained so the
            // sticky-commit guard can recognise the host's per-frame invite re-broadcast.
            seed.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite();
            farm.agentStates[0] = seed;

            // Human peer present in-stage and online for the whole co-op level: the channel-tracker
            // must keep us InLevel through the squad-event churn (it only ends the level on a genuine
            // remote leave / disconnect, which this test never simulates).
            var coopSession = farm.sessions[0];
            coopSession.channel = 1;
            coopSession.remoteChannelStatus = 10;
            coopSession.remoteOnline = true;
            farm.sessions[0] = coopSession;
            farm.prevRemoteChannelStatus[0] = 10;

            Assert.IsTrue(BotRelayFlowTestFixtures.TrySimulateReplayLoad(0, ref farm, catalog, 0.0));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);

            int drainTicks = 0;
            bool replayDone = false;
            double elapsed = 0.0;
            int totalTicks = (int)(duration / Dt) + 30;

            for (int t = 0; t < totalTicks; ++t)
            {
                elapsed += Dt;

                var injectWriter = injects.AsParallelWriter();
                BotReplayLogic.Tick(0, ref farm, catalog, Dt, ref injectWriter, 1);

                bool playingBeforeExecute = (farm.replayRuntime[0].flags & BotReplayRuntimeFlags.Playing) != 0;
                if (!replayDone)
                {
                    ++drainTicks;
                    if (!playingBeforeExecute)
                        replayDone = true;
                }

                var config = BotRelayFlowTestFixtures.CreateTickConfig(elapsed);
                config.injectEnabled = 1;
                config.injectWriter = injects.AsParallelWriter();

                // The host re-broadcasts the ENTIRE squad bundle every frame while it waits for the
                // bot's stable Joined presence — invite included. The invite re-broadcast is the path
                // that used to tear the bot out of the level (InLevel -> PendingInvite every frame).
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadInvite,
                    squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent { type = BotRelayEventType.SquadLeave });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadJoin,
                    squadJoin = new ClientMessageSquadJoinToRead { squadInviteID = BotRelayFlowTestFixtures.SquadInviteId }
                });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent { type = BotRelayEventType.ChapterStage, chapterStageID = 5 });
                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.Play,
                    play = new ClientMessagePlay { levelID = 1, stage = 0 }
                });

                BotAgentLogic.Execute(0, ref farm, in config);

                if (playingBeforeExecute)
                    Assert.AreEqual(
                        BotState.InLevel,
                        farm.agentStates[0].state,
                        $"Bot fell out of InLevel at tick {t} while the replay was still playing — " +
                        $"this is the flap that restarts the replay and hides the bot.");
            }

            int selectSkillInjects = 0;
            int gameplayInjects = 0;
            while (injects.TryDequeue(out var inj))
            {
                if (!BotRelayFlowTestFixtures.TryReadInjectMessageType(inj, out int type))
                    continue;

                // Count only the recorded gameplay frames the replay emits. Control messages the
                // agent injects on (re)entry (PlayerProperty/Status) are a separate concern.
                if (type == (int)ReplyMessageType.Move || type == (int)ReplyMessageType.SelectSkill)
                    ++gameplayInjects;
                if (type == (int)ReplyMessageType.SelectSkill)
                    ++selectSkillInjects;
            }

            Assert.AreEqual(
                1,
                selectSkillInjects,
                $"SelectSkill replayed {selectSkillInjects} times; a rare frame must emit exactly once per playthrough (was firing repeatedly on entry).");

            Assert.AreEqual(
                whitelistedFrames,
                gameplayInjects,
                $"Expected each recorded gameplay frame injected exactly once ({whitelistedFrames}); got {gameplayInjects}. " +
                $"More than one pass means the replay restarted (the flap that re-dumps early frames).");

            int expectedMinDrainTicks = (int)(duration / Dt) - 5;
            Assert.GreaterOrEqual(
                drainTicks,
                expectedMinDrainTicks,
                $"Replay drained in {drainTicks} ticks (expected ~{(int)(duration / Dt)}). Draining too fast means " +
                $"pacing is broken and the recording is dumped rather than played over its real duration.");
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
                0.0001,
                "Replay time must follow wall-clock server time. Clamping each tick to 0.1 s " +
                "permanently slow-motions recordings whenever the Linux server falls below 10 Hz.");
            Assert.AreEqual(
                9,
                injects.Count,
                "Frames at 0.125s through 1.125s are all eligible relative to the first gameplay " +
                "frame. Replay must not carry the three frames above the retired six-message cap " +
                "into later ticks.");
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

            Assert.AreEqual(
                gameplayFrameCount,
                injects.Count,
                "A recorded same-timestamp batch is one temporal unit and must be injected in the " +
                "same server tick. RecordingAudit, not runtime throttling, rejects malformed batches.");
            Assert.AreEqual(
                recording.Value.frames.Length,
                farm.replayRuntime[0].nextFrameIndex,
                "No eligible frame may be deferred after the recorded timestamp has been reached.");
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
    public void HostRebroadcastPlayEveryFrame_HonorsPlayAtMostOncePerCooldown()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.InSquad;
            state.targetUserStageID = 5;

            // Simulate the pre-fix defect surface: PlayHandled cleared every frame (as a spurious
            // Join churn would) followed by a Play. The cooldown must bound honored Plays.
            for (int i = 0; i < StormTicks; ++i)
            {
                var config = BotRelayFlowTestFixtures.CreateTickConfig(i * TickDelta);
                config.injectEnabled = 1;
                config.injectWriter = injects.AsParallelWriter();

                farm.agentFlags[0] &= ~BotAgentRuntimeFlags.PlayHandled;

                BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.Play,
                    play = new ClientMessagePlay { levelID = 1, stage = 0 }
                });

                BotAgentLogic.Execute(0, ref farm, in config);
            }

            int injectCount = injects.Count;
            Assert.LessOrEqual(
                injectCount,
                8,
                $"Play re-processed too often ({injectCount} Status injects over {StormTicks} frames); " +
                $"the re-handle cooldown is not bounding the amplification.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }
}
