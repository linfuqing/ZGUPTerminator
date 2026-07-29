using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// EditMode harness: real UTP WireCatalog capture + BotRelayManager queue + farm drain path.
/// Simulates RouteSend(1390→botVport) without full ClientData / NetworkRelayServerSystem.
/// </summary>
internal struct BotRelayIntegrationSession : System.IDisposable
{
    public const ushort ListenPort = 1390;
    public const ushort BotVirtualPort = 48162;

    public BotRelayFarmNative farm;
    public BlobAssetReference<BotRelayCatalogBlob> catalogBlob;
    public bool ownsManagerRef;

    public void Dispose()
    {
        farm.Dispose();
        if (catalogBlob.IsCreated)
            catalogBlob.Dispose();

        if (ownsManagerRef && BotRelayManager.Instance.IsCreated)
            BotRelayManager.Instance.Release();
    }
}

internal static class BotRelayIntegrationTestFixtures
{
    public static BotRelayIntegrationSession CreateConnectedSession(bool captureWireCatalog = true)
    {
        var session = default(BotRelayIntegrationSession);
        ref var manager = ref BotRelayManager.Instance;

        if (!manager.IsCreated)
        {
            manager.AddRef();
            session.ownsManagerRef = true;
        }
        else
        {
            manager.Reset();
        }

        if (captureWireCatalog)
        {
            session.catalogBlob = __CaptureCatalogBlob();

            // Capture uses the shared Manager (listen swap, virtual ports, queue traffic). Re-bind test ports.
            manager.Reset();
        }

        manager.RegisterListenPort(BotRelayIntegrationSession.ListenPort);
        manager.EnsureVirtualPort(BotRelayIntegrationSession.BotVirtualPort);
        manager.RegisterBotVirtualPortBurst(
            BotRelayWireTestFixtures.BotUserId,
            BotRelayIntegrationSession.BotVirtualPort);

        session.farm = __CreateConnectedFarm(BotRelayIntegrationSession.BotVirtualPort);
        __SeedWireLiveServerHelloFromCatalog(ref session);
        return session;
    }

    /// <summary>Simulates post-connect server hello before a deferred (runtime) invite arrives.</summary>
    public static void SeedWireLiveServerHelloFromCatalog(ref BotRelayIntegrationSession session) =>
        __SeedWireLiveServerHelloFromCatalog(ref session);

    private static void __SeedWireLiveServerHelloFromCatalog(ref BotRelayIntegrationSession session)
    {
        if (!session.catalogBlob.IsCreated)
            return;

        ref var catalog = ref session.catalogBlob.Value;
        ref var capturedHello = ref BotRelayWireBytes.GetTemplate(ref catalog, BotRelayCatalogPacketSlot.ServerHello);
        if (BotRelayWireBytes.TryCopyWireToPacket(ref capturedHello, out var hello))
            session.farm.wireLiveServerHello[0] = hello;
    }

    public static void RouteServerToBot(byte[] wireBytes)
    {
        if (wireBytes == null || wireBytes.Length == 0)
            throw new System.ArgumentException("wireBytes empty");

        using var payload = new NativeArray<byte>(wireBytes, Allocator.Temp);
        BotRelayManager.Instance.EnqueueServerToBot(
            BotRelayIntegrationSession.BotVirtualPort,
            BotRelayIntegrationSession.ListenPort,
            payload);
    }

    public static void RouteLiveInviteWireToBot(ref BotRelayIntegrationSession session)
    {
        RouteNullFramedInviteToBot(ref session);
        DrainInbound(ref session);
        if (session.farm.inboundCount[0] > 0)
            return;

        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        if (!BotRelayWireTestFixtures.TryExtractLiveInviteWire(in wire, out var app))
            throw new System.InvalidOperationException("Live invite app extract failed.");

        BotRelaySlotOps.TryEnqueueInbound(
            0,
            session.farm.inbound,
            session.farm.inboundCount,
            in app,
            ref BotRelayManager.Instance);
    }

    public static void DrainInbound(ref BotRelayIntegrationSession session)
    {
        ref var manager = ref BotRelayManager.Instance;
        ref var catalog = ref session.catalogBlob.Value;
        BotRelayBurstWireOps.DrainInbound(0, ref session.farm, ref manager, ref catalog);
    }

    /// <summary>UTP pipeline + framed Play app (same shape as <see cref="BuildDriverShapeInviteWireBytes"/>).</summary>
    public static byte[] BuildDriverShapePlayWireBytes(uint levelId = 7, int stage = 2, uint senderId = 0)
    {
        if (senderId == 0)
            senderId = BotRelayWireTestFixtures.RealPlayerUserId;

        if (!BotRelayWireTestFixtures.TryBuildInboundPlayApp(levelId, stage, senderId, out var app) || app.IsEmpty)
            throw new System.InvalidOperationException("Play app build failed.");

        var wire = BotRelayWireTestFixtures.WrapPipelineFramedApp(
            BotRelayWireTestFixtures.PipelineHeader,
            in app);
        return BotRelayWireTestFixtures.ToBytes(wire);
    }

    /// <summary>UTP pipeline + framed invite app (legacy app from live 117B extract).</summary>
    public static byte[] BuildDriverShapeInviteWireBytes()
    {
        if (!BotRelayWireTestFixtures.TryBuildInviteApp(
                BotRelayWireTestFixtures.RealPlayerUserId,
                1,
                0,
                0,
                out var app) ||
            app.IsEmpty)
        {
            throw new System.InvalidOperationException("Canonical invite app build failed.");
        }

        var wire = BotRelayWireTestFixtures.WrapPipelineFramedApp(
            BotRelayWireTestFixtures.PipelineHeader,
            in app);
        return BotRelayWireTestFixtures.ToBytes(wire);
    }

    /// <summary>
    /// Null-pipeline server→bot invite wire: a fixed transport prefix of the session's
    /// captured <c>inboundAppPayloadOffset</c> followed by the real invite app. This mirrors
    /// what the bot receives once its connection is routed through the null pipeline
    /// (see <see cref="BotRelayPipeline"/>): the runtime decode strips exactly that offset to
    /// recover the app — no per-message scanning.
    /// </summary>
    public static byte[] BuildNullFramedInviteWireBytes(ref BotRelayIntegrationSession session)
    {
        var liveWire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);
        if (!BotRelayWireTestFixtures.TryExtractLiveInviteWire(in liveWire, out var app) || app.IsEmpty)
            throw new System.InvalidOperationException("Live invite app extract failed.");

        if (!session.catalogBlob.IsCreated)
            throw new System.InvalidOperationException("Session catalog required for null-framed invite wire.");

        int offset = session.catalogBlob.Value.inboundAppPayloadOffset;
        if (offset < 0)
            throw new System.InvalidOperationException(
                $"Captured inboundAppPayloadOffset invalid ({offset}); null-pipeline capture did not measure the offset.");

        var bytes = new byte[offset + app.length];
        for (int i = 0; i < app.length; ++i)
            bytes[offset + i] = app.GetByte(i);
        return bytes;
    }

    /// <summary>Routes the null-framed invite wire (production server→bot shape under null pipeline).</summary>
    public static void RouteNullFramedInviteToBot(ref BotRelayIntegrationSession session) =>
        RouteServerToBot(BuildNullFramedInviteWireBytes(ref session));

    /// <summary>Null-pipeline prefix + relay app (Status/Join/Create downlinks).</summary>
    public static byte[] BuildNullFramedRelayAppWireBytes(ref BotRelayIntegrationSession session, in BotRelayPacket app)
    {
        if (app.IsEmpty)
            throw new System.ArgumentException("app empty");

        if (!session.catalogBlob.IsCreated)
            throw new System.InvalidOperationException("Session catalog required for null-framed relay app wire.");

        int offset = session.catalogBlob.Value.inboundAppPayloadOffset;
        if (offset < 0)
            offset = 0;

        var bytes = new byte[offset + app.length];
        for (int i = 0; i < app.length; ++i)
            bytes[offset + i] = app.GetByte(i);
        return bytes;
    }

    public static void PumpAgentInbound(ref BotRelayIntegrationSession session)
    {
        BotRelaySlotOps.PumpInboundBurst(0, ref session.farm);
    }

    public const double JoinMismatchDeferSeconds = 1.5;
    public const double JoinMismatchAckFallbackSeconds = 1.0;

    /// <summary>Runtime invite after connect: RouteSend → DrainInbound (production Burst path).</summary>
    public static void RouteDeferredInviteAndDrain(ref BotRelayIntegrationSession session)
    {
        RouteNullFramedInviteToBot(ref session);
        DrainInbound(ref session);
    }

    private static BotRelayFarmNative __CreateConnectedFarm(ushort virtualPort)
    {
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        ref var transport = ref farm.transport.ElementAt(0);
        transport.virtualPort = virtualPort;
        transport.flags = BotRelayTransportFlags.Connected;
        farm.inboxSentInitialStatus[0] = 1;
        return farm;
    }

    private static BlobAssetReference<BotRelayCatalogBlob> __CaptureCatalogBlob()
    {
        var settings = BotRelayWorld.CreateNetworkSettings();
        var header = BotRelayWireTestFixtures.BuildBotHeader();
        BotRelayManager.ManagerAccessHandle.Complete();
        var catalogState = BotRelayWireCatalog.Capture(
            ref settings,
            BotRelayIntegrationSession.ListenPort,
            in header,
            BotRelayFlowTestFixtures.MatchLevel);

        if (!catalogState.IsValid)
        {
            catalogState.Dispose();
            throw new System.InvalidOperationException("BotRelayWireCatalog.Capture failed in EditMode.");
        }

        var blob = BotRelayCatalogBlobBuilder.Create(in catalogState);
        catalogState.Dispose();
        return blob;
    }
}
