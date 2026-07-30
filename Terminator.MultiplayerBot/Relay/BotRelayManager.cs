using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Networking.Transport;

public struct BotRelayManager
{
    private const int MaxPacketPayloadSize = 1472;
    internal const int ConnectCaptureCapacity = 32;

    private struct SharedInstanceKeyV4
    {
    }

    private struct SharedManagerAccessKey
    {
    }

    private struct SharedReplayTickKey
    {
    }

    private static readonly SharedStatic<BotRelayManager> s_Instance =
        SharedStatic<BotRelayManager>.GetOrCreate<SharedInstanceKeyV4>();

    private static readonly SharedStatic<JobHandle> s_ManagerAccessHandle =
        SharedStatic<JobHandle>.GetOrCreate<SharedManagerAccessKey>();

    private static readonly SharedStatic<JobHandle> s_ReplayTickHandle =
        SharedStatic<JobHandle>.GetOrCreate<SharedReplayTickKey>();

    internal static JobHandle ManagerAccessHandle
    {
        get => s_ManagerAccessHandle.Data;
        set => s_ManagerAccessHandle.Data = value;
    }

    internal static JobHandle ReplayTickHandle
    {
        get => s_ReplayTickHandle.Data;
        set => s_ReplayTickHandle.Data = value;
    }

    internal static void CompleteReplayTick()
    {
        ReplayTickHandle.Complete();
        ReplayTickHandle = default;
    }

#if UNITY_EDITOR
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnLoad()
    {
        s_Instance.Data = default;
        s_ManagerAccessHandle.Data = default;
        s_ReplayTickHandle.Data = default;
        BotRelayReplayRuntimeGate.Reset();
        BotRelayInboundProbe.Reset();
    }
#endif

    public static ref BotRelayManager Instance => ref s_Instance.Data;

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct PacketData
    {
        [FieldOffset(0)] public ushort fromPort;
        [FieldOffset(2)] public int length;
        [FieldOffset(6)] public fixed byte data[MaxPacketPayloadSize];

        public void CopyTo(out BotRelayPacket packet)
        {
            packet = default;
            if (length <= 0)
                return;

            packet.length = length;
            for (int i = 0; i < length; ++i)
                packet.data[i] = data[i];
        }

        public void WriteFrom(in BotRelayPacket packet)
        {
            length = packet.length;
            if (length <= 0)
                return;

            for (int i = 0; i < length; ++i)
                data[i] = packet.data[i];
        }

        public void WriteFrom(NativeArray<byte> payload)
        {
            if (!payload.IsCreated || payload.Length <= 0)
            {
                length = 0;
                return;
            }

            length = payload.Length;
            for (int i = 0; i < length; ++i)
                data[i] = payload[i];
        }

        public void CopyPayloadFrom(ref Unity.Networking.Transport.PacketProcessor processor, int maxSize)
        {
            fixed (byte* dst = data)
                processor.CopyPayload(dst, maxSize);
        }

        public void AppendTo(ref Unity.Networking.Transport.PacketProcessor processor)
        {
            if (length <= 0)
                return;

            fixed (byte* src = data)
                processor.AppendToPayload(src, length);
        }
    }

    private BotRelayMultiQueue<PacketData> m_Queue;
    private BotRelayMultiQueue<BotRelayPacket> m_PeeledAppQueue;
    private BlobAssetReference<BotRelayCatalogBlob> m_PeelCatalogBlob;
    private NativeParallelHashMap<ushort, int> m_PortChannels;
    private ushort m_ListenPort;
    private int m_RefCount;

    internal int connectCaptureActive;
    internal int connectCaptureClientCount;
    internal int connectCaptureServerCount;
    internal NativeArray<int> connectCaptureClientLengths;
    internal NativeArray<int> connectCaptureServerLengths;
    internal NativeArray<byte> connectCaptureClientBytes;
    internal NativeArray<byte> connectCaptureServerBytes;

    internal int diagRouteSendPeeled;
    internal int diagPeeledAppsDequeued;
    internal int diagRouteSendEnqueued;
    internal int diagRouteSendZeroPort;
    internal int diagRouteSendDropped;
    internal int diagSendJobPackets;
    internal int diagInboundDequeued;
    internal int diagDrainPacketsBudget;
    internal int diagDrainBudgetExhausted;
    internal int diagInboundUnmapped;
    internal int diagInboundAppsEnqueued;
    internal int diagLinkAckInjected;
    internal int diagLastRouteDestPort;
    internal int diagLastRouteLen;

    private NativeParallelHashMap<uint, ushort> m_BotVirtualPorts;
    private int m_RegisteredBotPortCount;
    private ushort m_SoleBotVirtualPort;

    public bool IsCreated => m_Queue.IsCreated;

    public void AddRef()
    {
        if (m_RefCount == 0)
        {
            m_Queue = new BotRelayMultiQueue<PacketData>(128);
            m_PeeledAppQueue = new BotRelayMultiQueue<BotRelayPacket>(128);
            m_PeelCatalogBlob = default;
            m_PortChannels = new NativeParallelHashMap<ushort, int>(64, Allocator.Persistent);

            m_BotVirtualPorts = new NativeParallelHashMap<uint, ushort>(16, Allocator.Persistent);
            m_ListenPort = 0;
            m_RegisteredBotPortCount = 0;
            m_SoleBotVirtualPort = 0;
            connectCaptureActive = 0;
            connectCaptureClientCount = 0;
            connectCaptureServerCount = 0;
            __EnsureConnectCaptureBuffers();
        }

        ++m_RefCount;
        __EnsureConnectCaptureBuffers();
    }

    private void __EnsureConnectCaptureBuffers()
    {
        if (!connectCaptureClientLengths.IsCreated)
        {
            connectCaptureClientLengths = new NativeArray<int>(ConnectCaptureCapacity, Allocator.Persistent);
            connectCaptureServerLengths = new NativeArray<int>(ConnectCaptureCapacity, Allocator.Persistent);
            connectCaptureClientBytes =
                new NativeArray<byte>(ConnectCaptureCapacity * BotRelayPacket.MaxPayloadSize, Allocator.Persistent);
            connectCaptureServerBytes =
                new NativeArray<byte>(ConnectCaptureCapacity * BotRelayPacket.MaxPayloadSize, Allocator.Persistent);
        }

    }

    public void Release()
    {
        --m_RefCount;
        if (m_RefCount > 0)
            return;

        CompleteManagerAccess();
        __ResetConnectCaptureState();
        if (connectCaptureClientLengths.IsCreated)
        {
            connectCaptureClientLengths.Dispose();
            connectCaptureServerLengths.Dispose();
            connectCaptureClientBytes.Dispose();
            connectCaptureServerBytes.Dispose();
        }

        connectCaptureClientLengths = default;
        connectCaptureServerLengths = default;
        connectCaptureClientBytes = default;
        connectCaptureServerBytes = default;
        m_Queue.Dispose();
        if (m_PeeledAppQueue.IsCreated)
            m_PeeledAppQueue.Dispose();

        if (m_BotVirtualPorts.IsCreated)
            m_BotVirtualPorts.Dispose();
        m_PortChannels.Dispose();
        m_ListenPort = 0;
        m_RegisteredBotPortCount = 0;
        m_SoleBotVirtualPort = 0;
    }

    [BurstDiscard]
    internal void ResetDiagnostics()
    {
        diagRouteSendEnqueued = 0;
        diagRouteSendPeeled = 0;
        diagPeeledAppsDequeued = 0;
        diagRouteSendZeroPort = 0;
        diagRouteSendDropped = 0;
        diagSendJobPackets = 0;
        diagInboundDequeued = 0;
        diagDrainPacketsBudget = 0;
        diagDrainBudgetExhausted = 0;
        diagInboundUnmapped = 0;
        diagInboundAppsEnqueued = 0;
        diagLinkAckInjected = 0;
        diagLastRouteDestPort = 0;
        diagLastRouteLen = 0;
    }

    public void Reset()
    {
        CompleteManagerAccess();
        BotRelayReplayRuntimeGate.Reset();
        BotRelayInboundProbe.Reset();
        ResetDiagnostics();
        if (!m_Queue.IsCreated)
            return;

        m_Queue.ClearAll();
        if (m_PeeledAppQueue.IsCreated)
            m_PeeledAppQueue.ClearAll();
        m_PeelCatalogBlob = default;
        m_PortChannels.Clear();

        if (m_BotVirtualPorts.IsCreated)
            m_BotVirtualPorts.Clear();
        m_RegisteredBotPortCount = 0;
        m_SoleBotVirtualPort = 0;
        m_ListenPort = 0;
        connectCaptureActive = 0;
        connectCaptureClientCount = 0;
        connectCaptureServerCount = 0;
        CancelConnectCapture();
    }

    public bool HasInboundForBotBurst(ushort virtualPort) =>
        TryGetChannelByPort(virtualPort, out int channel) && m_Queue.Peek(channel, out _);

    [BurstDiscard]
    private void CompleteManagerAccess()
    {
        if (JobsUtility.IsExecutingJob)
            return;

        ManagerAccessHandle.Complete();
    }

    [BurstDiscard]
    public void RegisterListenPort(ushort port)
    {
        CompleteManagerAccess();
        __EnsureCreated();
        m_ListenPort = port;
        EnsurePortChannel(port);
    }

    [BurstDiscard]
    public ushort AllocateVirtualPort()
    {
        CompleteManagerAccess();
        __EnsureManagedRng();
        return AllocateVirtualPortBurst(ref __ManagedRng);
    }

    public ushort AllocateVirtualPortBurst(ref Unity.Mathematics.Random rng)
    {
        ushort port;
        int guard = 0;
        do
        {
            port = (ushort)rng.NextUInt(14000u, 60000u);
            if (++guard > 512)
                break;
        } while (m_PortChannels.ContainsKey(port));

        EnsurePortChannelBurst(port);
        return port;
    }

    private static Unity.Mathematics.Random __ManagedRng;

    [BurstDiscard]
    private static void __EnsureManagedRng()
    {
        if (__ManagedRng.state == 0)
            __ManagedRng = Unity.Mathematics.Random.CreateFromIndex(0x8FA3C271u);
    }

    [BurstDiscard]
    public void EnsureVirtualPort(ushort port) => EnsurePortChannelBurst(port);

    public void RegisterBotVirtualPortBurst(uint userID, ushort virtualPort)
    {
        if (userID == 0 || virtualPort == 0 || !m_BotVirtualPorts.IsCreated)
            return;

        EnsurePortChannelBurst(virtualPort);
        if (m_BotVirtualPorts.TryAdd(userID, virtualPort))
            ++m_RegisteredBotPortCount;
        else
            m_BotVirtualPorts[userID] = virtualPort;

        m_SoleBotVirtualPort = m_RegisteredBotPortCount == 1 ? virtualPort : (ushort)0;
    }
    public bool IsListenPort(ushort port) => port == m_ListenPort;

    internal ushort ListenPort => m_ListenPort;

    public void EnqueueConnectLinkToListenFromPacketBurst(
        ushort fromVirtualPort,
        in BotRelayPacket packet)
    {
        if (packet.IsEmpty || m_ListenPort == 0)
            return;

        if (!TryGetChannelByPort(m_ListenPort, out int listenChannel))
            return;

        EnqueuePacketFromPacketBurst(listenChannel, fromVirtualPort, in packet);
    }

    public bool TryDequeueToBotBurst(ushort virtualPort, out BotRelayPacket packet)
    {
        packet = default;

        if (!TryGetChannelByPort(virtualPort, out int channel))
            return false;

        if (!m_Queue.Dequeue(channel, out PacketData data))
            return false;

        if (data.length <= 0)
            return true;

        ++diagInboundDequeued;
        data.CopyTo(out packet);
        return true;
    }

    public bool HasInboundForListen(NetworkEndpoint listenEndpoint) =>
        HasInboundForListenBurst(listenEndpoint);

    public bool HasInboundForListenBurst(NetworkEndpoint listenEndpoint) =>
        TryGetChannelByPort(listenEndpoint.Port, out int channel) && m_Queue.Peek(channel, out _);

    internal bool TryDequeueListenPacket(
        NetworkEndpoint listenEndpoint,
        out PacketData packet,
        out NetworkEndpoint remote)
    {
        packet = default;
        remote = default;

        if (!TryGetChannelByPort(listenEndpoint.Port, out int channel))
            return false;

        if (!m_Queue.Dequeue(channel, out packet))
            return false;

        remote = NetworkEndpoint.LoopbackIpv4.WithPort(packet.fromPort);
        return packet.length > 0;
    }

    [BurstDiscard]
    public void SetPeelCatalogBlob(in BlobAssetReference<BotRelayCatalogBlob> blob)
    {
        CompleteManagerAccess();
        m_PeelCatalogBlob = blob;
    }

    public bool TryDequeuePeeledAppBurst(ushort virtualPort, out BotRelayPacket app)
    {
        app = default;
        if (!m_PeeledAppQueue.IsCreated || !TryGetChannelByPort(virtualPort, out int channel))
            return false;

        if (!m_PeeledAppQueue.Dequeue(channel, out app))
            return false;

        if (!app.IsEmpty)
            ++diagPeeledAppsDequeued;

        return true;
    }

    private bool __IsBotVirtualPortBurst(ushort port)
    {
        if (port == 0 || !m_BotVirtualPorts.IsCreated)
            return false;

        var ports = m_BotVirtualPorts.GetEnumerator();
        while (ports.MoveNext())
        {
            if (ports.Current.Value == port)
                return true;
        }

        return false;
    }
    private bool __TryPeelAndEnqueueAppsBurst(
        int channel,
        ushort botVirtualPort,
        in PacketData packetData,
        out bool fullyConsumed)
    {
        fullyConsumed = false;
        if (!m_PeeledAppQueue.IsCreated || !m_PeelCatalogBlob.IsCreated)
            return false;

        packetData.CopyTo(out var wire);
        if (wire.IsEmpty)
            return false;

        ref var catalog = ref m_PeelCatalogBlob.Value;
        var batch = default(BotRelayPopEventsWalkBatch);
        bool peelSucceeded = BotRelayRoutePeelOps.TryPeelAllServerToBotApps(
            in wire,
            ref catalog,
            ref batch,
            out fullyConsumed);

        if (peelSucceeded && batch.appCount > 0)
        {
            for (int i = 0; i < batch.appCount; ++i)
            {
                var app = batch.GetApp(i);
                m_PeeledAppQueue.Enqueue(channel, app);
                ++diagInboundAppsEnqueued;
                if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int relayType))
                    BotRelayAgentDiagnostics.LogRouteSendPeeled(relayType, wire.length, app.length);
            }
        }

        return peelSucceeded && batch.appCount > 0;
    }
    internal unsafe void RouteSend(NetworkEndpoint localEndpoint, ref PacketsQueue sendQueue)
    {
        diagSendJobPackets += sendQueue.Count;

        for (int i = 0; i < sendQueue.Count; ++i)
        {
            var packetProcessor = sendQueue[i];
            if (packetProcessor.Length == 0)
                continue;

            var packetData = new PacketData
            {
                fromPort = localEndpoint.Port,
                length = packetProcessor.Length
            };
            packetData.CopyPayloadFrom(ref packetProcessor, MaxPacketPayloadSize);

            ushort destinationPort = packetProcessor.EndpointRef.Port;
            if (destinationPort != 0)
            {
                __RoutePacketToPort(localEndpoint.Port, destinationPort, in packetData);
                continue;
            }

            if (!m_BotVirtualPorts.IsCreated || m_RegisteredBotPortCount == 0)
            {
                ++diagRouteSendZeroPort;
                continue;
            }

            // NetworkRelayType.All uses endpoint port 0. Fan it out to every registered bot.
            bool routed = false;
            var botPorts = m_BotVirtualPorts.GetEnumerator();
            while (botPorts.MoveNext())
            {
                ushort botVirtualPort = botPorts.Current.Value;
                if (botVirtualPort == 0)
                    continue;

                routed = true;
                __RoutePacketToPort(localEndpoint.Port, botVirtualPort, in packetData);
            }

            if (!routed)
                ++diagRouteSendZeroPort;
        }
    }
    private void __RoutePacketToPort(ushort fromPort, ushort destinationPort, in PacketData packetData)
    {
        if (!TryGetChannelByPort(destinationPort, out int channel))
        {
            EnsurePortChannelBurst(destinationPort);
            if (!TryGetChannelByPort(destinationPort, out channel))
            {
                ++diagRouteSendDropped;
                BotRelayAgentDiagnostics.LogRouteSendDropped(fromPort, destinationPort, packetData.length);
                return;
            }
        }

        diagLastRouteDestPort = destinationPort;
        diagLastRouteLen = packetData.length;

        bool peelFullyConsumed = false;
        if (__IsBotVirtualPortBurst(destinationPort) &&
            __TryPeelAndEnqueueAppsBurst(channel, destinationPort, in packetData, out peelFullyConsumed) &&
            peelFullyConsumed)
        {
            ++diagRouteSendPeeled;
            BotRelayAgentDiagnostics.LogRouteSendEnqueued(fromPort, destinationPort, packetData.length);
            if (connectCaptureActive != 0 && packetData.length > 0 && m_ListenPort != 0 &&
                destinationPort != m_ListenPort)
                __TryRecordConnectCaptureWire(true, in packetData);
            return;
        }

        ++diagRouteSendEnqueued;
        BotRelayAgentDiagnostics.LogRouteSendEnqueued(fromPort, destinationPort, packetData.length);
        if (packetData.length >= 80 && packetData.length <= 160)
        {
            packetData.CopyTo(out var previewPacket);
            BotRelayAgentDiagnostics.LogRouteWirePreview(
                fromPort, destinationPort, packetData.length, in previewPacket);
        }

        if (connectCaptureActive != 0 && packetData.length > 0 && m_ListenPort != 0)
        {
            if (destinationPort != m_ListenPort)
                __TryRecordConnectCaptureWire(true, in packetData);
            else if (TryGetChannelByPort(m_ListenPort, out int listenChannel) && channel == listenChannel)
                __TryRecordConnectCaptureWire(false, in packetData);
        }

        m_Queue.Enqueue(channel, packetData);
    }
    [BurstDiscard]
    private void __EnsureCreated()
    {
        if (m_PortChannels.IsCreated)
            return;

        if (m_RefCount == 0)
        {
            AddRef();
            return;
        }

        m_Queue = new BotRelayMultiQueue<PacketData>(128);
        m_PortChannels = new NativeParallelHashMap<ushort, int>(64, Allocator.Persistent);
    }

    private void EnsurePortChannel(ushort port) => EnsurePortChannelBurst(port);

    private void EnsurePortChannelBurst(ushort port)
    {
        if (!m_PortChannels.IsCreated)
            return;

        if (m_PortChannels.ContainsKey(port))
            return;

        int channel = m_PortChannels.Count() + 1;
        m_PortChannels.TryAdd(port, channel);
    }

    private bool TryGetChannelByPort(ushort port, out int channel) =>
        m_PortChannels.TryGetValue(port, out channel);

    [BurstDiscard]
    public string BuildDiagnosticsSnapshotNoSync()
    {
        ref var manager = ref Instance;
        return
            $"routeEnq={manager.diagRouteSendEnqueued}, routeZero={manager.diagRouteSendZeroPort}, " +
            $"routeDrop={manager.diagRouteSendDropped}, sendPkts={manager.diagSendJobPackets}, " +
            $"inDeq={manager.diagInboundDequeued}, inApp={manager.diagInboundAppsEnqueued}, " +
            $"inUnmapped={manager.diagInboundUnmapped}, linkAckInjected={manager.diagLinkAckInjected}, " +
            $"lastDest={manager.diagLastRouteDestPort}, lastLen={manager.diagLastRouteLen}, " +
            $"bots={manager.m_RegisteredBotPortCount}, soleVport={manager.m_SoleBotVirtualPort}";
    }

    [BurstDiscard]
    public void ClearListenQueue()
    {
        CompleteManagerAccess();
        if (m_ListenPort == 0 || !TryGetChannelByPort(m_ListenPort, out int channel))
            return;

        while (m_Queue.Dequeue(channel, out _)) { }
    }

    [BurstDiscard]
    private unsafe void EnqueuePacket(int channel, ushort fromPort, NativeArray<byte> payload)
    {
        if (payload.Length > MaxPacketPayloadSize)
            return;

        var packetData = new PacketData
        {
            fromPort = fromPort,
            length = payload.Length
        };

        packetData.WriteFrom(payload);
        __EnqueuePacketData(channel, packetData);
    }

    private void EnqueuePacketFromPacketBurst(int channel, ushort fromPort, in BotRelayPacket packet)
    {
        if (packet.length > MaxPacketPayloadSize)
            return;

        var packetData = new PacketData
        {
            fromPort = fromPort,
            length = packet.length
        };

        packetData.WriteFrom(in packet);
        m_Queue.Enqueue(channel, packetData);
    }

    private void __EnqueuePacketData(int channel, in PacketData packetData)
    {
        if (packetData.length > 0 &&
            connectCaptureActive != 0 &&
            m_ListenPort != 0 &&
            TryGetChannelByPort(m_ListenPort, out int listenChannel) &&
            channel == listenChannel)
            __TryRecordConnectCaptureWire(false, in packetData);

        m_Queue.Enqueue(channel, packetData);
    }

    private unsafe void __TryRecordConnectCaptureWire(bool serverSide, in PacketData packetData)
    {
        if (connectCaptureActive == 0 || packetData.length <= 0)
            return;

        if (serverSide)
        {
            __TryRecordConnectCaptureWireSide(
                ref connectCaptureServerCount,
                connectCaptureServerLengths,
                connectCaptureServerBytes,
                in packetData);
            return;
        }

        __TryRecordConnectCaptureWireSide(
            ref connectCaptureClientCount,
            connectCaptureClientLengths,
            connectCaptureClientBytes,
            in packetData);
    }

    private unsafe void __TryRecordConnectCaptureWireSide(
        ref int count,
        NativeArray<int> lengths,
        NativeArray<byte> bytes,
        in PacketData packetData)
    {
        if (!lengths.IsCreated || !bytes.IsCreated || count >= ConnectCaptureCapacity)
            return;

        if (count > 0)
        {
            int lastIndex = count - 1;
            int lastLength = lengths[lastIndex];
            if (lastLength == packetData.length)
            {
                int lastOffset = lastIndex * BotRelayPacket.MaxPayloadSize;
                bool same = true;
                for (int i = 0; i < lastLength; ++i)
                {
                    if (bytes[lastOffset + i] != packetData.data[i])
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                    return;
            }
        }

        int writeOffset = count * BotRelayPacket.MaxPayloadSize;
        for (int i = 0; i < packetData.length; ++i)
            bytes[writeOffset + i] = packetData.data[i];

        lengths[count] = packetData.length;
        count++;
    }

    [BurstDiscard]
    internal void BeginConnectCapture()
    {
        __ResetConnectCaptureState();
        connectCaptureActive = 1;
    }

    [BurstDiscard]
    internal BotRelayConnectWireState EndConnectCapture()
    {
        var state = default(BotRelayConnectWireState);

        if (connectCaptureActive == 0)
            return state;

        connectCaptureActive = 0;

        if (connectCaptureClientCount > 0)
            state.clientPackets = __CopyConnectCaptureSide(false);

        if (connectCaptureServerCount > 0)
            state.serverPackets = __CopyConnectCaptureSide(true);

        connectCaptureClientCount = 0;
        connectCaptureServerCount = 0;

        if (state.IsValid)
        {
            int serverCount = state.serverPackets.IsCreated ? state.serverPackets.Length : 0;
            UnityEngine.Debug.Log(
                $"[BotRelay] Captured {state.clientPackets.Length} client wire + {serverCount} server handshake wire(s).");
        }

        return state;
    }

    [BurstDiscard]
    internal void CancelConnectCapture()
    {
        __ResetConnectCaptureState();
    }

    private void __ResetConnectCaptureState()
    {
        connectCaptureActive = 0;
        connectCaptureClientCount = 0;
        connectCaptureServerCount = 0;
    }

    [BurstDiscard]
    private NativeList<BotRelayConnectWirePacket> __CopyConnectCaptureSide(bool serverSide)
    {
        int count = serverSide ? connectCaptureServerCount : connectCaptureClientCount;
        var lengths = serverSide ? connectCaptureServerLengths : connectCaptureClientLengths;
        var bytes = serverSide ? connectCaptureServerBytes : connectCaptureClientBytes;

        var list = new NativeList<BotRelayConnectWirePacket>(count, Allocator.Persistent);
        for (int i = 0; i < count; ++i)
        {
            var packet = default(BotRelayPacket);
            int length = lengths[i];
            packet.length = length;
            int offset = i * BotRelayPacket.MaxPayloadSize;
            for (int b = 0; b < length; ++b)
                packet.SetByte(b, bytes[offset + b]);

            if (list.Length > 0)
            {
                var last = list[list.Length - 1];
                if (BotRelayWireBytes.Equals(last.wire, packet))
                    continue;
            }

            list.Add(new BotRelayConnectWirePacket
            {
                wire = packet
            });
        }

        return list;
    }

    public void EnqueueServerToBotBurst(ushort botVirtualPort, ushort fromListenPort, NativeArray<byte> payload)
    {
        if (!payload.IsCreated || payload.Length == 0 || botVirtualPort == 0)
            return;

        if (!TryGetChannelByPort(botVirtualPort, out int channel))
            return;

        if (payload.Length > MaxPacketPayloadSize)
            return;

        var packetData = new PacketData
        {
            fromPort = fromListenPort,
            length = payload.Length
        };

        packetData.WriteFrom(payload);
        bool peelFullyConsumed = false;
        if (__IsBotVirtualPortBurst(botVirtualPort) &&
            __TryPeelAndEnqueueAppsBurst(
                channel,
                botVirtualPort,
                in packetData,
                out peelFullyConsumed) &&
            peelFullyConsumed)
        {
            ++diagRouteSendPeeled;
            return;
        }

        m_Queue.Enqueue(channel, packetData);
    }
}

/// <summary>
/// Cheap gates so replay systems skip ECS farm access when idle (avoids main-thread deadlock with ReceiveJob).
/// </summary>
internal static class BotRelayReplayRuntimeGate
{
    struct PendingLevelStartKey { }
    struct InLevelKey { }

    static readonly SharedStatic<int> s_PendingLevelStart =
        SharedStatic<int>.GetOrCreate<PendingLevelStartKey>();

    static readonly SharedStatic<int> s_InLevel =
        SharedStatic<int>.GetOrCreate<InLevelKey>();

    public static bool HasPendingLevelStart => s_PendingLevelStart.Data > 0;

    public static bool HasInLevel => s_InLevel.Data > 0;

    public static void AddPendingLevelStart() =>
        Interlocked.Increment(ref s_PendingLevelStart.Data);

    public static void RemovePendingLevelStart()
    {
        if (Interlocked.Decrement(ref s_PendingLevelStart.Data) < 0)
            Interlocked.Exchange(ref s_PendingLevelStart.Data, 0);
    }

    public static void AddInLevel() =>
        Interlocked.Increment(ref s_InLevel.Data);

    public static void RemoveInLevel()
    {
        if (Interlocked.Decrement(ref s_InLevel.Data) < 0)
            Interlocked.Exchange(ref s_InLevel.Data, 0);
    }

    public static void Reset()
    {
        Interlocked.Exchange(ref s_PendingLevelStart.Data, 0);
        Interlocked.Exchange(ref s_InLevel.Data, 0);
    }
}
