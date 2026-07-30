using System;

using System.Collections;

using Unity.Collections;

using Unity.Entities;

using Unity.Networking.Transport;

using UnityEngine;

using UnityEngine.SceneManagement;

#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.SceneManagement;

#endif

using ZG;



/// <summary>

/// PlayMode harness: loads Server scene, waits for co-deploy bot farm, drives invite/match flows

/// through the same ECS tick path as manual Play (NetworkRelayServerSystem + BotRelay farm).

/// </summary>

internal static class BotRelayPlayModeHarness

{

    public const string ServerScenePath = "Assets/Scenes/Server.unity";

    public const float DefaultBootTimeoutSeconds = 45f;

    public const float DefaultInviteTimeoutMaxFallback = 10f;

    public static IEnumerator LoadServerSceneSingle()

    {

#if UNITY_EDITOR

        var parameters = new LoadSceneParameters(LoadSceneMode.Single);

        var op = EditorSceneManager.LoadSceneAsyncInPlayMode(ServerScenePath, parameters);

        if (op == null)

            throw new InvalidOperationException($"Failed to load PlayMode scene: {ServerScenePath}");



        while (!op.isDone)

            yield return null;

#else

        throw new InvalidOperationException("BotRelay PlayMode tests require the Unity Editor.");

#endif



        for (int i = 0; i < 10; ++i)

            yield return null;

    }



    public static IEnumerator WaitForBotFarmReady(float timeoutSeconds = DefaultBootTimeoutSeconds)

    {

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)

        {

            if (__TryIsFarmReadyNoSync(out _))
            {
                ResetAgentForTestIsolation();
                yield return WaitForInjectQueueDrained();
                yield return WaitForRouteSendSettled(stableSeconds: 0.5f, timeoutSeconds: 8f);
                yield break;
            }



            yield return null;

        }



        bool farmReady = __TryIsFarmReady(out var detail);
        throw new TimeoutException(

            $"Bot farm not ready within {timeoutSeconds:F0}s. " +

            $"serverReady={BotRelayServerBridge.IsServerReady()}, farmReady={farmReady}, detail={detail}");

    }

    /// <summary>Wait for farm boot without clearing the boot-time idle match (used by idle-match-only PlayMode tests).</summary>
    public static IEnumerator WaitForBotFarmReadyPreserveBootMatch(float timeoutSeconds = DefaultBootTimeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (__TryIsFarmReadyNoSync(out _))
                yield break;

            yield return null;
        }

        bool farmReady = __TryIsFarmReady(out var detail);
        throw new TimeoutException(
            $"Bot farm not ready within {timeoutSeconds:F0}s. " +
            $"serverReady={BotRelayServerBridge.IsServerReady()}, farmReady={farmReady}, detail={detail}");
    }



    public static void ResetDiagnostics()
    {
        __SyncEntityJobsOnly();
        if (BotRelayManager.Instance.IsCreated)
            BotRelayManager.Instance.ResetDiagnostics();

        BotRelayHostTestClient.ResetSessionState();
    }

    /// <summary>
    /// Farm boot often idle-matches before the test body runs; reset counters and agent so tests see a fresh Idle bot.
    /// Also cancels server-side match/squad state left over from boot idle-match (RouteSend spam blocks re-match).
    /// </summary>
    public static void ResetAgentForTestIsolation(int agentIndex = 0)
    {
        ResetDiagnostics();
        if (!__TryGetWorldEntity(out var entityManager, out var worldEntity))
            return;

        __SyncEntityJobsOnly();

        if (!entityManager.HasComponent<BotRelayFarmNative>(worldEntity))
            return;

        var farm = entityManager.GetComponentData<BotRelayFarmNative>(worldEntity);
        if (agentIndex < 0 || agentIndex >= farm.agentCount)
            return;

        ref var state = ref farm.agentStates.ElementAt(agentIndex);
        if (state.state == BotState.Matching)
            BotMatchGuard.ReleaseMatchingSlot(ref farm, agentIndex);

        state.state = BotState.Idle;
        state.pendingInvite = default;
        state.inviteJoinTime = 0;
        farm.sessions[agentIndex].ResetChannel();
        // Production uses +Infinity as a boot sentinel that __TickState deliberately converts
        // into "match now" once initial Status is ready. Test isolation needs the opposite:
        // keep this agent idle until the next scenario explicitly schedules it.
        farm.nextIdleMatchTime[agentIndex] = double.MaxValue;
        entityManager.SetComponentData(worldEntity, farm);

        // Test isolation must use the production app-layer injection boundary. The old helper
        // wrote UTP-shaped outbound slots and manually flushed them to Listen, which neither
        // exercised Handler.Read nor cleaned Server state after the migration.
        uint botUserId = state.userID;
        BotRelayPlayModeServerProbe.TryEnqueueInjectControl(
            botUserId, NetworkRelayMessageType.Leave, 0, hasArg: false);
        BotRelayPlayModeServerProbe.TryEnqueueInjectControl(
            botUserId, NetworkRelayMessageType.Mismatch, 0, hasArg: false);
        BotRelayPlayModeServerProbe.TryEnqueueInjectControl(
            botUserId, NetworkRelayMessageType.Status, 0, hasArg: true);

        farm = entityManager.GetComponentData<BotRelayFarmNative>(worldEntity);
        farm.inboundCount[agentIndex] = 0;
        farm.eventCount[agentIndex] = 0;
        farm.nextIdleMatchTime[agentIndex] = double.MaxValue;
        entityManager.SetComponentData(worldEntity, farm);
    }

    /// <summary>Let server RouteSend drain after cancel flush before asserting a fresh idle match.</summary>
    public static IEnumerator WaitForRouteSendSettled(float stableSeconds = 0.35f, float timeoutSeconds = 4f)
    {
        float stableSince = -1f;
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        int lastRoute = __PeekRouteSendEnqueuedCount();
        while (Time.realtimeSinceStartup < deadline)
        {
            int route = __PeekRouteSendEnqueuedCount();
            if (route == lastRoute)
            {
                if (stableSince < 0f)
                    stableSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - stableSince >= stableSeconds)
                    yield break;
            }
            else
            {
                lastRoute = route;
                stableSince = -1f;
            }

            yield return null;
        }
    }

    public static int GetRouteSendEnqueuedCount() => __PeekRouteSendEnqueuedCount();

    public static bool TryGetListenPort(out ushort listenPort)

    {

        listenPort = BotRelayManager.Instance.ListenPort;

        return listenPort != 0;

    }



    public static bool TryGetBotVirtualPort(int agentIndex, out ushort virtualPort)

    {

        virtualPort = 0;

        if (!TryReadFarmSync(agentIndex, out var farm, out _))

            return false;



        virtualPort = farm.transport[agentIndex].virtualPort;

        return virtualPort != 0;

    }



    public static bool TryGetAgentState(int agentIndex, out BotState state)

    {

        state = default;

        if (!TryReadFarmSync(agentIndex, out var farm, out _))

            return false;



        state = farm.agentStates[agentIndex].state;

        return true;

    }

    /// <summary>Returns the number of live farm agents without assuming a fixed BotConfig.</summary>
    public static int GetBotAgentCount()
    {
        if (!TryReadFarmSync(0, out var farm, out _))
            return 0;

        return farm.agentCount;
    }

    /// <summary>Reads the exclusive matching lease. A non-negative value identifies its sole owner.</summary>
    public static bool TryGetMatchingSlotOwner(out int owner)
    {
        owner = BotMatchGuard.NoMatchingSlotOwner;
        if (!TryReadFarmSync(0, out var farm, out _))
            return false;

        if (!farm.matchingSlotOwner.IsCreated || farm.matchingSlotOwner.Length == 0)
            return false;

        owner = farm.matchingSlotOwner[0];
        return true;
    }

    /// <summary>Poll agent state without blocking ReceiveJob (safe for wait loops).</summary>
    static bool __TryPeekAgentState(int agentIndex, out BotState state)
    {
        state = default;
        if (!TryReadFarmNoSync(agentIndex, out var farm, out _))
            return false;

        state = farm.agentStates[agentIndex].state;
        return true;
    }



    static string __DescribeAgentForDiagnostics(int agentIndex)
    {
        if (!TryReadFarmNoSync(agentIndex, out var farm, out _))
            return "farm unavailable";

        ref readonly var state = ref farm.agentStates.ElementAt(agentIndex);
        ref readonly var session = ref farm.sessions.ElementAt(agentIndex);
        ref readonly var transport = ref farm.transport.ElementAt(agentIndex);
        int matchingSlotOwner = farm.matchingSlotOwner.IsCreated
            ? farm.matchingSlotOwner[0]
            : BotMatchGuard.NoMatchingSlotOwner;

        string portMap = string.Empty;
        for (int i = 0; i < farm.agentCount; ++i)
        {
            if (i > 0)
                portMap += ",";
            portMap += i + ":" + farm.agentStates.ElementAt(i).userID + "@" + farm.transport.ElementAt(i).virtualPort;
        }

        return $"state={state.state}, user={state.userID}, vport={transport.virtualPort}, nextIdle={farm.nextIdleMatchTime[agentIndex]:F3}, " +
               $"connected={(transport.flags & BotRelayTransportFlags.Connected) != 0}, serverStatus={farm.relayServerStatusInjected[agentIndex]}, " +
               $"matchSlot={matchingSlotOwner}, channel={session.channel}, channelStatus={session.channelStatus}, " +
               $"remoteStatus={session.remoteChannelStatus}, inbox={farm.inboundCount[agentIndex]}, events={farm.eventCount[agentIndex]}, " +
               $"transport={transport.flags}; probe(lastRelay={BotRelayInboundProbe.LastRelayType}, inbound={BotRelayInboundProbe.InboundCount}, " +
               $"client={BotRelayInboundProbe.LastClientMsgType}, drops={BotRelayInboundProbe.InboundDrops}/{BotRelayInboundProbe.EventDrops}); " +
               $"ports=[{portMap}]; " +
               BotRelayManager.Instance.BuildDiagnosticsSnapshotNoSync();
    }
    public static bool TryGetPendingInviteId(int agentIndex, out uint squadInviteId)

    {

        squadInviteId = 0;

        if (!TryReadFarmSync(agentIndex, out var farm, out _))

            return false;



        ref readonly var agent = ref farm.agentStates.ElementAt(agentIndex);

        if (!agent.HasPendingInvite)

            return false;



        squadInviteId = agent.pendingInvite.squadInviteID;

        return squadInviteId != 0;

    }



    public static bool TryGetFarmConfig(out BotRelayFarmConfig config)

    {

        config = default;

        if (!__TryGetWorldEntity(out var entityManager, out var worldEntity))

            return false;



        if (!entityManager.HasComponent<BotRelayFarmConfig>(worldEntity))

            return false;



        config = entityManager.GetComponentData<BotRelayFarmConfig>(worldEntity);

        return true;

    }



    public static float GetInviteTimeoutMaxSeconds()

    {

        if (TryGetFarmConfig(out var config) && config.inviteTimeoutMax > 0f)

            return config.inviteTimeoutMax;



        return DefaultInviteTimeoutMaxFallback;

    }



    /// <summary>
    /// Returns whether the agent has queued its app-layer Join message. This is the production
    /// send boundary: it deliberately does not inspect the retired business-UTP wire counters.
    /// </summary>
    public static bool TryGetJoinDispatched(int agentIndex, out bool dispatched)
    {
        dispatched = false;
        if (!TryReadFarmSync(agentIndex, out var farm, out _))
            return false;

        dispatched = (farm.agentFlags[agentIndex] & BotAgentRuntimeFlags.JoinDispatched) != 0;
        return true;
    }

    /// <summary>Wait for test-isolation controls to cross the production inject boundary.</summary>
    public static IEnumerator WaitForInjectQueueDrained(float timeoutSeconds = 4f)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            int count = BotRelayPlayModeServerProbe.GetInjectCount();
            if (count == 0)
                yield break;

            yield return null;
        }
    }

    public static bool TryGetBotUserId(int agentIndex, out uint userId)
    {
        userId = 0;
        if (!TryReadFarmSync(agentIndex, out var farm, out _))
            return false;

        userId = farm.agentStates[agentIndex].userID;
        return userId != 0;
    }

    public static bool TryBotHasJoinFailEvent(int agentIndex)
    {
        if (!TryReadFarmSync(agentIndex, out var farm, out _))
            return false;

        int count = farm.eventCount[agentIndex];
        int baseIdx = agentIndex * BotRelayFarmNative.MaxEventsPerAgent;
        for (int i = 0; i < count; ++i)
        {
            if (farm.events[baseIdx + i].type == BotRelayEventType.SquadJoinFail)
                return true;
        }

        return false;
    }

    /// <summary>Reproduce manual Play boot: enable idle MatchToSend (spawn defers via nextIdleMatchTime=∞).</summary>
public static void ScheduleExclusiveIdleMatchNow(int agentIndex = 0)
    {
        if (!TryReadFarmSync(agentIndex, out var farm, out var worldEntity))
            throw new InvalidOperationException("Farm unavailable for ScheduleExclusiveIdleMatchNow.");

        if (!__TryGetWorldEntity(out var entityManager, out _))
            throw new InvalidOperationException("World entity unavailable.");

        if (agentIndex < 0 || agentIndex >= farm.agentCount)
            throw new ArgumentOutOfRangeException(nameof(agentIndex));

        // Farm boot can start any configured bot first. Quiet the non-target agents before
        // scheduling this deterministic reproduction, otherwise its result depends on entity
        // update order rather than the target agent's MatchToSend injection path.
        for (int i = 0; i < farm.agentCount; ++i)
        {
            if (i == agentIndex)
                continue;

            ref var otherState = ref farm.agentStates.ElementAt(i);
            if (otherState.state == BotState.Matching)
            {
                BotMatchGuard.ReleaseMatchingSlot(ref farm, i);
                otherState.state = BotState.Idle;
            }

            // +Infinity is the production boot sentinel and is deliberately re-armed by
            // BotAgentLogic once initial Status is ready. Use a finite far-future value so
            // non-target agents remain quiet for this deterministic test scenario.
            farm.nextIdleMatchTime[i] = double.MaxValue;
        }

        // Do not allow a boot-time match by the target itself to satisfy this test without
        // exercising the scheduled idle-match transition.
        ref var targetState = ref farm.agentStates.ElementAt(agentIndex);
        if (targetState.state == BotState.Matching)
        {
            BotMatchGuard.ReleaseMatchingSlot(ref farm, agentIndex);
            targetState.state = BotState.Idle;
        }

        farm.nextIdleMatchTime[agentIndex] = 0;
        entityManager.SetComponentData(worldEntity, farm);
    }

    /// <summary>真人房主：真实 ClientData 连 ReplyServer（见 BotRelayHostTestClient）。</summary>
    public static IEnumerator EstablishHostSquadChannelStub(float settleSeconds = 1.5f) =>
        BotRelayHostTestClient.EstablishAsSquadHost(settleSeconds);

    /// <summary>Same path as manual Play: server RouteSend → bot vport queue.</summary>

    public static void RouteCurrentInviteToBot(int agentIndex = 0)

    {

        if (!TryGetBotVirtualPort(agentIndex, out var botPort))

            throw new InvalidOperationException("Bot virtual port unavailable.");



        if (!TryGetListenPort(out var listenPort))

            listenPort = BotRelayIntegrationSession.ListenPort;



        if (!__TryGetCatalogBlob(out var blob))
            throw new InvalidOperationException("Bot relay catalog unavailable.");

        if (!BotRelayWireTestFixtures.TryBuildInviteApp(
                BotRelayWireTestFixtures.RealPlayerUserId,
                1,
                0,
                0,
                out var inviteApp))
        {
            throw new InvalidOperationException("Failed to build current Invite app.");
        }

        ref var catalog = ref blob.Value;
        if (!BotRelayWireTestFixtures.TryBuildConnectShapedInboundWire(
                ref catalog,
                in inviteApp,
                out var wire))
        {
            throw new InvalidOperationException("Failed to build current measured Invite frame.");
        }

        using var payload = new NativeArray<byte>(
            BotRelayWireTestFixtures.ToBytes(in wire),
            Allocator.Temp);

        __SyncEntityJobsOnly();
        BotRelayManager.Instance.EnqueueServerToBotBurst(botPort, listenPort, payload);

    }



    /// <summary>

    /// Server→bot self Join echo on the RouteSend path.

    /// PlayMode Server scene has no external host client; this mirrors the payload-free echo that
    /// ServerRelay emits after accepting this bot's Join.

    /// </summary>

    public static void RouteServerJoinConfirmationToBot(int agentIndex, uint squadInviteId = 1)
    {
        if (!BotRelayWireTestFixtures.TryBuildInboundSelfJoinEchoApp(
                (int)squadInviteId,
                (int)ClientRemotePlayerFlag.Online,
                out var joinApp))
            throw new InvalidOperationException("Failed to build inbound self Join echo app.");

        if (!__TryGetWorldEntity(out var entityManager, out var worldEntity))
            throw new InvalidOperationException("BotRelay world unavailable for Join confirm.");

        __SyncEntityJobsOnly();

        var farm = entityManager.GetComponentData<BotRelayFarmNative>(worldEntity);
        if (agentIndex < 0 || agentIndex >= farm.agentCount)
            throw new InvalidOperationException($"Invalid agent index {agentIndex}.");

        if (!BotRelaySlotOps.TryEnqueueInbound(
                agentIndex,
                farm.inbound,
                farm.inboundCount,
                in joinApp,
                ref BotRelayManager.Instance))
            throw new InvalidOperationException("Failed to enqueue Join confirm app to bot inbox.");

        entityManager.SetComponentData(worldEntity, farm);
    }

    public static IEnumerator WaitUntil(

        Func<bool> predicate,

        float timeoutSeconds,

        string failureMessage = null)

    {

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)

        {

            if (predicate())

                yield break;



            yield return null;

        }



        throw new TimeoutException(failureMessage ?? $"Condition not met within {timeoutSeconds:F1}s.");

    }

    public static IEnumerator WaitUntil(
        Func<bool> predicate,
        float timeoutSeconds,
        Func<string> failureMessageFactory)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (predicate())
                yield break;

            yield return null;
        }

        throw new TimeoutException(
            failureMessageFactory?.Invoke() ?? $"Condition not met within {timeoutSeconds:F1}s.");
    }



    public static IEnumerator WaitForAgentState(

        int agentIndex,

        BotState expected,

        float timeoutSeconds)

    {

        yield return WaitUntil(

            () => __TryPeekAgentState(agentIndex, out var state) && state == expected,

            timeoutSeconds,

            () => $"Agent {agentIndex} did not reach state {expected} within {timeoutSeconds:F1}s. {__DescribeAgentForDiagnostics(agentIndex)}");

    }



    /// <summary>Returns false on timeout instead of throwing.</summary>

    public static IEnumerator TryWaitForAgentState(

        int agentIndex,

        BotState expected,

        float timeoutSeconds,

        bool[] reached)

    {

        reached[0] = false;

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)

        {

            if (__TryPeekAgentState(agentIndex, out var state) && state == expected)

            {

                reached[0] = true;

                yield break;

            }



            yield return null;

        }

    }



    public static bool TryGetBotServerChannel(uint botUserId, out int channel)
    {
        return BotRelayPlayModeServerProbe.TryGetIdentity(botUserId, out _, out channel) &&
               channel != NetworkRelayServerIdentity.CHANNEL_NULL;
    }

    public static IEnumerator WaitForServerBotJoinedChannel(uint botUserId, float timeoutSeconds)
    {
        yield return WaitUntil(
            () => TryGetBotServerChannel(botUserId, out _),
            timeoutSeconds,
            () => $"Server bot {botUserId} did not join a squad channel within {timeoutSeconds:F1}s.");
    }

    public static IEnumerator WaitForServerJoinRelayLog(
        BotRelayPlayModeLogAsserts.ServerJoinRelayLogScope serverJoinLog,
        uint botUserId,
        float timeoutSeconds)
    {
        yield return WaitUntil(
            () => serverJoinLog.TryHasBotJoinProcessed(),
            timeoutSeconds,
            () =>
                $"Server did not log Join processing for bot {botUserId} within {timeoutSeconds:F1}s.");
    }

    public static IEnumerator WaitForServerJoinRelayReadType6(
        BotRelayPlayModeLogAsserts.ServerJoinRelayLogScope serverJoinLog,
        uint botUserId,
        float timeoutSeconds)
    {
        yield return WaitUntil(
            () => serverJoinLog.TryHasNetworkRelayServerReadJoinType6(),
            timeoutSeconds,
            () =>
                $"Server did not log NetworkRelayServerRead type=6 for bot {botUserId} within {timeoutSeconds:F1}s. Captured: {serverJoinLog.DescribeCaptured()}");
    }

    public static IEnumerator WaitForJoinDispatched(int agentIndex, float timeoutSeconds)
    {
        yield return WaitUntil(
            () => __TryPeekJoinDispatched(agentIndex),
            timeoutSeconds,
            "Bot did not queue the app-layer Join inject within invite delay window.");
    }

    public static bool TryGetCatalogBlob(out BlobAssetReference<BotRelayCatalogBlob> blob) =>
        __TryGetCatalogBlob(out blob);

    private static bool __TryGetCatalogBlob(out BlobAssetReference<BotRelayCatalogBlob> blob)

    {

        blob = default;

        if (!__TryGetWorldEntity(out var entityManager, out var worldEntity))

            return false;



        if (!entityManager.HasComponent<BotRelayWireCatalogBlobRef>(worldEntity))

            return false;



        blob = entityManager.GetComponentData<BotRelayWireCatalogBlobRef>(worldEntity).blob;

        return blob.IsCreated;

    }



    /// <summary>Finish ECS burst jobs only — never Complete ManagerAccessHandle (deadlocks ReceiveJob).</summary>
    static void __SyncEntityJobsOnly()
    {
        if (!__TryGetWorldEntity(out var entityManager, out _))
            return;

        entityManager.CompleteAllTrackedJobs();
    }

    static int __PeekRouteSendEnqueuedCount() =>
        BotRelayManager.Instance.IsCreated ? BotRelayManager.Instance.diagRouteSendEnqueued : 0;

    static bool __TryPeekJoinDispatched(int agentIndex)
    {
        if (!TryReadFarmNoSync(agentIndex, out var farm, out _))
            return false;

        return (farm.agentFlags[agentIndex] & BotAgentRuntimeFlags.JoinDispatched) != 0;
    }

    static bool TryReadFarmNoSync(int agentIndex, out BotRelayFarmNative farm, out Entity worldEntity)
    {
        farm = default;
        worldEntity = Entity.Null;

        if (!__TryGetWorldEntity(out var entityManager, out worldEntity))
            return false;

        if (!entityManager.HasComponent<BotRelayFarmNative>(worldEntity))
            return false;

        farm = entityManager.GetComponentData<BotRelayFarmNative>(worldEntity);
        return agentIndex >= 0 && agentIndex < farm.agentCount;
    }

    static bool TryReadFarmSync(int agentIndex, out BotRelayFarmNative farm, out Entity worldEntity)
    {
        farm = default;
        worldEntity = Entity.Null;

        if (!__TryGetWorldEntity(out var entityManager, out worldEntity))
            return false;

        __SyncEntityJobsOnly();

        if (!entityManager.HasComponent<BotRelayFarmNative>(worldEntity))
            return false;

        farm = entityManager.GetComponentData<BotRelayFarmNative>(worldEntity);
        return agentIndex >= 0 && agentIndex < farm.agentCount;
    }



    private static bool __TryIsFarmReady(out string detail) =>
        __TryIsFarmReadyNoSync(out detail);

    private static bool __TryIsFarmReadyNoSync(out string detail)
    {
        detail = string.Empty;

        if (!BotRelayServerBridge.IsServerReady())
        {
            detail = "server not ready";
            return false;
        }

        if (!__TryGetWorldEntity(out var entityManager, out var worldEntity))
        {
            detail = "no BotRelayWorldTag entity";
            return false;
        }

        if (!entityManager.HasComponent<BotRelayWireCatalogReadyTag>(worldEntity))
        {
            detail = "wire catalog not ready";
            return false;
        }

        if (!entityManager.HasComponent<BotRelayFarmNative>(worldEntity))
        {
            detail = "farm component missing";
            return false;
        }

        var farm = entityManager.GetComponentData<BotRelayFarmNative>(worldEntity);
        if (farm.agentCount <= 0)
        {
            detail = "agentCount=0";
            return false;
        }

                if (farm.transport[0].virtualPort == 0)
        {
            detail = "bot virtualPort=0";
            return false;
        }

        // A virtual port alone only proves that the Farm was spawned. Idle MatchToSend is
        // deliberately gated until this connection's live server hello marks the transport
        // Connected; otherwise a test can schedule matching before the server identity exists.
        if ((farm.transport[0].flags & BotRelayTransportFlags.Connected) == 0)
        {
            detail = "bot transport not connected";
            return false;
        }

        detail = $"agents={farm.agentCount}, vport={farm.transport[0].virtualPort}";
        return true;
    }



    private static bool __TryGetWorldEntity(out EntityManager entityManager, out Entity worldEntity)

    {

        entityManager = default;

        worldEntity = Entity.Null;



        var world = World.DefaultGameObjectInjectionWorld;

        if (world == null || !world.IsCreated)

            return false;



        entityManager = world.EntityManager;

        return BotRelayWorld.TryGetWorldEntity(entityManager, out worldEntity);

    }

}


