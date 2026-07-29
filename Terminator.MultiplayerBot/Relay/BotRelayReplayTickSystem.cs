using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ZG
{
    [BurstCompile]
    [UpdateAfter(typeof(BotRelayReplayLoadSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
    public partial struct BotRelayReplayTickSystem : ISystem
    {
        private EntityQuery __group;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<BotRelayWorldTag, BotRelayFarmNative, BotReplayCatalog>()
                    .Build(ref state);

            state.RequireForUpdate(__group);
            state.RequireForUpdate<BotRelayInjectQueueSingleton>();
            state.RequireForUpdate<NetworkRelayServerInjectSingleton>();
        }

        public void OnDestroy(ref SystemState state)
        {
            state.Dependency.Complete();
            BotRelayManager.CompleteReplayTick();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<BotRelayWorldTag>(out var worldEntity))
                return;

            var catalog = SystemAPI.GetComponent<BotReplayCatalog>(worldEntity);
            if (!catalog.IsCreated || catalog.EntryCount == 0)
                return;

            if (!BotRelayReplayRuntimeGate.HasInLevel)
                return;

            ref var farm = ref SystemAPI.GetComponentRW<BotRelayFarmNative>(worldEntity).ValueRW;
            if (farm.agentCount <= 0)
                return;

            var tickJob = new ReplayTickJob
            {
                farm = farm,
                catalogEntries = catalog.entries,
                deltaTime = SystemAPI.Time.DeltaTime,
                injectEnabled = 0
            };

            // App-layer inject: enqueue into the game-owned producer queue; BotRelayInjectFlattenSystem
            // drains it before next frame's NetworkRelayServerSystem. GetSingletonRW orders this
            // producer ahead of the flatten job via the queue singleton's write dependency.
            var injectQueue = SystemAPI.GetSingletonRW<BotRelayInjectQueueSingleton>();
            tickJob.injectEnabled = 1;
            tickJob.injectWriter = injectQueue.ValueRW.queue.AsParallelWriter();

            var job = tickJob.Schedule(state.Dependency);

            BotRelayManager.ReplayTickHandle = job;
            state.Dependency = job;
        }

        [BurstCompile]
        private struct ReplayTickJob : IJob
        {
            public BotRelayFarmNative farm;
            [ReadOnly] public NativeArray<BotReplayCatalogEntry> catalogEntries;
            public double deltaTime;
            public byte injectEnabled;
            public NativeQueue<BotRelayInject>.ParallelWriter injectWriter;

            public void Execute()
            {
                if (!catalogEntries.IsCreated)
                    return;

                for (int i = 0; i < farm.agentCount; ++i)
                {
                    ref var runtime = ref farm.replayRuntime.ElementAt(i);
                    if (farm.agentStates[i].state != BotState.InLevel)
                    {
                        if ((runtime.flags & BotReplayRuntimeFlags.Playing) != 0)
                            BotReplayLogic.Stop(ref runtime);

                        continue;
                    }

                    // Peer is offline/left the stage (BotAgentLogic.__TickChannelTracker): pause
                    // playback entirely — do not advance playbackTime and do not inject. This holds
                    // the level open without saturating the non-ACKing peer (NetworkSendMessage -5),
                    // and resumes exactly where it paused when the peer reconnects (no backlog dump).
                    if ((farm.agentFlags[i] & BotAgentRuntimeFlags.RemoteAbsentSuppressed) != 0)
                        continue;

                    BotReplayLogic.Tick(i, ref farm, in catalogEntries, deltaTime, ref injectWriter, injectEnabled);
                }
            }
        }
    }
}
