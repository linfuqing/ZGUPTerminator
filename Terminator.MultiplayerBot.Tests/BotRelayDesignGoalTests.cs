using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
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
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig(0.0);
            tickConfig.replayCatalogMissing = 1;
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injects.AsParallelWriter();

            BotAgentLogic.Execute(0, ref farm, in tickConfig);
            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.AreEqual(0, injects.Count);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].HasPendingInvite);
            Assert.IsFalse(HasAnySquadSlotClaim(in farm, 0));
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    public void WorldInvite_ParseTransportPayload_EnqueuesSquadInvite()
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
                BotRelayWireTestFixtures.BuildBotHeader());

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
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var invite = BotRelayFlowTestFixtures.BuildPublicInvite();
            var tickConfig = new BotRelayFarmTickConfig
            {
                inviteTimeoutMin = 10f,
                inviteTimeoutMax = 10f,
                elapsedTime = 0,
                frameSeed = 1,
                injectEnabled = 1,
                injectWriter = injects.AsParallelWriter()
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
            int beforeJoinDispatch = injects.Count;

            tickConfig.elapsedTime = 10;
            BotAgentLogic.Execute(0, ref farm, tickConfig);
            Assert.AreNotEqual(0, farm.agentFlags[0] & BotAgentRuntimeFlags.JoinDispatched);
            Assert.Greater(injects.Count, beforeJoinDispatch);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void DuplicateSquadInvite_AfterJoinSent_DoesNotRearmTimer()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var invite = BotRelayFlowTestFixtures.BuildPublicInvite();
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            tickConfig.inviteTimeoutMin = 1f;
            tickConfig.inviteTimeoutMax = 1f;
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injects.AsParallelWriter();

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = invite
            });
            BotAgentLogic.Execute(0, ref farm, tickConfig);

            tickConfig.elapsedTime = 1f;
            BotAgentLogic.Execute(0, ref farm, tickConfig);

            double joinResponseDeadline = 1.0 + BotAgentLogic.SquadJoinResponseTimeoutSeconds;
            Assert.AreEqual(joinResponseDeadline, farm.agentStates[0].inviteJoinTime, 0.001d);
            Assert.AreNotEqual(0, farm.agentFlags[0] & BotAgentRuntimeFlags.JoinDispatched);
            int injectsAfterJoin = injects.Count;

            tickConfig.elapsedTime = 3.6f;
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = invite
            });
            tickConfig.frameSeed = 99;
            BotAgentLogic.Execute(0, ref farm, tickConfig);
            Assert.AreEqual(joinResponseDeadline, farm.agentStates[0].inviteJoinTime, 0.001d);
            Assert.AreEqual(injectsAfterJoin, injects.Count);
        }
        finally
        {
            injects.Dispose();
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
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
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
            Assert.IsFalse(farm.agentStates[0].HasPendingInvite);
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
            Assert.IsTrue(farm.agentStates[0].HasPendingInvite);
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
                ref var transport = ref farm.transport.ElementAt(i);
                transport.flags &= ~BotRelayTransportFlags.Connected;
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
            Assert.IsTrue(HasSquadSlotClaim(in farm, 0, invite.squadInviteID));
            Assert.IsFalse(HasAnySquadSlotClaim(in farm, 1));

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
            Assert.IsTrue(HasSquadSlotClaim(in farm, 0, invite.squadInviteID));

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
            Assert.IsTrue(HasSquadSlotClaim(
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
            Assert.IsFalse(HasAnySquadSlotClaim(in farm, 0));
            Assert.IsTrue(HasSquadSlotClaim(
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
            Assert.IsTrue(HasSquadSlotClaim(in farm, 0, invite.squadInviteID));
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
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            tickConfig.inviteTimeoutMin = 1f;
            tickConfig.inviteTimeoutMax = 1f;
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injects.AsParallelWriter();

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
            int afterFirstDispatch = injects.Count;
            Assert.Greater(afterFirstDispatch, 0);
            Assert.AreNotEqual(0, farm.agentFlags[0] & BotAgentRuntimeFlags.JoinDispatched);

            for (int i = 0; i < 30; ++i)
            {
                tickConfig.elapsedTime = 3.6f + i * 0.1f;
                BotAgentLogic.Execute(0, ref farm, tickConfig);
            }

            Assert.AreEqual(afterFirstDispatch, injects.Count);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PendingInvite_NoJoinResponse_TimesOutReleasesLeaseAndAcceptsLaterSameSquad()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            uint squadID = BotRelayFlowTestFixtures.SquadInviteId;
            double deadline = __DispatchInviteWithoutServerReply(ref farm, ref injects, squadID);

            var beforeDeadline = __CreateInjectTick(
                ref injects,
                deadline - 0.01);
            BotAgentLogic.Execute(0, ref farm, in beforeDeadline);
            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsTrue(HasSquadSlotClaim(in farm, 0, squadID));

            var deadlineTick = __CreateInjectTick(ref injects, deadline);
            BotAgentLogic.Execute(0, ref farm, in deadlineTick);

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].HasPendingInvite);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.IsFalse(HasAnySquadSlotClaim(in farm, 0));

            int[] expectedCleanup =
            {
                (int)NetworkRelayMessageType.Leave,
                (int)NetworkRelayMessageType.Mismatch,
                (int)NetworkRelayMessageType.Status
            };
            for (int i = 0; i < expectedCleanup.Length; ++i)
            {
                Assert.IsTrue(injects.TryDequeue(out var inject), $"missing cleanup inject {i}");
                Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type));
                Assert.AreEqual(expectedCleanup[i], type, $"cleanup inject {i}");
            }
            Assert.AreEqual(0, injects.Count, "Timeout cleanup must not retry the expired Join.");

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(squadID)
            });
            var laterInviteTick = __CreateInjectTick(ref injects, deadline + 0.1);
            BotAgentLogic.Execute(0, ref farm, in laterInviteTick);

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsTrue(farm.agentStates[0].HasPendingInvite);
            Assert.AreEqual(squadID, farm.agentStates[0].pendingInvite.squadInviteID);
            Assert.IsTrue(HasSquadSlotClaim(in farm, 0, squadID));
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void Idle_UnexpectedJoinAfterTimeout_IsRejectedByStateGate()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            uint squadID = BotRelayFlowTestFixtures.SquadInviteId;
            double deadline = __DispatchInviteWithoutServerReply(ref farm, ref injects, squadID);
            var deadlineTick = __CreateInjectTick(ref injects, deadline);
            BotAgentLogic.Execute(0, ref farm, in deadlineTick);
            while (injects.TryDequeue(out _))
            {
            }

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                channelMessageType = NetworkRelayMessageType.Join,
                squadJoin = new ClientMessageSquadJoinToRead { squadInviteID = squadID }
            });
            var lateJoinTick = __CreateInjectTick(ref injects, deadline + 0.1);
            BotAgentLogic.Execute(0, ref farm, in lateJoinTick);

            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].HasPendingInvite);
            Assert.IsFalse(HasAnySquadSlotClaim(in farm, 0));
            Assert.IsFalse(farm.sessions[0].IsInSquad);
            Assert.AreEqual(3, injects.Count, "Unexpected Idle Join must be flattened with Leave/Mismatch/Status.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PendingInvite_DifferentSquadJoin_DoesNotOverwriteCurrentInvite()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            uint oldSquadID = BotRelayFlowTestFixtures.SquadInviteId;
            uint newSquadID = oldSquadID + 1;
            double deadline = __DispatchInviteWithoutServerReply(ref farm, ref injects, oldSquadID);
            var deadlineTick = __CreateInjectTick(ref injects, deadline);
            BotAgentLogic.Execute(0, ref farm, in deadlineTick);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(newSquadID)
            });
            var newInviteTick = __CreateInjectTick(ref injects, deadline + 0.1);
            BotAgentLogic.Execute(0, ref farm, in newInviteTick);
            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                channelMessageType = NetworkRelayMessageType.Join,
                squadJoin = new ClientMessageSquadJoinToRead { squadInviteID = oldSquadID }
            });
            var lateOldJoinTick = __CreateInjectTick(ref injects, deadline + 0.2);
            BotAgentLogic.Execute(0, ref farm, in lateOldJoinTick);

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsTrue(farm.agentStates[0].HasPendingInvite);
            Assert.AreEqual(newSquadID, farm.agentStates[0].pendingInvite.squadInviteID);
            Assert.IsTrue(HasSquadSlotClaim(in farm, 0, newSquadID));
            Assert.IsFalse(farm.sessions[0].IsInSquad);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    private static double __DispatchInviteWithoutServerReply(
        ref BotRelayFarmNative farm,
        ref NativeQueue<BotRelayInject> injects,
        uint squadID)
    {
        BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
        {
            type = BotRelayEventType.SquadInvite,
            squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(squadID)
        });

        var inviteTick = __CreateInjectTick(ref injects, 0.0);
        BotAgentLogic.Execute(0, ref farm, in inviteTick);
        var dispatchTick = __CreateInjectTick(ref injects, 1.0);
        BotAgentLogic.Execute(0, ref farm, in dispatchTick);

        int joinCount = 0;
        while (injects.TryDequeue(out var inject))
        {
            if (BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type) &&
                type == (int)NetworkRelayMessageType.Join)
            {
                ++joinCount;
            }
        }

        Assert.AreEqual(1, joinCount, "The invitation must dispatch exactly one Join.");
        Assert.AreNotEqual(0, farm.agentFlags[0] & BotAgentRuntimeFlags.JoinDispatched);
        return farm.agentStates[0].inviteJoinTime;
    }

    private static BotRelayFarmTickConfig __CreateInjectTick(
        ref NativeQueue<BotRelayInject> injects,
        double elapsedTime)
    {
        var config = BotRelayFlowTestFixtures.CreateTickConfig(elapsedTime);
        config.injectEnabled = 1;
        config.injectWriter = injects.AsParallelWriter();
        return config;
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
    [Category("Regression")]
    public void InviteWhileInSquad_DifferentSquad_Ignored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            farm.sessions[0] = session;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(99u)
            });
            var config = BotRelayFlowTestFixtures.CreateTickConfig(1f);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
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
    public void InviteWhileInSquad_SameSquad_RebroadcastIgnored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            agentState.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite();
            farm.agentStates[0] = agentState;
            var session = farm.sessions[0];
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            farm.sessions[0] = session;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });
            var config = BotRelayFlowTestFixtures.CreateTickConfig(1f);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
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
    public void InviteWhileInLevel_AnyInvite_Ignored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InLevel;
            agentState.stateEnterTime = 0.0;
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

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite(99u)
            });
            var config = BotRelayFlowTestFixtures.CreateTickConfig(5f);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].HasPendingInvite);
            Assert.IsFalse(HasAnySquadSlotClaim(in farm, 0));
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
    public void InviteWhileLeaving_Ignored()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.Leaving;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadInvite,
                squadInvite = BotRelayFlowTestFixtures.BuildPublicInvite()
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));

            // Leaving completes in one tick (Leave+Status inject → Idle); invite must not re-arm PendingInvite.
            Assert.AreNotEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsFalse(farm.agentStates[0].HasPendingInvite);
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
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var config = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in config);
            Assert.AreEqual(BotState.Matching, farm.agentStates[0].state);
            Assert.IsTrue(injects.TryDequeue(out var matchInject));

            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in matchInject, out int type));
            Assert.AreEqual((int)NetworkRelayMessageType.Match, type);
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadMatchDistance(in matchInject.packet, out int level));
            Assert.AreEqual(0, level, "Zero-based rank index 0 must be sent unchanged.");
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_BootDeferredIdleMatch_ArmsAfterRelayReady()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            farm.nextIdleMatchTime[0] = double.PositiveInfinity;

            var config = BotRelayFlowTestFixtures.CreateTickConfig(10.0);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual(BotState.Matching, farm.agentStates[0].state);
            Assert.IsFalse(double.IsPositiveInfinity(farm.nextIdleMatchTime[0]));
            Assert.Greater(injects.Count, 0);
        }
        finally
        {
            injects.Dispose();
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchResponse_IdleBot_UsesAgentProfileMatchLevel()
    {
        const int profileLevel = 7;
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            ref var state = ref farm.agentStates.ElementAt(0);
            state.matchLevel = profileLevel;
            farm.nextIdleMatchTime[0] = 0;

            var config = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in config);

            Assert.AreEqual(BotState.Matching, state.state);
            Assert.AreEqual(profileLevel, state.matchLevel);
            Assert.IsTrue(injects.TryDequeue(out var matchInject));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadMatchDistance(
                in matchInject.packet,
                out int transmittedLevel));
            Assert.AreEqual(profileLevel, transmittedLevel);
        }
        finally
        {
            injects.Dispose();
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

            ref var session = ref farm.sessions.ElementAt(0);
            session.channel = (int)BotRelayFlowTestFixtures.SquadInviteId;
            session.remoteChannelStatus = 210;
            session.remoteOnline = true;

            ref var runtime = ref farm.replayRuntime.ElementAt(0);
            runtime.flags = BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing;

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
            Assert.IsTrue(farm.agentStates[0].HasPendingInvite);
            Assert.AreEqual(nextSquadInviteId, farm.agentStates[0].pendingInvite.squadInviteID);
            Assert.IsTrue(HasSquadSlotClaim(in farm, 0, nextSquadInviteId));
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
                BotRelayWireTestFixtures.BuildBotHeader());

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
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            farm.sessions[0] = session;
            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ApplyMatch,
                header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId }
            });

            var config = BotRelayFlowTestFixtures.CreateTickConfig(0f);
            config.injectEnabled = 1;
            config.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in config);
            Assert.IsTrue(injects.TryDequeue(out var applyMatch));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in applyMatch, out int type));
            Assert.AreEqual((int)ClientMessageType.ApplyMatch, type);
        }
        finally
        {
            injects.Dispose();
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
            Assert.IsFalse(farm.agentStates[0].HasPendingInvite);
            Assert.IsFalse(HasAnySquadSlotClaim(in farm, 0));
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
    public void PremadeSquad_JoinBeforeApplyMatch_KeepsStatusZero()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite();
            farm.agentStates[0] = agentState;
            agentState = farm.agentStates[0];
            agentState.pendingInviteHostUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            farm.agentStates[0] = agentState;
            farm.agentFlags[0] = BotAgentRuntimeFlags.JoinDispatched;

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
            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig();
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(0, farm.sessions[0].matchID);
            Assert.AreEqual(
                (int)BotRelayFlowTestFixtures.SquadInviteId,
                farm.sessions[0].channel);
            Assert.AreEqual(
                BotRelayFlowTestFixtures.RealPlayerUserId,
                farm.sessions[0].remoteUserID);
            Assert.AreEqual(0, farm.sessions[0].channelStatus,
                "Joining a ranked lobby must not make the bot look in-level.");
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(0, injects.Count);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ApplyMatch,
                header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId }
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
            Assert.AreEqual(0, farm.sessions[0].matchID);
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
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ApplyMatch,
                header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId }
            });

            var tickConfig = BotRelayFlowTestFixtures.CreateTickConfig();
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injects.AsParallelWriter();
            BotAgentLogic.Execute(0, ref farm, in tickConfig);

            Assert.IsTrue(injects.TryDequeue(out var inject));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in inject, out int type));
            Assert.AreEqual((int)ClientMessageType.ApplyMatch, type);
            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(0, farm.sessions[0].matchID);
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
            Assert.AreEqual(9, farm.sessions[0].matchID);

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.ApplyMatch,
                header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId }
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
            var recordedPacket = __BuildRecordedPlayerPropertyPacket(activeSkillCount: 24);

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
    [Category("Regression")]
    public void ReplayPayloadType_RequiresCurrentChannelHeaderAndBody_ForPacketAndBlob()
    {
        var valid = __BuildRecordedPlayerPropertyPacket();
        __AssertReplyMessageTypeForPacketAndBlob(in valid, true, ReplyMessageType.PlayerProperty);

        var wrongRelay = __BuildRecordedPlayerPropertyPacket(relayType: NetworkRelayType.All);
        __AssertReplyMessageTypeForPacketAndBlob(in wrongRelay, false, default);

        var headerOnly = __BuildRecordedHeaderOnlyPacket(
            ReplyMessageType.PlayerProperty,
            NetworkRelayType.Channel);
        __AssertReplyMessageTypeForPacketAndBlob(in headerOnly, false, default);

        var oversized = default(BotRelayPacket);
        oversized.length = BotRelayPacket.MaxPayloadSize + 1;
        Assert.IsFalse(BotReplayPayloadUtility.TryGetReplyMessageType(in oversized, out _));
    }

    [TestCase(ReplyMessageType.Chat)]
    [TestCase(ReplyMessageType.Camera)]
    [TestCase(ReplyMessageType.Move)]
    [TestCase(ReplyMessageType.Damage)]
    [TestCase(ReplyMessageType.SelectSkill)]
    [TestCase(ReplyMessageType.PlayerProperty)]
    [Category("Regression")]
    public void ReplayPayloadType_AcceptsExactCurrentBody_ForPacketAndBlob(ReplyMessageType messageType)
    {
        var packet = __BuildRecordedGameplayPacket(messageType);
        __AssertReplyMessageTypeForPacketAndBlob(in packet, true, messageType);
    }

    [TestCase(ReplyMessageType.Chat)]
    [TestCase(ReplyMessageType.Camera)]
    [TestCase(ReplyMessageType.Move)]
    [TestCase(ReplyMessageType.Damage)]
    [TestCase(ReplyMessageType.SelectSkill)]
    [TestCase(ReplyMessageType.PlayerProperty)]
    [Category("Regression")]
    public void ReplayPayloadType_RejectsLegacySenderPrefix_ForPacketAndBlob(ReplyMessageType messageType)
    {
        var packet = __BuildRecordedGameplayPacket(messageType, legacySenderPrefixed: true);
        __AssertReplyMessageTypeForPacketAndBlob(in packet, false, default);
    }

    [TestCase(ReplyMessageType.Chat)]
    [TestCase(ReplyMessageType.Camera)]
    [TestCase(ReplyMessageType.Move)]
    [TestCase(ReplyMessageType.Damage)]
    [TestCase(ReplyMessageType.SelectSkill)]
    [TestCase(ReplyMessageType.PlayerProperty)]
    [Category("Regression")]
    public void ReplayPayloadType_RejectsTrailingWholeByte_ForPacketAndBlob(ReplyMessageType messageType)
    {
        var packet = __BuildRecordedGameplayPacket(messageType, trailingByte: true);
        __AssertReplyMessageTypeForPacketAndBlob(in packet, false, default);
    }

    [TestCase(ReplyMessageType.Chat)]
    [TestCase(ReplyMessageType.Camera)]
    [TestCase(ReplyMessageType.Move)]
    [TestCase(ReplyMessageType.Damage)]
    [TestCase(ReplyMessageType.SelectSkill)]
    [TestCase(ReplyMessageType.PlayerProperty)]
    [Category("Regression")]
    public void ReplayPayloadType_RejectsNonChannelRelay_ForPacketAndBlob(ReplyMessageType messageType)
    {
        var packet = __BuildRecordedGameplayPacket(messageType, relayType: NetworkRelayType.All);
        __AssertReplyMessageTypeForPacketAndBlob(in packet, false, default);
    }

    [Test]
    [Category("Regression")]
    public void ReplayPayloadType_RejectsInviteEvenWhenItHasABody()
    {
        var packet = __BuildRecordedHeaderOnlyPacket(
            ReplyMessageType.Invite,
            NetworkRelayType.Channel,
            appendBodyByte: true);
        __AssertReplyMessageTypeForPacketAndBlob(in packet, false, default);
    }

    [Test]
    [Category("Regression")]
    public void PlayerPropertyPacket_RejectsWrongTypeRelayLegacySenderAndTrailingByte()
    {
        var current = __BuildRecordedPlayerPropertyPacket();
        Assert.IsTrue(BotRelayCodec.TryBuildPlayerPropertyPacket(in current, out var validated));
        Assert.AreEqual(current.length, validated.length);

        var wrongType = __BuildRecordedPlayerPropertyPacket(messageType: ReplyMessageType.Damage);
        Assert.IsFalse(BotRelayCodec.TryBuildPlayerPropertyPacket(in wrongType, out _));

        var wrongRelay = __BuildRecordedPlayerPropertyPacket(relayType: NetworkRelayType.All);
        Assert.IsFalse(BotRelayCodec.TryBuildPlayerPropertyPacket(in wrongRelay, out _));

        var legacySenderPrefixed = __BuildRecordedPlayerPropertyPacket(legacySenderPrefixed: true);
        Assert.IsFalse(BotRelayCodec.TryBuildPlayerPropertyPacket(in legacySenderPrefixed, out _));

        var trailingByte = __BuildRecordedPlayerPropertyPacket(trailingByte: true);
        Assert.IsFalse(BotRelayCodec.TryBuildPlayerPropertyPacket(in trailingByte, out _));
    }

    [Test]
    [Category("Regression")]
    public void ReplayPayloadBlob_InvalidFrameBounds_AreRejectedBeforePointerAccess()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<BotRecordingBlob>();
        var frames = builder.Allocate(ref root.frames, 1);
        builder.Allocate(ref root.payloadBytes, 2);
        frames[0] = new BotReplayFrameMeta
        {
            payloadOffset = 1,
            payloadLength = 2
        };

        var recording = builder.CreateBlobAssetReference<BotRecordingBlob>(Allocator.Persistent);
        builder.Dispose();
        try
        {
            Assert.IsFalse(BotReplayPayloadUtility.TryGetReplyMessageType(recording, 0, out _));
            Assert.IsFalse(BotReplayPayloadUtility.TryCopyFrameToPacket(recording, 0, out _));
        }
        finally
        {
            recording.Dispose();
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
    public void MatchResponse_BeforeTemporaryJoin_ReleasesFarmMatchingLease()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.Matching;
            farm.agentStates[0] = agentState;
            Assert.IsTrue(BotMatchGuard.TryClaimMatchingSlot(ref farm, 0));

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = 3, level = 5 }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(3, farm.sessions[0].matchID);
            Assert.AreEqual(
                BotState.Matching,
                farm.agentStates[0].state,
                "Match may precede the temporary squad Join.");
            Assert.AreEqual(
                BotMatchGuard.NoMatchingSlotOwner,
                farm.matchingSlotOwner[0],
                "Once MatchToRead commits this generation, another idle Bot may enter the pool.");
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
            farm.agentStates[0] = agentState;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID,
                "Remote Status is not a substitute for the local ApplyStart descriptor.");
            Assert.AreEqual(BotLevelLoginPhase.None, farm.agentStates[0].levelLoginPhase);
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
            var agentState = farm.agentStates[0];
            agentState.state = BotState.Matching;
            farm.agentStates[0] = agentState;

            var session = farm.sessions[0];
            session.matchID = 9;
            farm.sessions[0] = session;

            int channelFlag = (int)NetworkRelayChannelFlag.Online | (int)NetworkRelayChannelFlag.Creator;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundHostCreateEchoApp(1, channelFlag, out var createApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in createApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
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
                BotRelayWireTestFixtures.BuildBotHeader());
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
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
            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            farm.sessions[0] = session;

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
    public void RelayQueryMessage_RemoteSnapshotParsesStageFromChannelFlag()
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
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            farm.sessions[0] = session;

            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayQueryHeaderApp(
                squadChannel,
                channelFlag,
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var queryApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in queryApp,
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
    public void ShortRelayStatusApp_CurrentDirectValidation_AcceptsExactApp()
    {
        const int stageId = 213;
        int channelFlag = (stageId << (int)NetworkRelayChannelFlag.ShiftToStatus) |
                          (int)NetworkRelayChannelFlag.Online;
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayStatusApp(
            channelFlag,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            out var statusApp));

        Assert.LessOrEqual(statusApp.length, 24);
        Assert.IsTrue(BotRelayWireBytes.TryAcceptCurrentInboundApp(in statusApp, out var app));
        Assert.IsFalse(app.IsEmpty);
        Assert.IsTrue(BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type));
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
            var squadSession = session.farm.sessions[0];
            squadSession.channel = 1;
            squadSession.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            squadSession.remotePlayerCount = 1;
            session.farm.sessions[0] = squadSession;

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
            squadSession.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            squadSession.remotePlayerCount = 1;
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
    public void ServerToBot_ConnectShapedCreate_AfterMatch_ReachesInSquadAndPreservesMatch()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            BotRelayIntegrationTestFixtures.SeedWireLiveServerHelloFromCatalog(ref session);

            var agentState = session.farm.agentStates[0];
            agentState.state = BotState.Matching;
            session.farm.agentStates[0] = agentState;
            var matchedSession = session.farm.sessions[0];
            matchedSession.matchID = 7;
            session.farm.sessions[0] = matchedSession;

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
            Assert.AreEqual(7, session.farm.sessions[0].matchID);
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
            agentState.pendingInvite = BotRelayFlowTestFixtures.BuildPublicInvite(0);
            agentState.pendingInviteHostUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.farm.agentStates[0] = agentState;
            session.farm.agentFlags[0] = BotAgentRuntimeFlags.JoinDispatched;

            const uint squadChannel = 0;
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundSelfJoinEchoApp(
                (int)squadChannel,
                (int)ClientRemotePlayerFlag.Online,
                out var joinApp));
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref session.catalogBlob.Value,
                in joinApp,
                out var wire));

            BotRelayIntegrationTestFixtures.RouteServerToBot(BotRelayWireTestFixtures.ToBytes(in wire));
            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            BotAgentLogic.Execute(0, ref session.farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotState.InSquad, session.farm.agentStates[0].state);
            Assert.AreEqual((int)squadChannel, session.farm.sessions[0].channel);
            Assert.AreEqual(0, session.farm.sessions[0].channelStatus);
            Assert.AreEqual(BotRelayFlowTestFixtures.RealPlayerUserId, session.farm.sessions[0].remoteUserID);
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
            var session = farm.sessions[0];
            session.channel = 1;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            farm.sessions[0] = session;

            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayPlayerPropertyApp(
                BotRelayFlowTestFixtures.RealPlayerUserId,
                out var app));
            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out var evt));
            Assert.AreEqual(BotRelayEventType.RemotePlayerProperty, evt.type);
            Assert.AreEqual(BotRelayFlowTestFixtures.RealPlayerUserId, evt.header.userID);
            Assert.IsFalse(farm.sessions[0].remoteOnline, "Inbox parsing must not pre-commit live presence.");
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void MatchHost_WithoutMatchStart_DoesNotArmEntry()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var session = farm.sessions[0];
            session.channel = 1;
            session.matchID = 9;
            session.isHost = true;
            farm.sessions[0] = session;

            var agentState = farm.agentStates[0];
            agentState.state = BotState.InSquad;
            farm.agentStates[0] = agentState;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadHostWithoutCommittedMatch_DoesNotArmLevelEntry()
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
            farm.agentStates[0] = agentState;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
        }
        finally
        {
            farm.Dispose();
        }
    }

    /// <summary>
    /// Current Play is consumed from the measured RouteSend frame, but it is not a Bot event and
    /// cannot authorize level entry.
    /// </summary>
    [Test]
    [Category("Regression")]
    public void ServerToBot_CurrentFramedPlay_RouteSendConsumesWithoutBotEvent()
    {
        var session = BotRelayIntegrationTestFixtures.CreateConnectedSession();
        try
        {
            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundPlayApp(
                7,
                2,
                BotRelayWireTestFixtures.RealPlayerUserId,
                out var playApp));
            BotRelayIntegrationTestFixtures.RouteServerToBot(
                BotRelayIntegrationTestFixtures.BuildNullFramedRelayAppWireBytes(
                    ref session,
                    in playApp));

            BotRelayIntegrationTestFixtures.DrainInbound(ref session);
            Assert.Greater(session.farm.inboundCount[0], 0, "DrainInbound should extract Play app from pipeline wire.");

            BotRelayIntegrationTestFixtures.PumpAgentInbound(ref session);
            Assert.AreEqual(0, session.farm.eventCount[0]);
            Assert.IsFalse(BotRelaySlotInbox.TryDequeueEvent(0, ref session.farm, out _));
            Assert.AreEqual(0u, session.farm.levelStartMessages[0].userStageID);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SoloMatch_MatchThenTemporarySquadJoin_CommitsWithoutApplyMatch()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.Matching;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.Match,
                match = new ClientMessageMatchToRead { matchID = 7, level = 0 }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));
            Assert.AreEqual(BotState.Matching, farm.agentStates[0].state);
            Assert.AreEqual(7, farm.sessions[0].matchID);
            Assert.IsFalse(farm.sessions[0].IsInSquad);

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
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(7, farm.sessions[0].matchID);
            Assert.AreEqual(
                (int)BotRelayFlowTestFixtures.SquadInviteId,
                farm.sessions[0].channel);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void Matching_SquadJoinBeforeMatch_IsRejected()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.Matching;
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                channelMessageType = NetworkRelayMessageType.Join,
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
            Assert.AreEqual(BotState.Idle, farm.agentStates[0].state);
            Assert.AreEqual(0, farm.sessions[0].matchID);
            Assert.IsFalse(farm.sessions[0].IsInSquad);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PendingInvite_ExactJoin_CommitsAndKeepsMatchIdZero()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.pendingInvite = new ClientMessageSquadInviteToRead { squadInviteID = 99 };
            farm.agentStates[0] = agentState;
            farm.agentFlags[0] = BotAgentRuntimeFlags.JoinDispatched;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                channelMessageType = NetworkRelayMessageType.Join,
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
            Assert.AreEqual(0, farm.sessions[0].matchID);
            Assert.AreEqual(99, farm.sessions[0].channel);
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotLevelLoginPhase.None, farm.agentStates[0].levelLoginPhase);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void RelayQueryMessage_BodylessSelfSnapshot_DoesNotOverwriteRemotePresence()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            const int squadChannel = 0;
            var session = farm.sessions[0];
            session.channel = squadChannel;
            session.remoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            session.remotePlayerCount = 1;
            session.remoteChannelStatus = 213;
            session.remoteOnline = true;
            farm.sessions[0] = session;

            Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundRelayQueryApp(
                squadChannel,
                0,
                out var queryApp));
            BotRelaySlotInbox.ParseTransportPayload(
                in queryApp,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.AreEqual(213, farm.sessions[0].remoteChannelStatus);
            Assert.IsTrue(farm.sessions[0].remoteOnline);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void PendingInvite_RemoteCreateBeforeJoinDispatch_DoesNotCommitMembership()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.pendingInvite =
                new ClientMessageSquadInviteToRead { squadInviteID = 99 };
            farm.agentStates[0] = agentState;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                channelMessageType = NetworkRelayMessageType.Create,
                channelHasRemotePayload = true,
                channelHasRemoteHeader = true,
                channelRemoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId,
                channelRemoteHeader = new ClientHeader
                {
                    userID = BotRelayFlowTestFixtures.RealPlayerUserId
                },
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 99,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = ClientRemotePlayerFlag.Online |
                               ClientRemotePlayerFlag.Creator
                    }
                }
            });

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(0f));

            Assert.AreEqual(BotState.PendingInvite, farm.agentStates[0].state);
            Assert.IsTrue(farm.agentStates[0].HasPendingInvite);
            Assert.IsFalse(farm.sessions[0].IsInSquad);
            Assert.AreEqual(0u, farm.sessions[0].remoteUserID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    [Category("Regression")]
    public void SquadInviteJoin_DuplicateJoinBroadcast_KeepsMatchIdZero()
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            var agentState = farm.agentStates[0];
            agentState.state = BotState.PendingInvite;
            agentState.pendingInvite = new ClientMessageSquadInviteToRead { squadInviteID = 1 };
            agentState.pendingInviteHostUserID = BotRelayFlowTestFixtures.RealPlayerUserId;
            farm.agentStates[0] = agentState;
            farm.agentFlags[0] = BotAgentRuntimeFlags.JoinDispatched;

            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                channelMessageType = NetworkRelayMessageType.Join,
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
            Assert.AreEqual(0, farm.sessions[0].matchID);

            // Host re-broadcast Join every frame while waiting for stable presence.
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.SquadJoin,
                channelMessageType = NetworkRelayMessageType.Join,
                channelHasRemotePayload = true,
                channelHasRemoteHeader = true,
                channelRemoteUserID = BotRelayFlowTestFixtures.RealPlayerUserId,
                channelRemoteHeader = new ClientHeader
                {
                    userID = BotRelayFlowTestFixtures.RealPlayerUserId,
                    power = 123
                },
                squadJoin = new ClientMessageSquadJoinToRead
                {
                    squadInviteID = 1,
                    playerStatus = new ClientMessageRemotePlayerStatus
                    {
                        flag = (ClientRemotePlayerFlag)(
                            (int)ClientRemotePlayerFlag.Online |
                            (5 << (int)ClientRemotePlayerFlag.Shift))
                    }
                }
            });
            BotRelayFlowTestFixtures.EnqueueEvent(0, ref farm, new BotRelayEvent
            {
                type = BotRelayEventType.RemotePlayerProperty,
                header = new ClientHeader { userID = BotRelayFlowTestFixtures.RealPlayerUserId }
            });
            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(0, farm.sessions[0].matchID);
            Assert.AreEqual(BotRelayFlowTestFixtures.RealPlayerUserId, farm.sessions[0].remoteUserID);
            Assert.AreEqual(5, farm.sessions[0].remoteChannelStatus);
            Assert.IsTrue(farm.sessions[0].remoteOnline);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
        }
        finally
        {
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
            session.channel = 1;
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
                BotRelayWireTestFixtures.BuildBotHeader());

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
                type = BotRelayEventType.Mismatch,
                header = BotRelayWireTestFixtures.BuildBotHeader(),
                match = new ClientMessageMatchToRead { matchID = 7 }
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

    [TestCase(0u)]
    [TestCase(BotRelayFlowTestFixtures.RealPlayerUserId)]
    public void MatchResponse_ParseMismatch_EnqueuesMismatchEvent(uint sourceId)
    {
        const int mismatchId = 7;
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundMismatchApp(
            mismatchId,
            sourceId,
            out var app));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out var evt));
            Assert.AreEqual(BotRelayEventType.Mismatch, evt.type);
            Assert.AreEqual(mismatchId, evt.match.matchID);
            Assert.AreEqual(
                sourceId == 0 ? BotRelayFlowTestFixtures.BotUserId : sourceId,
                evt.header.userID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void SquadJoinResponse_ParseJoin_EnqueuesSnapshotWithoutPrecommittingSession()
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
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.IsTrue(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out var evt));
            Assert.AreEqual(BotRelayEventType.SquadJoin, evt.type);
            Assert.AreEqual(BotRelayFlowTestFixtures.SquadInviteId, evt.squadJoin.squadInviteID);
            Assert.AreEqual(NetworkRelayMessageType.Join, evt.channelMessageType);
            Assert.IsTrue(evt.channelHasRemotePayload);
            Assert.IsTrue(evt.channelHasRemoteHeader);
            Assert.AreEqual(BotRelayFlowTestFixtures.RealPlayerUserId, evt.channelRemoteUserID);
            Assert.AreEqual(BotRelayFlowTestFixtures.RealPlayerUserId, evt.channelRemoteHeader.userID);
            Assert.IsFalse(farm.sessions[0].IsInSquad);
            Assert.AreEqual(0u, farm.sessions[0].remoteUserID);
            Assert.AreEqual(0, farm.sessions[0].remotePlayerCount);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void ChannelResponse_RemoteIdWithoutCurrentHeader_IsRejected()
    {
        Assert.IsTrue(BotRelayWireTestFixtures.TryBuildInboundHostCreateWithRemoteApp(
            (int)BotRelayFlowTestFixtures.SquadInviteId,
            (int)ClientRemotePlayerFlag.Online,
            BotRelayFlowTestFixtures.RealPlayerUserId,
            out var app));

        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            BotRelaySlotInbox.ParseTransportPayload(
                in app,
                0,
                ref farm,
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.IsFalse(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out _));
            Assert.IsFalse(farm.sessions[0].IsInSquad);
            Assert.AreEqual(0u, farm.sessions[0].remoteUserID);
        }
        finally
        {
            farm.Dispose();
        }
    }

    [Test]
    public void PlayResponse_ParsePlay_ConsumesWithoutBotEvent()
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
                BotRelayWireTestFixtures.BuildBotHeader());

            Assert.IsFalse(BotRelaySlotInbox.TryDequeueEvent(0, ref farm, out _));
            Assert.AreEqual(0u, farm.levelStartMessages[0].userStageID);
            Assert.AreEqual(BotAgentRuntimeFlags.None, farm.agentFlags[0]);
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
            BotRelayReplayRuntimeGate.Reset();
            BotRelayReplayRuntimeGate.AddPendingLevelStart();
            farm.agentFlags[0] =
                BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart;
            farm.levelStartMessages[0] = new ClientMessageMatchStart
            {
                matchID = 9,
                userStageID = 210,
                levelID = 22,
                stage = 0,
                sceneName = new FixedString32Bytes("Scenes/Level4-1.scene")
            };

            var session = farm.sessions[0];
            session.matchID = 9;
            session.remoteChannelStatus = 0;
            farm.sessions[0] = session;

            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            state.levelLoginPhase = BotLevelLoginPhase.PlayerPropertySent;
            farm.agentStates[0] = state;

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingLevelStart(
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
            BotRelayReplayRuntimeGate.Reset();
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
            BotRelayReplayRuntimeGate.Reset();
            BotRelayReplayRuntimeGate.AddPendingLevelStart();
            farm.agentFlags[0] =
                BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart;
            farm.levelStartMessages[0] = new ClientMessageMatchStart
            {
                matchID = 0,
                userStageID = 213,
                levelID = 99,
                stage = 2,
                sceneName = new FixedString32Bytes("Scenes/Missing.scene")
            };
            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            state.levelLoginPhase = BotLevelLoginPhase.PlayerPropertySent;
            farm.agentStates[0] = state;

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var __injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingLevelStart(
                0,
                ref farm,
                in catalog,
                0.0,
                ref __injectWriter,
                1);

            Assert.AreEqual(BotState.Leaving, farm.agentStates[0].state,
                "A missing recording must enter the unified squad cleanup path instead of occupying the Bot.");
            Assert.AreEqual(0, injects.Count,
                "A missing recording must not inject Status without PlayerProperty.");
            Assert.AreEqual(0, farm.sessions[0].channelStatus,
                "Missing exact keys fail closed and leave the Bot in lobby Status 0.");
            Assert.AreEqual(
                BotReplayRuntimeState.InvalidCatalogIndex,
                farm.replayRuntime[0].catalogIndex,
                "A missing exact key must not start another folder's replay.");
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
            BotRelayReplayRuntimeGate.AddPendingLevelStart();
            farm.agentFlags[0] =
                BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart;
            farm.levelStartMessages[0] = new ClientMessageMatchStart
            {
                matchID = 0,
                userStageID = 10,
                levelID = 1,
                stage = 0,
                sceneName = new FixedString32Bytes("Scenes/Level1.scene")
            };
            var levelState = farm.agentStates[0];
            levelState.state = BotState.InSquad;
            levelState.levelLoginPhase = BotLevelLoginPhase.PlayerPropertySent;
            farm.agentStates[0] = levelState;

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
            Assert.IsFalse(BotRelayReplayRuntimeGate.HasPendingLevelStart);
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
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            BotRelayReplayRuntimeGate.Reset();
            BotRelayReplayRuntimeGate.AddPendingLevelStart();
            farm.agentFlags[0] =
                BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart;
            farm.levelStartMessages[0] = new ClientMessageMatchStart
            {
                matchID = 0,
                userStageID = 10,
                levelID = 1,
                stage = 0,
                sceneName = new FixedString32Bytes("Scenes/Level1.scene")
            };
            var levelState = farm.agentStates[0];
            levelState.state = BotState.InSquad;
            levelState.levelLoginPhase = BotLevelLoginPhase.PlayerPropertySent;
            farm.agentStates[0] = levelState;

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingLevelStart(
                0,
                ref farm,
                in catalog,
                0.0,
                ref injectWriter,
                1);
            Assert.AreEqual(BotState.InLevel, farm.agentStates[0].state);
            Assert.AreEqual(BotReplayRuntimeFlags.Loaded | BotReplayRuntimeFlags.Playing, farm.replayRuntime[0].flags);

            int injectsBefore = injects.Count;
            BotReplayLogic.Tick(0, ref farm, catalogEntries, 1.0, ref injectWriter, 1);
            Assert.Greater(injects.Count, injectsBefore);
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
    public void PendingLevelStart_FirstPass_SendsOnlyPlayerPropertyAndKeepsStatusZero()
    {
        var recording = BotRelayFlowTestFixtures.CreateSyntheticRecording(out _);
        var catalogEntries = BotRelayFlowTestFixtures.CreateSyntheticCatalog(recording, Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            BotRelayReplayRuntimeGate.Reset();
            BotRelayReplayRuntimeGate.AddPendingLevelStart();
            farm.agentFlags[0] = BotAgentRuntimeFlags.PendingLevelStart;
            farm.levelStartMessages[0] = new ClientMessageMatchStart
            {
                matchID = 0,
                userStageID = 10,
                levelID = 1,
                stage = 0,
                sceneName = new FixedString32Bytes("Scenes/Level1.scene")
            };
            var state = farm.agentStates[0];
            state.state = BotState.InSquad;
            state.levelLoginPhase = BotLevelLoginPhase.PendingPlayerProperty;
            farm.agentStates[0] = state;

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingLevelStart(
                0,
                ref farm,
                in catalog,
                0.0,
                ref injectWriter,
                1);

            Assert.AreEqual(BotState.InSquad, farm.agentStates[0].state);
            Assert.AreEqual(BotLevelLoginPhase.PlayerPropertySent, farm.agentStates[0].levelLoginPhase);
            Assert.AreEqual(0, farm.sessions[0].channelStatus);
            Assert.AreEqual(1, injects.Count);
            Assert.IsTrue(injects.TryDequeue(out var property));
            Assert.IsTrue(BotRelayFlowTestFixtures.TryReadInjectMessageType(in property, out int type));
            Assert.AreEqual((int)ReplyMessageType.PlayerProperty, type);
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
    public void InSquad_StatusFlapWhilePendingLevelStart_DoesNotLeave()
    {
        // While MatchStart's replay load is pending, remoteChannelStatus may flap non-zero → 0 as
        // the scene loads. The bot must remain in the squad until the two-tick handshake resolves.
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

            farm.agentFlags[0] =
                BotAgentRuntimeFlags.PendingLevelStart | BotAgentRuntimeFlags.LevelStartHandled;

            session.remoteChannelStatus = 0;
            session.remoteOnline = false;
            farm.sessions[0] = session;

            BotAgentLogic.Execute(0, ref farm, BotRelayFlowTestFixtures.CreateTickConfig(1f));
            Assert.AreEqual(
                BotState.InSquad,
                farm.agentStates[0].state,
                "A remote status flap during the MatchStart handshake must not evict the bot.");
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
        var injects = new NativeQueue<BotRelayInject>(Allocator.TempJob);
        try
        {
            BotRelayReplayRuntimeGate.Reset();
            BotRelayReplayRuntimeGate.AddPendingLevelStart();
            var staleState = farm.agentStates[0];
            staleState.state = BotState.InSquad;
            staleState.remotePresenceSeen = true;
            staleState.remoteAbsentSince = 50.0;
            staleState.levelLoginPhase = BotLevelLoginPhase.PlayerPropertySent;
            farm.agentStates[0] = staleState;
            farm.agentFlags[0] =
                BotAgentRuntimeFlags.LevelStartHandled | BotAgentRuntimeFlags.PendingLevelStart;
            farm.levelStartMessages[0] = new ClientMessageMatchStart
            {
                matchID = 0,
                userStageID = 10,
                levelID = 1,
                stage = 0,
                sceneName = new FixedString32Bytes("Scenes/Level1.scene")
            };

            var catalog = new BotReplayCatalog { entries = catalogEntries };
            var injectWriter = injects.AsParallelWriter();
            BotRelayReplayLoadOps.HandlePendingLevelStart(
                0,
                ref farm,
                in catalog,
                100.0,
                ref injectWriter,
                1);
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
            BotRelayReplayRuntimeGate.Reset();
            injects.Dispose();
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

    private static bool HasSquadSlotClaim(
        in BotRelayFarmNative farm,
        int index,
        uint squadInviteID) =>
        farm.squadSlotSquadKeys.IsCreated &&
        index >= 0 &&
        index < farm.squadSlotSquadKeys.Length &&
        farm.squadSlotSquadKeys[index] == (long)squadInviteID + 1L;

    private static bool HasAnySquadSlotClaim(in BotRelayFarmNative farm, int index) =>
        farm.squadSlotSquadKeys.IsCreated &&
        index >= 0 &&
        index < farm.squadSlotSquadKeys.Length &&
        farm.squadSlotSquadKeys[index] != 0;

    private static BotRelayPacket __BuildRecordedPlayerPropertyPacket(
        ReplyMessageType messageType = ReplyMessageType.PlayerProperty,
        NetworkRelayType relayType = NetworkRelayType.Channel,
        bool legacySenderPrefixed = false,
        bool trailingByte = false,
        int activeSkillCount = 0)
    {
        using var bytes = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        var model = StreamCompressionModel.Default;
        writer.WriteReplyHeader((int)messageType, relayType);
        if (legacySenderPrefixed)
        {
            writer.WritePackedUInt(BotRelayFlowTestFixtures.BotUserId, model);
            writer.Flush();
        }

        var property = new LevelPlayerProperty
        {
            effectTargetHP = 1,
            instanceName = new FixedString32Bytes("RecordedBot")
        };
        for (int i = 0; i < activeSkillCount; ++i)
        {
            property.activeSkills.Add(new LevelPlayerActiveSkill
            {
                name = new FixedString32Bytes($"RecordedSkillPayload_{i:D2}"),
                damageScale = i + 0.5f
            });
        }

        property.Write(ref writer, model);
        if (trailingByte)
        {
            writer.Flush();
            writer.WriteByte(0x7F);
        }

        Assert.IsFalse(writer.HasFailedWrites);
        var packet = default(BotRelayPacket);
        packet.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            packet.SetByte(i, bytes[i]);
        return packet;
    }

    private static BotRelayPacket __BuildRecordedGameplayPacket(
        ReplyMessageType messageType,
        NetworkRelayType relayType = NetworkRelayType.Channel,
        bool legacySenderPrefixed = false,
        bool trailingByte = false)
    {
        using var bytes = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        var model = StreamCompressionModel.Default;
        writer.WriteReplyHeader((int)messageType, relayType);
        if (legacySenderPrefixed)
        {
            writer.WritePackedUInt(BotRelayFlowTestFixtures.BotUserId, model);
            writer.Flush();
        }

        switch (messageType)
        {
            case ReplyMessageType.Chat:
                writer.WriteFixedString512(new FixedString512Bytes("ReplayChat"));
                break;
            case ReplyMessageType.Camera:
                new RemoteCameraForward
                {
                    value = new float2(0f, 1f)
                }.Write(ref writer, model);
                break;
            case ReplyMessageType.Move:
                writer.WritePackedInt(1, model);
                new RemotePosition
                {
                    type = RemotePosition.Type.Normal,
                    value = new float2(1f, 2f)
                }.Write(ref writer, model);
                break;
            case ReplyMessageType.Damage:
                new RemoteEffectTargetDamage
                {
                    hp = 17,
                    shield = 3,
                    layerMask = 1,
                    messageLayerMask = 2
                }.Write(ref writer, model);
                break;
            case ReplyMessageType.SelectSkill:
                writer.WritePackedInt(1, model);
                new LevelSkill
                {
                    index = 1,
                    originIndex = -1,
                    activeIndex = -1,
                    damageScale = 1f
                }.Write(ref writer, model);
                break;
            case ReplyMessageType.PlayerProperty:
                new LevelPlayerProperty
                {
                    effectTargetHP = 1,
                    instanceName = new FixedString32Bytes("RecordedBot")
                }.Write(ref writer, model);
                break;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(messageType), messageType, null);
        }

        if (trailingByte)
        {
            writer.Flush();
            writer.WriteByte(0x7F);
        }

        Assert.IsFalse(writer.HasFailedWrites);
        var packet = default(BotRelayPacket);
        packet.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            packet.SetByte(i, bytes[i]);
        return packet;
    }

    private static BotRelayPacket __BuildRecordedHeaderOnlyPacket(
        ReplyMessageType messageType,
        NetworkRelayType relayType,
        bool appendBodyByte = false)
    {
        using var bytes = new NativeArray<byte>(16, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        writer.WriteReplyHeader((int)messageType, relayType);
        if (appendBodyByte)
            writer.WriteByte(0x01);

        var packet = default(BotRelayPacket);
        packet.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            packet.SetByte(i, bytes[i]);
        return packet;
    }

    private static void __AssertReplyMessageTypeForPacketAndBlob(
        in BotRelayPacket packet,
        bool expectedResult,
        ReplyMessageType expectedType)
    {
        Assert.AreEqual(
            expectedResult,
            BotReplayPayloadUtility.TryGetReplyMessageType(in packet, out var packetType));
        if (expectedResult)
            Assert.AreEqual(expectedType, packetType);

        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<BotRecordingBlob>();
        var frames = builder.Allocate(ref root.frames, 1);
        var payloadBytes = builder.Allocate(ref root.payloadBytes, packet.length);
        frames[0] = new BotReplayFrameMeta
        {
            payloadOffset = 0,
            payloadLength = (ushort)packet.length
        };
        for (int i = 0; i < packet.length; ++i)
            payloadBytes[i] = packet.GetByte(i);

        var recording = builder.CreateBlobAssetReference<BotRecordingBlob>(Allocator.Persistent);
        builder.Dispose();
        try
        {
            Assert.AreEqual(
                expectedResult,
                BotReplayPayloadUtility.TryGetReplyMessageType(recording, 0, out var blobType));
            if (expectedResult)
                Assert.AreEqual(expectedType, blobType);
        }
        finally
        {
            recording.Dispose();
        }
    }
}
