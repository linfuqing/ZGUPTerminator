using Unity.Burst;

/// <summary>
/// Server-to-Bot RouteSend peel using only the measured current PopEvents framing.
/// Invite payloads are accepted only in their complete canonical SendRelay representation.
/// </summary>
[BurstCompile]
internal static class BotRelayRoutePeelOps
{
    public static bool TryPeelAllServerToBotApps(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog,
        ref BotRelayPopEventsWalkBatch batch,
        out bool fullyConsumed)
    {
        batch = default;
        fullyConsumed = false;
        if (wire.IsEmpty || !catalog.IsValid)
            return false;

        if (BotRelayPopEventsWalkOps.TryWalkServerToBotApps(
                in wire,
                catalog.inboundAppPayloadOffset,
                ref batch,
                out fullyConsumed) &&
            fullyConsumed &&
            __FilterPeelCandidates(ref batch))
        {
            return true;
        }

        batch = default;
        fullyConsumed = false;
        return false;
    }

    private static bool __FilterPeelCandidates(ref BotRelayPopEventsWalkBatch batch)
    {
        int peelCount = 0;
        for (int i = 0; i < batch.appCount; ++i)
        {
            var app = batch.GetApp(i);
            if (!IsPeelCandidateApp(in app))
                continue;

            if (peelCount != i)
                batch.SetApp(peelCount, in app);

            ++peelCount;
        }

        batch.appCount = peelCount;
        return peelCount > 0;
    }

    internal static bool IsPeelCandidateApp(in BotRelayPacket app)
        => BotRelayWireBytes.TryAcceptCurrentInboundApp(in app, out _);
}
