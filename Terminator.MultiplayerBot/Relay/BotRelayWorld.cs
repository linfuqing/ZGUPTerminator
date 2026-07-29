using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;
using ZG;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class BotRelayWorld
{
    private static bool s_shutdownDone;

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void __RegisterEditorPlayModeHooks()
    {
        EditorApplication.playModeStateChanged += __OnEditorPlayModeStateChanged;
    }

    private static void __OnEditorPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            s_shutdownDone = false;
            Shutdown();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
            s_shutdownDone = false;
    }
#endif

    public static bool TryInitialize(BotConfig config)
    {
        if (config == null || !config.enableCoDeploy)
            return false;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        var entityManager = world.EntityManager;
        if (TryGetWorldEntity(entityManager, out var existingWorldEntity))
        {
            if (entityManager.HasComponent<BotRelayFarmNative>(existingWorldEntity) &&
                entityManager.GetComponentData<BotRelayFarmNative>(existingWorldEntity).agentCount > 0)
            {
                Debug.LogWarning("[BotRelayWorld] Already initialized.");
                return true;
            }

            Debug.LogWarning("[BotRelayWorld] Stale relay world detected, rebuilding.");
            s_shutdownDone = false;
            Shutdown(entityManager);
        }

        s_shutdownDone = false;

        if (!BotRelayServerBridge.IsServerReady())
            return false;

        // NetworkRelayServer lives on NetworkRelayServerSystem entity (not in default singleton query).
        return __TryInitializeAfterServerReady(config, Entity.Null);
    }

    internal sealed class InitContext
    {
        public NetworkDriver listenDriver;
        public NetworkDriver clientDriver;
        public NetworkPipeline clientPipeline;
        public BotRelayWireCatalogState wireCatalog;
        public int driverIndex;
        
        /// <summary>Index 0 reuses the full-session catalog; other slots own Connect-only captures until blob publish.</summary>
        public BotRelayConnectWireState[] agentConnectWires;

        public void DisposeAgentConnectWires()
        {
            if (agentConnectWires == null)
                return;

            for (int i = 0; i < agentConnectWires.Length; ++i)
                agentConnectWires[i].Dispose();

            agentConnectWires = null;
        }
public ushort port;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool __TryInitializeAfterServerReady(
        BotConfig config,
        Entity serverEntity)
    {
        var ctx = new InitContext
        {
            port = config.botRelayPort != 0 ? config.botRelayPort : (ushort)1390
        };

        if (!__TryCaptureWireCatalog(config, ctx))
            return false;

        if (!__TryCreateListenDriver(config, ctx))
            return false;

        if (!__TryAttachListenDriver(ctx))
            return false;

        if (!BotRelayFarmSpawner.TrySpawn(config, serverEntity, ctx))
            return false;

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool __TryCreateListenDriver(BotConfig config, InitContext ctx)
    {
        var settings = CreateNetworkSettings();
        var listenInterface = new BotRelayNetworkInterface();
        ctx.listenDriver = NetworkDriver.Create(ref listenInterface, settings);

        // Match the relay server's MultiNetworkDriver pipeline count before AddDriver (bots still
        // send on the null pipeline via the per-userID pipelines map). See BotRelayPipeline.
        BotRelayPipeline.CreateServerMatchingPipeline(ref ctx.listenDriver);

        BotRelayManager.Instance.RegisterListenPort(ctx.port);
        if (ctx.listenDriver.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(ctx.port)) != 0 ||
            ctx.listenDriver.Listen() != 0)
        {
            ctx.listenDriver.Dispose();
            Debug.LogError($"[BotRelay] Failed to bind/listen bot relay driver on port {ctx.port}.");
            return false;
        }

        return true;
    }

[MethodImpl(MethodImplOptions.NoInlining)]
    private static bool __TryCaptureWireCatalog(BotConfig config, InitContext ctx)
    {
        var profiles = config.botProfiles;
        if (profiles == null || profiles.Length == 0)
        {
            Debug.LogError("[BotFarm] botProfiles is empty.");
            return false;
        }

        var settings = CreateNetworkSettings();
        var sampleHeader = config.ToClientHeader(profiles[0]);
        var matchLevel = BotConfig.ResolveMatchLevel(in profiles[0]);
        int agentCount = Mathf.Clamp(config.maxBots, config.minBots, profiles.Length);
        if (agentCount < 1)
            agentCount = 1;
        bool captured = false;

        try
        {
            BotRelayManager.ManagerAccessHandle.Complete();
            ctx.wireCatalog = BotRelayWireCatalog.Capture(ref settings, ctx.port, in sampleHeader, matchLevel);
            if (!ctx.wireCatalog.IsValid)
                return false;

            // A raw UTP Connect wire contains the ClientHeader. Capturing only profile[0] caused
            // every virtual connection to register as that bot on the real server. Capture the
            // handshake for each remaining bot now, then copy it into the immutable Burst catalog.
            ctx.agentConnectWires = new BotRelayConnectWireState[agentCount];
            for (int i = 1; i < agentCount; ++i)
            {
                var header = config.ToClientHeader(profiles[i % profiles.Length]);
                var connectSettings = CreateNetworkSettings();
                var connectWire = BotRelayConnectWire.CaptureConnectWire(ref connectSettings, ctx.port, in header);
                if (!connectWire.IsValid)
                {
                    connectWire.Dispose();
                    Debug.LogError($"[BotRelay] Failed to capture Connect wire for bot {header.userID}.");
                    return false;
                }

                ctx.agentConnectWires[i] = connectWire;
            }

            captured = true;
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[BotRelay] WireCatalog capture failed: {ex}");
            return false;
        }
        finally
        {
            if (!captured)
            {
                ctx.DisposeAgentConnectWires();
                ctx.wireCatalog.Dispose();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool __TryAttachListenDriver(InitContext ctx)
    {
        BotRelayManager.Instance.ClearListenQueue();

        ref var server = ref NetworkRelayServerManager.server;
        ctx.driverIndex = server.AddDriver(ref ctx.listenDriver);
        if (ctx.driverIndex < 0)
        {
            ctx.DisposeAgentConnectWires();
            ctx.wireCatalog.Dispose();
            if (ctx.listenDriver.IsCreated)
                ctx.listenDriver.Dispose();
            Debug.LogError("[BotRelay] AddDriver failed.");
            return false;
        }

        return true;
    }

    public static void Shutdown()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        Shutdown(world.EntityManager);
    }

    public static void Shutdown(EntityManager entityManager)
    {
        if (s_shutdownDone)
            return;

        s_shutdownDone = true;

        var world = entityManager.World;
        if (world != null && world.IsCreated)
            __CompleteBotRelaySystems(world);

        BotRelayManager.CompleteReplayTick();
        BotRelayManager.ManagerAccessHandle.Complete();
        entityManager.CompleteAllTrackedJobs();

        NetworkDriver clientDriver = default;
        Entity serverEntity = Entity.Null;

        if (TryGetWorldEntity(entityManager, out var worldEntity))
        {
            if (entityManager.HasComponent<BotRelayHubState>(worldEntity))
                serverEntity = entityManager.GetComponentData<BotRelayHubState>(worldEntity).serverEntity;

            if (entityManager.HasComponent<BotRelayClientResources>(worldEntity))
                clientDriver = entityManager.GetComponentData<BotRelayClientResources>(worldEntity).driver;

            if (entityManager.HasComponent<BotRelayWireCatalogState>(worldEntity))
            {
                var catalog = entityManager.GetComponentData<BotRelayWireCatalogState>(worldEntity);
                catalog.Dispose();
            }

            BotRelayWireCatalogHost.Dispose();

            if (entityManager.HasComponent<BotReplayCatalog>(worldEntity))
            {
                var catalog = entityManager.GetComponentData<BotReplayCatalog>(worldEntity);
                catalog.Dispose();
            }

            if (entityManager.HasComponent<BotRelayFarmNative>(worldEntity))
            {
                var farm = entityManager.GetComponentData<BotRelayFarmNative>(worldEntity);
                if (clientDriver.IsCreated)
                {
                    for (int i = 0; i < farm.agentCount; ++i)
                        BotRelaySlotOps.TryDisconnect(ref farm, i, ref clientDriver);
                }
                else
                {
                    for (int i = 0; i < farm.agentCount; ++i)
                        BotRelaySlotOps.TryDisconnect(ref farm, i);
                }

                farm.Dispose();
            }

            entityManager.DestroyEntity(worldEntity);
        }

        if (clientDriver.IsCreated)
            clientDriver.Dispose();

        BotRelayManager.Instance.Reset();
    }

    private static void __CompleteBotRelaySystems(World world)
    {
        __CompleteUnmanagedSystem<BotRelayReplayTickSystem>(world);
        __CompleteUnmanagedSystem<BotRelayPreTickSystem>(world);
        __CompleteUnmanagedSystem<BotRelayPostTickSystem>(world);
    }

    private static void __CompleteUnmanagedSystem<T>(World world) where T : unmanaged, ISystem
    {
        var handle = world.Unmanaged.GetExistingUnmanagedSystem<T>();
        if (!world.Unmanaged.IsSystemValid(handle))
            return;

        ref var state = ref world.Unmanaged.ResolveSystemStateRef(handle);
        state.Dependency.Complete();
    }

    public static bool TryGetWorldEntity(EntityManager entityManager, out Entity worldEntity)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<BotRelayWorldTag>());
        if (query.IsEmpty)
        {
            worldEntity = Entity.Null;
            return false;
        }

        worldEntity = query.GetSingletonEntity();
        return true;
    }

    internal static NetworkSettings CreateNetworkSettings()
    {
        var settings = new NetworkSettings(Allocator.Temp);
        settings.WithNetworkConfigParameters(
            1000,
            60,
            0,
            0,
            2000,
            Mathf.CeilToInt(Time.maximumDeltaTime * 1000),
            0,
            4096,
            4096);
        return settings;
    }
}
