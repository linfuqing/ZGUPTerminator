using Unity.Burst;
using Unity.Collections;
using ZG;

/// <summary>
/// Co-deploy server→bot RouteSend peel: multi-frame PopEvents walk at enqueue time so Connect
/// shells cannot swallow Create/Join/Status downlinks. See Documentation~/MultiplayerBot/联机机器人-ServerToBot全帧解码.md.
/// </summary>
[BurstCompile]
internal static class BotRelayRoutePeelOps
{
    internal const int PeelPathNone = 0;
    internal const int PeelPathMappingExact = 1;
    internal const int PeelPathMappingOffset = 2;
    internal const int PeelPathCatalogOffset = 3;
    internal const int PeelPathPopEventsWalk = 4;
    internal const int PeelPathRejectedFallback = 5;
    internal const int PeelPathPipelineInvite = 6;

    public static bool TryPeelAllServerToBotApps(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog,
        ref BotRelayPopEventsWalkBatch batch,
        out bool fullyConsumed,
        out int peelPath)
    {
        batch = default;
        fullyConsumed = false;
        peelPath = PeelPathNone;
        if (wire.IsEmpty || !catalog.IsValid)
            return false;

        if (!BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire) &&
            __TryPeelInboundMappingExact(in wire, ref catalog, ref batch, out fullyConsumed))
        {
            peelPath = PeelPathMappingExact;
            return batch.appCount > 0;
        }

        if (!BotRelayWireBytes.LooksLikePipelineWire(in wire) &&
            catalog.inboundAppPayloadOffset >= 0 &&
            __TryPeelCatalogOffsetApp(in wire, catalog.inboundAppPayloadOffset, ref batch, out fullyConsumed))
        {
            peelPath = PeelPathCatalogOffset;
            return batch.appCount > 0;
        }

        if (BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire) &&
            !BotRelayPopEventsWalkOps.__TryWalkHasJoinAtCanonicalConnectOffset(in wire) &&
            BotRelayInviteDecodeOps.TryDecodeServerToBotInviteWire(
                in wire,
                ref catalog,
                out var inviteApp,
                out int inviteDecodePath) &&
            !inviteApp.IsEmpty)
        {
            batch = default;
            if (!batch.TryAdd(in inviteApp))
                return false;

            fullyConsumed = true;
            peelPath = __MapInviteDecodeToPeelPath(inviteDecodePath);
            return true;
        }

        if (!BotRelayPopEventsWalkOps.TryWalkServerToBotApps(
                in wire,
                ref batch,
                out fullyConsumed) ||
            batch.appCount < 1)
            return false;

        peelPath = PeelPathPopEventsWalk;
        if (!__FilterPeelCandidates(ref batch))
        {
            return false;
        }

        if (BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire) &&
            !BotRelayPopEventsWalkOps.BatchContainsInvite(in batch) &&
            !BotRelayPopEventsWalkOps.BatchContainsRelayMessageType(
                in batch,
                NetworkRelayMessageType.Query))
        {
            batch = default;
            fullyConsumed = false;
            peelPath = PeelPathRejectedFallback;
            return false;
        }

        return true;
    }

    public static bool TryExtractServerToBotApp(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket app)
    {
        app = default;
        var batch = default(BotRelayPopEventsWalkBatch);
        if (!TryPeelAllServerToBotApps(in wire, ref catalog, ref batch, out _, out _) ||
            batch.appCount < 1)
            return false;

        app = batch.GetApp(0);
        return !app.IsEmpty;
    }

    internal static FixedString64Bytes DescribePeelPath(int peelPath) =>
        peelPath switch
        {
            PeelPathMappingExact => "mappingExact",
            PeelPathMappingOffset => "mappingOffset",
            PeelPathCatalogOffset => "catalogOffset",
            PeelPathPopEventsWalk => "popEventsWalk",
            PeelPathRejectedFallback => "rejectedFallbackRaw",
            PeelPathPipelineInvite => "pipelineInvite",
            _ => "none"
        };

    private static int __MapInviteDecodeToPeelPath(int decodePath) =>
        decodePath switch
        {
            BotRelayInviteDecodeOps.DecodePathPipeline => PeelPathPipelineInvite,
            BotRelayInviteDecodeOps.DecodePathMappingExact => PeelPathMappingExact,
            BotRelayInviteDecodeOps.DecodePathMappingOffset => PeelPathMappingOffset,
            BotRelayInviteDecodeOps.DecodePathCatalogOffset => PeelPathCatalogOffset,
            _ => PeelPathNone
        };

    private static bool __TryPeelInboundMappingExact(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog,
        ref BotRelayPopEventsWalkBatch batch,
        out bool fullyConsumed)
    {
        fullyConsumed = false;
        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
        {
            var mapping = catalog.inboundMappings[i];
            ref var templateWire = ref BotRelayWireBytes.GetWire(ref catalog, mapping.wireIndex);
            if (!BotRelayWireBytes.Equals(ref templateWire, in wire))
                continue;

            ref var appWire = ref BotRelayWireBytes.GetWire(ref catalog, mapping.appIndex);
            if (!BotRelayWireBytes.TryCopyWireToPacket(ref appWire, out var app) ||
                app.IsEmpty ||
                !IsPeelCandidateApp(in app))
                return false;

            batch = default;
            if (!batch.TryAdd(in app))
                return false;

            fullyConsumed = true;
            return true;
        }

        return false;
    }

    private static bool __TryPeelCatalogOffsetApp(
        in BotRelayPacket wire,
        int inboundAppPayloadOffset,
        ref BotRelayPopEventsWalkBatch batch,
        out bool fullyConsumed)
    {
        fullyConsumed = false;
        if (!BotRelayWireBytes.TryExtractCatalogInboundApp(
                in wire,
                inboundAppPayloadOffset,
                out var app) ||
            app.IsEmpty ||
            !IsPeelCandidateApp(in app))
            return false;

        batch = default;
        if (!batch.TryAdd(in app))
            return false;

        fullyConsumed = true;
        return true;
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
        return batch.appCount > 0;
    }

    internal static bool IsPeelCandidateApp(in BotRelayPacket app)
    {
        if (!BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type))
            return BotRelayInviteDecodeOps.IsValidInviteApp(in app);

        if (type == (int)NetworkRelayMessageType.Connect)
            return false;

        if (type >= 0 && type <= (int)NetworkRelayMessageType.Query)
            return true;

        if (type == (int)ReplyMessageType.Invite)
            return BotRelayInviteDecodeOps.IsValidInviteApp(in app);

        if (type > (int)NetworkRelayMessageType.Query + 1 && type < (int)ReplyMessageType.Chat)
            return __IsClientRelayAppLargeEnough(in app, type);

        return type > (int)NetworkRelayMessageType.Query;
    }

    private static bool __IsClientRelayAppLargeEnough(in BotRelayPacket app, int type)
    {
        if (type <= (int)NetworkRelayMessageType.Query)
            return true;

        if (type > (int)NetworkRelayMessageType.Query + 1 && type < (int)ReplyMessageType.Chat)
            return app.length >= BotRelayMessageUtility.SendRelayHeaderMinBytes + 2;

        return true;
    }
}
