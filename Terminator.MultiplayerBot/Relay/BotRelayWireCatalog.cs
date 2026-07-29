using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Networking.Transport;
using UnityEngine;
using ZG;

public struct BotRelayInboundWireMapping
{
    public BotRelayPacket wire;
    public BotRelayPacket appFrame;
}

/// <summary>
/// Init-only captured UTP wire templates + runtime Manager bridge (single Listen Driver path).
/// All templates originate from one ephemeral UTP session (connect → status → match).
/// </summary>
public struct BotRelayWireCatalogState : IComponentData, System.IDisposable
{
    public BotRelayConnectWireState connectWire;
    public int connectClientWireCount;
    public BotRelayPacket serverHelloWire;
    public BotRelayPacket statusOutboundWire;
    public BotRelayPacket matchOutboundWire;
    public BotRelayPacket statusAppTemplate;
    public BotRelayPacket matchAppTemplate;
    public BotRelayPacket squadJoinOutboundWire;
    public BotRelayPacket squadJoinAppTemplate;
    public BotRelayPacket applyMatchOutboundWire;
    public BotRelayPacket applyMatchAppTemplate;
    public BotRelayPacket squadLeaveOutboundWire;
    public BotRelayPacket squadLeaveAppTemplate;
    public int squadJoinPayloadOffset;
    public int statusOutboundPayloadOffset;
    public int matchOutboundPayloadOffset;
    public int inboundAppPayloadOffset;
    public NativeList<BotRelayInboundWireMapping> inboundMappings;
    public NativeList<BotRelayPacket> linkAckTemplates;

    public bool IsValid => connectWire.IsValid;

    public void Dispose()
    {
        connectWire.Dispose();

        if (inboundMappings.IsCreated)
            inboundMappings.Dispose();

        if (linkAckTemplates.IsCreated)
            linkAckTemplates.Dispose();

        serverHelloWire = default;
        connectClientWireCount = 0;
        statusOutboundWire = default;
        matchOutboundWire = default;
        statusAppTemplate = default;
        matchAppTemplate = default;
        squadJoinOutboundWire = default;
        squadJoinAppTemplate = default;
        applyMatchOutboundWire = default;
        applyMatchAppTemplate = default;
        squadLeaveOutboundWire = default;
        squadLeaveAppTemplate = default;
        squadJoinPayloadOffset = -1;
        statusOutboundPayloadOffset = -1;
        matchOutboundPayloadOffset = -1;
        inboundAppPayloadOffset = -1;
    }
}

internal static class BotRelayWireCatalog
{
    public static BotRelayWireCatalogState Capture(
        ref NetworkSettings settings,
        ushort listenPort,
        in ClientHeader header,
        int matchLevel)
    {
        var catalog = default(BotRelayWireCatalogState);
        catalog.inboundMappings = new NativeList<BotRelayInboundWireMapping>(8, Allocator.Persistent);

        if (!__TryCaptureFullSession(ref settings, listenPort, in header, matchLevel, ref catalog))
        {
            catalog.Dispose();
            return default;
        }

        Debug.Log(
            $"[BotRelay] WireCatalog ready: connect={catalog.connectWire.clientPackets.Length}, " +
            $"connectInject={catalog.connectClientWireCount}, " +
            $"serverHello={!catalog.serverHelloWire.IsEmpty}, " +
            $"statusWire={!catalog.statusOutboundWire.IsEmpty}, matchWire={!catalog.matchOutboundWire.IsEmpty}, " +
            $"joinWire={!catalog.squadJoinOutboundWire.IsEmpty}, applyWire={!catalog.applyMatchOutboundWire.IsEmpty}, " +
            $"leaveWire={!catalog.squadLeaveOutboundWire.IsEmpty}, " +
            $"inboundMaps={catalog.inboundMappings.Length}, inboundAppOffset={catalog.inboundAppPayloadOffset}, linkAcks={(catalog.linkAckTemplates.IsCreated ? catalog.linkAckTemplates.Length : 0)}, " +
            $"inviteMap={__DescribeInviteInboundMapping(catalog)}, " +
            $"connectHex={__HexPreview(catalog.connectWire.clientPackets[0].wire)}, " +
            $"statusHex={__HexPreview(catalog.statusOutboundWire)}, " +
            $"helloHex={__HexPreview(catalog.serverHelloWire)}.");
        return catalog;
    }

    public static void MaybeQueueInitialStatus(int index, ref BotRelayFarmNative farm)
    {
        if ((farm.transport[index].flags & BotRelayTransportFlags.Connected) == 0)
            return;

        if (farm.wireLiveServerHello[index].IsEmpty)
            return;

        if (farm.inboxSentInitialStatus[index] != 0)
            return;

        BotRelaySlotOps.TryWriteInitialStatus(index, ref farm);
    }

    public static bool RequestConnect(
        int index,
        ref BotRelayFarmNative farm,
        in BotRelayWireCatalogState catalog,
        in ClientHeader header)
    {
        ref var state = ref farm.transport.ElementAt(index);
        if ((state.flags & BotRelayTransportFlags.ConnectPending) == 0)
            return false;

        if (!catalog.IsValid)
        {
            Debug.LogError($"[BotRelay] Bot {header.userID}: wire catalog is empty.");
            return false;
        }

        ushort virtualPort = BotRelayManager.Instance.AllocateVirtualPort();
        state.virtualPort = virtualPort;

        int injectCount = catalog.connectClientWireCount > 0
            ? catalog.connectClientWireCount
            : 1;
        injectCount = math.min(injectCount, catalog.connectWire.clientPackets.Length);

        for (int i = 0; i < injectCount; ++i)
        {
            var entry = catalog.connectWire.clientPackets[i];
            if (entry.wire.IsEmpty)
                continue;

            BotRelayManager.Instance.EnqueueToListenFromPacketBurst(
                virtualPort,
                in entry.wire,
                BotRelayListenInjectClass.ConnectHandshake);
        }

        state.flags &= ~BotRelayTransportFlags.ConnectPending;
        state.flags |= BotRelayTransportFlags.ConnectRequested;
        state.flags &= ~BotRelayTransportFlags.Connected;
        farm.wireLiveServerHello[index] = default;
        Debug.Log($"[BotRelay] Bot {header.userID} injected connect on vport {virtualPort}.");
        return true;
    }

    public static void FlushOutbound(int index, ref BotRelayFarmNative farm, in BotRelayWireCatalogState catalog)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if (transport.virtualPort == 0)
            return;

        if ((transport.flags & BotRelayTransportFlags.Connected) == 0)
            return;

        var liveHello = farm.wireLiveServerHello[index];
        if (liveHello.IsEmpty || catalog.serverHelloWire.IsEmpty)
            return;

        if (!catalog.connectWire.clientPackets.IsCreated || catalog.connectWire.clientPackets.Length == 0)
            return;

        var connectTemplate = catalog.connectWire.clientPackets[0].wire;

        int baseIndex = index * BotRelayFarmNative.MaxOutboundPerAgent;
        int count = farm.outboundCount[index];
        if (count <= 0)
            return;

        bool sentAny = false;
        int sentIndex = -1;
        for (int i = 0; i < count; ++i)
        {
            var appPacket = farm.outbound[baseIndex + i];
            if (appPacket.IsEmpty)
                continue;

            if (!__TryResolveOutboundWire(in appPacket, in catalog, out var wireTemplate, out var wireLabel))
                continue;

            int scratchBase = index * BotRelayPacket.MaxPayloadSize;
            int scratchFrameBase = scratchBase + BotRelayPacket.MaxPayloadSize / 2;

            var wirePacket = wireTemplate;
            if (wireLabel != "Join" && __LooksLikeConnectHandshake(in wirePacket) &&
                __IsConnectClientCapturedWire(in catalog, in wirePacket) &&
                !__IsCatalogOutboundTemplateWire(in catalog, in wirePacket))
            {
                Debug.LogError(
                    $"[BotRelay] Bot {transport.header.userID} refused outbound wire: " +
                    "payload is unmodified UTP Connect client capture.");
                continue;
            }

            int embedPayloadOffset = -1;
            if (wireLabel == "Join")
            {
                embedPayloadOffset = catalog.squadJoinPayloadOffset;
            }
            else if (wireLabel == "Match")
            {
                embedPayloadOffset = catalog.matchOutboundPayloadOffset;
            }
            else if (wireLabel == "Mismatch")
            {
                embedPayloadOffset = catalog.statusOutboundPayloadOffset;
            }

            if ((wireLabel == "Join" || wireLabel == "Match" || wireLabel == "Mismatch") &&
                embedPayloadOffset < 0)
            {
                continue;
            }

            if (BotRelayWireBytes.ShouldComposePipelineListenUtpForOutboundFlush(in wirePacket) &&
                __TryFindLongestUtPConnectWire(in catalog, out var connectWire) &&
                !BotRelayWireBytes.TryComposePipelineListenUtpWireWithConnect(
                    in connectWire,
                    ref wirePacket,
                    ref embedPayloadOffset))
            {
                Debug.LogError(
                    $"[BotRelay] Bot {transport.header.userID} refused outbound wire: " +
                    $"pipeline UTP compose failed for {wireLabel}.");
                continue;
            }

            if (!__WireEquals(in catalog.serverHelloWire, in liveHello))
            {
                if (!__TryApplySessionPatch(
                        ref wirePacket,
                        in connectTemplate,
                        in catalog.serverHelloWire,
                        in liveHello))
                    continue;
            }

            if (wireLabel == "Join" &&
                !__TryEmbedLiveApp(
                    ref wirePacket,
                    embedPayloadOffset,
                    in appPacket,
                    in catalog.squadJoinAppTemplate))
                continue;

            if (wireLabel == "Match" &&
                !__TryEmbedLiveApp(
                    ref wirePacket,
                    embedPayloadOffset,
                    in appPacket,
                    in catalog.matchAppTemplate))
                continue;

            if (wireLabel == "Mismatch" &&
                !__TryEmbedLiveApp(
                    ref wirePacket,
                    embedPayloadOffset,
                    in appPacket,
                    in catalog.statusAppTemplate))
                continue;

            BotRelayManager.Instance.EnqueueToListenFromPacketBurst(transport.virtualPort, in wirePacket);
            if (wireLabel == "Join")
            {
                ++BotRelayManager.Instance.diagJoinWireInjected;
                BotRelayManager.Instance.RecordJoinListenInjectBurst(transport.virtualPort, in wirePacket);
                BotRelayAgentDiagnostics.LogJoinWireFlushed(
                    transport.header.userID,
                    wirePacket.length,
                    transport.virtualPort);
            }
            else if (wireLabel == "Mismatch")
                ++BotRelayManager.Instance.diagMismatchWireInjected;
            else if (wireLabel == "Match")
                ++BotRelayManager.Instance.diagMatchWireInjected;

            sentAny = true;
            sentIndex = i;
            Debug.Log(
                $"[BotRelay] Bot {transport.header.userID} flushed {wireLabel} " +
                $"UTP wire ({wirePacket.length}B) on vport {transport.virtualPort}.");
            break;
        }

        if (!sentAny)
            return;

        for (int j = sentIndex + 1; j < count; ++j)
            farm.outbound[baseIndex + j - 1] = farm.outbound[baseIndex + j];
        farm.outboundCount[index] = count - 1;
    }

    public static void DrainInbound(int index, ref BotRelayFarmNative farm, in BotRelayWireCatalogState catalog)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if (transport.virtualPort == 0)
            return;

        ushort virtualPort = transport.virtualPort;
        int drainBudget = BotRelayJobLimits.MaxBotPacketsPerDrainJob;
        while (drainBudget-- > 0 && BotRelayManager.Instance.TryDequeueToBot(virtualPort, out var wirePacket))
        {
            ++BotRelayManager.Instance.diagDrainPacketsBudget;
            if (wirePacket.IsEmpty)
                continue;

            bool awaitingHello = farm.wireLiveServerHello[index].IsEmpty &&
                (transport.flags & BotRelayTransportFlags.ConnectRequested) != 0;

            if (awaitingHello)
            {
                transport.flags |= BotRelayTransportFlags.Connected;
                __StoreLiveHello(index, ref farm, in wirePacket);
                Debug.Log(
                    $"[BotRelay] Bot {transport.header.userID} server hello (first inbound) on vport {virtualPort}, " +
                    $"helloMatch={__WireEquals(in catalog.serverHelloWire, in wirePacket)}.");
                continue;
            }

            if (__TryUnwrapInbound(
                    in wirePacket,
                    in catalog,
                    farm.wireScratch,
                    index * BotRelayPacket.MaxPayloadSize,
                    out var appPacket))
            {
                transport.flags |= BotRelayTransportFlags.Connected;
                if (appPacket.IsEmpty ||
                    (!catalog.serverHelloWire.IsEmpty &&
                     __WireEquals(in catalog.serverHelloWire, in wirePacket)))
                    __StoreLiveHello(index, ref farm, in wirePacket);

                if (appPacket.IsEmpty)
                {
                    BotRelaySlotOps.TryWriteInitialStatus(index, ref farm);
                    Debug.Log($"[BotRelay] Bot {transport.header.userID} server hello received on vport {virtualPort}.");
                }
                else
                {
                    BotRelaySlotOps.TryEnqueueInbound(
                        index, farm.inbound, farm.inboundCount, in appPacket, ref BotRelayManager.Instance);
                    if (wirePacket.length > BotRelayWireBytes.MinOutboundTransportWireBytes)
                        __StoreJoinListenShell(index, ref farm, in wirePacket);
                }

                continue;
            }

            if (__WireEquals(in catalog.serverHelloWire, in wirePacket) ||
                __MatchesAnyServerTemplate(in wirePacket, in catalog))
            {
                transport.flags |= BotRelayTransportFlags.Connected;
                __StoreLiveHello(index, ref farm, in wirePacket);
                Debug.Log($"[BotRelay] Bot {transport.header.userID} server hello matched on vport {virtualPort}.");
                continue;
            }

            if ((transport.flags & BotRelayTransportFlags.Connected) != 0)
            {
                var liveHello = farm.wireLiveServerHello[index];
                if (__LooksLikeLinkAckCandidate(in wirePacket, in catalog) &&
                    __TryInjectLinkAck(
                        virtualPort,
                        in catalog,
                        in wirePacket,
                        in liveHello,
                        transport.header.userID))
                    continue;
            }

            Debug.LogWarning(
                $"[BotRelay] Bot {transport.header.userID} inbound wire unmapped (len={wirePacket.length}).");
        }

        if (drainBudget < 0 && BotRelayManager.Instance.HasInboundForBotBurst(virtualPort))
            ++BotRelayManager.Instance.diagDrainBudgetExhausted;

        __PumpLinkLayerAcks(index, ref farm, in catalog);
    }

    /// <summary>
    /// Drain server→bot link-layer packets (heartbeat/ACK) and inject captured ACK templates back to Listen.
    /// </summary>
    public static void PumpLinkLayerAcks(int index, ref BotRelayFarmNative farm, in BotRelayWireCatalogState catalog)
    {
        __PumpLinkLayerAcks(index, ref farm, in catalog);
    }

    private static void __PumpLinkLayerAcks(int index, ref BotRelayFarmNative farm, in BotRelayWireCatalogState catalog)
    {
        ref var transport = ref farm.transport.ElementAt(index);
        if (transport.virtualPort == 0)
            return;

        if ((transport.flags & BotRelayTransportFlags.Connected) == 0)
            return;

        if (farm.wireLiveServerHello[index].IsEmpty)
            return;

        ushort virtualPort = transport.virtualPort;
        var liveHello = farm.wireLiveServerHello[index];
        int ackBudget = BotRelayJobLimits.MaxLinkAckPacketsPerPump;

        while (ackBudget-- > 0 &&
               BotRelayManager.Instance.TryDequeueToBot(virtualPort, out var wirePacket))
        {
            if (wirePacket.IsEmpty)
                continue;

            if (__TryUnwrapInbound(
                    in wirePacket,
                    in catalog,
                    farm.wireScratch,
                    index * BotRelayPacket.MaxPayloadSize,
                    out var appPacket) && !appPacket.IsEmpty)
            {
                BotRelaySlotOps.TryEnqueueInbound(
                    index, farm.inbound, farm.inboundCount, in appPacket, ref BotRelayManager.Instance);
                continue;
            }

            if (__TryInjectLinkAck(virtualPort, in catalog, in wirePacket, in liveHello, transport.header.userID))
                continue;

            if (!__WireEquals(in catalog.serverHelloWire, in wirePacket))
            {
                Debug.LogWarning(
                    $"[BotRelay] Bot {transport.header.userID} link wire unhandled (len={wirePacket.length}).");
            }
        }
    }

    private static bool __TryInjectLinkAck(
        ushort virtualPort,
        in BotRelayWireCatalogState catalog,
        in BotRelayPacket inboundWire,
        in BotRelayPacket liveHello,
        uint userId)
    {
        if (!catalog.connectWire.clientPackets.IsCreated || catalog.connectWire.clientPackets.Length == 0)
            return false;

        if (__LooksLikeConnectHandshake(in inboundWire))
            return false;

        var connectTemplate = catalog.connectWire.clientPackets[0].wire;
        BotRelayPacket ackTemplate;

        if (!catalog.linkAckTemplates.IsCreated || catalog.linkAckTemplates.Length == 0)
        {
            if (!catalog.statusOutboundWire.IsEmpty)
                ackTemplate = catalog.statusOutboundWire;
            else
                return false;
        }
        else
        {
            ackTemplate = catalog.linkAckTemplates[0];

            for (int i = 0; i < catalog.linkAckTemplates.Length; ++i)
            {
                var candidate = catalog.linkAckTemplates[i];
                if (candidate.length == inboundWire.length)
                {
                    ackTemplate = candidate;
                    break;
                }
            }
        }

        var ackWire = ackTemplate;
        if (!catalog.serverHelloWire.IsEmpty && !liveHello.IsEmpty &&
            !__WireEquals(in catalog.serverHelloWire, in liveHello))
        {
            if (!__TryApplySessionPatch(
                    ref ackWire,
                    in connectTemplate,
                    in catalog.serverHelloWire,
                    in liveHello))
                return false;
        }

        ++BotRelayManager.Instance.diagLinkAckSwallowed;
        return true;
    }

    private static void __CaptureLinkAckTemplates(ref BotRelayWireCatalogState catalog, int firstLinkIndex)
    {
        catalog.linkAckTemplates = new NativeList<BotRelayPacket>(8, Allocator.Persistent);

        if (!catalog.connectWire.clientPackets.IsCreated)
            return;

        for (int i = catalog.connectClientWireCount; i < catalog.connectWire.clientPackets.Length; ++i)
        {
            var wire = catalog.connectWire.clientPackets[i].wire;
            if (!__LooksLikeLinkAckCandidate(in wire, in catalog))
                continue;

            if (__ContainsLinkAck(ref catalog, in wire))
                continue;

            catalog.linkAckTemplates.Add(wire);
        }
    }

    private static bool __LooksLikeLinkAckCandidate(in BotRelayPacket wire, in BotRelayWireCatalogState catalog)
    {
        if (wire.IsEmpty || wire.length > 64 || __LooksLikeConnectHandshake(in wire))
            return false;

        if (!catalog.statusOutboundWire.IsEmpty && __WireEquals(in wire, in catalog.statusOutboundWire))
            return false;

        if (!catalog.matchOutboundWire.IsEmpty && __WireEquals(in wire, in catalog.matchOutboundWire))
            return false;

        if (wire.length >= 3)
        {
            unsafe
            {
                fixed (byte* p = wire.data)
                {
                    if (p[0] == (byte)'U' && p[1] == (byte)'T' && p[2] == (byte)'P')
                        return true;
                }
            }
        }

        return wire.length <= 24;
    }

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
        ref NetworkSettings settings,
        ushort restoreListenPort,
        in ClientHeader header,
        int matchLevel,
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
                matchLevel,
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
        int matchLevel,
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
        BotRelayConnectCapture.Begin(connectPayload);

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
                __RecordInboundEvents(ref clientDriver, connection, ref catalog);
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

        int clientWireBeforeStatus = BotRelayConnectCapture.ClientPacketCount;
        catalog.connectClientWireCount = clientWireBeforeStatus;

        if (__TrySendStatusWire(ref listenDriver, ref clientDriver, ref listenConnection, connection, BotRelayPipeline.Null, out catalog.statusAppTemplate))
        {
            __RecordInboundEvents(ref clientDriver, connection, ref catalog);
            if (BotRelayConnectCapture.ClientPacketCount > clientWireBeforeStatus &&
                BotRelayConnectCapture.TryCopyClientWire(BotRelayConnectCapture.ClientPacketCount - 1, out catalog.statusOutboundWire))
            {
                __TryFindFramedAppOffset(
                    in catalog.statusOutboundWire,
                    in catalog.statusAppTemplate,
                    out catalog.statusOutboundPayloadOffset);
            }
        }

        int clientWireBeforeMatch = BotRelayConnectCapture.ClientPacketCount;

        if (__TrySendMatchWire(ref listenDriver, ref clientDriver, ref listenConnection, connection, BotRelayPipeline.Null, matchLevel, out catalog.matchAppTemplate))
        {
            __RecordInboundEvents(ref clientDriver, connection, ref catalog);
            if (BotRelayConnectCapture.ClientPacketCount > clientWireBeforeMatch &&
                BotRelayConnectCapture.TryCopyClientWire(BotRelayConnectCapture.ClientPacketCount - 1, out catalog.matchOutboundWire))
            {
                __TryFindFramedAppOffset(
                    in catalog.matchOutboundWire,
                    in catalog.matchAppTemplate,
                    out catalog.matchOutboundPayloadOffset);
            }
        }

        __TryCaptureInviteInboundWire(
            ref listenDriver,
            ref clientDriver,
            ref listenConnection,
            connection,
            BotRelayPipeline.Null,
            in header,
            ref catalog);

        __TryCapturePhase3OutboundWire(
            ref listenDriver,
            ref clientDriver,
            ref listenConnection,
            connection,
            BotRelayPipeline.Null,
            ref catalog);

        int clientWireBeforeLink = BotRelayConnectCapture.ClientPacketCount;
        for (int i = 0; i < 96; ++i)
        {
            __ScheduleCapturePair(ref listenDriver, ref clientDriver);
            __DrainCaptureEvents(ref listenDriver, ref clientDriver, ref listenConnection, connection);
        }

        int clientCaptureCount = BotRelayConnectCapture.ClientPacketCount;
        int serverCaptureCount = BotRelayConnectCapture.ServerPacketCount;
        catalog.connectWire = BotRelayConnectCapture.End();
        __CaptureLinkAckTemplates(ref catalog, clientWireBeforeLink);

        if (!catalog.connectWire.IsValid)
        {
            Debug.LogError(
                $"[BotRelay] WireCatalog: connect wire empty after handshake " +
                $"(clientCapture={clientCaptureCount}, serverCapture={serverCaptureCount}).");
            return false;
        }

        if (catalog.serverHelloWire.IsEmpty &&
            catalog.connectWire.serverPackets.IsCreated &&
            catalog.connectWire.serverPackets.Length > 0)
            catalog.serverHelloWire = catalog.connectWire.serverPackets[0].wire;

        __EnsureServerHelloMapping(ref catalog);
        __ResolveInboundAppPayloadOffset(ref catalog);
        return catalog.connectWire.IsValid;
    }

    private static void __ResolveInboundAppPayloadOffset(ref BotRelayWireCatalogState catalog)
    {
        catalog.inboundAppPayloadOffset = -1;

        if (!catalog.statusOutboundWire.IsEmpty &&
            !catalog.statusAppTemplate.IsEmpty &&
            BotRelayWireBytes.TryResolveInboundAppPayloadOffset(
                in catalog.statusOutboundWire,
                in catalog.statusAppTemplate,
                out int statusAppOffset))
        {
            catalog.inboundAppPayloadOffset = statusAppOffset;
            return;
        }

        if (!catalog.inboundMappings.IsCreated)
            return;

        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
        {
            var mapping = catalog.inboundMappings[i];
            if (BotRelayWireBytes.TryResolveInboundAppPayloadOffset(
                    in mapping.wire,
                    in mapping.appFrame,
                    out int offset))
            {
                catalog.inboundAppPayloadOffset = offset;
                return;
            }
        }
    }

    private static void __TryCapturePhase3OutboundWire(
        ref NetworkDriver listenDriver,
        ref NetworkDriver clientDriver,
        ref NetworkConnection listenConnection,
        NetworkConnection connection,
        NetworkPipeline pipeline,
        ref BotRelayWireCatalogState catalog)
    {
        if (__TryBuildSquadJoinApp(1, out catalog.squadJoinAppTemplate))
        {
            int wireBefore = BotRelayConnectCapture.ClientPacketCount;
            if (__TrySendFramedApp(ref listenDriver, ref clientDriver, ref listenConnection, connection, pipeline, in catalog.squadJoinAppTemplate))
            {
                __RecordInboundEvents(ref clientDriver, connection, ref catalog);
                if (BotRelayConnectCapture.ClientPacketCount > wireBefore)
                {
                    BotRelayConnectCapture.TryCopyClientWire(
                        BotRelayConnectCapture.ClientPacketCount - 1,
                        out catalog.squadJoinOutboundWire);
                    __TryFindFramedAppOffset(
                        in catalog.squadJoinOutboundWire,
                        in catalog.squadJoinAppTemplate,
                        out catalog.squadJoinPayloadOffset);
                }
            }
        }

        if (__TryBuildApplyMatchApp(out catalog.applyMatchAppTemplate))
        {
            int wireBefore = BotRelayConnectCapture.ClientPacketCount;
            if (__TrySendFramedApp(ref listenDriver, ref clientDriver, ref listenConnection, connection, pipeline, in catalog.applyMatchAppTemplate))
            {
                __RecordInboundEvents(ref clientDriver, connection, ref catalog);
                if (BotRelayConnectCapture.ClientPacketCount > wireBefore)
                    BotRelayConnectCapture.TryCopyClientWire(
                        BotRelayConnectCapture.ClientPacketCount - 1,
                        out catalog.applyMatchOutboundWire);
            }
        }

        if (__TryBuildSquadLeaveApp(out catalog.squadLeaveAppTemplate))
        {
            int wireBefore = BotRelayConnectCapture.ClientPacketCount;
            if (__TrySendFramedApp(ref listenDriver, ref clientDriver, ref listenConnection, connection, pipeline, in catalog.squadLeaveAppTemplate))
            {
                __RecordInboundEvents(ref clientDriver, connection, ref catalog);
                if (BotRelayConnectCapture.ClientPacketCount > wireBefore)
                    BotRelayConnectCapture.TryCopyClientWire(
                        BotRelayConnectCapture.ClientPacketCount - 1,
                        out catalog.squadLeaveOutboundWire);
            }
        }
    }

    private static bool __TrySendStatusWire(
        ref NetworkDriver listenDriver,
        ref NetworkDriver clientDriver,
        ref NetworkConnection listenConnection,
        NetworkConnection connection,
        NetworkPipeline pipeline,
        out BotRelayPacket appTemplate) =>
        __TryBuildStatusApp(out appTemplate) &&
        __TrySendFramedApp(ref listenDriver, ref clientDriver, ref listenConnection, connection, pipeline, in appTemplate);

    private static bool __TrySendMatchWire(
        ref NetworkDriver listenDriver,
        ref NetworkDriver clientDriver,
        ref NetworkConnection listenConnection,
        NetworkConnection connection,
        NetworkPipeline pipeline,
        int matchLevel,
        out BotRelayPacket appTemplate) =>
        __TryBuildMatchApp(matchLevel, out appTemplate) &&
        __TrySendFramedApp(ref listenDriver, ref clientDriver, ref listenConnection, connection, pipeline, in appTemplate);

    private static bool __TryBuildStatusApp(out BotRelayPacket appTemplate)
    {
        appTemplate = default;
        using var packets = new NativeArray<BotRelayPacket>(1, Allocator.Temp);
        using var counts = new NativeArray<int>(1, Allocator.Temp);
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var send = BotRelaySlotOps.CreateCaptureSendBuffer(packets, counts, scratch);

        if (!BotRelayCodec.TryWriteStatus(ref send, 0) || counts[0] <= 0)
            return false;

        appTemplate = packets[0];
        return !appTemplate.IsEmpty;
    }

    private static bool __TryBuildMatchApp(int matchLevel, out BotRelayPacket appTemplate)
    {
        appTemplate = default;
        using var packets = new NativeArray<BotRelayPacket>(1, Allocator.Temp);
        using var counts = new NativeArray<int>(1, Allocator.Temp);
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var send = BotRelaySlotOps.CreateCaptureSendBuffer(packets, counts, scratch);

        if (!BotRelayCodec.TryWriteMatchToSend(ref send, matchLevel) || counts[0] <= 0)
            return false;

        appTemplate = packets[0];
        return !appTemplate.IsEmpty;
    }

    private static bool __TryBuildSquadJoinApp(uint squadInviteId, out BotRelayPacket appTemplate)
    {
        appTemplate = default;
        using var packets = new NativeArray<BotRelayPacket>(1, Allocator.Temp);
        using var counts = new NativeArray<int>(1, Allocator.Temp);
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var send = BotRelaySlotOps.CreateCaptureSendBuffer(packets, counts, scratch);

        if (!BotRelayCodec.TryWriteSquadJoin(ref send, squadInviteId) || counts[0] <= 0)
            return false;

        appTemplate = packets[0];
        return !appTemplate.IsEmpty;
    }

    private static bool __TryBuildApplyMatchApp(out BotRelayPacket appTemplate)
    {
        appTemplate = default;
        using var packets = new NativeArray<BotRelayPacket>(1, Allocator.Temp);
        using var counts = new NativeArray<int>(1, Allocator.Temp);
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var send = BotRelaySlotOps.CreateCaptureSendBuffer(packets, counts, scratch);

        if (!BotRelayCodec.TryWriteApplyMatch(ref send) || counts[0] <= 0)
            return false;

        appTemplate = packets[0];
        return !appTemplate.IsEmpty;
    }

    private static bool __TryBuildSquadLeaveApp(out BotRelayPacket appTemplate)
    {
        appTemplate = default;
        using var packets = new NativeArray<BotRelayPacket>(1, Allocator.Temp);
        using var counts = new NativeArray<int>(1, Allocator.Temp);
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var send = BotRelaySlotOps.CreateCaptureSendBuffer(packets, counts, scratch);

        if (!BotRelayCodec.TryWriteSquadLeave(ref send) || counts[0] <= 0)
            return false;

        appTemplate = packets[0];
        return !appTemplate.IsEmpty;
    }

    private static bool __TrySendFramedApp(
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

        if (clientDriver.BeginSend(pipeline, connection, out var writer) < 0)
            return false;

        writer.WriteBytes(framed);
        clientDriver.EndSend(writer);
        __ScheduleCapturePair(ref listenDriver, ref clientDriver);
        __DrainCaptureEvents(ref listenDriver, ref clientDriver, ref listenConnection, connection);
        return true;
    }

    private static void __TryCaptureInviteInboundWire(
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
            return;
        }

        int inboundBefore = catalog.inboundMappings.Length;
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
                __RecordInboundEvents(ref clientDriver, connection, ref catalog);
                if (catalog.inboundMappings.Length > inboundBefore)
                {
                    break;
                }

                __DrainCaptureEvents(ref listenDriver, ref clientDriver, ref listenConnection, connection);
            }

            if (catalog.inboundMappings.Length <= inboundBefore)
                __TryRecordInviteInboundFromCapture(ref catalog, serverWireBefore, in inviteApp);
        }
        else
        {
            Debug.LogWarning("[BotRelay] WireCatalog: invite inbound live capture send skipped; synthesizing mapping.");
        }

        if (catalog.inboundMappings.Length <= inboundBefore)
            __TrySynthesizeInviteInboundMapping(ref catalog, in inviteApp);

        if (catalog.inboundMappings.Length <= inboundBefore)
            Debug.LogWarning("[BotRelay] WireCatalog: invite inbound wire not recorded.");
        else
            __ResolveInboundAppPayloadOffset(ref catalog);
    }

    private static void __TrySynthesizeInviteInboundMapping(
        ref BotRelayWireCatalogState catalog,
        in BotRelayPacket inviteApp)
    {
        if (inviteApp.IsEmpty)
            return;

        var wire = __BuildPipelineFramedInviteShell(in catalog, in inviteApp);
        if (wire.IsEmpty)
            return;

        if (__ContainsInboundWire(ref catalog, in wire))
            return;

        catalog.inboundMappings.Add(new BotRelayInboundWireMapping
        {
            wire = wire,
            appFrame = inviteApp
        });
    }

    private static BotRelayPacket __BuildPipelineFramedInviteShell(
        in BotRelayWireCatalogState catalog,
        in BotRelayPacket inviteApp)
    {
        var wire = default(BotRelayPacket);
        using var appBytes = new NativeArray<byte>(inviteApp.length, Allocator.Temp);
        if (!inviteApp.TryCopyTo(appBytes))
            return wire;

        using var framed = BotRelayCodec.FrameForTransport(appBytes);
        if (!framed.IsCreated || framed.Length <= sizeof(ushort))
            return wire;

        BotRelayPacket prefixSource = !catalog.statusOutboundWire.IsEmpty
            ? catalog.statusOutboundWire
            : catalog.serverHelloWire;

        int envelopeLen = BotRelayWireBytes.PipelinePrefixBytes;
        if (!prefixSource.IsEmpty &&
            prefixSource.length >= BotRelayWireBytes.PipelinePrefixBytes &&
            prefixSource.GetByte(0) == 0x01)
        {
            envelopeLen = math.min(10, prefixSource.length);
        }

        int wireLength = envelopeLen + framed.Length;
        if (wireLength <= 0 || wireLength > BotRelayPacket.MaxPayloadSize)
            return wire;

        wire.length = wireLength;
        for (int i = 0; i < envelopeLen; ++i)
        {
            wire.SetByte(i, prefixSource.IsEmpty ? (byte)0 : prefixSource.GetByte(i));
        }

        for (int i = 0; i < framed.Length; ++i)
            wire.SetByte(envelopeLen + i, framed[i]);

        return wire;
    }

    private static void __TryRecordInviteInboundFromCapture(
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

            if (__ContainsInboundWire(ref catalog, in wirePacket))
            {
                continue;
            }

            catalog.inboundMappings.Add(new BotRelayInboundWireMapping
            {
                wire = wirePacket,
                appFrame = inviteApp
            });
            return;
        }
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
        if (!__ContainsInboundWire(ref catalog, in helloWire))
        {
            catalog.inboundMappings.Add(new BotRelayInboundWireMapping
            {
                wire = helloWire,
                appFrame = default
            });
        }
    }

    private static void __EnsureServerHelloMapping(ref BotRelayWireCatalogState catalog)
    {
        if (catalog.serverHelloWire.IsEmpty)
            return;

        if (__ContainsInboundWire(ref catalog, in catalog.serverHelloWire))
            return;

        catalog.inboundMappings.Add(new BotRelayInboundWireMapping
        {
            wire = catalog.serverHelloWire,
            appFrame = default
        });
    }

    private static void __RecordInboundEvents(
        ref NetworkDriver clientDriver,
        NetworkConnection connection,
        ref BotRelayWireCatalogState catalog)
    {
        int serverIndex = catalog.inboundMappings.Length;

        while (true)
        {
            var cmd = clientDriver.PopEventForConnection(connection, out var stream, out _);
            if (cmd == NetworkEvent.Type.Empty)
                break;

            if (cmd != NetworkEvent.Type.Connect && cmd != NetworkEvent.Type.Data)
                continue;

            if (!BotRelayConnectCapture.TryCopyServerWire(serverIndex, out var wirePacket))
            {
                ++serverIndex;
                continue;
            }

            var appPacket = cmd == NetworkEvent.Type.Data ? __ExtractAppFrame(ref stream) : default;
            if (__ContainsInboundWire(ref catalog, in wirePacket))
            {
                ++serverIndex;
                continue;
            }

            catalog.inboundMappings.Add(new BotRelayInboundWireMapping
            {
                wire = wirePacket,
                appFrame = appPacket
            });
            ++serverIndex;
        }
    }

    private static bool __ContainsInboundWire(ref BotRelayWireCatalogState catalog, in BotRelayPacket wire)
    {
        if (!catalog.inboundMappings.IsCreated)
            return false;

        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
        {
            var mapping = catalog.inboundMappings[i];
            if (__WireEquals(in mapping.wire, in wire))
                return true;
        }

        return false;
    }

    private static BotRelayPacket __ExtractAppFrame(ref DataStreamReader stream)
    {
        var packet = default(BotRelayPacket);
        if (stream.Length <= 0)
            return packet;

        int length = stream.Length - stream.GetBytesRead();
        if (length <= 0 || length > BotRelayPacket.MaxPayloadSize)
            return packet;

        var temp = new NativeArray<byte>(length, Allocator.Temp);
        stream.ReadBytes(temp);
        packet.TryWriteFrom(temp);
        temp.Dispose();
        return packet;
    }

    private static bool __TryUnwrapInbound(
        in BotRelayPacket wirePacket,
        in BotRelayWireCatalogState catalog,
        NativeArray<byte> scratch,
        int scratchOffset,
        out BotRelayPacket appPacket)
    {
        appPacket = default;
        if (wirePacket.IsEmpty)
            return false;

        if (catalog.inboundMappings.IsCreated)
        {
            for (int i = 0; i < catalog.inboundMappings.Length; ++i)
            {
                var mapping = catalog.inboundMappings[i];
                if (__WireEquals(in mapping.wire, in wirePacket))
                {
                    appPacket = mapping.appFrame;
                    return true;
                }
            }
        }

        if (!catalog.serverHelloWire.IsEmpty && __WireEquals(in catalog.serverHelloWire, in wirePacket))
            return true;

        if (__MatchesAnyServerTemplate(in wirePacket, in catalog))
            return true;

        return BotRelayWireBytes.TryExtractRelayAppPayload(
            in wirePacket,
            scratch,
            scratchOffset,
            in catalog.serverHelloWire,
            catalog.inboundAppPayloadOffset,
            out appPacket);
    }

    private static bool __MatchesAnyServerTemplate(in BotRelayPacket wirePacket, in BotRelayWireCatalogState catalog)
    {
        if (!catalog.connectWire.serverPackets.IsCreated)
            return false;

        for (int i = 0; i < catalog.connectWire.serverPackets.Length; ++i)
        {
            var entry = catalog.connectWire.serverPackets[i];
            if (__WireEquals(in entry.wire, in wirePacket))
                return true;
        }

        return false;
    }

    private static bool __TryResolveOutboundWire(
        in BotRelayPacket appPacket,
        in BotRelayWireCatalogState catalog,
        out BotRelayPacket wirePacket,
        out string wireLabel)
    {
        wirePacket = default;
        wireLabel = null;
        if (appPacket.IsEmpty)
            return false;

        if (__AppMatches(in appPacket, in catalog.statusAppTemplate) && !catalog.statusOutboundWire.IsEmpty)
        {
            wirePacket = catalog.statusOutboundWire;
            wireLabel = "Status";
            return true;
        }

        if (__AppMatches(in appPacket, in catalog.matchAppTemplate) && !catalog.matchOutboundWire.IsEmpty)
        {
            wirePacket = catalog.matchOutboundWire;
            wireLabel = "Match";
            return true;
        }

        if (__AppMatches(in appPacket, in catalog.applyMatchAppTemplate) && !catalog.applyMatchOutboundWire.IsEmpty)
        {
            wirePacket = catalog.applyMatchOutboundWire;
            wireLabel = "ApplyMatch";
            return true;
        }

        if (__AppMatches(in appPacket, in catalog.squadLeaveAppTemplate) && !catalog.squadLeaveOutboundWire.IsEmpty)
        {
            wirePacket = catalog.squadLeaveOutboundWire;
            wireLabel = "Leave";
            return true;
        }

        if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in appPacket, out int relayType) &&
            relayType == (int)NetworkRelayMessageType.Join)
        {
            if (!catalog.squadJoinOutboundWire.IsEmpty &&
                catalog.squadJoinOutboundWire.length > BotRelayWireBytes.MinRelayTransportShellBytes &&
                __IsJoinCompatibleManagedShell(in catalog, in catalog.squadJoinOutboundWire))
                wirePacket = catalog.squadJoinOutboundWire;
            else if (!catalog.squadJoinOutboundWire.IsEmpty)
                wirePacket = catalog.squadJoinOutboundWire;

            wireLabel = "Join";
            return true;
        }

        if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in appPacket, out int matchRelayType) &&
            matchRelayType == (int)NetworkRelayMessageType.Match &&
            !catalog.matchOutboundWire.IsEmpty)
        {
            wirePacket = catalog.matchOutboundWire;
            wireLabel = "Match";
            return true;
        }

        if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in appPacket, out int mismatchRelayType) &&
            mismatchRelayType == (int)NetworkRelayMessageType.Mismatch &&
            !catalog.statusOutboundWire.IsEmpty)
        {
            wirePacket = catalog.statusOutboundWire;
            wireLabel = "Mismatch";
            return true;
        }

        return false;
    }

    private static bool __IsJoinCompatibleManagedShell(
        in BotRelayWireCatalogState catalog,
        in BotRelayPacket wire)
    {
        if (wire.IsEmpty ||
            BotRelayWireBytes.LooksLikeConnectHandshake(in wire) ||
            wire.length <= BotRelayWireBytes.MinRelayTransportShellBytes)
            return false;

        if (!catalog.statusOutboundWire.IsEmpty && __WireEquals(in catalog.statusOutboundWire, in wire))
            return false;

        if (!catalog.matchOutboundWire.IsEmpty && __WireEquals(in catalog.matchOutboundWire, in wire))
            return false;

        return true;
    }

    private static bool __AppSameKind(in BotRelayPacket left, in BotRelayPacket right)
    {
        if (right.IsEmpty || left.length != right.length)
            return false;

        int prefix = math.min(2, left.length);
        if (prefix <= 0)
            return true;

        unsafe
        {
            fixed (byte* l = left.data)
            fixed (byte* r = right.data)
            {
                for (int i = 0; i < prefix; ++i)
                {
                    if (l[i] != r[i])
                        return false;
                }
            }
        }

        return true;
    }

    private static bool __TryEmbedLiveApp(
        ref BotRelayPacket wirePacket,
        int payloadOffset,
        in BotRelayPacket liveApp,
        in BotRelayPacket capturedApp)
    {
        if (payloadOffset < 0)
            return false;

        using var appBytes = new NativeArray<byte>(liveApp.length, Allocator.Temp);
        if (!liveApp.TryCopyTo(appBytes))
            return false;

        using var framed = BotRelayCodec.FrameForTransport(appBytes);
        if (!framed.IsCreated)
            return false;

        if (payloadOffset + framed.Length > wirePacket.length)
            return false;

        bool alreadyEmbedded = true;
        for (int i = 0; i < framed.Length; ++i)
        {
            if (wirePacket.GetByte(payloadOffset + i) != framed[i])
            {
                alreadyEmbedded = false;
                break;
            }
        }

        if (alreadyEmbedded)
            return true;

        var wireLocal = wirePacket;
        var wireBytes = new NativeArray<byte>(wireLocal.length, Allocator.Temp);
        try
        {
            if (!wireLocal.TryCopyTo(wireBytes))
                return false;

            for (int i = 0; i < framed.Length; ++i)
                wireBytes[payloadOffset + i] = framed[i];

            if (!wireLocal.TryWriteFrom(wireBytes, wireLocal.length))
                return false;

            wirePacket = wireLocal;
            return true;
        }
        finally
        {
            if (wireBytes.IsCreated)
                wireBytes.Dispose();
        }
    }

    private static bool __TryFindFramedAppOffset(
        in BotRelayPacket wire,
        in BotRelayPacket app,
        out int offset)
    {
        offset = -1;
        if (wire.IsEmpty || app.IsEmpty)
            return false;

        using var appBytes = new NativeArray<byte>(app.length, Allocator.Temp);
        if (!app.TryCopyTo(appBytes))
            return false;

        using var framed = BotRelayCodec.FrameForTransport(appBytes);
        if (!framed.IsCreated)
            return false;

        return __TryFindSubsequence(in wire, framed, out offset);
    }

    private static bool __TryFindSubsequence(in BotRelayPacket haystack, NativeArray<byte> needle, out int offset)
    {
        offset = -1;
        if (!needle.IsCreated || needle.Length == 0 || haystack.length < needle.Length)
            return false;

        using var haystackBytes = new NativeArray<byte>(haystack.length, Allocator.Temp);
        if (!haystack.TryCopyTo(haystackBytes))
            return false;

        int limit = haystack.length - needle.Length;
        for (int i = 0; i <= limit; ++i)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; ++j)
            {
                if (haystackBytes[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                offset = i;
                return true;
            }
        }

        return false;
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

    private static bool __AppMatches(in BotRelayPacket left, in BotRelayPacket right) =>
        !right.IsEmpty && __WireEquals(in left, in right);

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

    private static bool __IsCatalogOutboundTemplateWire(
        in BotRelayWireCatalogState catalog,
        in BotRelayPacket wire)
    {
        if (wire.IsEmpty)
            return false;

        if (!catalog.statusOutboundWire.IsEmpty && __WireEquals(in catalog.statusOutboundWire, in wire))
            return true;
        if (!catalog.matchOutboundWire.IsEmpty && __WireEquals(in catalog.matchOutboundWire, in wire))
            return true;
        if (!catalog.squadJoinOutboundWire.IsEmpty && __WireEquals(in catalog.squadJoinOutboundWire, in wire))
            return true;
        if (!catalog.applyMatchOutboundWire.IsEmpty && __WireEquals(in catalog.applyMatchOutboundWire, in wire))
            return true;

        return !catalog.squadLeaveOutboundWire.IsEmpty && __WireEquals(in catalog.squadLeaveOutboundWire, in wire);
    }

    private static bool __IsConnectClientCapturedWire(
        in BotRelayWireCatalogState catalog,
        in BotRelayPacket wire)
    {
        if (__IsCatalogOutboundTemplateWire(in catalog, in wire))
            return false;

        if (wire.IsEmpty || !catalog.connectWire.clientPackets.IsCreated)
            return false;

        for (int i = 0; i < catalog.connectWire.clientPackets.Length; ++i)
        {
            var entry = catalog.connectWire.clientPackets[i];
            if (__WireEquals(in entry.wire, in wire))
                return true;
        }

        return false;
    }

    private static bool __TryFindLongestUtPConnectWire(
        in BotRelayWireCatalogState catalog,
        out BotRelayPacket connectWire)
    {
        connectWire = default;
        if (!catalog.connectWire.clientPackets.IsCreated || catalog.connectWire.clientPackets.Length == 0)
            return false;

        int bestLength = 0;
        for (int i = 0; i < catalog.connectWire.clientPackets.Length; ++i)
        {
            var entry = catalog.connectWire.clientPackets[i];
            var candidate = entry.wire;
            if (candidate.IsEmpty || !BotRelayWireBytes.LooksLikeConnectHandshake(in candidate))
                continue;

            if (candidate.length > bestLength)
            {
                bestLength = candidate.length;
                connectWire = candidate;
            }
        }

        return !connectWire.IsEmpty;
    }

    private static void __StoreLiveHello(int index, ref BotRelayFarmNative farm, in BotRelayPacket wirePacket)
    {
        if (wirePacket.IsEmpty)
            return;

        farm.wireLiveServerHello[index] = wirePacket;
    }

    private static void __StoreJoinListenShell(int index, ref BotRelayFarmNative farm, in BotRelayPacket wirePacket)
    {
        if (wirePacket.IsEmpty)
            return;

        farm.wireLiveJoinListenShell[index] = wirePacket;
    }

    private static bool __TryApplySessionPatch(
        ref BotRelayPacket outbound,
        in BotRelayPacket connectTemplate,
        in BotRelayPacket capturedHello,
        in BotRelayPacket liveHello)
    {
        if (outbound.IsEmpty || connectTemplate.IsEmpty || capturedHello.IsEmpty || liveHello.IsEmpty)
            return false;

        if (__WireEquals(in capturedHello, in liveHello))
            return true;

        int n = math.min(
            outbound.length,
            math.min(connectTemplate.length, math.min(capturedHello.length, liveHello.length)));

        unsafe
        {
            fixed (byte* o = outbound.data)
            fixed (byte* c = connectTemplate.data)
            fixed (byte* ch = capturedHello.data)
            fixed (byte* lh = liveHello.data)
            {
                for (int i = 0; i < n; ++i)
                {
                    if (ch[i] == lh[i])
                        continue;
                    if (c[i] == o[i])
                        continue;

                    o[i] = (byte)(o[i] + (lh[i] - ch[i]));
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

    private static string __DescribeInviteInboundMapping(in BotRelayWireCatalogState catalog)
    {
        if (!catalog.inboundMappings.IsCreated)
            return "none";

        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
        {
            var mapping = catalog.inboundMappings[i];
            if (mapping.appFrame.IsEmpty ||
                !BotRelayWireBytes.TryReadRelayMessageTypeHeader(in mapping.appFrame, out int type) ||
                type != (int)ReplyMessageType.Invite)
                continue;

            if (BotRelayWireBytes.TryResolveInboundAppPayloadOffset(
                    in mapping.wire,
                    in mapping.appFrame,
                    out int offset))
            {
                return $"wireLen={mapping.wire.length} appLen={mapping.appFrame.length} off={offset}";
            }

            return $"wireLen={mapping.wire.length} appLen={mapping.appFrame.length} off=?";
        }

        return "none";
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
