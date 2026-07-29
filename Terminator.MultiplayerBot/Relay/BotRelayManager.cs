using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Networking.Transport;

public enum BotRelayListenInjectClass : byte
{
    RelayOutbound = 0,
    ConnectHandshake = 1,
}

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

    internal ushort encoderTapVirtualPort;
    internal ushort encoderInjectFromPort;
    internal ushort encoderListenPort;
    internal byte encoderSuppressSend;
    internal BotRelayPacket encoderInjectWire;
    internal byte encoderInjectWirePending;

    internal int connectCaptureActive;
    internal int connectCaptureClientCount;
    internal int connectCaptureServerCount;
    internal NativeArray<ushort> connectCaptureClientPorts;
    internal NativeArray<ushort> connectCaptureServerPorts;
    internal NativeArray<int> connectCaptureClientLengths;
    internal NativeArray<int> connectCaptureServerLengths;
    internal NativeArray<byte> connectCaptureClientBytes;
    internal NativeArray<byte> connectCaptureServerBytes;
    internal NativeArray<byte> connectCaptureConnectPayload;

    internal int diagRouteSendPeeled;
    internal int diagPeeledAppsDequeued;
    internal int diagRouteSendEnqueued;
    internal int diagRouteSendZeroPort;
    internal int diagRouteSendDropped;
    internal int diagSendJobPackets;
    internal int diagInboundDequeued;
    internal int diagInboundSkippedEmpty;
    internal int diagDrainPacketsBudget;
    internal int diagDrainBudgetExhausted;
    internal int diagListenInjectSkippedPopEvents;
    internal int diagInboundUnmapped;
    internal int diagInboundAppsEnqueued;
    internal int diagLinkAckSwallowed;
    internal int diagJoinWireInjected;
    internal int diagMatchWireInjected;
    internal int diagMismatchWireInjected;
    internal int diagLastRouteDestPort;
    internal int diagLastRouteLen;
    internal BotRelayPacket diagLastJoinListenWire;
    internal ushort diagLastJoinListenFromPort;

    private NativeParallelHashMap<uint, ushort> m_BotVirtualPorts;
    private NativeParallelHashMap<int, ushort> m_ConnectionIndexVirtualPorts;
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
            m_ConnectionIndexVirtualPorts = new NativeParallelHashMap<int, ushort>(16, Allocator.Persistent);
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
        if (!connectCaptureClientPorts.IsCreated)
        {
            connectCaptureClientPorts = new NativeArray<ushort>(ConnectCaptureCapacity, Allocator.Persistent);
            connectCaptureServerPorts = new NativeArray<ushort>(ConnectCaptureCapacity, Allocator.Persistent);
            connectCaptureClientLengths = new NativeArray<int>(ConnectCaptureCapacity, Allocator.Persistent);
            connectCaptureServerLengths = new NativeArray<int>(ConnectCaptureCapacity, Allocator.Persistent);
            connectCaptureClientBytes =
                new NativeArray<byte>(ConnectCaptureCapacity * BotRelayPacket.MaxPayloadSize, Allocator.Persistent);
            connectCaptureServerBytes =
                new NativeArray<byte>(ConnectCaptureCapacity * BotRelayPacket.MaxPayloadSize, Allocator.Persistent);
        }

        if (!connectCaptureConnectPayload.IsCreated)
            connectCaptureConnectPayload = new NativeArray<byte>(0, Allocator.Persistent);
    }

    public void Release()
    {
        --m_RefCount;
        if (m_RefCount > 0)
            return;

        CompleteManagerAccess();
        __ResetConnectCaptureState();
        if (connectCaptureClientPorts.IsCreated)
        {
            connectCaptureClientPorts.Dispose();
            connectCaptureServerPorts.Dispose();
            connectCaptureClientLengths.Dispose();
            connectCaptureServerLengths.Dispose();
            connectCaptureClientBytes.Dispose();
            connectCaptureServerBytes.Dispose();
        }

        if (connectCaptureConnectPayload.IsCreated)
            connectCaptureConnectPayload.Dispose();

        connectCaptureClientPorts = default;
        connectCaptureServerPorts = default;
        connectCaptureClientLengths = default;
        connectCaptureServerLengths = default;
        connectCaptureClientBytes = default;
        connectCaptureServerBytes = default;
        connectCaptureConnectPayload = default;
        m_Queue.Dispose();
        if (m_PeeledAppQueue.IsCreated)
            m_PeeledAppQueue.Dispose();

        if (m_BotVirtualPorts.IsCreated)
            m_BotVirtualPorts.Dispose();
        if (m_ConnectionIndexVirtualPorts.IsCreated)
            m_ConnectionIndexVirtualPorts.Dispose();
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
        diagInboundSkippedEmpty = 0;
        diagDrainPacketsBudget = 0;
        diagDrainBudgetExhausted = 0;
        diagListenInjectSkippedPopEvents = 0;
        diagInboundUnmapped = 0;
        diagInboundAppsEnqueued = 0;
        diagLinkAckSwallowed = 0;
        diagJoinWireInjected = 0;
        diagMatchWireInjected = 0;
        diagMismatchWireInjected = 0;
        diagLastRouteDestPort = 0;
        diagLastRouteLen = 0;
        diagLastJoinListenWire = default;
        diagLastJoinListenFromPort = 0;
    }

    [BurstDiscard]
    internal void RecordJoinListenInjectBurst(ushort fromVirtualPort, in BotRelayPacket wire)
    {
        diagLastJoinListenFromPort = fromVirtualPort;
        diagLastJoinListenWire = wire;
    }

    [BurstDiscard]
    public bool TryGetLastJoinListenInject(out BotRelayPacket wire, out ushort fromVirtualPort)
    {
        CompleteManagerAccess();
        return TryPeekLastJoinListenInject(out wire, out fromVirtualPort);
    }

    /// <summary>Read last Join listen inject without blocking ReceiveJob (PlayMode harness polling).</summary>
    [BurstDiscard]
    public bool TryPeekLastJoinListenInject(out BotRelayPacket wire, out ushort fromVirtualPort)
    {
        wire = diagLastJoinListenWire;
        fromVirtualPort = diagLastJoinListenFromPort;
        return !wire.IsEmpty;
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
        if (m_ConnectionIndexVirtualPorts.IsCreated)
            m_ConnectionIndexVirtualPorts.Clear();
        m_RegisteredBotPortCount = 0;
        m_SoleBotVirtualPort = 0;
        m_ListenPort = 0;
        encoderTapVirtualPort = 0;
        encoderInjectFromPort = 0;
        encoderListenPort = 0;
        encoderSuppressSend = 0;
        encoderInjectWirePending = 0;
        encoderInjectWire = default;
        connectCaptureActive = 0;
        connectCaptureClientCount = 0;
        connectCaptureServerCount = 0;
        CancelConnectCapture();
    }

    [BurstDiscard]
    public void SetEncoderInjectWire(in BotRelayPacket packet)
    {
        CompleteManagerAccess();
        encoderInjectWire = packet;
        encoderInjectWirePending = 1;
    }

    [BurstDiscard]
    public void ClearEncoderInjectWire()
    {
        CompleteManagerAccess();
        encoderInjectWirePending = 0;
        encoderInjectWire = default;
    }

    [BurstDiscard]
    public void SetEncoderTap(ushort tapVirtualPort, ushort injectFromPort, ushort listenPort)
    {
        CompleteManagerAccess();
        encoderTapVirtualPort = tapVirtualPort;
        encoderInjectFromPort = injectFromPort;
        encoderListenPort = listenPort;
    }

    [BurstDiscard]
    public void SetEncoderSuppressSend(bool suppress)
    {
        CompleteManagerAccess();
        encoderSuppressSend = suppress ? (byte)1 : (byte)0;
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
    public void SetConnectionVirtualPortBurst(int connectionIndex, ushort virtualPort)
    {
        if (connectionIndex < 0 || virtualPort == 0 || !m_ConnectionIndexVirtualPorts.IsCreated)
            return;

        m_ConnectionIndexVirtualPorts[connectionIndex] = virtualPort;
    }

    public void EnsureVirtualPortBurst(ushort port) => EnsurePortChannelBurst(port);

    public bool IsListenPort(ushort port) => port == m_ListenPort;

    [BurstDiscard]
    public void EnqueueToListen(ushort fromVirtualPort, NativeArray<byte> payload)
    {
        if (!payload.IsCreated || payload.Length == 0 || m_ListenPort == 0)
            return;

        CompleteManagerAccess();
        EnqueueToListenBurst(fromVirtualPort, payload);
    }

    public void EnqueueToListenBurst(
        ushort fromVirtualPort,
        NativeArray<byte> payload,
        BotRelayListenInjectClass injectClass = BotRelayListenInjectClass.RelayOutbound)
    {
        if (!payload.IsCreated || payload.Length == 0 || m_ListenPort == 0)
            return;

        if (injectClass == BotRelayListenInjectClass.RelayOutbound)
        {
            var packet = default(BotRelayPacket);
            if (packet.TryWriteFrom(payload) &&
                BotRelayWireBytes.ShouldSkipListenInjectForPopEvents(in packet))
            {
                ++diagListenInjectSkippedPopEvents;
                return;
            }
        }

        if (!TryGetChannelByPort(m_ListenPort, out int listenChannel))
            return;

        EnqueuePacketBurst(listenChannel, fromVirtualPort, payload);
    }

    public void EnqueueToListenFromPacketBurst(
        ushort fromVirtualPort,
        in BotRelayPacket packet,
        BotRelayListenInjectClass injectClass = BotRelayListenInjectClass.RelayOutbound)
    {
        if (packet.IsEmpty || m_ListenPort == 0)
            return;

        if (injectClass == BotRelayListenInjectClass.RelayOutbound &&
            BotRelayWireBytes.ShouldSkipListenInjectForPopEvents(in packet))
        {
            ++diagListenInjectSkippedPopEvents;
            return;
        }

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
        {
            ++diagInboundSkippedEmpty;
            return true;
        }

        ++diagInboundDequeued;
        data.CopyTo(out packet);
        return true;
    }

    [BurstDiscard]
    public bool TryDequeueToBot(ushort virtualPort, out BotRelayPacket packet)
    {
        CompleteManagerAccess();
        return TryDequeueToBotBurst(virtualPort, out packet);
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
            out fullyConsumed,
            out int peelPath);

        int peeledRelayType = -1;
        int peeledAppLength = 0;
        if (peelSucceeded && batch.appCount > 0)
        {
            for (int i = 0; i < batch.appCount; ++i)
            {
                var app = batch.GetApp(i);
                if (BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire) &&
                    BotRelayInviteDecodeOps.TryFinalizeInviteAppForRoutePeel(
                        in app,
                        in wire,
                        ref catalog,
                        out var finalized) &&
                    !finalized.IsEmpty)
                {
                    app = finalized;
                }

                m_PeeledAppQueue.Enqueue(channel, app);
                ++diagInboundAppsEnqueued;
                if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int relayType))
                {
                    peeledRelayType = relayType;
                    peeledAppLength = app.length;
                    BotRelayAgentDiagnostics.LogRouteSendPeeled(relayType, wire.length, app.length);
                }
            }
        }

        if (packetData.length >= 80 && packetData.length <= 160)
        {
            BotRelayAgentDiagnostics.LogInviteShellRouteSend(
                packetData.fromPort,
                botVirtualPort,
                packetData.length,
                catalog.inboundAppPayloadOffset,
                catalog.inboundMappings.Length,
                BotRelayRoutePeelOps.DescribePeelPath(peelPath),
                peelSucceeded,
                fullyConsumed,
                peeledRelayType,
                peeledAppLength,
                in wire);
        }

        if (BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire) && botVirtualPort != 0)
            BotRelayInviteShellCacheStore.Store(in wire, botVirtualPort);

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
                __TryRecordConnectCaptureWire(true, destinationPort, in packetData);
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
                __TryRecordConnectCaptureWire(true, destinationPort, in packetData);
            else if (TryGetChannelByPort(m_ListenPort, out int listenChannel) && channel == listenChannel)
                __TryRecordConnectCaptureWire(false, packetData.fromPort, in packetData);
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
    public string BuildDiagnosticsSnapshot() =>
        BuildDiagnosticsSnapshotNoSync();

    [BurstDiscard]
    public string BuildDiagnosticsSnapshotNoSync()
    {
        ref var manager = ref Instance;
        return
            $"routeEnq={manager.diagRouteSendEnqueued}, joinInj={manager.diagJoinWireInjected}, " +
            $"matchInj={manager.diagMatchWireInjected}, mismatchInj={manager.diagMismatchWireInjected}, " +
            $"routeZero={manager.diagRouteSendZeroPort}, " +
            $"routeDrop={manager.diagRouteSendDropped}, sendPkts={manager.diagSendJobPackets}, " +
            $"inDeq={manager.diagInboundDequeued}, inApp={manager.diagInboundAppsEnqueued}, " +
            $"inUnmapped={manager.diagInboundUnmapped}, linkAck={manager.diagLinkAckSwallowed}, " +
            $"listenSkip={manager.diagListenInjectSkippedPopEvents}, " +
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

    private unsafe void EnqueuePacketBurst(int channel, ushort fromPort, NativeArray<byte> payload)
    {
        if (payload.Length > MaxPacketPayloadSize)
            return;

        var packetData = new PacketData
        {
            fromPort = fromPort,
            length = payload.Length
        };

        packetData.WriteFrom(payload);
        m_Queue.Enqueue(channel, packetData);
    }

    private void __EnqueuePacketData(int channel, in PacketData packetData)
    {
        if (packetData.length > 0 &&
            connectCaptureActive != 0 &&
            m_ListenPort != 0 &&
            TryGetChannelByPort(m_ListenPort, out int listenChannel) &&
            channel == listenChannel)
            __TryRecordConnectCaptureWire(false, packetData.fromPort, in packetData);

        m_Queue.Enqueue(channel, packetData);
    }

    private unsafe void __TryRecordConnectCaptureWire(bool serverSide, ushort port, in PacketData packetData)
    {
        if (connectCaptureActive == 0 || packetData.length <= 0)
            return;

        if (serverSide)
        {
            __TryRecordConnectCaptureWireSide(
                ref connectCaptureServerCount,
                connectCaptureServerPorts,
                connectCaptureServerLengths,
                connectCaptureServerBytes,
                port,
                in packetData);
            return;
        }

        __TryRecordConnectCaptureWireSide(
            ref connectCaptureClientCount,
            connectCaptureClientPorts,
            connectCaptureClientLengths,
            connectCaptureClientBytes,
            port,
            in packetData);
    }

    private unsafe void __TryRecordConnectCaptureWireSide(
        ref int count,
        NativeArray<ushort> ports,
        NativeArray<int> lengths,
        NativeArray<byte> bytes,
        ushort port,
        in PacketData packetData)
    {
        if (!ports.IsCreated || count >= ConnectCaptureCapacity)
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
        ports[count] = port;
        count++;
    }

    [BurstDiscard]
    internal void BeginConnectCapture(NativeArray<byte> connectPayload)
    {
        __ResetConnectCaptureState();
        connectCaptureActive = 1;

        if (connectCaptureConnectPayload.IsCreated)
            connectCaptureConnectPayload.Dispose();

        if (connectPayload.IsCreated && connectPayload.Length > 0)
        {
            connectCaptureConnectPayload = new NativeArray<byte>(connectPayload.Length, Allocator.Persistent);
            NativeArray<byte>.Copy(connectPayload, connectCaptureConnectPayload);
        }
        else
            connectCaptureConnectPayload = new NativeArray<byte>(0, Allocator.Persistent);
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

        if (connectCaptureConnectPayload.IsCreated && connectCaptureConnectPayload.Length > 0)
        {
            state.capturedConnectPayload =
                new NativeArray<byte>(connectCaptureConnectPayload.Length, Allocator.Persistent);
            NativeArray<byte>.Copy(connectCaptureConnectPayload, state.capturedConnectPayload);
        }

        connectCaptureClientCount = 0;
        connectCaptureServerCount = 0;

        if (connectCaptureConnectPayload.IsCreated)
            connectCaptureConnectPayload.Dispose();

        connectCaptureConnectPayload = new NativeArray<byte>(0, Allocator.Persistent);

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

        if (connectCaptureConnectPayload.IsCreated)
            connectCaptureConnectPayload.Dispose();

        connectCaptureConnectPayload = new NativeArray<byte>(0, Allocator.Persistent);
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
        var ports = serverSide ? connectCaptureServerPorts : connectCaptureClientPorts;
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
                fromVirtualPort = ports[i],
                wire = packet
            });
        }

        return list;
    }

    [BurstDiscard]
    public void EnqueueServerToBot(ushort botVirtualPort, ushort fromListenPort, NativeArray<byte> payload)
    {
        if (!payload.IsCreated || payload.Length == 0 || botVirtualPort == 0)
            return;

        CompleteManagerAccess();
        EnqueueServerToBotBurst(botVirtualPort, fromListenPort, payload);
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
    struct PendingPlayKey { }
    struct InLevelKey { }

    static readonly SharedStatic<int> s_PendingPlay =
        SharedStatic<int>.GetOrCreate<PendingPlayKey>();

    static readonly SharedStatic<int> s_InLevel =
        SharedStatic<int>.GetOrCreate<InLevelKey>();

    public static bool HasPendingPlay => s_PendingPlay.Data > 0;

    public static bool HasInLevel => s_InLevel.Data > 0;

    public static void AddPendingPlay() => ++s_PendingPlay.Data;

    public static void RemovePendingPlay()
    {
        int value = s_PendingPlay.Data - 1;
        s_PendingPlay.Data = value < 0 ? 0 : value;
    }

    public static void AddInLevel() => ++s_InLevel.Data;

    public static void RemoveInLevel()
    {
        int value = s_InLevel.Data - 1;
        s_InLevel.Data = value < 0 ? 0 : value;
    }

    public static void Reset()
    {
        s_PendingPlay.Data = 0;
        s_InLevel.Data = 0;
    }
}
