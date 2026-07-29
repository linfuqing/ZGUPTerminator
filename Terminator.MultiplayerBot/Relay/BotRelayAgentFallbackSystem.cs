#if BOT_RELAY_WIRE_FALLBACK
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ZG
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
    [UpdateBefore(typeof(NetworkRelayServerSystem))]
    public partial struct BotRelayPreTickFallbackSystem : ISystem
    {
        private EntityQuery __fallbackGroup;

        public void OnCreate(ref SystemState state)
        {
            using (var fallbackBuilder = new EntityQueryBuilder(Allocator.Temp))
                __fallbackGroup = fallbackBuilder
                    .WithAll<BotRelayWorldTag, BotRelayClientResources, BotRelayFarmNative>()
                    .Build(ref state);

            state.RequireForUpdate(__fallbackGroup);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<BotRelayWorldTag>(out var worldEntity))
                return;

            ref var farm = ref SystemAPI.GetComponentRW<BotRelayFarmNative>(worldEntity).ValueRW;
            if (farm.agentCount <= 0)
                return;

            ref var resources = ref SystemAPI.GetComponentRW<BotRelayClientResources>(worldEntity).ValueRW;
            if (!resources.driver.IsCreated)
                return;

            ref var driver = ref resources.driver;
            var pipeline = resources.pipeline;

            resources.driverDependency.Complete();

            var update = driver.ScheduleUpdate(default);
            update.Complete();

            for (int i = 0; i < farm.agentCount; ++i)
                BotRelaySlotOps.ConnectBurst(i, ref farm.transport, ref driver, farm.wireScratch);

            for (int i = 0; i < farm.agentCount; ++i)
            {
                BotRelaySlotOps.PreFlushMain(
                    i,
                    ref farm.transport,
                    ref driver,
                    pipeline,
                    farm.outbound,
                    farm.outboundCount,
                    farm.wireScratch);
            }

            var flush = driver.ScheduleFlushSend(default);
            flush.Complete();
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
    [UpdateAfter(typeof(NetworkRelayServerSystem))]
    public partial struct BotRelayPostTickFallbackSystem : ISystem
    {
        private EntityQuery __fallbackGroup;

        public void OnCreate(ref SystemState state)
        {
            using (var fallbackBuilder = new EntityQueryBuilder(Allocator.Temp))
                __fallbackGroup = fallbackBuilder
                    .WithAll<BotRelayWorldTag, BotRelayClientResources, BotRelayFarmNative, BotRelayFarmConfig>()
                    .Build(ref state);

            state.RequireForUpdate(__fallbackGroup);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<BotRelayWorldTag>(out var worldEntity))
                return;

            ref var farm = ref SystemAPI.GetComponentRW<BotRelayFarmNative>(worldEntity).ValueRW;
            if (farm.agentCount <= 0)
                return;

            var config = SystemAPI.GetComponent<BotRelayFarmConfig>(worldEntity);
            var replayCatalog = SystemAPI.GetComponent<BotReplayCatalog>(worldEntity);
            var tickConfig = new BotRelayFarmTickConfig
            {
                inviteTimeoutMin = config.inviteTimeoutMin,
                inviteTimeoutMax = config.inviteTimeoutMax,
                remoteOfflineLeaveTimeout = config.remoteOfflineLeaveTimeout,
                elapsedTime = SystemAPI.Time.ElapsedTime,
                frameSeed = (uint)state.GlobalSystemVersion,
                replayCatalogMissing = (byte)(!replayCatalog.IsCreated || replayCatalog.EntryCount == 0 ? 1 : 0)
            };

            ref var resources = ref SystemAPI.GetComponentRW<BotRelayClientResources>(worldEntity).ValueRW;
            if (!resources.driver.IsCreated)
                return;

            ref var driver = ref resources.driver;

            resources.driverDependency.Complete();

            var update = driver.ScheduleUpdate(default);
            update.Complete();

            for (int i = 0; i < farm.agentCount; ++i)
            {
                BotRelaySlotOps.PostReceiveMain(
                    i,
                    ref farm.transport,
                    ref driver,
                    farm.inbound,
                    farm.inboundCount,
                    farm.outbound,
                    farm.outboundCount,
                    farm.inboxSentInitialStatus,
                    farm.wireScratch);

                BotRelaySlotOps.PumpInboundBurst(i, ref farm);
                BotAgentLogic.Execute(i, ref farm, in tickConfig);
            }

            var flush = driver.ScheduleFlushSend(default);
            flush.Complete();
        }
    }
}
#endif
