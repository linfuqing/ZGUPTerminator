using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;
using ZG;

/// <summary>
/// Init-only UTP Connect/link capture plus current server-to-bot inbound calibration.
/// Bot business messages never use these wires; they are injected through the server API.
/// </summary>
public struct BotRelayWireCatalogState : System.IDisposable
{
    public BotRelayConnectWireState connectWire;
    public int connectClientWireCount;
    public BotRelayPacket serverHelloWire;
    public int inboundAppPayloadOffset;
    public NativeList<BotRelayPacket> linkAckTemplates;

    public bool IsValid => connectWire.IsValid;

    public void Dispose()
    {
        connectWire.Dispose();

        if (linkAckTemplates.IsCreated)
            linkAckTemplates.Dispose();

        serverHelloWire = default;
        connectClientWireCount = 0;
        inboundAppPayloadOffset = -1;
    }
}

internal static class BotRelayWireCatalog
{
    public static BotRelayWireCatalogState Capture(
        ushort listenPort,
        in ClientHeader header)
    {
        var catalog = default(BotRelayWireCatalogState);
        catalog.inboundAppPayloadOffset = -1;

        if (!__TryCaptureFullSession(listenPort, in header, ref catalog))
        {
            catalog.Dispose();
            return default;
        }

        Debug.Log(
            $"[BotRelay] WireCatalog ready: connect={catalog.connectWire.clientPackets.Length}, " +
            $"connectInject={catalog.connectClientWireCount}, " +
            $"serverHello={!catalog.serverHelloWire.IsEmpty}, " +
            $"inboundAppOffset={catalog.inboundAppPayloadOffset}, linkAcks={(catalog.linkAckTemplates.IsCreated ? catalog.linkAckTemplates.Length : 0)}, " +
            $"connectHex={__HexPreview(catalog.connectWire.clientPackets[0].wire)}, " +
            $"helloHex={__HexPreview(catalog.serverHelloWire)}.");
        return catalog;
    }

    /// <summary>
    /// Drain server→bot link-layer packets (heartbeat/ACK) and inject captured ACK templates back to Listen.
    /// </summary>
    private static void __CaptureLinkAckTemplates(ref BotRelayWireCatalogState catalog, int firstLinkIndex)
    {
        catalog.linkAckTemplates = new NativeList<BotRelayPacket>(8, Allocator.Persistent);

        if (!catalog.connectWire.clientPackets.IsCreated)
            return;

        int start = firstLinkIndex;
        for (int i = start; i < catalog.connectWire.clientPackets.Length; ++i)
        {
            var wire = catalog.connectWire.clientPackets[i].wire;
            if (!__LooksLikeLinkAckCandidate(in wire))
                continue;

            if (__ContainsLinkAck(ref catalog, in wire))
                continue;

            catalog.linkAckTemplates.Add(wire);
        }
    }

    private static bool __LooksLikeLinkAckCandidate(in BotRelayPacket wire)
        => !wire.IsEmpty &&
           wire.length <= 64 &&
           !__LooksLikeConnectHandshake(in wire);

    private static bool __ContainsLinkAck(ref BotRelayWireCatalogState catalog, in BotRelayPacket wire)
    {
        if (!catalog.linkAckTemplates.IsCreated)
            return false;

        for (int i = 0; i < catalog.linkAckTemplates.Length; ++i)
        {
            var template = catalog.linkAckTemplates[i];
            if (__WireEquals(in template, in wire))
                return true;
        }

        return false;
    }

    private static NetworkSettings __CreateCaptureNetworkSettings()
    {
        var settings = new NetworkSettings(Allocator.Temp);
        settings.WithNetworkConfigParameters(
            1000,
            60,
            30 * 1000,
            500,
            2000,
            Mathf.CeilToInt(Time.maximumDeltaTime * 1000),
            0,
            4096,
            4096);
        return settings;
    }

    private static bool __TryCaptureFullSession(
        ushort restoreListenPort,
        in ClientHeader header,
        ref BotRelayWireCatalogState catalog)
    {
        using var captureSettings = __CreateCaptureNetworkSettings();
        var listenDriver = default(NetworkDriver);
        var clientDriver = default(NetworkDriver);
        var connection = default(NetworkConnection);
        var listenConnection = default(NetworkConnection);

        try
        {
            return __TryCaptureFullSessionCore(
                captureSettings,
                in header,
                ref catalog,
                ref listenDriver,
                ref clientDriver,
                ref connection,
                ref listenConnection);
        }
        finally
        {
            BotRelayManager.ManagerAccessHandle.Complete();
            __DisposeCapturePair(ref listenDriver, ref clientDriver, ref listenConnection, connection);
            BotRelayManager.Instance.RegisterListenPort(restoreListenPort);
        }
    }

    private static bool __TryCaptureFullSessionCore(
        NetworkSettings captureSettings,
        in ClientHeader header,
        ref BotRelayWireCatalogState catalog,
        ref NetworkDriver listenDriver,
        ref NetworkDriver clientDriver,
        ref NetworkConnection connection,
        ref NetworkConnection listenConnection)
    {
        BotRelayDriverScheduleUtil.BeginCaptureSession();

        var listenInterface = new BotRelayNetworkInterface();
        listenDriver = NetworkDriver.Create(ref listenInterface, captureSettings);

        var ephemeralListenPort = BotRelayManager.Instance.AllocateVirtualPort();
        if (listenDriver.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(ephemeralListenPort)) != 0 ||
            listenDriver.Listen() != 0)
            return false;

        BotRelayManager.Instance.RegisterListenPort(ephemeralListenPort);

        var clientInterface = new BotRelayNetworkInterface();
        clientDriver = NetworkDriver.Create(ref clientInterface, captureSettings);

        if (clientDriver.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(0)) != 0)
            return false;

        using var connectPayload = BotRelayCodec.BuildConnectPayload(in header);
        BotRelayManager.ManagerAccessHandle.Complete();
        BotRelayConnectCapture.Begin();

        var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(ephemeralListenPort);
        connection = clientDriver.Connect(endpoint, connectPayload);
        if (connection == default)
        {
            BotRelayConnectCapture.Cancel();
            return false;
        }

        bool handshakeOk = false;
        for (int i = 0; i < 24; ++i)
        {
            __ScheduleCapturePair(ref listenDriver, ref clientDriver);

            var cmd = clientDriver.PopEventForConnection(connection, out _, out _);
            if (cmd == NetworkEvent.Type.Connect)
            {
                handshakeOk = true;
                __RecordServerHelloFromCapture(ref catalog);
                __DrainCaptureEvents(
                    ref listenDriver, ref clientDriver, ref listenConnection, connection);
                break;
            }

            __DrainCaptureEvents(
                ref listenDriver, ref clientDriver, ref listenConnection, connection);
        }

        if (!handshakeOk)
        {
            BotRelayConnectCapture.Cancel();
            Debug.LogError("[BotRelay] WireCatalog: handshake failed.");
            return false;
        }

        catalog.connectClientWireCount = BotRelayConnectCapture.ClientPacketCount;
        int firstLinkAckWire = catalog.connectClientWireCount;

        if (!__TryCaptureInviteInboundWire(
                ref listenDriver,
                ref clientDriver,
                ref listenConnection,
                connection,
                BotRelayPipeline.Null,
                in header,
                ref catalog))
        {
            BotRelayConnectCapture.Cancel();
            Debug.LogError("[BotRelay] WireCatalog: current Invite inbound capture failed.");
            return false;
        }

        for (int i = 0; i < 96; ++i)
        {
            __ScheduleCapturePair(ref listenDriver, ref clientDriver);
            __DrainCaptureEvents(ref listenDriver, ref clientDriver, ref listenConnection, connection);
        }

        int clientCaptureCount = BotRelayConnectCapture.ClientPacketCount;
        int serverCaptureCount = BotRelayConnectCapture.ServerPacketCount;
        catalog.connectWire = BotRelayConnectCapture.End();
        __CaptureLinkAckTemplates(ref catalog, firstLinkAckWire);

        if (!catalog.connectWire.IsValid)
        {
            Debug.LogError(
                $"[BotRelay] WireCatalog: connect wire empty after handshake " +
                $"(clientCapture={clientCaptureCount}, serverCapture={serverCaptureCount}).");
            return false;
        }

        if (catalog.serverHelloWire.IsEmpty)
        {
            Debug.LogError("[BotRelay] WireCatalog: current server hello was not captured.");
            return false;
        }

        return true;
    }

    private static bool __TryCaptureInviteInboundWire(
        ref NetworkDriver listenDriver,
        ref NetworkDriver clientDriver,
        ref NetworkConnection listenConnection,
        NetworkConnection connection,
        NetworkPipeline pipeline,
        in ClientHeader header,
        ref BotRelayWireCatalogState catalog)
    {
        if (!BotRelayCodec.TryBuildWorldInviteRelayApp(in header, 1, 0, 0, out var inviteApp))
        {
            Debug.LogWarning("[BotRelay] WireCatalog: TryBuildWorldInviteRelayApp failed.");
            return false;
        }

        int serverWireBefore = BotRelayConnectCapture.ServerPacketCount;

        if (__TrySendServerFramedApp(
                ref listenDriver,
                ref clientDriver,
                ref listenConnection,
                connection,
                pipeline,
                in inviteApp))
        {
            for (int i = 0; i < 16; ++i)
            {
                __ScheduleCapturePair(ref listenDriver, ref clientDriver);
                __DrainCaptureEvents(ref listenDriver, ref clientDriver, ref listenConnection, connection);
            }

        }
        else
        {
            Debug.LogWarning("[BotRelay] WireCatalog: invite inbound live capture send failed.");
            return false;
        }

        // Correlate once at capture time by the complete current frame. Only the measured
        // scalar offset survives; the synthetic Invite body and raw business wire do not.
        if (!__TryCalibrateInboundAppOffsetFromCapture(
                ref catalog,
                serverWireBefore,
                in inviteApp))
        {
            Debug.LogWarning("[BotRelay] WireCatalog: invite inbound wire not recorded.");
            return false;
        }

        return catalog.inboundAppPayloadOffset >= sizeof(ushort);
    }

    private static bool __TryCalibrateInboundAppOffsetFromCapture(
        ref BotRelayWireCatalogState catalog,
        int serverWireBefore,
        in BotRelayPacket inviteApp)
    {
        int serverCount = BotRelayConnectCapture.ServerPacketCount;
        for (int wireIndex = serverWireBefore; wireIndex < serverCount; ++wireIndex)
        {
            if (!BotRelayConnectCapture.TryCopyServerWire(wireIndex, out var wirePacket) || wirePacket.IsEmpty)
            {
                continue;
            }

            if (!BotRelayWireBytes.TryResolveInboundAppPayloadOffset(
                    in wirePacket,
                    in inviteApp,
                    out int offset))
            {
                continue;
            }

            catalog.inboundAppPayloadOffset = offset;
            return true;
        }

        return false;
    }

    private static bool __TrySendServerFramedApp(
        ref NetworkDriver listenDriver,
        ref NetworkDriver clientDriver,
        ref NetworkConnection listenConnection,
        NetworkConnection connection,
        NetworkPipeline pipeline,
        in BotRelayPacket appTemplate)
    {
        using var appBytes = new NativeArray<byte>(appTemplate.length, Allocator.Temp);
        if (!appTemplate.TryCopyTo(appBytes))
            return false;

        using var framed = BotRelayCodec.FrameForTransport(appBytes);
        if (!framed.IsCreated)
            return false;

        BotRelayDriverScheduleUtil.EnsureCaptureIdle();
        __EnsureListenConnection(ref listenDriver, ref listenConnection);
        if (!listenConnection.IsCreated)
            return false;

        if (listenDriver.BeginSend(pipeline, listenConnection, out var writer) < 0)
            return false;

        writer.WriteBytes(framed);
        listenDriver.EndSend(writer);
        return true;
    }

    private static void __RecordServerHelloFromCapture(ref BotRelayWireCatalogState catalog)
    {
        if (!BotRelayConnectCapture.TryCopyServerWire(0, out var helloWire) || helloWire.IsEmpty)
            return;

        catalog.serverHelloWire = helloWire;
    }

    private static void __ScheduleCapturePair(ref NetworkDriver listenDriver, ref NetworkDriver clientDriver) =>
        BotRelayDriverScheduleUtil.StepCapturePair(ref listenDriver, ref clientDriver);

    private static void __EnsureListenConnection(ref NetworkDriver listenDriver, ref NetworkConnection listenConnection)
    {
        if (listenConnection.IsCreated || !listenDriver.IsCreated)
            return;

        var accepted = listenDriver.Accept();
        if (accepted != default)
            listenConnection = accepted;
    }

    private static void __DrainConnectionEvents(ref NetworkDriver driver, NetworkConnection connection)
    {
        if (!driver.IsCreated || !connection.IsCreated)
            return;

        while (driver.PopEventForConnection(connection, out _, out _) != NetworkEvent.Type.Empty)
        { }
    }

    private static void __DrainCaptureEvents(
        ref NetworkDriver listenDriver,
        ref NetworkDriver clientDriver,
        ref NetworkConnection listenConnection,
        NetworkConnection clientConnection)
    {
        __EnsureListenConnection(ref listenDriver, ref listenConnection);
        __DrainConnectionEvents(ref listenDriver, listenConnection);
        __DrainConnectionEvents(ref clientDriver, clientConnection);
    }

    private static void __DisposeCapturePair(
        ref NetworkDriver listenDriver,
        ref NetworkDriver clientDriver,
        ref NetworkConnection listenConnection,
        NetworkConnection clientConnection)
    {
        BotRelayDriverScheduleUtil.CompleteCaptureSession();

        __DrainCaptureEvents(ref listenDriver, ref clientDriver, ref listenConnection, clientConnection);

        if (listenDriver.IsCreated)
        {
            while (listenDriver.PopEvent(out _, out _) != NetworkEvent.Type.Empty)
            { }
        }

        if (clientDriver.IsCreated)
        {
            while (clientDriver.PopEvent(out _, out _) != NetworkEvent.Type.Empty)
            { }
        }

        if (clientDriver.IsCreated)
            clientDriver.Dispose();
        clientDriver = default;

        if (listenDriver.IsCreated)
            listenDriver.Dispose();
        listenDriver = default;
        listenConnection = default;
    }

    private static bool __WireEquals(in BotRelayPacket left, in BotRelayPacket right)
    {
        if (left.length != right.length)
            return false;

        if (left.length <= 0)
            return true;

        unsafe
        {
            fixed (byte* l = left.data)
            fixed (byte* r = right.data)
            {
                for (int i = 0; i < left.length; ++i)
                {
                    if (l[i] != r[i])
                        return false;
                }
            }
        }

        return true;
    }

    private static bool __LooksLikeConnectHandshake(in BotRelayPacket wire)
    {
        if (wire.length < 5)
            return false;

        unsafe
        {
            fixed (byte* p = wire.data)
                return p[0] == (byte)'U' && p[1] == (byte)'T' && p[2] == (byte)'P' &&
                       p[3] == 0x01 && (p[4] == 0x01 || p[4] == 0x02);
        }
    }

    private static string __HexPreview(in BotRelayPacket packet, int maxBytes = 16)
    {
        if (packet.IsEmpty)
            return "-";

        int n = packet.length < maxBytes ? packet.length : maxBytes;
        unsafe
        {
            fixed (byte* p = packet.data)
            {
                var chars = new char[n * 3];
                int w = 0;
                for (int i = 0; i < n; ++i)
                {
                    byte b = p[i];
                    chars[w++] = "0123456789ABCDEF"[b >> 4];
                    chars[w++] = "0123456789ABCDEF"[b & 0xF];
                    chars[w++] = i + 1 < n ? ' ' : '\0';
                }

                return new string(chars, 0, w > 0 && chars[w - 1] == '\0' ? w - 1 : w);
            }
        }
    }
}
