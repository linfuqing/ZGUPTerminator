using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Maps to Documentation~/MultiplayerBot/联机机器人.md design goals: world invite, match, in-level replay.
/// </summary>
public class BotRelayDesignGoalTests
{
    [Test]
    [Category("Regression")]
    public void RouteSendPeeledTelemetry_IsSilentByDefault()
    {
        const string Prefix = "[BotRelay] RouteSend peeled relay type=";
        int matchingLogs = 0;

        void CountMatchingLog(string condition, string _, UnityEngine.LogType __)
        {
            if (condition.StartsWith(Prefix, System.StringComparison.Ordinal))
                ++matchingLogs;
        }

        UnityEngine.Application.logMessageReceived += CountMatchingLog;
        try
        {
            BotRelayDefines.VerboseTelemetry = false;
            BotRelayAgentDiagnostics.LogRouteSendPeeled(102, 30, 6);
            Assert.AreEqual(
                0,
                matchingLogs,
                "Normal Move/Camera routing must not write a log plus stack trace for every packet.");

            BotRelayDefines.VerboseTelemetry = true;
            BotRelayAgentDiagnostics.LogRouteSendPeeled(102, 30, 6);
            Assert.AreEqual(1, matchingLogs, "The diagnostic must remain available when explicitly enabled.");
        }
        finally
        {
            BotRelayDefines.VerboseTelemetry = false;
            UnityEngine.Application.logMessageReceived -= CountMatchingLog;
        }
    }

    [Test]
    [Category("Regression")]
    public void ReplayJobHelpers_DoNotExposeStandaloneBurstDirectCallEntrypoints()
    {
        var methodFlags = System.Reflection.BindingFlags.Public |
                          System.Reflection.BindingFlags.Static;
        var burstAttribute = typeof(Unity.Burst.BurstCompileAttribute);

        foreach (string methodName in new[] { nameof(BotReplayLogic.Stop), nameof(BotReplayLogic.Tick) })
        {
            var method = typeof(BotReplayLogic).GetMethod(methodName, methodFlags);
            Assert.NotNull(method, methodName);
            Assert.IsEmpty(
                method.GetCustomAttributes(burstAttribute, inherit: false),
                $"{methodName} is called transitively from Burst jobs and must not get a standalone " +
                "$BurstDirectCall wrapper; Linux/Mono managed fallback otherwise throws before " +
                "the bot can commit SquadJoin/InSquad.");
        }

        var nestedTypes = typeof(BotReplayLogic).GetNestedTypes(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        Assert.IsFalse(
            System.Array.Exists(
                nestedTypes,
                type => type.Name.Contains("BurstDirectCall") &&
                        (type.Name.StartsWith(nameof(BotReplayLogic.Stop)) ||
                         type.Name.StartsWith(nameof(BotReplayLogic.Tick)))),
            "ILPP output must not contain Stop/Tick $BurstDirectCall wrappers.");
    }

    [Test]
    [Category("Regression")]
    public void MissingReplayCatalog_IdleBotDoesNotMatchOrAcceptInvite()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig(0.0);
            tickConfig.replayCatalogMissing = 1;

            BotAgentLogic.Execute(0, ref farm, in tickConfig);
            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].hasPendingInvite);
            Assert.IsFalse(BotMatchGuard.HasAnySquadSlotClaim(in farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void WorldInvite_ParseTransportPayload_EnqueuesSquadInvite()
    {
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        Assert.IsTrue(
            BotRelayWireBytes.TryExtractAppViaTransportFraming(in wire, out var app),
            "Canonical invite wire must extract invite app.");

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
    public void DuplicateSquadInvite_DoesNotResetPendingJoinTimer()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var invite = BotRelayFlowTestFixtures.BuildPublicInvite();
            var tickConfig = new BotRelayFarmTickConfig
            {
                inviteTimeoutMin = 10f,
                inviteTimeoutMax = 10f,
                elapsedTime = 0,
                frameSeed = 1
            };

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = invite
            });
            BotAgentLogic.Execute(0, ref farm, tickConfig);
            var joinAt = farm.agentStates[0].inviteJoinTime;
            Assert.AreEqual(10d, joinAt, 0.001d);

            tickConfig.elapsedTime = 5;
            tickConfig.frameSeed = 2;
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = invite
            });
            BotAgentLogic.Execute(0, ref farm, tickConfig);
            Assert.AreEqual(joinAt, farm.agentStates[0].inviteJoinTime, 0.001d);

            tickConfig.elapsedTime = 10;
            BotAgentLogic.Execute(0, ref farm, tickConfig);
            Assert.Greater(BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0), 0);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void DuplicateSquadInvite_AfterJoinSent_DoesNotRearmTimer()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var invite = BotRelayFlowTestFixtures.BuildPublicInvite();
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            tickConfig.inviteTimeoutMin = 1f;
            tickConfig.inviteTimeoutMax = 1f;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = invite
            });
            BotAgentLogic.Execute(0, ref farm, tickConfig);

            tickConfig.elapsedTime = 1f;
            BotAgentLogic.Execute(0, ref farm, tickConfig);

            tickConfig.elapsedTime = 3.6f;
            BotAgentLogic.Execute(0, ref farm, tickConfig);
            Assert.AreEqual(double.MaxValue, farm.agentStates[0].inviteJoinTime, 1d);
            int outboundAfterJoin = BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = invite
            });
            tickConfig.frameSeed = 99;
            BotAgentLogic.Execute(0, ref farm, tickConfig);
            Assert.AreEqual(double.MaxValue, farm.agentStates[0].inviteJoinTime, 1d);
            Assert.AreEqual(outboundAfterJoin, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadInvite_FromMatching_ImmediatelyInjectsMismatchPrep()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.Matching;
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.matchID = 7;
            farm.sessions[0] = session;

            var config = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(0)
            });
            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.AreEqual(0, farm.sessions[0].matchID);
            Assert.AreEqual(BotAgentRuntimeFlags.JoinMismatchQueued, farm.agentFlags[0]);
            Assert.AreEqual(double.PositiveInfinity, farm.nextIdleMatchTime[0]);
            Assert.GreaterOrEqual(injects.Count, 2);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }



[Test]
    [Category("Regression")]
    public void SquadJoinFail_ReleasesInviteAndAcceptsLaterInvite()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var invite = BotRelayFlowTestFixtures.BuildPublicInvite();
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig(0f);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = invite
            });
            BotAgentLogic.Execute(0, ref farm, in tickConfig);
            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);

            ClientMessageSquadJoinToRead joinFail;
            joinFail.playerStatus.flag = 0;
            joinFail.squadInviteID = invite.squadInviteID;
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoinFail,
                squadJoin = joinFail
            });

            tickConfig.elapsedTime = 1f;
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].hasPendingInvite);
            Assert.Greater(farm.nextIdleMatchTime[0], tickConfig.elapsedTime);

            // The previous Join is not retried, but a later incoming Invite event is eligible
            // again. It deliberately uses the same squad channel: squadInviteID is a squad id,
            // not a unique invitation-event token.
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = invite
            });
            tickConfig.elapsedTime = 2f;
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsTrue(farm.agentStates[0].hasPendingInvite);
            Assert.AreEqual(invite.squadInviteID, farm.agentStates[0].pendingInvite.squadInviteID);
        }
        finally
        {
            farm.Dispose();
        }
    }


    [Test]
    [Category("Regression")]
    public void SquadLease_DeduplicatesSameSquad_AllowsDifferentSquads_AndReleasesIndependently()
    {
        var farm = BotRelayFarmNative.Create(2, Allocator.TempJob);
        try
        {
            for (int i = 0; i < farm.agentCount; ++i)
            {
                farm.agentStates[i] = new BotAgentState
                {
                    state = BotState.Idle,
                    userID = BotRelayFlowTestFixtures.BotUserId + (uint)i,
                    stateEnterTime = 0,
                    matchLevel = BotRelayFlowTestFixtures.MatchLevel
                };
                // Keep the assertion focused on Invite arbitration; boot-time Idle matching is
                // exercised by the dedicated PlayMode tests and would otherwise move agent 1.
                farm.inboxSentInitialStatus[i] = 0;
                farm.nextIdleMatchTime[i] = double.PositiveInfinity;
            }

            var invite = BotRelayFlowTestFixtures.BuildPublicInvite();
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            for (int i = 0; i < farm.agentCount; ++i)
            {
                BotRelayFlowTestFixtures.EnqueueEvent(i, ref farm, new BotRelayEvent
                {
                    type = BotRelayEventType.SquadInvite,
                    squadInvite = invite
                });
                BotAgentLogic.Execute(i, ref farm, in tickConfig);
            }

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.AreEqual(BotState.Idle, farm.agentStates[1].state);
            Assert.IsTrue(BotMatchGuard.HasSquadSlotClaim(in farm, 0, invite.squadInviteID));
            Assert.IsFalse(BotMatchGuard.HasAnySquadSlotClaim(in farm, 1));

            // A Bot that has committed to an Invite must not overwrite it with a second channel.
            // The product promise is that only Idle/Matching Bots react to a later Invite.
            var competingInvite = invite;
            competingInvite.squadInviteID++;
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = competingInvite
            });
            BotAgentLogic.Execute(0, ref farm, in tickConfig);
            Assert.AreEqual(invite.squadInviteID, farm.agentStates[0].pendingInvite.squadInviteID);
            Assert.IsTrue(BotMatchGuard.HasSquadSlotClaim(in farm, 0, invite.squadInviteID));

            // A different human squad must not be blocked by the first squad's lease. The idle
            // agent may accept it concurrently, while the committed first agent remains unchanged.
            BotRelayFlowTestFixtures.EnqueueEvent(1, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = competingInvite
            });
            BotAgentLogic.Execute(1, ref farm, in tickConfig);
            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[1].state);
            Assert.AreEqual(competingInvite.squadInviteID, farm.agentStates[1].pendingInvite.squadInviteID);
            Assert.IsTrue(BotMatchGuard.HasSquadSlotClaim(
                in farm, 1, competingInvite.squadInviteID));

            ClientMessageSquadJoinToRead joinFail;
            joinFail.playerStatus.flag = 0;
            joinFail.squadInviteID = invite.squadInviteID;
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoinFail,
                squadJoin = joinFail
            });
            tickConfig.elapsedTime = 1f;
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.IsFalse(BotMatchGuard.HasAnySquadSlotClaim(in farm, 0));
            Assert.IsTrue(BotMatchGuard.HasSquadSlotClaim(
                in farm, 1, competingInvite.squadInviteID));

            // A later event may use the same squad id. It is eligible because the prior event
            // released the lease rather than tombstoning the squad channel.
            var laterPrivateInvite = invite;
            laterPrivateInvite.channel = ClientChannel.Private;
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = laterPrivateInvite
            });
            tickConfig.elapsedTime = 2f;
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsTrue(BotMatchGuard.HasSquadSlotClaim(in farm, 0, invite.squadInviteID));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PendingInvite_DispatchesJoinOnlyOnceAcrossTicks()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            tickConfig.inviteTimeoutMin = 1f;
            tickConfig.inviteTimeoutMax = 1f;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });
            BotAgentLogic.Execute(0, ref farm, tickConfig);

            tickConfig.elapsedTime = 1f;
            BotAgentLogic.Execute(0, ref farm, tickConfig);

            tickConfig.elapsedTime = 3.6f;
            BotAgentLogic.Execute(0, ref farm, tickConfig);
            int afterFirstDispatch = BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0);
            Assert.Greater(afterFirstDispatch, 0);
            Assert.AreNotEqual(0, farm.agentFlags[0] & BotAgentRuntimeFlags.JoinDispatched);

            for (int i = 0; i < 30; ++i)
            {
                tickConfig.elapsedTime = 3.6f + i * 0.1f;
                BotAgentLogic.Execute(0, ref farm, tickConfig);
            }

            Assert.AreEqual(afterFirstDispatch, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void HostMessageParser_CreateAndJoin_UpdatesReplyMessageSharedLikeClientData()
    {
        using var scope = BotRelayHostTestClient.BeginScope();
        try
        {
            int channelFlag = (int)NetworkRelayChannelFlag.Online | (int)NetworkRelayChannelFlag.Creator;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundHostCreateEchoApp(1, channelFlag, out var createApp));
            Assert.IsTrue(BotRelayHostMessageParser.TryApplyForHost(in createApp));
            Assert.IsTrue(ReplyMessageShared.isHost);
            Assert.AreEqual(1, ReplyMessageShared.channel);

            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundJoinApp(
                1,
                BotRelayWireTestFixtures.BotUserId,
                out var joinApp));
            Assert.IsTrue(BotRelayHostMessageParser.TryApplyForHost(in joinApp));
            Assert.AreEqual(BotRelayWireTestFixtures.BotUserId, LevelPlayerShared<RemotePlayer>.id);
            Assert.Greater(ReplyMessageShared.remotePlayerCount, 0);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Test]
    public void WorldInviteResponse_AfterDelay_SendsSquadJoin()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f, 0f));
            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsTrue(farm.agentStates[0].hasPendingInvite);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f, 0f));
            int mismatchCount = BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0);
            Assert.GreaterOrEqual(mismatchCount, 1);
            var mismatchPacket = BotRelayFlowTestFixtures.GetOutboundPacket(ref farm, 0, mismatchCount - 1);
            Assert.IsTrue(
                BotRelayFlowTestFixtures.TryReadFirstMessageType(in mismatchPacket, out int mismatchType));
            Assert.AreEqual((int)NetworkRelayMessageType.Mismatch, mismatchType);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(3.6f, 0f));
            int outboundCount = BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0);
            Assert.GreaterOrEqual(outboundCount, mismatchCount + 1);

            bool hasMismatch = false;
            bool hasJoin = false;
            for (int i = 0; i < outboundCount; ++i)
            {
                var packet = BotRelayFlowTestFixtures.GetOutboundPacket(ref farm, 0, i);
                Assert.IsTrue(BotRelayFlowTestFixtures.TryReadFirstMessageType(in packet, out int type));
                if (type == (int)NetworkRelayMessageType.Mismatch)
                    hasMismatch = true;
                if (type == (int)NetworkRelayMessageType.Join)
                    hasJoin = true;
            }

            Assert.IsTrue(hasMismatch);
            Assert.IsTrue(hasJoin);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void BotAgentLogic_InviteWhileMatching_QueuesMismatchBeforeJoin()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.Matching, farm.agentStates[0].state);
            farm.outboundCount[0] = 0;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });
            var inviteTick = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            inviteTick.frameSeed = 1;
            BotAgentLogic.Execute(0, ref farm, inviteTick);
            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);

            var joinAt = farm.agentStates[0].inviteJoinTime;
            var joinTick = BotRelayFlowTestFixtures.CreateTickConfig(joinAt);
            joinTick.frameSeed = 2;
            BotAgentLogic.Execute(0, ref farm, joinTick);

            var joinAfterMismatchTick = BotRelayFlowTestFixtures.CreateTickConfig(joinAt + 1.55);
            joinAfterMismatchTick.frameSeed = 3;
            BotAgentLogic.Execute(0, ref farm, joinAfterMismatchTick);

            int outboundCount = BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0);
            Assert.GreaterOrEqual(outboundCount, 2, "Must queue Mismatch then Join when inviting during Matching.");

            int mismatchIndex = -1;
            int joinIndex = -1;
            for (int i = 0; i < outboundCount; ++i)
            {
                var packet = BotRelayFlowTestFixtures.GetOutboundPacket(ref farm, 0, i);
                Assert.IsTrue(BotRelayFlowTestFixtures.TryReadFirstMessageType(in packet, out int type));
                if (type == (int)NetworkRelayMessageType.Mismatch && mismatchIndex < 0)
                    mismatchIndex = i;
                if (type == (int)NetworkRelayMessageType.Join && joinIndex < 0)
                    joinIndex = i;
            }

            Assert.GreaterOrEqual(mismatchIndex, 0, "Mismatch must be queued for server match gate.");
            Assert.GreaterOrEqual(joinIndex, 0, "Join must be queued after invite delay.");
            Assert.Less(mismatchIndex, joinIndex, "Mismatch must precede Join in outbound queue.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InviteWhileInSquad_DifferentSquad_Ignored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            farm.sessions[0] = session;
            farm.outboundCount[0] = 0;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(99u)
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InviteWhileInSquad_SameSquad_RebroadcastIgnored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite();
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            farm.sessions[0] = session;
            farm.outboundCount[0] = 0;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InviteWhileInLevel_AnyInvite_Ignored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InLevel;
            agentState.stateEnterTime = 0.0;
            agentState.targetUserStageID = 5;
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            session.channelStatus = 5;
            session.remoteChannelStatus = 5;
            session.remoteOnline = true;
            farm.sessions[0] = session;
            var replay = farm.replayRuntime[0];
            replay.flags = BotReplayRuntimeFlags.Loaded;
            farm.replayRuntime[0] = replay;
            farm.outboundCount[0] = 0;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(99u)
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(5f));

            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].hasPendingInvite);
            Assert.IsFalse(BotMatchGuard.HasAnySquadSlotClaim(in farm, 0));
            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InviteWhileLeaving_Ignored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.Leaving;
            farm.agentStates[0] = agentState;
            farm.outboundCount[0] = 0;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));

            // Leaving completes in one tick (Leave+Status inject → Idle); invite must not re-arm PendingInvite.
            Assert.AreNotEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].hasPendingInvite);
            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void MatchResponse_IdleBot_SendsMatchWhenReady()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.Matching, farm.agentStates[0].state);
            Assert.Greater(BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0), 0);

            var matchPacket = BotRelayFlowTestFixtures.GetOutboundPacket(ref farm, 0, 0);
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadFirstMessageType(in matchPacket, out int type));
            Assert.AreEqual((int)NetworkRelayMessageType.Match, type);
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadMatchDistance(in matchPacket, out int level));
            Assert.AreEqual(0, level, "Zero-based rank index 0 must be sent unchanged.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_BootDeferredIdleMatch_ArmsAfterRelayReady()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            farm.nextIdleMatchTime[0] = double.PositiveInfinity;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(10.0));

            Assert.AreEqual(BotState.Matching, farm.agentStates[0].state);
            Assert.IsFalse(double.IsPositiveInfinity(farm.nextIdleMatchTime[0]));
            Assert.Greater(BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0), 0);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_IdleBot_UsesAgentProfileMatchLevel()
    {
        const int profileLevel = 7;
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.matchLevel = profileLevel;
            farm.nextIdleMatchTime[0] = 0;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotState.Matching, state.state);
            Assert.AreEqual(profileLevel, state.matchLevel);
            Assert.Greater(BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0), 0);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RelayBoot_ArmsIdleMatch_InjectServerStatusOnce()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            farm.nextIdleMatchTime[0] = double.PositiveInfinity;

            var config = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();

            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual((byte)1, farm.relayServerStatusInjected[0]);

            int statusInjectCount = 0;
            while (injects.TryDequeue(out var inject))
            {
                if (BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type) &&
                    type == (int)NetworkRelayMessageType.Status)
                {
                    statusInjectCount++;
                }
            }

            Assert.AreEqual(
                1,
                statusInjectCount,
                "Boot must register relay-server presence with exactly one Status(0) inject; " +
                "MatchToSend on the same tick is covered by BootDeferredIdleMatch.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_AfterSquadDrop_InjectCleanupAndDefersRematch()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.InSquad;
            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = 1;
            farm.nextIdleMatchTime[0] = 0;

            var config = BotRelayFlowTestFixtures.CreateTickConfig(10.0);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadDrop
            });
            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.Greater(farm.nextIdleMatchTime[0], config.elapsedTime);
            Assert.AreEqual(2, injects.Count);

            int mismatchType = 0;
            int statusType = 0;
            while (injects.TryDequeue(out var inject))
            {
                Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type));
                if (type == (int)NetworkRelayMessageType.Mismatch)
                    mismatchType = type;
                if (type == (int)NetworkRelayMessageType.Status)
                    statusType = type;
            }

            Assert.AreEqual((int)NetworkRelayMessageType.Mismatch, mismatchType);
            Assert.AreEqual((int)NetworkRelayMessageType.Status, statusType);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InLevelDrop_FreesSquadLeaseBeforeImmediatelyFollowingInvite()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.state = BotState.InLevel;
            state.targetUserStageID = 210;

            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            session.remoteChannelStatus = 210;
            session.remoteOnline = true;

            ref var runtime = ref farm.replayRuntime.ElementAt(0);
            runtime.flags = BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing;
            farm.agentFlags[0] |= BotAgentRuntimeFlags.PlayHandled;

            Assert.IsTrue(BotMatchGuard.TryClaimSquadSlot(
                ref farm, 0, BotRelayFlowTestFixtures.SquadInviteId));

            // Host removal and the next Invite may arrive in one agent tick. Drop must flatten the
            // old level before the one-shot replacement Invite is evaluated.
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadDrop
            });
            const uint nextSquadInviteId = 77;
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(nextSquadInviteId)
            });

            var config = BotRelayFlowTestFixtures.CreateTickConfig(10.0);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsTrue(farm.agentStates[0].hasPendingInvite);
            Assert.AreEqual(nextSquadInviteId, farm.agentStates[0].pendingInvite.squadInviteID);
            Assert.IsTrue(BotMatchGuard.HasSquadSlotClaim(in farm, 0, nextSquadInviteId));
            Assert.AreEqual(BotReplayRuntimeFlags.None, farm.replayRuntime[0].flags);
            Assert.IsFalse(farm.sessions[0].IsInSquad);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    public void MatchResponse_ParseMatch_EnqueuesMatchEvent()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMatchApp(
            9,
            BotRelayFlowTestFixtures.MatchLevel,
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
            Assert.AreEqual(BotRelayEventType.Match, evt.type);
            Assert.AreEqual(9, evt.match.matchID);
            Assert.AreEqual(BotRelayFlowTestFixtures.MatchLevel, evt.match.level);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void MatchResponse_InSquad_OnApplyMatch_SendsApplyMatch()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            farm.sessions[0] = session;
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ApplyMatch
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.Greater(BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0), 0);

            var packet = BotRelayFlowTestFixtures.GetOutboundPacket(ref farm, 0, 0);
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadFirstMessageType(in packet, out int type));
            Assert.AreEqual((int)ClientMessageType.ApplyMatch, type);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchLevel_ZeroBasedRankZero_RemainsZero()
    {
        var profile = new BotConfig.BotProfile { matchLevel = 0 };
        Assert.AreEqual(0, BotConfig.ResolveMatchLevel(in profile));

        profile.matchLevel = -1;
        Assert.AreEqual(0, BotConfig.ResolveMatchLevel(in profile));
    }

    [Test]
    [Category("Regression")]
    public void InviteWhileStateIdleButLiveStageStatus_Ignored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channelStatus = 5;
            farm.sessions[0] = session;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(99u)
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(5f));

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].hasPendingInvite);
            Assert.IsFalse(BotMatchGuard.HasAnySquadSlotClaim(in farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void WorldInviteResponse_InjectPath_QueuesMismatchBeforeJoinWithoutLegacyOutbound()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });

            var inviteTick = BotRelayFlowTestFixtures.CreateTickConfig(0f, 0f);
            inviteTick.injectEnabled = 1;
            inviteTick.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in inviteTick);

            var joinTick = BotRelayFlowTestFixtures.CreateTickConfig(1.1f, 0f);
            joinTick.injectEnabled = 1;
            joinTick.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in joinTick);

            Assert.AreNotEqual(0, farm.agentFlags[0] & BotAgentRuntimeFlags.JoinDispatched);
            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));

            int mismatchIndex = -1;
            int statusIndex = -1;
            int joinIndex = -1;
            int index = 0;
            while (injects.TryDequeue(out var inject))
            {
                Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type));
                if (type == (int)NetworkRelayMessageType.Mismatch && mismatchIndex < 0)
                    mismatchIndex = index;
                if (type == (int)NetworkRelayMessageType.Status && statusIndex < 0)
                    statusIndex = index;
                if (type == (int)NetworkRelayMessageType.Join && joinIndex < 0)
                    joinIndex = index;
                ++index;
            }

            Assert.GreaterOrEqual(mismatchIndex, 0);
            Assert.GreaterOrEqual(statusIndex, 0, "Join preparation must refresh Status(0) for Server identity registration.");
            Assert.GreaterOrEqual(joinIndex, 0);
            Assert.Less(mismatchIndex, statusIndex);
            Assert.Less(statusIndex, joinIndex);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PremadeSquad_JoinAndChapterStageBeforeApplyMatch_KeepStatusZero()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.hasPendingInvite = true;
            agentState.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite();
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
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
                type = BotRelayEventType.ChapterStage,
                chapterStageID = 68
            });

            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig();
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].matchPaired);
            Assert.AreEqual(0, farm.sessions[0].channelStatus,
                "Join/ChapterStage in a ranked lobby must not make the bot look in-level.");
            Assert.AreEqual(0, injects.Count);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ApplyMatch
            });
            tickConfig.elapsedTime = 1f;
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.IsTrue(injects.TryDequeue(out var applyMatch));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(
                in applyMatch, out int messageType));
            Assert.AreEqual((int)ClientMessageType.ApplyMatch, messageType);
            Assert.AreEqual(0, injects.Count,
                "ApplyMatch consent must not be accompanied by a Status packet.");
            Assert.AreEqual(0, farm.sessions[0].channelStatus);
            Assert.IsFalse(farm.agentStates[0].matchPaired);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PremadeSquad_ApplyMatchIsConsent_ThenMatchStartsCommonMatchFlow()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.remotePlayerCount = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remoteChannelStatus = 26;
            farm.sessions[0] = session;
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.targetUserStageID = 26;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ApplyMatch
            });

            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig();
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
            Assert.IsTrue(injects.TryDequeue(out var inject));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type));
            Assert.AreEqual((int)ClientMessageType.ApplyMatch, type);
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].matchPaired);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead
                {
                    matchID = 9,
                    level = 0
                }
            });
            tickConfig.elapsedTime = 1f;
            BotAgentLogic.Execute(0, ref farm, in tickConfig);
            Assert.IsTrue(farm.agentStates[0].matchPaired);
            Assert.AreEqual(9, farm.sessions[0].matchID);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ApplyMatch
            });
            tickConfig.elapsedTime = 2f;
            BotAgentLogic.Execute(0, ref farm, in tickConfig);
            while (injects.TryDequeue(out var postMatchInject))
            {
                Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(
                    in postMatchInject, out int postMatchType));
                Assert.AreNotEqual((int)ClientMessageType.ApplyMatch, postMatchType,
                    "A late ApplyMatch after authoritative MatchToRead must not restart team consent.");
            }
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InjectAppPacket_LargeRecordedFrame_PreservesPacketBeyondLegacy500ByteCap()
    {
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            using var recordedBytes = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
            var recordedWriter = new DataStreamWriter(recordedBytes);
            recordedWriter.WriteReplyHeader((int)ReplyMessageType.PlayerProperty, NetworkRelayType.Channel);
            for (int i = 0; i < 678; ++i)
                recordedWriter.WriteByte((byte)((i * 17) & 0xFF));

            var recordedPacket = default(BotRelayPacket);
            Assert.IsTrue(recordedPacket.TryWriteFrom(recordedBytes, recordedWriter.Length));
            Assert.Greater(recordedPacket.length, 500);
            Assert.IsTrue(BotRelayCodec.TryBuildPlayerPropertyPacket(in recordedPacket, out var packet));
            Assert.Greater(packet.length, 500);

            var writer = injects.AsParallelWriter();
            Assert.IsTrue(BotRelayInjectOps.InjectAppPacket(ref writer, 117u, in packet));
            Assert.IsTrue(injects.TryDequeue(out var inject));
            Assert.AreEqual(117u, inject.id);
            Assert.AreEqual(packet.length, inject.packet.length);

            for (int i = 0; i < packet.length; ++i)
                Assert.AreEqual(packet.GetByte(i), inject.packet.GetByte(i), $"byte {i}");
        }
        finally
        {
            injects.Dispose();
        }
    }

    [Test]
    public void MatchResponse_OnMatchSuccess_StaysInSquad()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            farm.sessions[0] = session;
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = 3, level = 5 }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchHost_RemoteStageWithoutMatchStart_DoesNotArmReplay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.matchID = 9;
            session.remotePlayerCount = 1;
            session.remoteOnline = true;
            session.remoteChannelStatus = 213;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.isHost = true;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchPaired = true;
            farm.agentStates[0] = agentState;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(0u, farm.agentStates[0].targetUserStageID,
                "Remote Status is not a substitute for the local ApplyStart descriptor.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PremadeSquad_MatchStart_ArmsLoginPropertyFromApplyStartTuple()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 5;
            session.remotePlayerCount = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remoteOnline = true;
            session.remoteChannelStatus = 56;
            farm.sessions[0] = session;

            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            state.targetUserStageID = 56;
            farm.agentStates[0] = state;
            farm.lastSeenChapterStage[0] = 56;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = 3, level = 0 }
            });
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.MatchStart,
                matchStart = new ClientMessageMatchStart
                {
                    matchID = 3,
                    userStageID = 210,
                    isRestart = true,
                    levelID = 22,
                    stage = 0,
                    levelName = new FixedString32Bytes("Bronze"),
                    sceneName = new FixedString32Bytes("Scenes/Level2-2.scene")
                }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.IsTrue(farm.agentStates[0].matchPaired);
            Assert.IsFalse(farm.agentStates[0].awaitingFreshMatchStage,
                "ApplyStart's server Stage must break the human-Host/Bot login deadlock.");
            Assert.AreEqual(210u, farm.agentStates[0].targetUserStageID);
            Assert.AreEqual(0, farm.sessions[0].remoteChannelStatus,
                "The configured local Stage must not be forged as the remote human's Status.");
            Assert.AreEqual(
                BotAgentRuntimeFlags.MatchStartReceived | BotAgentRuntimeFlags.PendingPlay,
                farm.agentFlags[0]);
            Assert.AreEqual(
                BotMatchLoginPhase.PendingPlayerProperty,
                farm.agentStates[0].matchLoginPhase,
                "The first matching tick publishes PlayerProperty only and keeps Status at 0.");
            Assert.AreEqual(22u, farm.pendingPlayMessage[0].levelID);
            Assert.AreEqual("Scenes/Level2-2.scene", farm.pendingPlayMessage[0].sceneName.ToString());
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PremadeSquad_MatchClearsLobbyStageAndWaitsForFreshStatus()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 5;
            session.remotePlayerCount = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remoteOnline = true;
            session.remoteChannelStatus = 56;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.targetUserStageID = 56;
            agentState.remotePlayerPropertySeen = true;
            farm.agentStates[0] = agentState;
            farm.lastSeenChapterStage[0] = 56;
            farm.prevRemoteChannelStatus[0] = 56;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = 9, level = 0 }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.IsTrue(farm.agentStates[0].matchPaired);
            Assert.IsTrue(farm.agentStates[0].awaitingFreshMatchStage);
            Assert.IsFalse(farm.agentStates[0].remotePlayerPropertySeen);
            Assert.AreEqual(0, farm.sessions[0].remoteChannelStatus);
            Assert.AreEqual(0u, farm.agentStates[0].targetUserStageID);
            Assert.AreEqual(0u, farm.lastSeenChapterStage[0]);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0],
                "The stale lobby Stage must not arm replay on the Match tick.");

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 5,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = (ClientRemotePlayerFlag)((56 << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                                                        (int)NetworkRelayChannelFlag.Online)
                    }
                }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0.5f));
            Assert.IsTrue(farm.agentStates[0].awaitingFreshMatchStage);
            Assert.AreEqual(0, farm.sessions[0].remoteChannelStatus);
            Assert.AreEqual(0u, farm.agentStates[0].targetUserStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0],
                "A repeated Join carrying the old Stage must not satisfy the fresh-stage gate.");

            const int currentStage = 210;
            int channelFlag = (currentStage << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                              (int)NetworkRelayChannelFlag.Online |
                              (int)NetworkRelayChannelFlag.Creator;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayStatusApp(
                channelFlag,
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var statusApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in statusApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);

            Assert.IsFalse(farm.agentStates[0].awaitingFreshMatchStage);
            Assert.AreEqual(currentStage, farm.sessions[0].remoteChannelStatus);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));

            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0],
                "Fresh remote Status is diagnostic only; matching entry requires MatchStart.");
            Assert.AreEqual((uint)currentStage, farm.agentStates[0].targetUserStageID);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.MatchStart,
                matchStart = new ClientMessageMatchStart
                {
                    matchID = 9,
                    userStageID = currentStage,
                    levelID = 22,
                    stage = 0,
                    sceneName = new FixedString32Bytes("Scenes/Level2-2.scene")
                }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.5f));
            Assert.AreEqual(
                BotAgentRuntimeFlags.MatchStartReceived | BotAgentRuntimeFlags.PendingPlay,
                farm.agentFlags[0]);
            Assert.AreEqual(
                BotMatchLoginPhase.PendingPlayerProperty,
                farm.agentStates[0].matchLoginPhase);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_LegacyPlay_IsIgnored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.remotePlayerCount = 1;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchPaired = true;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Play,
                play = new ClientMessagePlay
                {
                    levelID = 7,
                    stage = 2,
                    sceneName = new FixedString32Bytes("Arena_Main")
                }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(0u, farm.pendingPlayMessage[0].levelID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchCreateThenPeerJoin_KeepsIsHost()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            int channelFlag = (int)NetworkRelayChannelFlag.Online | (int)NetworkRelayChannelFlag.Creator;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundHostCreateEchoApp(1, channelFlag, out var createApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in createApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);
            Assert.IsTrue(farm.sessions[0].isHost);
            Assert.AreEqual(1, farm.sessions[0].channel);

            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundJoinApp(
                1,
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var joinApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in joinApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);
            Assert.IsTrue(farm.sessions[0].isHost);
            Assert.AreEqual(BotRelayFlowTestFixtures.RealPlayerUserId, farm.sessions[0].remoteUserID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RelayStatusMessage_ParsesRemoteStageFromChannelFlag()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            const int stageId = 210;
            int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                              (int)NetworkRelayChannelFlag.Online |
                              (int)NetworkRelayChannelFlag.Creator;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayStatusApp(
                channelFlag,
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var statusApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in statusApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);
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
    public void RelayQueryMessage_ParsesRemoteStageFromChannelFlag()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            const int stageId = 213;
            const int squadChannel = 0;
            int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                              (int)NetworkRelayChannelFlag.Online |
                              (int)NetworkRelayChannelFlag.Creator;
            var session = farm.sessions[0];
            session.channel = squadChannel;
            farm.sessions[0] = session;

            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayQueryApp(
                squadChannel,
                channelFlag,
                out var queryApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in queryApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);
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
    public void ShortRelayStatusWire_NormalizesToRelayApp()
    {
        const int stageId = 213;
        int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                          (int)NetworkRelayChannelFlag.Online;
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayStatusApp(
            channelFlag,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            out var statusApp));

        Assert.LessOrEqual(statusApp.length, 24);
        Assert.IsTrue(BotRelayWireBytes.TryNormalizePopEventsPayload(in statusApp, out var app));
        Assert.IsFalse(app.IsEmpty);
        Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageType(in app, out int type));
        Assert.AreEqual((int)NetworkRelayMessageType.Status, type);
    }

    [Test]
    [Category("Regression")]
    public void ServerToBot_ShortStatusWire_ReachesSession()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.SeedWireLiveServerHelloFromCatalog(ref session);
            const int stageId = 213;
            int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                              (int)NetworkRelayChannelFlag.Online;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayStatusApp(
                channelFlag,
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var statusApp));

            var wire = BotRelayIntegrationTestFixtures.BuildNullFramedRelayAppWireBytes(ref session, in statusApp);
            BotRelayIntegrationTestFixtures.RouteServerToBot(wire);
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.AreEqual(stageId, session.farm.sessions[0].remoteChannelStatus);
            Assert.IsTrue(session.farm.sessions[0].remoteOnline);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ServerToBot_ConnectShapedMultiFrame_ChapterStageAndStatus213()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.SeedWireLiveServerHelloFromCatalog(ref session);

            const int stageId = 213;
            const int squadChannel = 1;
            int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                              (int)NetworkRelayChannelFlag.Online |
                              (int)NetworkRelayChannelFlag.Creator;
            var squadSession = session.farm.sessions[0];
            squadSession.channel = squadChannel;
            session.farm.sessions[0] = squadSession;

            var agentState = session.farm.agentStates[0];
            agentState.state = BotState.InSquad;
            session.farm.agentStates[0] = agentState;

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

            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);

            Assert.AreEqual(stageId, session.farm.sessions[0].remoteChannelStatus);
            Assert.IsTrue(session.farm.sessions[0].remoteOnline);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ServerToBot_ConnectShapedCreate_ReachesInSquadButDoesNotImplyMatchSuccess()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.SeedWireLiveServerHelloFromCatalog(ref session);

            var agentState = session.farm.agentStates[0];
            agentState.state = BotState.Matching;
            session.farm.agentStates[0] = agentState;

            const int channel = 1;
            int channelFlag = (int)ClientRemotePlayerFlag.Creator;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundHostCreateEchoApp(
                channel,
                channelFlag,
                out var createApp));
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref session.catalogBlob.Value,
                in createApp,
                out var wire));

            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            BotAgentLogic.Execute(0, ref session.farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotState.InSquad, session.farm.agentStates[0].state);
            Assert.AreEqual(channel, session.farm.sessions[0].channel);
            Assert.IsFalse(session.farm.agentStates[0].matchPaired);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ServerToBot_ConnectShapedJoin_ReachesInSquadFromPendingInvite()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.SeedWireLiveServerHelloFromCatalog(ref session);

            var agentState = session.farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.hasPendingInvite = true;
            agentState.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite(0);
            session.farm.agentStates[0] = agentState;

            const uint squadChannel = 0;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundJoinApp(
                squadChannel,
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var joinApp));
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref session.catalogBlob.Value,
                in joinApp,
                out var wire));
            Assert.IsFalse(
                BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                    in wire,
                    ref session.catalogBlob.Value,
                    out _,
                    out _),
                "Join shell must not be misclassified as Invite.");

            BotRelayManager.Instance.SetPeelCatalogBlob(session.catalogBlob);
            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            BotAgentLogic.Execute(0, ref session.farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotState.InSquad, session.farm.agentStates[0].state);
            Assert.AreEqual((int)squadChannel, session.farm.sessions[0].channel);
            Assert.AreEqual(0, session.farm.sessions[0].channelStatus);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RelayPlayerPropertyMessage_EnqueuesRemotePlayerProperty()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayPlayerPropertyApp(
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var app));
            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out var evt));
            Assert.AreEqual(BotRelayEventType.RemotePlayerProperty, evt.type);
            Assert.IsTrue(farm.sessions[0].remoteOnline);
            Assert.AreNotEqual(BotRelayWireTestFixtures.BotUserId, farm.sessions[0].remoteUserID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchHost_WaitsForRemoteStatusBeforeEntering()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.isHost = true;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchPaired = true;
            farm.agentStates[0] = agentState;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadInSquad_IsHostWithoutMatchPaired_DoesNotBroadcastPlay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.isHost = true;
            session.remotePlayerCount = 1;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchPaired = false;
            farm.agentStates[0] = agentState;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
        }
        finally
        {
            farm.Dispose();
        }
    }

    /// <summary>
    /// Squad-invite path: Host Play downlink via RouteSend pipeline wire (Invite integration analogue).
    /// PlayHandled logic is covered by <see cref="PlayResponse_AgentLogic_SetsPendingPlayFlag"/>.
    /// </summary>
    [Test]
    [Category("Regression")]
    public void ServerToBot_PipelinePlay_RouteSendEnqueuesPlayEvent()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayIntegrationTestFixtures.BuildDriverShapePlayWireBytes());

            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            Assert.Greater(session.farm.inboundCount[0], 0, "DrainInbound should extract Play app from pipeline wire.");

            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            Assert.Greater(session.farm.eventCount[0], 0, "PumpInbound should enqueue Play event.");
            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref session.farm, out var evt));
            Assert.AreEqual(BotRelayEventType.Play, evt.type);
            Assert.AreEqual(7u, evt.play.levelID);
            Assert.AreEqual(2, evt.play.stage);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SoloMatch_TemporarySquadJoinThenMatchStartsWithoutApplyMatch()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 0;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.Matching;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 0,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = ClientRemotePlayerFlag.Online
                    }
                }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].matchPaired);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = 7, level = 0 }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.IsTrue(farm.agentStates[0].matchPaired);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_SquadJoinWithoutPendingInvite_DoesNotInferMatchSuccess()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 42;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.hasPendingInvite = false;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 42,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = ClientRemotePlayerFlag.Online
                    }
                }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].matchPaired);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_SquadInviteJoin_DoesNotSetMatchPaired()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 99;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.hasPendingInvite = true;
            agentState.targetUserStageID = 213;
            agentState.pendingInvite = new ClientMessageSquadInviteToRead { squadInviteID = 99 };
            farm.agentStates[0] = agentState;
            farm.lastSeenChapterStage[0] = 213;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 99,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = ClientRemotePlayerFlag.Online
                    }
                }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].matchPaired);
            Assert.AreEqual(0u, farm.agentStates[0].targetUserStageID);
            Assert.AreEqual(0u, farm.lastSeenChapterStage[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadInviteJoin_DuplicateJoinBroadcast_DoesNotSetMatchPaired()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.remotePlayerCount = 1;
            session.remoteOnline = true;
            session.remoteChannelStatus = 5;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.hasPendingInvite = true;
            agentState.pendingInvite = new ClientMessageSquadInviteToRead { squadInviteID = 1 };
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 1,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = ClientRemotePlayerFlag.Online
                    }
                }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].matchPaired);

            // Host re-broadcast Join every frame while waiting for stable presence.
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 1,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = ClientRemotePlayerFlag.Online
                    }
                }
            });
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.RemotePlayerProperty,
                header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.IsFalse(farm.agentStates[0].matchPaired);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void TryCommitPendingPlay_AfterPlayCapture_ArmsPendingPlay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchLevel = 1;
            agentState.playLevelID = 1;
            agentState.squadHostPlaySeen = true;
            farm.agentStates[0] = agentState;
            {
                var session = farm.sessions[0];
                session.channel = 0;
                farm.sessions[0] = session;
            }
            farm.lastSeenChapterStage[0] = 5;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };

            var pendingPlay = farm.pendingPlayMessage[0];
            var committed = BotAgentLogic.TryCommitPendingPlayForLevelEntry(
                0,
                ref farm,
                BotRelayFlowTestFixtures.CreateTickConfig(1f),
                in pendingPlay);

            Assert.IsTrue(committed);
            Assert.AreEqual(
                BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay,
                farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadInvite_ChapterStageAfterManualPlayCapture_ArmsPendingPlay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchLevel = 1;
            agentState.playLevelID = 1;
            agentState.squadHostPlaySeen = true;
            farm.agentStates[0] = agentState;
            {
                var session = farm.sessions[0];
                session.channel = 0;
                farm.sessions[0] = session;
            }
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ChapterStage,
                chapterStageID = 5
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(5u, farm.lastSeenChapterStage[0]);
            Assert.AreEqual(
                BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay,
                farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadInvite_PlayBeforeChapterStage_ArmsPendingPlay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchLevel = 1;
            farm.agentStates[0] = agentState;
            {
                var session = farm.sessions[0];
                session.channel = 0;
                farm.sessions[0] = session;
            }

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Play,
                play = new ClientMessagePlay { levelID = 1, stage = 0 }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(1u, farm.agentStates[0].playLevelID);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ChapterStage,
                chapterStageID = 5
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(2f));
            Assert.AreEqual(5u, farm.lastSeenChapterStage[0]);
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(
                BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay,
                farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void HandlePendingPlay_StageZero_KeepsPendingPlay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        var catalogEntries = new NativeArray<BotReplayCatalogEntry>(0, Allocator.TempJob);
        try
        {
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var __injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingPlay(
                0,
                ref farm,
                in catalog,
                0.0,
                ref __injectWriter,
                1);

            Assert.AreEqual(BotAgentRuntimeFlags.PendingPlay, farm.agentFlags[0]);
            Assert.AreEqual(0, injects.Count);
        }
        finally
        {
            injects.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }







    [Test]
    [Category("Regression")]
    public void SquadInvite_PlayThenChapterStageNextTick_ArmsPendingPlay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchLevel = 1;
            farm.agentStates[0] = agentState;
            {
                var session = farm.sessions[0];
                session.channel = 0;
                farm.sessions[0] = session;
            }

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Play,
                play = new ClientMessagePlay { levelID = 1, stage = 0 }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(1u, farm.agentStates[0].playLevelID);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ChapterStage,
                chapterStageID = 5
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(2f));
            Assert.AreEqual(
                BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay,
                farm.agentFlags[0]);
            Assert.AreEqual(5u, farm.lastSeenChapterStage[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadInvite_ChapterStageWithoutHostPlay_DoesNotArmReplay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchLevel = 1;
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.channel = 0;
            farm.sessions[0] = session;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ChapterStage,
                chapterStageID = 5
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));

            Assert.AreEqual(BotAgentRuntimeFlags.None,
                farm.agentFlags[0] & (BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay));
            Assert.AreEqual(5u, farm.lastSeenChapterStage[0]);
            Assert.AreEqual(0, farm.sessions[0].channelStatus,
                "ChapterStage alone is lobby metadata and must not publish Bot Status.");
        }
        finally
        {
            farm.Dispose();
        }
    }





    [Test]
    [Category("Regression")]
    public void SquadInvite_ChapterStageZeroAfterHostPlay_PreservesStageAndArmsReplay()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchLevel = 1;
            agentState.squadHostPlaySeen = true;
            agentState.targetUserStageID = 5;
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.channel = 0;
            farm.sessions[0] = session;
            farm.lastSeenChapterStage[0] = 5;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ChapterStage,
                chapterStageID = 0
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));

            Assert.AreEqual(5u, farm.lastSeenChapterStage[0]);
            Assert.AreEqual(5u, farm.agentStates[0].targetUserStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay,
                farm.agentFlags[0]);
            Assert.AreEqual(1u, farm.pendingPlayMessage[0].levelID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void HandlePendingPlay_PostPlay_SendsPropertyThenNonZeroStatusWithoutClear()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.squadHostPlaySeen = true;
            agentState.targetUserStageID = 5;
            farm.agentStates[0] = agentState;
            farm.lastSeenChapterStage[0] = 5;
            farm.agentFlags[0] = BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingPlay(
                0, ref farm, in catalog, 0.0, ref injectWriter, 1);

            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.AreEqual(5, farm.sessions[0].channelStatus);
            Assert.AreEqual(2, injects.Count,
                "Post-Play handshake must contain one PlayerProperty and one non-zero Status only.");
            Assert.IsTrue(injects.TryDequeue(out var propertyInject));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in propertyInject, out int propertyType));
            Assert.AreEqual((int)ReplyMessageType.PlayerProperty, propertyType);
            Assert.IsTrue(injects.TryDequeue(out var statusInject));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in statusInject, out int statusType));
            Assert.AreEqual((int)NetworkRelayMessageType.Status, statusType);
            Assert.AreEqual(0, injects.Count, "A trailing Status(0) would cancel the host after Joined.");
        }
        finally
        {
            injects.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RelayDisconnectMessage_PreservesRemoteStageAndOnlyMarksOffline()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            const int stageId = 210;
            var session = farm.sessions[0];
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            session.remoteChannelStatus = stageId;
            session.remoteOnline = true;
            farm.sessions[0] = session;

            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayDisconnectApp(
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var disconnectApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in disconnectApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader(),
                sentInitialStatus: true);

            Assert.AreEqual(
                stageId,
                farm.sessions[0].remoteChannelStatus,
                "Disconnect has no status payload and must preserve the Server's last stage value.");
            Assert.IsFalse(farm.sessions[0].remoteOnline);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchStart_LoginPropertyThenStatus_UsesTwoTickHandshake()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.matchPaired = true;
            agentState.awaitingFreshMatchStage = false;
            agentState.targetUserStageID = 10;
            agentState.matchLoginPhase = BotMatchLoginPhase.PendingPlayerProperty;
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.channel = 7;
            session.matchID = 7;
            session.remotePlayerCount = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remoteOnline = true;
            farm.sessions[0] = session;
            farm.agentFlags[0] =
                BotAgentRuntimeFlags.MatchStartReceived | BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay
            {
                levelID = 1,
                stage = 0,
                sceneName = new FixedString32Bytes("Scenes/Level1.scene")
            };

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingPlay(
                0, ref farm, in catalog, 0.0, ref injectWriter, 1);

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(BotMatchLoginPhase.PlayerPropertySent, farm.agentStates[0].matchLoginPhase);
            Assert.AreEqual(0, farm.sessions[0].channelStatus,
                "The login phase must keep Status at 0 so the Host can leave its remote-login wait.");
            Assert.AreEqual(BotAgentRuntimeFlags.MatchStartReceived, farm.agentFlags[0]);
            Assert.AreEqual(1, injects.Count, "The first match-entry tick must inject PlayerProperty only.");
            Assert.IsTrue(injects.TryDequeue(out var loginProperty));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in loginProperty, out int loginType));
            Assert.AreEqual((int)ReplyMessageType.PlayerProperty, loginType);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(
                BotAgentRuntimeFlags.MatchStartReceived |
                BotAgentRuntimeFlags.PlayHandled |
                BotAgentRuntimeFlags.PendingPlay,
                farm.agentFlags[0],
                "After PlayerProperty succeeds, MatchStart authorizes the non-zero stage handshake.");

            BotRelayReplayLoadOps.HandlePendingPlay(
                0, ref farm, in catalog, 1.0, ref injectWriter, 1);
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.AreEqual(10, farm.sessions[0].channelStatus);
            Assert.AreEqual(2, injects.Count,
                "Final match entry must inject PlayerProperty followed by non-zero Status.");
        }
        finally
        {
            injects.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchStartStage_IsNotOverwrittenByMismatchedChapterStage()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            state.matchPaired = true;
            state.awaitingFreshMatchStage = false;
            state.targetUserStageID = 210;
            farm.agentStates[0] = state;
            farm.agentFlags[0] = BotAgentRuntimeFlags.MatchStartReceived;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ChapterStage,
                chapterStageID = 68
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(210u, farm.agentStates[0].targetUserStageID);
            Assert.AreEqual(68u, farm.lastSeenChapterStage[0]);
            Assert.AreEqual(68, farm.sessions[0].remoteChannelStatus);
        }
        finally
        {
            farm.Dispose();
        }
    }







    [Test]
    [Category("Regression")]
    public void PendingPlay_AfterHostPlay_EntersInLevelWithoutLoginClearPhase()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.squadHostPlaySeen = true;
            agentState.targetUserStageID = 10;
            farm.agentStates[0] = agentState;
            farm.agentFlags[0] = BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };
            farm.lastSeenChapterStage[0] = 10;
            BotRelayReplayRuntimeGate.AddPendingPlay();

            var job = new BotRelayReplayLoadJob
            {
                farm = farm,
                catalog = new BotReplayCatalog { entries = catalogEntries },
                elapsedTime = 0.0,
                injectEnabled = 1,
                injectWriter = injects.AsParallelWriter()
            };
            job.Execute();
            farm = job.farm;

            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.AreEqual(BotAgentRuntimeFlags.PlayHandled, farm.agentFlags[0]);
            Assert.AreEqual(10, farm.sessions[0].channelStatus);
        }
        finally
        {
            injects.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ResolveUserStageID_PrefersRemoteChannelStatusOverAmbiguousCatalog()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.remoteChannelStatus = 5;
            farm.sessions[0] = session;
            farm.lastSeenChapterStage[0] = 0;
            var play = new ClientMessagePlay { levelID = 99, stage = 0 };
            uint stage = BotRelayPlayStageOps.ResolveUserStageIDForPlay(
                0,
                ref farm,
                in play);
            Assert.AreEqual(5u, stage);
        }
        finally
        {
            if (recording.IsCreated)
            {
                recording.Dispose();
            }

            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadPlay_AfterRankedExit_UsesChapterStageNotStale213()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.targetUserStageID = 213;
            farm.agentStates[0] = agentState;
            farm.lastSeenChapterStage[0] = 0;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ChapterStage,
                chapterStageID = 5
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(5u, farm.agentStates[0].targetUserStageID);
            Assert.AreEqual(5u, farm.lastSeenChapterStage[0]);

            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay
            {
                levelID = 1,
                stage = 0
            };

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var __injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingPlay(
                0,
                ref farm,
                in catalog,
                0.0,
                ref __injectWriter,
                1);

            Assert.AreEqual(5, farm.sessions[0].channelStatus);
            Assert.Greater(injects.Count, 0);
        }
        finally
        {
            injects.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_InviteSquadWithoutMatchId_DoesNotAutoEnterOnRemoteStage()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.matchID = 0;
            session.remotePlayerCount = 1;
            session.remoteOnline = true;
            session.remoteChannelStatus = 213;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            farm.agentStates[0] = agentState;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void MatchResponse_OnMismatch_ReturnsToIdleFromMatching()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var matchingState = farm.agentStates[0];
            matchingState.state = BotState.Matching;
            farm.agentStates[0] = matchingState;
            BotMatchGuard.TryClaimMatchingSlot(ref farm, 0);
            farm.nextIdleMatchTime[0] = 1000.0;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Mismatch
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.AreEqual(BotMatchGuard.NoMatchingSlotOwner, farm.matchingSlotOwner[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void MatchResponse_ParseMismatch_EnqueuesMismatchEvent()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMismatchApp(out var app));

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
            Assert.AreEqual(BotRelayEventType.Mismatch, evt.type);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void SquadJoinResponse_ParseJoin_EnqueuesSquadJoinAndSetsSession()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundJoinApp(
            BotRelayFlowTestFixtures.SquadInviteId,
            BotRelayFlowTestFixtures.RealPlayerUserId,
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
            Assert.AreEqual(BotRelayEventType.SquadJoin, evt.type);
            Assert.AreEqual(BotRelayFlowTestFixtures.SquadInviteId, evt.squadJoin.squadInviteID);
            Assert.AreEqual(BotRelayFlowTestFixtures.SquadInviteId, farm.sessions[0].channel);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void PlayResponse_ParsePlay_EnqueuesPlayEvent()
    {
        FixedString32Bytes levelName = "Level7";
        FixedString32Bytes sceneName = "Recordings/Level7.scene";
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundPlayApp(
            7,
            2,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            levelName,
            sceneName,
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
            Assert.AreEqual(BotRelayEventType.Play, evt.type);
            Assert.AreEqual(7u, evt.play.levelID);
            Assert.AreEqual(2, evt.play.stage);
            Assert.AreEqual(levelName, evt.play.levelName);
            Assert.AreEqual(sceneName, evt.play.sceneName);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void PlayResponse_AgentLogic_SetsPendingPlayFlag()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            state.matchPaired = true;
            farm.agentStates[0] = state;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Play,
                play = new ClientMessagePlay
                {
                    levelID = 7,
                    stage = 2
                }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotAgentRuntimeFlags.PlayHandled | BotAgentRuntimeFlags.PendingPlay, farm.agentFlags[0]);
            Assert.AreEqual(7u, farm.pendingPlayMessage[0].levelID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ReplayLoad_MatchMissingStageAndLevelFolders_UsesSceneKey()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = new NativeArray<BotReplayCatalogEntry>(1, Allocator.TempJob);
        catalogEntries[0] = new BotReplayCatalogEntry
        {
            sceneName = new FixedString32Bytes("Level4-1.scene"),
            stageIndex = -1,
            recording = recording
        };
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay
            {
                levelID = 0,
                stage = 0,
                sceneName = new FixedString32Bytes("Scenes/Level4-1.scene")
            };

            var session = farm.sessions[0];
            session.remoteChannelStatus = 0;
            farm.sessions[0] = session;

            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            state.matchPaired = true;
            state.targetUserStageID = 210;
            farm.agentStates[0] = state;

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingPlay(
                0,
                ref farm,
                in catalog,
                0.0,
                ref injectWriter,
                1);

            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.AreEqual(0, farm.replayRuntime[0].catalogIndex);
            Assert.AreEqual(210, farm.sessions[0].channelStatus);
            Assert.GreaterOrEqual(
                injects.Count,
                2,
                "Scene-key match entry must inject first PlayerProperty and live Status.");
        }
        finally
        {
            injects.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ReplayLoad_MissingExactKeys_DoesNotCrossKeyFallback()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay
            {
                levelID = 1,
                stage = 213
            };
            var playState = farm.agentStates[0];
            playState.targetUserStageID = 213;
            farm.agentStates[0] = playState;

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var __injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingPlay(
                0,
                ref farm,
                in catalog,
                0.0,
                ref __injectWriter,
                1);

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.Greater(injects.Count, 0);
            Assert.AreEqual(213, farm.sessions[0].channelStatus);
            Assert.AreEqual(
                BotReplayRuntimeState.InvalidCatalogIndex,
                farm.replayRuntime[0].catalogIndex,
                "A missing exact key must not start another folder's replay.");
        }
        finally
        {
            injects.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MultiQueue_UnmaterializedBucket_IsEmptyUntilRouteWrites()
    {
        var queue = new BotRelayMultiQueue<int>(4);
        try
        {
            Assert.IsFalse(queue.Dequeue(6, out _));
            Assert.IsFalse(queue.Peek(6, out _));

            queue.Enqueue(6, 117);

            Assert.IsTrue(queue.Peek(6, out var peeked));
            Assert.AreEqual(117, peeked);
            Assert.IsTrue(queue.Dequeue(6, out var dequeued));
            Assert.AreEqual(117, dequeued);
        }
        finally
        {
            queue.Dispose();
        }
    }
    [Test]
    [Category("Regression")]
    public void InLevelReplayLoad_ScheduledBurstJob_ArmsReplayGate()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            BotRelayReplayRuntimeGate.Reset();
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };
            var playState = farm.agentStates[0];
            playState.targetUserStageID = 10;
            farm.agentStates[0] = playState;

            var job = new BotRelayReplayLoadJob
            {
                farm = farm,
                catalog = new BotReplayCatalog { entries = catalogEntries },
                elapsedTime = 0.0,
                injectEnabled = 1,
                injectWriter = injects.AsParallelWriter()
            };
            job.Schedule().Complete();

            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.IsTrue(
                BotRelayReplayRuntimeGate.HasInLevel,
                "Scheduled replay-load job must arm ReplayTick; a BurstDiscard gate update leaves bots InLevel but motionless.");
        }
        finally
        {
            BotRelayReplayRuntimeGate.Reset();
            injects.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }
    [Test]
    public void InLevelReplay_LoadAndTick_EnqueuesReplayFramesAndEntersInLevel()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay
            {
                levelID = 1,
                stage = 0
            };
            var playState = farm.agentStates[0];
            playState.targetUserStageID = 10;
            farm.agentStates[0] = playState;

            Assert.IsTrue(BotRelayFlowTestFixtures.TrySimulateReplayLoad(0, ref farm, catalogEntries, 0.0));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.AreEqual(BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing, farm.replayRuntime[0].flags);

            int outboundBefore = BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0);
            var noInject = default(NativeQueue<BotRelayInject>.ParallelWriter);
            BotReplayLogic.Tick(0, ref farm, catalogEntries, 1.0, ref noInject, 0);
            int outboundAfter = BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0);
            Assert.Greater(outboundAfter, outboundBefore);
        }
        finally
        {
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    public void InLevelReplay_FirstPlayerProperty_IsSentOnLoad()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };
            farm.lastSeenChapterStage[0] = 10;

            Assert.IsTrue(BotRelayFlowTestFixtures.TrySimulateReplayLoad(0, ref farm, catalogEntries, 0.0));
            Assert.Greater(BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0), 0);

            var packet = BotRelayFlowTestFixtures.GetOutboundPacket(ref farm, 0, 0);
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadFirstMessageType(in packet, out int type));
            Assert.AreEqual((int)ReplyMessageType.PlayerProperty, type);
        }
        finally
        {
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void ResolveUserStageID_MatchDoesNotTreatPlayStageIndexAsLiveStageID()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var state = farm.agentStates[0];
            state.matchPaired = true;
            farm.agentStates[0] = state;

            var play = new ClientMessagePlay { levelID = 14, stage = 158 };
            uint stageID = BotRelayPlayStageOps.ResolveUserStageIDForPlay(
                0,
                ref farm,
                in play);

            Assert.AreEqual(0u, stageID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InLevelReplay_InjectPath_UsesQueueWithoutLegacyOutboundSlots()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var runtime = ref farm.replayRuntime.ElementAt(0);
            runtime.catalogIndex = 0;
            BotReplayLogic.PrepareRuntime(recording, ref runtime);
            BotReplayLogic.Begin(ref runtime);

            var injectWriter = injects.AsParallelWriter();
            BotReplayLogic.Tick(0, ref farm, catalogEntries, 1.0, ref injectWriter, 1);

            Assert.Greater(injects.Count, 0);
            Assert.AreEqual(0, BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0));
        }
        finally
        {
            injects.Dispose();
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InSquad_StatusFlapWhilePendingPlay_DoesNotLeave()
    {
        // When the host clicks Play, remoteChannelStatus often flaps non-zero → 0 while the scene
        // loads. The bot is still InSquad (replay load runs after PostTick) with PendingPlay set —
        // that flap must not trigger an immediate SquadLeave or the bot never streams in-level.
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var squadState = farm.agentStates[0];
            squadState.state = BotState.InSquad;
            farm.agentStates[0] = squadState;

            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteChannelStatus = 5;
            session.remoteOnline = true;
            farm.sessions[0] = session;
            farm.prevRemoteChannelStatus[0] = 5;

            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay | BotAgentRuntimeFlags.PlayHandled;

            session.remoteChannelStatus = 0;
            session.remoteOnline = false;
            farm.sessions[0] = session;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(
                BotState.InSquad,
                farm.agentStates[0].state,
                "A remote status flap during the Play handshake must not evict the bot from the squad.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InLevelReplayLoad_ResetsStaleRemotePresenceSeen()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var staleState = farm.agentStates[0];
            staleState.state = BotState.InSquad;
            staleState.remotePresenceSeen = true;
            staleState.remoteAbsentSince = 50.0;
            farm.agentStates[0] = staleState;
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingPlay;
            farm.pendingPlayMessage[0] = new ClientMessagePlay { levelID = 1, stage = 0 };
            farm.lastSeenChapterStage[0] = 10;

            Assert.IsTrue(BotRelayFlowTestFixtures.TrySimulateReplayLoad(0, ref farm, catalogEntries, 100.0));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].remotePresenceSeen);
            Assert.AreEqual(-1.0, farm.agentStates[0].remoteAbsentSince, 0.001);
            Assert.AreEqual(
                BotAgentRuntimeFlags.None,
                farm.agentFlags[0] & BotAgentRuntimeFlags.RemoteAbsentSuppressed,
                "InLevel entry must clear stale streaming suppression.");
        }
        finally
        {
            if (recording.IsCreated)
                recording.Dispose();
            catalogEntries.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    public void RemoteLeaveDuringLevel_IsIgnored_StaysInLevel()
    {
        // Design goal (BotAgentLogic.__TickChannelTracker): InLevel is sticky against a *brief*
        // remote-leave flap. While the host waits for the bot's stable Joined presence it
        // re-broadcasts the channel Create/Join/Leave bundle every frame, so remoteChannelStatus
        // flaps to 0 spuriously. Honouring a single flap would drop the bot out of the level, restart
        // the replay (repeated SelectSkill on re-entry, send-queue overflow -5) and keep the bot
        // invisible. A single flap frame must therefore be ignored. (A *sustained* leave is honoured
        // instead — see RemoteSustainedLeaveDuringLevel_TransitionsToLeaving.)
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var inLevelState = farm.agentStates[0];
            inLevelState.state = BotState.InLevel;
            farm.agentStates[0] = inLevelState;
            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteChannelStatus = 5;
            session.remoteOnline = true;
            farm.sessions[0] = session;
            farm.prevRemoteChannelStatus[0] = 5;

            // Confirm the peer present first so the presence baseline is latched (otherwise a
            // never-confirmed peer just streams by default and the flap is trivially ignored).
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0.5));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);

            // A single handshake-flap frame: status momentarily 0.
            session.remoteChannelStatus = 0;
            session.remoteOnline = false;
            farm.sessions[0] = session;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(
                BotState.InLevel,
                farm.agentStates[0].state,
                "A single remote channel-leave flap must not knock the bot out of a level it is in.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RemoteSustainedLeaveDuringLevel_TransitionsToLeaving()
    {
        // Complements the sticky test above. A single handshake-flap frame stays InLevel, but when the
        // human genuinely leaves the level (remoteChannelStatus stays 0 past the InLevel arm grace +
        // the debounce window) the bot must stop. Otherwise the server keeps relaying our replay
        // gameplay to the now non-ACKing human and saturates its reliable send window
        // (NetworkSendMessage -5). Exit target is Leaving (which emits SquadLeave + Status(0)).
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var s = farm.agentStates[0];
            s.state = BotState.InLevel;
            s.stateEnterTime = 0.0;
            s.remoteAbsentSince = -1.0; // entered InLevel with the peer present (mirrors __TransitionTo)
            farm.agentStates[0] = s;

            // Keep the replay "Playing" so the replay-completion exit (__TickState) can't fire and
            // steal the transition — we want to prove the remote-leave path drives it.
            var runtime = farm.replayRuntime[0];
            runtime.flags = BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing;
            farm.replayRuntime[0] = runtime;

            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteChannelStatus = 5; // human present in stage
            session.remoteOnline = true;     // ...and online
            farm.sessions[0] = session;
            farm.prevRemoteChannelStatus[0] = 5;

            // Frame past the 3.0s arm grace with the human present -> stays InLevel, debounce cleared.
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(4.0));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);

            // Human genuinely leaves the level: stage status drops to 0 (not a mere disconnect).
            session.remoteChannelStatus = 0;
            session.remoteOnline = false;
            farm.sessions[0] = session;

            // First absent frame: debounce starts, still sticky (flap protection).
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(4.0));
            Assert.AreEqual(
                BotState.InLevel,
                farm.agentStates[0].state,
                "A single absent frame must not end the level (handshake-flap protection).");

            // Sustained absence past the debounce window (4.0 + 2.0 + 0.1) -> Leaving.
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(6.1));
            Assert.AreEqual(
                BotState.Leaving,
                farm.agentStates[0].state,
                "A sustained remote leave in-level must move the bot to Leaving so it stops flooding " +
                "the departed human (NetworkSendMessage -5).");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RemoteDisconnectDuringLevel_ReconnectBeforeTimeout_StaysInLevelAndResumesStream()
    {
        // A short disconnect preserves the squad for reconnect and suppresses replay traffic. The
        // same unified timeout used in the lobby eventually releases the Bot if reconnect never wins.
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var s = farm.agentStates[0];
            s.state = BotState.InLevel;
            s.stateEnterTime = 0.0;
            s.remoteAbsentSince = -1.0;
            farm.agentStates[0] = s;

            var runtime = farm.replayRuntime[0];
            runtime.flags = BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing;
            farm.replayRuntime[0] = runtime;

            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteChannelStatus = 5; // still "in" the stage
            session.remoteOnline = true;
            farm.sessions[0] = session;
            farm.prevRemoteChannelStatus[0] = 5;

            // Present frame past the arm grace: streaming, not suppressed.
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(4.0));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.AreEqual(
                (BotAgentRuntimeFlags)0,
                farm.agentFlags[0] & BotAgentRuntimeFlags.RemoteAbsentSuppressed,
                "A present, online peer must not suppress streaming.");

            // Human disconnects: offline, but the stage status is preserved for reconnect.
            session.remoteOnline = false;
            farm.sessions[0] = session;

            // First offline tick starts the unified disconnect grace.
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(5.0));
            Assert.AreEqual(
                BotState.InLevel,
                farm.agentStates[0].state,
                "A short disconnect must preserve the in-level squad for reconnect.");
            Assert.AreNotEqual(
                (BotAgentRuntimeFlags)0,
                farm.agentFlags[0] & BotAgentRuntimeFlags.RemoteAbsentSuppressed,
                "While the peer is offline the bot must pause streaming (NetworkSendMessage -5 guard).");

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(9.9));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state, "The 5s grace has not expired.");

            // Human reconnects: streaming resumes, still in the level.
            session.remoteOnline = true;
            farm.sessions[0] = session;
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(10.0));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.Less(farm.agentStates[0].remoteOfflineSince, 0.0, "Reconnect must cancel the offline timer.");
            Assert.AreEqual(
                (BotAgentRuntimeFlags)0,
                farm.agentFlags[0] & BotAgentRuntimeFlags.RemoteAbsentSuppressed,
                "On reconnect the bot must resume streaming to the peer.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RemoteDisconnectDuringLevel_LeavesAfterUnifiedOfflineTimeout()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var state = farm.agentStates[0];
            state.state = BotState.InLevel;
            state.stateEnterTime = 0.0;
            state.remoteAbsentSince = -1.0;
            farm.agentStates[0] = state;

            var runtime = farm.replayRuntime[0];
            runtime.flags = BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing;
            farm.replayRuntime[0] = runtime;

            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            session.remoteChannelStatus = 5;
            session.remoteOnline = true;
            farm.sessions[0] = session;
            farm.prevRemoteChannelStatus[0] = 5;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(4.0));

            session.remoteOnline = false;
            farm.sessions[0] = session;
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(5.0));
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(10.1));
            Assert.AreEqual(
                BotState.Leaving,
                farm.agentStates[0].state,
                "An in-level human offline past the shared timeout must release the Bot slot.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RemoteDisconnectInSquad_LeavesAfterUnifiedOfflineTimeout()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            state.stateEnterTime = 0.0;
            farm.agentStates[0] = state;

            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            session.remoteChannelStatus = 0;
            session.remoteOnline = true;
            farm.sessions[0] = session;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0.0));

            session.remoteOnline = false;
            farm.sessions[0] = session;
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1.0));
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(5.9));
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(6.1));
            Assert.AreEqual(
                BotState.Leaving,
                farm.agentStates[0].state,
                "A lobby human offline past the shared timeout must not occupy the Bot indefinitely.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void InLevelBeforeRemotePresenceConfirmed_DoesNotSuppressStreaming()
    {
        // Regression guard for the "joined but invisible in-level" bug: streaming must default ON at
        // level entry. remoteOnline defaults false and is only set once a channel/status message
        // carrying the Online bit arrives; suppressing on that stale default would pause the replay
        // and the bot would never stream -> invisible. Suppression may only engage AFTER we positively
        // observe the peer present and then absent (see the disconnect/leave tests above).
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var s = farm.agentStates[0];
            s.state = BotState.InLevel;
            s.stateEnterTime = 0.0;
            s.remoteAbsentSince = -1.0;
            s.remotePresenceSeen = false; // never confirmed the peer this level
            farm.agentStates[0] = s;

            // Replay armed so the replay-finished exit can't fire and steal the assertion.
            var runtime = farm.replayRuntime[0];
            runtime.flags = BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing;
            farm.replayRuntime[0] = runtime;

            // Session as it can look right after entering the level: presence not yet refreshed.
            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteChannelStatus = 0;
            session.remoteOnline = false;
            farm.sessions[0] = session;
            farm.prevRemoteChannelStatus[0] = 0;

            // Several seconds pass with no presence confirmation at all.
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(5.0));

            Assert.AreEqual(
                BotState.InLevel,
                farm.agentStates[0].state,
                "Never having confirmed the peer must not end the level.");
            Assert.AreEqual(
                (BotAgentRuntimeFlags)0,
                farm.agentFlags[0] & BotAgentRuntimeFlags.RemoteAbsentSuppressed,
                "Streaming must default ON until presence is positively confirmed (else invisible bot).");
        }
        finally
        {
            farm.Dispose();
        }
    }
}
