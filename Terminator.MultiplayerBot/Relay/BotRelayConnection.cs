using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using ZG;

public struct BotRelayConnection
{
    private NetworkSendBuffer __sendBuffer;
    private BotRelayCodec.NetworkSendBufferWrapper __sendBufferWrapper;
    private ClientHeader __header;
    private NetworkConnection __connection;
    private ushort __listenPort;
    private bool __isCreated;
    private bool __isConnected;
    private bool __connectPending;
    private bool __connectRequested;
    private bool __sentInitialStatus;
    private bool __sendBufferDisposed;
    private NativeQueue<BotRelayPacket> __inboundTransport;

    public ClientHeader Header => __header;
    public bool IsCreated => __isCreated;
    public bool IsConnected => __isConnected;

    public static BotRelayConnection Create(in ClientHeader header)
    {
        var connection = new BotRelayConnection
        {
            __header = header,
            __sendBuffer = new NetworkSendBuffer(Allocator.Persistent),
            __inboundTransport = new NativeQueue<BotRelayPacket>(Allocator.Persistent),
            __isCreated = true
        };
        connection.__sendBufferWrapper = new BotRelayCodec.NetworkSendBufferWrapper { buffer = connection.__sendBuffer };
        return connection;
    }

    public bool PrepareConnect(ushort listenPort, in ClientHeader header)
    {
        if (!__isCreated)
            return false;

        __header = header;
        __listenPort = listenPort;
        __connectPending = true;
        return true;
    }

    public bool Connect(ref NetworkDriver driver, ushort listenPort, in ClientHeader header)
    {
        if (!__isCreated || !driver.IsCreated)
            return false;

        __header = header;
        __listenPort = listenPort;
        return TryEstablishConnection(ref driver);
    }

    private bool TryEstablishConnection(ref NetworkDriver driver)
    {
        if (!__connectPending || __connectRequested || __isConnected || !driver.IsCreated)
            return __isConnected;

        using var payload = BotRelayCodec.BuildConnectPayload(in __header);
        var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(__listenPort);
        __connection = driver.Connect(endpoint, payload);
        if (__connection == default)
            return false;

        __connectPending = false;
        __connectRequested = true;
        UnityEngine.Debug.Log($"[BotRelay] Bot {__header.userID} connecting to port {__listenPort}.");
        return true;
    }

    public void PreTickFlushBurst(ref NetworkDriver driver, NetworkPipeline pipeline)
    {
        if (!__isCreated || !driver.IsCreated)
            return;

        if (__connectPending || __connectRequested)
            TryEstablishConnection(ref driver);

        if (!__isConnected || __connection == default)
            return;

        int readIndex = 0;
        while (__sendBufferWrapper.TryReadNext(ref readIndex, out var bytes))
        {
            if (!bytes.IsCreated || bytes.Length == 0)
                continue;

            using var framed = BotRelayCodec.FrameForTransport(bytes);
            if (!framed.IsCreated)
                continue;

            if (driver.BeginSend(pipeline, __connection, out var writer) < 0)
                continue;

            writer.WriteBytes(framed);
            driver.EndSend(writer);
        }

        __sendBufferWrapper.ClearPending();
    }

    public void PostTickReceiveBurst(ref NetworkDriver driver)
    {
        if (!__isCreated || !driver.IsCreated)
            return;

        if (__connection == default)
        {
            if (__connectPending || __connectRequested)
                TryEstablishConnection(ref driver);

            return;
        }

        NetworkEvent.Type cmd;
        DataStreamReader stream;
        NetworkPipeline pipeline;
        while (true)
        {
            cmd = driver.PopEventForConnection(__connection, out stream, out pipeline);
            if (cmd == NetworkEvent.Type.Empty)
                break;

            switch (cmd)
            {
                case NetworkEvent.Type.Connect:
                    __isConnected = true;
                    UnityEngine.Debug.Log($"[BotRelay] Bot {__header.userID} transport connected.");
                    if (!__sentInitialStatus)
                    {
                        TryWriteStatus(0);
                        __sentInitialStatus = true;
                    }
                    break;
                case NetworkEvent.Type.Data:
                    if (!__isConnected)
                    {
                        __isConnected = true;
                        if (!__sentInitialStatus)
                        {
                            TryWriteStatus(0);
                            __sentInitialStatus = true;
                        }
                    }

                    EnqueueTransportPayload(ref stream);
                    break;
                case NetworkEvent.Type.Disconnect:
                    __isConnected = false;
                    break;
            }
        }
    }

    public bool TryDequeueInbound(out BotRelayPacket packet)
    {
        if (!__inboundTransport.IsCreated || __inboundTransport.Count == 0)
        {
            packet = default;
            return false;
        }

        packet = __inboundTransport.Dequeue();
        return true;
    }

    public void Dispose() => Dispose(skipDriverDisconnect: false);

    public void Dispose(bool skipDriverDisconnect)
    {
        if (!__isCreated)
            return;

        __isCreated = false;

        if (__inboundTransport.IsCreated)
            __inboundTransport.Dispose();

        if (!__sendBufferDisposed)
        {
            __sendBuffer.Dispose();
            __sendBufferDisposed = true;
            __sendBufferWrapper = default;
        }

        __connection = default;
        __isConnected = false;
        __connectPending = false;
        __connectRequested = false;
    }

    public void TryDisconnect(ref NetworkDriver driver)
    {
        if (__connection == default || !driver.IsCreated)
            return;

        driver.Disconnect(__connection);
        __connection = default;
        __isConnected = false;
    }

    public bool TryWriteMatchToSend(int level) =>
        BotRelayCodec.TryWriteMatchToSend(ref __sendBufferWrapper, level);

    public bool TryWriteSquadJoin(uint squadInviteID) =>
        BotRelayCodec.TryWriteSquadJoin(ref __sendBufferWrapper, squadInviteID);

    public bool TryWriteApplyMatch() =>
        BotRelayCodec.TryWriteApplyMatch(ref __sendBufferWrapper);

    public bool TryWriteSquadLeave() =>
        BotRelayCodec.TryWriteSquadLeave(ref __sendBufferWrapper);

    public bool TryWriteStatus(int channelStatus) =>
        BotRelayCodec.TryWriteStatus(ref __sendBufferWrapper, channelStatus);

    public bool TryWriteRelayPayload(byte[] payload) =>
        BotRelayCodec.TryWriteRelayPayload(ref __sendBufferWrapper, payload);

    public bool TryWriteRelayPayload(NativeArray<byte> payload) =>
        BotRelayCodec.TryWriteRelayPayload(ref __sendBufferWrapper, payload);

    public bool TryWritePlayerPropertyBody(byte[] recordedPayload) =>
        BotRelayCodec.TryWritePlayerPropertyBody(ref __sendBufferWrapper, recordedPayload);

    private void EnqueueTransportPayload(ref DataStreamReader stream)
    {
        while (stream.GetBytesRead() < stream.Length)
        {
            int frameStart = stream.GetBytesRead();
            int remaining = stream.Length - frameStart;
            if (remaining <= 0)
                break;

            if (remaining < sizeof(ushort))
            {
                EnqueueInnerBytes(ref stream, remaining);
                break;
            }

            ushort size = stream.ReadUShort();
            if (size == 0 || size > stream.Length - stream.GetBytesRead())
            {
                stream.SeekSet(frameStart);
                EnqueueInnerBytes(ref stream, stream.Length - frameStart);
                break;
            }

            EnqueueInnerBytes(ref stream, size);
        }
    }

    private void EnqueueInnerBytes(ref DataStreamReader stream, int length)
    {
        if (length <= 0)
            return;

        if (length > BotRelayPacket.MaxPayloadSize)
            length = BotRelayPacket.MaxPayloadSize;

        var temp = new NativeArray<byte>(length, Allocator.Temp);
        stream.ReadBytes(temp);

        var packet = default(BotRelayPacket);
        if (!packet.TryWriteFrom(temp))
        {
            temp.Dispose();
            return;
        }

        temp.Dispose();
        __inboundTransport.Enqueue(packet);
    }
}
