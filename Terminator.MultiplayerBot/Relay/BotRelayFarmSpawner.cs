using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

internal static class BotRelayFarmSpawner
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool TrySpawn(
        BotConfig config,
        Entity serverEntity,
        BotRelayWorld.InitContext ctx)
    {
        return __TrySpawnCore(config, serverEntity, ctx);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool __TrySpawnCore(
        BotConfig config,
        Entity serverEntity,
        BotRelayWorld.InitContext ctx)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        var entityManager = world.EntityManager;
        var profiles = config.botProfiles;
        int count = Mathf.Clamp(config.maxBots, config.minBots, profiles.Length);
        if (count < 1)
            count = 1;

        var worldEntity = __CreateWorldEntity(entityManager);
        __WriteHubComponents(entityManager, worldEntity, serverEntity, ctx);
        __WriteFarmComponents(entityManager, worldEntity, config, ctx, count);

        Debug.Log($"[BotRelay] Bot relay driver added on port {ctx.port}, index={ctx.driverIndex}.");
        Debug.Log($"[BotFarm] Initialized {count} bot agent(s).");
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Entity __CreateWorldEntity(EntityManager entityManager)
    {
        var componentTypes = new System.Collections.Generic.List<ComponentType>
        {
            ComponentType.ReadWrite<BotRelayWorldTag>(),
            ComponentType.ReadWrite<BotRelayHubState>(),
            ComponentType.ReadWrite<BotRelayWireCatalogReadyTag>(),
            ComponentType.ReadWrite<BotRelayWireCatalogBlobRef>(),
            ComponentType.ReadWrite<BotRelayFarmConfig>(),
            ComponentType.ReadWrite<BotRelayFarmNative>(),
            ComponentType.ReadWrite<BotReplayCatalog>()
        };

        return entityManager.CreateEntity(componentTypes.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void __WriteHubComponents(
        EntityManager entityManager,
        Entity worldEntity,
        Entity serverEntity,
        BotRelayWorld.InitContext ctx)
    {
        entityManager.SetComponentData(worldEntity, new BotRelayHubState
        {
            serverEntity = serverEntity
        });

        BotRelayWireCatalogHost.Publish(ref ctx.wireCatalog, ctx.agentConnectWires);
        ctx.DisposeAgentConnectWires();

        BotRelayManager.Instance.SetPeelCatalogBlob(BotRelayWireCatalogHost.CatalogBlob);

        entityManager.SetComponentData(worldEntity, new BotRelayWireCatalogBlobRef
        {
            blob = BotRelayWireCatalogHost.CatalogBlob
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void __WriteFarmComponents(
        EntityManager entityManager,
        Entity worldEntity,
        BotConfig config,
        BotRelayWorld.InitContext ctx,
        int count)
    {
        var profiles = config.botProfiles;
        entityManager.SetComponentData(worldEntity, new BotRelayFarmConfig
        {
            inviteTimeoutMin = config.inviteTimeoutMin,
            inviteTimeoutMax = config.inviteTimeoutMax,
            matchTimeoutMin = config.matchTimeoutMin,
            matchTimeoutMax = config.matchTimeoutMax,
            remoteOfflineLeaveTimeout = config.remoteOfflineLeaveTimeout
        });

        var farm = BotRelayFarmNative.Create(count, Allocator.Persistent);
        for (int i = 0; i < count; ++i)
        {
            var profile = profiles[i % profiles.Length];
            var header = config.ToClientHeader(profile);
            BotRelaySlotOps.PrepareAgent(
                ref farm,
                i,
                in header,
                BotConfig.ResolveMatchLevel(in profile));
            Debug.Log(
                $"[BotAgent:{profile.userID}] Created (slot {i}, matchLevel={BotConfig.ResolveMatchLevel(in profile)}).");
        }

        BotRelaySlotOps.AssignVirtualPortsAtSpawn(ref farm, ref BotRelayManager.Instance, 0xB07F4E00u ^ (uint)count);

        __RouteBotsThroughNullPipeline(entityManager, in farm, count);

        entityManager.SetComponentData(worldEntity, farm);

        var catalog = BotReplayCatalogBuilder.Build(Allocator.Persistent);
        entityManager.SetComponentData(worldEntity, catalog);
        if (!catalog.IsCreated || catalog.EntryCount == 0)
        {
            Debug.LogError(
                "[BotReplayCatalog] No valid recording is deployed. Bot matching and squad-invite acceptance are disabled; " +
                "copy validated .bin files into Assets/StreamingAssets/Recordings before running the server.");
        }
    }

    /// <summary>
    /// Routes every bot connection through the null pipeline (see <see cref="BotRelayPipeline"/>)
    /// by keying the relay server's per-connection pipeline map on each bot's userID. Human
    /// clients are absent from this map and keep the default reliable+fragmentation pipeline.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void __RouteBotsThroughNullPipeline(EntityManager entityManager, in BotRelayFarmNative farm, int count)
    {
        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<ZG.NetworkRelayServerInjectSingleton>());
        if (query.IsEmpty)
        {
            Debug.LogError(
                "[BotRelay] NetworkRelayServerInjectSingleton missing at spawn; bots cannot be routed to the null pipeline.");
            return;
        }

        var inject = query.GetSingleton<ZG.NetworkRelayServerInjectSingleton>();
        if (!inject.pipelines.IsCreated)
            return;

        for (int i = 0; i < count; ++i)
        {
            uint userID = farm.agentStates[i].userID;
            if (userID == 0)
                continue;

            inject.pipelines[userID] = BotRelayPipeline.Null;
        }
    }
}
