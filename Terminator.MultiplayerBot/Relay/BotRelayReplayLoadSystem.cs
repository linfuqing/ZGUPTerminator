using Unity.Entities;
using Unity.Jobs;

namespace ZG
{
    [UpdateAfter(typeof(BotRelayPostTickSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
    public partial struct BotRelayReplayLoadSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BotRelayInjectQueueSingleton>();
            state.RequireForUpdate<NetworkRelayServerInjectSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!BotRelayReplayRuntimeGate.HasPendingPlay)
                return;

            if (!SystemAPI.TryGetSingletonEntity<BotRelayWorldTag>(out var worldEntity))
                return;

            if (!SystemAPI.HasComponent<BotReplayCatalog>(worldEntity))
                return;

            var catalog = SystemAPI.GetComponent<BotReplayCatalog>(worldEntity);
            if (!catalog.IsCreated || catalog.EntryCount == 0)
                return;

            ref var farm = ref SystemAPI.GetComponentRW<BotRelayFarmNative>(worldEntity).ValueRW;
            if (farm.agentCount <= 0)
                return;

            var loadJob = new BotRelayReplayLoadJob
            {
                farm = farm,
                catalog = catalog,
                elapsedTime = SystemAPI.Time.ElapsedTime,
                injectEnabled = 0
            };

            // App-layer inject: enqueue into the game-owned producer queue; BotRelayInjectFlattenSystem
            // drains it before next frame's NetworkRelayServerSystem. GetSingletonRW orders this
            // producer ahead of the flatten job via the queue singleton's write dependency.
            var injectQueue = SystemAPI.GetSingletonRW<BotRelayInjectQueueSingleton>();
            loadJob.injectEnabled = 1;
            loadJob.injectWriter = injectQueue.ValueRW.queue.AsParallelWriter();

            var scheduledLoad = loadJob.ScheduleAfter(state.Dependency);

            state.Dependency = scheduledLoad;
            BotRelayManager.ManagerAccessHandle =
                Unity.Jobs.JobHandle.CombineDependencies(BotRelayManager.ManagerAccessHandle, scheduledLoad);
        }
    }
}
