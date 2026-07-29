using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;

[BurstCompile]
public struct BotRelayNetworkInterface : INetworkInterface
{
    internal const int MaxListenPacketsPerReceiveJob = BotRelayJobLimits.MaxListenPacketsPerReceiveJob;

    [ReadOnly] private NativeArray<NetworkEndpoint> m_LocalEndpoint;

    public NetworkEndpoint LocalEndpoint => m_LocalEndpoint.IsCreated ? m_LocalEndpoint[0] : default;

    public int Initialize(ref NetworkSettings settings, ref int packetPadding)
    {
        BotRelayManager.Instance.AddRef();
        m_LocalEndpoint = new NativeArray<NetworkEndpoint>(1, Allocator.Persistent);
        return 0;
    }

    public void Dispose()
    {
        if (m_LocalEndpoint.IsCreated)
            m_LocalEndpoint.Dispose();

        BotRelayManager.Instance.Release();
    }

    [BurstCompile]
    struct ReceiveJob : IJob
    {
        public NetworkEndpoint localEndPoint;
        public PacketsQueue ReceiveQueue;
        public OperationResult ReceiveResult;

        public unsafe void Execute()
        {
            ref var manager = ref BotRelayManager.Instance;
            int budget = MaxListenPacketsPerReceiveJob;
            while (budget-- > 0 && manager.HasInboundForListen(localEndPoint))
            {
                if (!manager.TryDequeueListenPacket(localEndPoint, out var packet, out var remote))
                    break;

                if (packet.length <= 0)
                    continue;

                if (!ReceiveQueue.EnqueuePacket(out var packetProcessor))
                {
                    ReceiveResult.ErrorCode = (int)StatusCode.NetworkReceiveQueueFull;
                    return;
                }

                packetProcessor.EndpointRef = remote;
                packet.AppendTo(ref packetProcessor);
            }
        }
    }

    [BurstCompile]
    struct SendJob : IJob
    {
        public NetworkEndpoint localEndPoint;
        public PacketsQueue SendQueue;

        public void Execute() => BotRelayManager.Instance.RouteSend(localEndPoint, ref SendQueue);
    }

    public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
    {
        var job = new ReceiveJob
        {
            localEndPoint = m_LocalEndpoint[0],
            ReceiveQueue = arguments.ReceiveQueue,
            ReceiveResult = arguments.ReceiveResult
        };

        dep = job.Schedule(JobHandle.CombineDependencies(dep, BotRelayManager.ManagerAccessHandle));
        BotRelayManager.ManagerAccessHandle = dep;
        return dep;
    }

    public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
    {
        var job = new SendJob
        {
            localEndPoint = m_LocalEndpoint[0],
            SendQueue = arguments.SendQueue
        };

        dep = job.Schedule(JobHandle.CombineDependencies(dep, BotRelayManager.ManagerAccessHandle));
        BotRelayManager.ManagerAccessHandle = dep;
        return dep;
    }

    public int Bind(NetworkEndpoint endpoint)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (!endpoint.IsLoopback && !endpoint.IsAny)
            throw new InvalidOperationException($"BotRelayNetworkInterface must bind to loopback, got {endpoint}.");
#endif

        ushort port = endpoint.Port;
        if (port == 0)
            port = BotRelayManager.Instance.AllocateVirtualPort();
        else if (!BotRelayManager.Instance.IsListenPort(port))
            BotRelayManager.Instance.EnsureVirtualPort(port);

        m_LocalEndpoint[0] = NetworkEndpoint.LoopbackIpv4.WithPort(port);
        return 0;
    }

    public int Listen() => 0;
}
