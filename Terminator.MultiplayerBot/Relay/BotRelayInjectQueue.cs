using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ZG
{
    /// <summary>
    /// Game-side inject record. Keeps the original self-contained shape (id + inline payload) so the
    /// per-agent Burst tick / replay jobs can keep writing through a lock-free
    /// <see cref="NativeQueue{T}.ParallelWriter"/> exactly as before. A single serial flatten job
    /// (<see cref="BotRelayInjectFlattenSystem"/>) converts these into the package's
    /// <see cref="NetworkRelayServerInjectSingleton"/> offset/count list right before the relay server
    /// drains it — no <c>Complete()</c> on any hot path.
    /// </summary>
    public struct BotRelayInject
    {
        public uint id;

        // Keep the queue record exactly as large as a supported relay app packet. The recordings
        // contain valid PlayerProperty/gameplay frames above the old 500B FixedList cap, so this
        // must not truncate or silently reject anything BotRelayPacket can carry.
        public BotRelayPacket packet;
    }

    /// <summary>
    /// Holds the persistent producer queue shared by every bot inject producer (agent tick, replay
    /// tick, replay load). Producers write via <see cref="NativeQueue{T}.ParallelWriter"/>; the
    /// flatten system drains it.
    /// </summary>
    public struct BotRelayInjectQueueSingleton : IComponentData
    {
        public NativeQueue<BotRelayInject> queue;
    }

    /// <summary>
    /// Drains <see cref="BotRelayInjectQueueSingleton.queue"/> into
    /// <see cref="NetworkRelayServerInjectSingleton"/> (offset/count list + shared byte buffer) once
    /// per frame, immediately before <c>NetworkRelayServerSystem</c> consumes it.
    ///
    /// Ordering / safety:
    /// - Producers RW <see cref="BotRelayInjectQueueSingleton"/> after the relay server; this system
    ///   RW's the same component before it, so ECS serializes queue access without any manual
    ///   <c>Complete()</c> (previous-frame producer output is flattened this frame — same 1-frame
    ///   latency the old NetworkRelayServer.injects path had).
    /// - This system RW's <see cref="NetworkRelayServerInjectSingleton"/> so the relay server's read
    ///   depends on the flatten job.
    /// - The list is cleared every flatten so the relay server never re-consumes a stale frame.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
    [UpdateBefore(typeof(NetworkRelayServerSystem))]
    public partial struct BotRelayInjectFlattenSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(new BotRelayInjectQueueSingleton
            {
                queue = new NativeQueue<BotRelayInject>(Allocator.Persistent)
            });

            state.RequireForUpdate<NetworkRelayServerInjectSingleton>();
            state.RequireForUpdate<BotRelayInjectQueueSingleton>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            state.Dependency.Complete();

            if (SystemAPI.TryGetSingleton<BotRelayInjectQueueSingleton>(out var injectQueue) &&
                injectQueue.queue.IsCreated)
                injectQueue.queue.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var injectQueue = ref SystemAPI.GetSingletonRW<BotRelayInjectQueueSingleton>().ValueRW;
            ref var injectSingleton = ref SystemAPI.GetSingletonRW<NetworkRelayServerInjectSingleton>().ValueRW;

            state.Dependency = new FlattenJob
            {
                queue = injectQueue.queue,
                injects = injectSingleton.injects,
                bytes = injectSingleton.bytes
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        private struct FlattenJob : IJob
        {
            public NativeQueue<BotRelayInject> queue;
            public NativeList<NetworkRelayServerInjectSingleton.Inject> injects;
            public NativeList<byte> bytes;

            public void Execute()
            {
                injects.Clear();
                bytes.Clear();

                while (queue.TryDequeue(out var item))
                {
                    int count = item.packet.length;
                    if (count <= 0)
                        continue;

                    int offset = bytes.Length;
                    for (int i = 0; i < count; ++i)
                        bytes.Add(item.packet.GetByte(i));

                    injects.Add(new NetworkRelayServerInjectSingleton.Inject
                    {
                        id = item.id,
                        byteOffset = offset,
                        byteCount = count
                    });
                }
            }
        }
    }
}
