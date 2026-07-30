using Unity.Entities;

/// <summary>
/// Off-ECS storage for init-captured wire templates. Runtime hot path reads immutable Blob catalog.
/// </summary>
internal static class BotRelayWireCatalogHost
{
    private static BlobAssetReference<BotRelayCatalogBlob> s_catalogBlob;

    public static BlobAssetReference<BotRelayCatalogBlob> CatalogBlob => s_catalogBlob;

    public static void Publish(
        ref BotRelayWireCatalogState catalog,
        BotRelayConnectWireState[] agentConnectWires)
    {
        BlobAssetReference<BotRelayCatalogBlob> nextBlob;
        try
        {
            nextBlob = BotRelayCatalogBlobBuilder.Create(in catalog, agentConnectWires);
        }
        finally
        {
            // Capture-only frames (including the synthetic Invite body used to measure
            // inboundAppPayloadOffset) must not survive publication.
            catalog.Dispose();
        }

        if (s_catalogBlob.IsCreated)
            s_catalogBlob.Dispose();

        s_catalogBlob = nextBlob;
    }

    public static void Dispose()
    {
        if (s_catalogBlob.IsCreated)
            s_catalogBlob.Dispose();

        s_catalogBlob = default;
    }
}
