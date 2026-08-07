using Unity.Entities;
using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class BotCoDeployBootstrap : MonoBehaviour
{
    [SerializeField] private BotConfig config;

    private bool __started;
    private bool __loggedMissingConfig;

    private void Start()
    {
        __EnsureConfig();
        if (!__IsCoDeployEnabled())
            return;

        TryInitialize();
    }

private void Update()
    {
        if (__HasLiveFarm())
        {
            __started = true;
            __initFailed = false;
            return;
        }

        // With Enter Play Mode Options disabling scene/domain reload, this MonoBehaviour can
        // outlive BotRelayWorld. Clear both success and sticky failure flags whenever the live
        // farm is gone so a prior init/PostTick fault cannot block the next PlayMode session.
        __started = false;
        __initFailed = false;

        if (!__EnsureConfig() || !__IsCoDeployEnabled())
            return;

        TryInitialize();
    }

private static bool __HasLiveFarm()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        var entityManager = world.EntityManager;
        if (!BotRelayWorld.TryGetWorldEntity(entityManager, out var worldEntity) ||
            !entityManager.HasComponent<BotRelayFarmNative>(worldEntity))
            return false;

        return entityManager.GetComponentData<BotRelayFarmNative>(worldEntity).agentCount > 0;
    }


    private bool __EnsureConfig()
    {
        if (config != null)
            return true;

        config = Resources.Load<BotConfig>("Bot/BotConfig");
        if (config != null)
            return true;

        if (!__loggedMissingConfig)
        {
            __loggedMissingConfig = true;
            Debug.LogError(
                "[BotCoDeploy] BotConfig missing. Assign Assets/Resources/Bot/BotConfig.asset on BotCoDeployBootstrap " +
                "or run Tools/MultiplayerBot/Ensure Bot Config Asset.");
        }

        return false;
    }

    private bool __IsCoDeployEnabled() => config != null && config.enableCoDeploy;

    private void OnApplicationQuit() => TryShutdown();

    private void OnDestroy() => TryShutdown();

    private void TryShutdown()
    {
        if (!__started)
            return;

        __started = false;
        BotRelayWorld.Shutdown();
    }

    private bool __initFailed;

    private void TryInitialize()
    {
        if (__started || __initFailed)
            return;

        try
        {
            if (!BotRelayWorld.TryInitialize(config))
            {
                if (__ShouldRetryInit())
                    return;

                __initFailed = true;
                Debug.LogError("[BotCoDeploy] Bot farm init failed.");
                return;
            }
        }
        catch (System.Exception ex)
        {
            __initFailed = true;
            Debug.LogError($"[BotCoDeploy] Bot farm init failed: {ex}");
            return;
        }

        __started = true;
        Debug.Log("[BotCoDeploy] Bot farm started.");
    }

    private static bool __ShouldRetryInit()
    {
        if (!BotRelayServerBridge.IsServerReady())
            return true;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return true;

        return false;
    }
}
