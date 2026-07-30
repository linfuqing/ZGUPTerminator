using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;
using ZG;

public struct BotRelayConnectWirePacket
{
    public BotRelayPacket wire;
}

public struct BotRelayConnectWireState : System.IDisposable
{
    public NativeList<BotRelayConnectWirePacket> clientPackets;
    public NativeList<BotRelayConnectWirePacket> serverPackets;

    public bool IsValid => clientPackets.IsCreated && clientPackets.Length > 0;

    public void Dispose()
    {
        if (clientPackets.IsCreated)
            clientPackets.Dispose();

        if (serverPackets.IsCreated)
            serverPackets.Dispose();

    }
}

internal static class BotRelayConnectWire
{
    public static BotRelayConnectWireState CaptureConnectWire(
        ref NetworkSettings settings,
        ushort listenPort,
        in ClientHeader header)
    {
        using var connectPayload = BotRelayCodec.BuildConnectPayload(in header);
        BotRelayConnectCapture.Begin();

        var listenDriver = default(NetworkDriver);
        var clientDriver = default(NetworkDriver);
        var connection = default(NetworkConnection);

        try
        {
            BotRelayDriverScheduleUtil.BeginCaptureSession();

            var listenInterface = new BotRelayNetworkInterface();
            listenDriver = NetworkDriver.Create(ref listenInterface, settings);

            var ephemeralListenPort = BotRelayManager.Instance.AllocateVirtualPort();
            if (listenDriver.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(ephemeralListenPort)) != 0 ||
                listenDriver.Listen() != 0)
            {
                BotRelayConnectCapture.Cancel();
                Debug.LogError("[BotRelay] Connect capture: failed to bind/listen ephemeral driver.");
                return default;
            }

            BotRelayManager.Instance.RegisterListenPort(ephemeralListenPort);

            var clientInterface = new BotRelayNetworkInterface();
            clientDriver = NetworkDriver.Create(ref clientInterface, settings);

            if (clientDriver.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(0)) != 0)
            {
                BotRelayConnectCapture.Cancel();
                Debug.LogError("[BotRelay] Connect capture: failed to bind client driver.");
                return default;
            }

            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(ephemeralListenPort);
            connection = clientDriver.Connect(endpoint, connectPayload);
            if (connection == default)
            {
                BotRelayConnectCapture.Cancel();
                Debug.LogError("[BotRelay] Connect capture: Connect returned default.");
                return default;
            }

            int lastClientCount = 0;
            int lastServerCount = 0;
            for (int i = 0; i < 16; ++i)
            {
                BotRelayDriverScheduleUtil.StepCapturePair(ref listenDriver, ref clientDriver);

                var accepted = listenDriver.Accept();
                if (accepted != default)
                {
                    while (listenDriver.PopEventForConnection(accepted, out _, out _) != NetworkEvent.Type.Empty)
                    { }
                }

                while (clientDriver.PopEventForConnection(connection, out _, out _) != NetworkEvent.Type.Empty)
                { }

                int clientCount = BotRelayConnectCapture.ClientPacketCount;
                int serverCount = BotRelayConnectCapture.ServerPacketCount;
                if (clientCount > 0 && clientCount == lastClientCount &&
                    serverCount > 0 && serverCount == lastServerCount)
                    break;

                lastClientCount = clientCount;
                lastServerCount = serverCount;
            }

            return BotRelayConnectCapture.End();
        }
        finally
        {
            BotRelayDriverScheduleUtil.CompleteCaptureSession();

            if (clientDriver.IsCreated)
            {
                while (clientDriver.PopEvent(out _, out _) != NetworkEvent.Type.Empty)
                { }
                clientDriver.Dispose();
            }

            if (listenDriver.IsCreated)
            {
                while (listenDriver.PopEvent(out _, out _) != NetworkEvent.Type.Empty)
                { }
                listenDriver.Dispose();
            }

            BotRelayManager.Instance.RegisterListenPort(listenPort);
        }
    }
}
