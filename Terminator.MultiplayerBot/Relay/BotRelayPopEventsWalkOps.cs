using Unity.Burst;
using ZG;

/// <summary>
/// Reader for the single current server-to-bot transport framing.
///
/// <see cref="BotRelayWireCatalog"/> measures the first application payload offset from a
/// live Null-pipeline capture. The corresponding stream starts exactly two bytes earlier and
/// contains repeated little-endian <c>ushort length + complete application payload</c> frames.
/// No fixed prefix, alternate stream start, or inner-payload scan is supported.
/// </summary>
[BurstCompile]
internal static class BotRelayPopEventsWalkOps
{
    public const int MaxAppsPerWire = 8;

    public static bool TryWalkServerToBotApps(
        in BotRelayPacket wire,
        int inboundAppPayloadOffset,
        ref BotRelayPopEventsWalkBatch batch,
        out bool fullyConsumed)
    {
        batch = default;
        fullyConsumed = false;
        if (!BotRelayWireBytes.TryGetCurrentPopEventsStreamStart(
                in wire,
                inboundAppPayloadOffset,
                out int streamStart))
        {
            return false;
        }

        return __TryWalkPopEventsAppsAt(
            in wire,
            streamStart,
            ref batch,
            out fullyConsumed);
    }

    public static bool TryExtractFirstServerToBotApp(
        in BotRelayPacket wire,
        int inboundAppPayloadOffset,
        out BotRelayPacket app)
    {
        app = default;
        var batch = default(BotRelayPopEventsWalkBatch);
        if (!TryWalkServerToBotApps(
                in wire,
                inboundAppPayloadOffset,
                ref batch,
                out bool fullyConsumed) ||
            !fullyConsumed ||
            batch.appCount < 1)
        {
            return false;
        }

        app = batch.GetApp(0);
        return !app.IsEmpty;
    }

    private static bool __TryWalkPopEventsAppsAt(
        in BotRelayPacket wire,
        int streamStart,
        ref BotRelayPopEventsWalkBatch batch,
        out bool fullyConsumed)
    {
        batch = default;
        fullyConsumed = false;
        if (streamStart < 0 || streamStart + sizeof(ushort) > wire.length)
            return false;

        int pos = streamStart;
        while (pos < wire.length)
        {
            if (pos + sizeof(ushort) > wire.length)
                return false;

            int size = wire.GetByte(pos) | (wire.GetByte(pos + 1) << 8);
            pos += sizeof(ushort);
            if (size < 1 || size > wire.length - pos)
                return false;

            BotRelayWireBytes.CopyAppFromOffset(in wire, pos, size, out var rawPayload);
            pos += size;

            if (!BotRelayWireBytes.TryAcceptCurrentInboundApp(in rawPayload, out var app))
                return false;

            if (!batch.TryAdd(in app))
                return false;
        }

        fullyConsumed = pos == wire.length;
        return fullyConsumed && batch.appCount > 0;
    }
}
