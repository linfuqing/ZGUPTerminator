using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Off-ECS storage for init-captured wire templates. Runtime hot path reads immutable Blob catalog.
/// </summary>
internal static class BotRelayWireCatalogHost
{
    private static BotRelayWireCatalogState s_catalog;
    private static BlobAssetReference<BotRelayCatalogBlob> s_catalogBlob;

    public static bool IsReady => s_catalog.IsValid && s_catalogBlob.IsCreated && s_catalogBlob.Value.IsValid;

    public static ref BotRelayWireCatalogState Catalog => ref s_catalog;

    public static BlobAssetReference<BotRelayCatalogBlob> CatalogBlob => s_catalogBlob;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Publish(
        in BotRelayWireCatalogState catalog,
        BotRelayConnectWireState[] agentConnectWires)
    {
        if (s_catalog.IsValid)
            s_catalog.Dispose();

        if (s_catalogBlob.IsCreated)
            s_catalogBlob.Dispose();

        s_catalog = catalog;
        s_catalogBlob = BotRelayCatalogBlobBuilder.Create(in catalog, agentConnectWires);
    }

    public static void Dispose()
    {
        if (s_catalog.IsValid)
            s_catalog.Dispose();

        if (s_catalogBlob.IsCreated)
            s_catalogBlob.Dispose();

        s_catalog = default;
        s_catalogBlob = default;
    }
}
