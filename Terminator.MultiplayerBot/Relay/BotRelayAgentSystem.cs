using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ZG
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
    [UpdateBefore(typeof(NetworkRelayServerSystem))]
    public partial struct BotRelayPreTickSystem : ISystem
    {
        private EntityQuery __wireGroup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var wireBuilder = new EntityQueryBuilder(Allocator.Temp))
                __wireGroup = wireBuilder
                    .WithAll<BotRelayWorldTag, BotRelayWireCatalogReadyTag, BotRelayFarmNative, BotRelayHubState>()
                    .Build(ref state);

            state.RequireForUpdate(__wireGroup);
            state.RequireForUpdate<BotRelayInjectQueueSingleton>();
            state.RequireForUpdate<NetworkRelayServerInjectSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<BotRelayWorldTag>(out var worldEntity))
                return;

            ref var farm = ref SystemAPI.GetComponentRW<BotRelayFarmNative>(worldEntity).ValueRW;
            if (farm.agentCount <= 0)
                return;

            var catalogBlob = SystemAPI.GetComponent<BotRelayWireCatalogBlobRef>(worldEntity).blob;
            if (!catalogBlob.IsCreated || !catalogBlob.Value.IsValid)
                return;

            var dep = JobHandle.CombineDependencies(state.Dependency, BotRelayManager.ManagerAccessHandle);
            var job = new BotRelayPreTickBurstJob
            {
                farm = farm,
                catalogBlob = catalogBlob
            }.Schedule(farm.agentCount, 1, dep);

            BotRelayManager.ManagerAccessHandle = job;
            state.Dependency = job;
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
    [UpdateAfter(typeof(NetworkRelayServerSystem))]
    public partial struct BotRelayPostTickSystem : ISystem
    {
        private EntityQuery __wireGroup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var wireBuilder = new EntityQueryBuilder(Allocator.Temp))
                __wireGroup = wireBuilder
                    .WithAll<BotRelayWorldTag, BotRelayWireCatalogReadyTag, BotRelayFarmNative, BotRelayFarmConfig>()
                    .Build(ref state);

            state.RequireForUpdate(__wireGroup);
            state.RequireForUpdate<BotRelayInjectQueueSingleton>();
            state.RequireForUpdate<NetworkRelayServerInjectSingleton>();
            state.RequireForUpdate<NetworkRelayServer>();
        }

        [BurstCompile]
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
                matchTimeoutMin = config.matchTimeoutMin,
                matchTimeoutMax = config.matchTimeoutMax,
                remoteOfflineLeaveTimeout = config.remoteOfflineLeaveTimeout,
                elapsedTime = SystemAPI.Time.ElapsedTime,
                frameSeed = (uint)state.GlobalSystemVersion,
                replayCatalogMissing = (byte)(!replayCatalog.IsCreated || replayCatalog.EntryCount == 0 ? 1 : 0)
            };

            // App-layer relay inject: enqueue into the game-owned producer queue. GetSingletonRW
            // registers a write dependency on BotRelayInjectQueueSingleton so BotRelayInjectFlattenSystem
            // (which drains it before NetworkRelayServerSystem) is ordered after this producer.
            var injectQueue = SystemAPI.GetSingletonRW<BotRelayInjectQueueSingleton>();
            tickConfig.injectEnabled = 1;
            tickConfig.injectWriter = injectQueue.ValueRW.queue.AsParallelWriter();

            var catalogBlob = SystemAPI.GetComponent<BotRelayWireCatalogBlobRef>(worldEntity).blob;
            if (!catalogBlob.IsCreated || !catalogBlob.Value.IsValid)
                return;

            // Copy of NetworkRelayServer: Native containers are handles. ReadOnly views are consumed
            // by MatchDispatchJob scheduled on state.Dependency (includes Server Scheduler) — no Complete.
            var serverReadOnly = SystemAPI.GetSingleton<NetworkRelayServer>().AsReadOnly();

            var dep = JobHandle.CombineDependencies(state.Dependency, BotRelayManager.ManagerAccessHandle);

            var drainJob = new BotRelayPostDrainBurstJob
            {
                farm = farm,
                catalogBlob = catalogBlob
            }.Schedule(farm.agentCount, 1, dep);

            var agentJob = new BotRelayPostAgentJob
            {
                farm = farm,
                tickConfig = tickConfig
            }.Schedule(farm.agentCount, 1, drainJob);

            var matchDispatchJob = new BotRelayMatchDispatchJob
            {
                farm = farm,
                tickConfig = tickConfig,
                server = serverReadOnly
            }.Schedule(agentJob);

            var flushJob = new BotRelayPostFlushBurstJob
            {
                farm = farm,
                catalogBlob = catalogBlob
            }.Schedule(farm.agentCount, 1, matchDispatchJob);

            BotRelayManager.ManagerAccessHandle = flushJob;
            state.Dependency = flushJob;
        }
    }
}
