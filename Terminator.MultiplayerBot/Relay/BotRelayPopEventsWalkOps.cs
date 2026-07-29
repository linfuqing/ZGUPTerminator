using Unity.Burst;
using Unity.Mathematics;
using ZG;

/// <summary>
/// L2 PopEvents transport walk for Server→Bot inbound wires. Mirrors
/// <see cref="BotRelaySlotOps"/> / ZGUP <c>NetworkClient.PopEvents</c> ushort framing loop.
/// </summary>
[BurstCompile]
internal static class BotRelayPopEventsWalkOps
{
    public const int MaxAppsPerWire = 8;

    public static bool TryWalkServerToBotApps(
        in BotRelayPacket wire,
        ref BotRelayPopEventsWalkBatch batch,
        out bool fullyConsumed)
    {
        batch = default;
        fullyConsumed = false;
        if (wire.IsEmpty)
            return false;

        if (!__TryResolveBestStreamStart(in wire, out int streamStart))
            return false;

        return __TryWalkPopEventsAppsAt(
            in wire,
            streamStart,
            ref batch,
            out fullyConsumed);
    }

    public static bool TryWalkServerToBotAppsAndEnqueueInbound(
        in BotRelayPacket wire,
        int agentIndex,
        ref BotRelayFarmNative farm,
        ref BotRelayManager manager)
    {
        if (wire.IsEmpty)
            return false;

        if (!__TryResolveBestStreamStart(in wire, out int streamStart))
            return false;

        int enqueued = 0;
        int pos = streamStart;
        while (pos + sizeof(ushort) <= wire.length)
        {
            int size = wire.GetByte(pos) | (wire.GetByte(pos + 1) << 8);
            pos += sizeof(ushort);
            if (size < 1 || size > wire.length - pos)
                return enqueued > 0;

            BotRelayWireBytes.CopyAppFromOffset(in wire, pos, size, out var rawPayload);
            pos += size;

            if (!__TryNormalizeInboundApp(in rawPayload, out var app) || app.IsEmpty)
                continue;

            BotRelaySlotOps.TryEnqueueInbound(
                agentIndex,
                farm.inbound,
                farm.inboundCount,
                in app,
                ref manager);
            ++enqueued;
        }

        return enqueued > 0;
    }

    public static bool TryExtractFirstServerToBotApp(in BotRelayPacket wire, out BotRelayPacket app)
    {
        app = default;
        var batch = default(BotRelayPopEventsWalkBatch);
        if (!TryWalkServerToBotApps(in wire, ref batch, out bool fullyConsumed) ||
            batch.appCount < 1 ||
            !fullyConsumed)
            return false;

        app = batch.GetApp(0);
        return !app.IsEmpty;
    }

    /// <summary>
    /// Pipeline server→bot invite shells (live Play ≈104B): scan stream starts for a PopEvents-framed
    /// payload that validates as SendRelay Invite — not the generic multi-app walk scorer.
    /// </summary>
    public static bool TryExtractPipelineInviteShell(in BotRelayPacket wire, out BotRelayPacket app)
    {
        app = default;
        if (wire.IsEmpty ||
            !BotRelayWireBytes.LooksLikePipelineWire(in wire))
            return false;

        return __TryResolveInvitePipelineStreamStart(in wire, out _, out app);
    }

    private static bool __TryResolveInvitePipelineStreamStart(
        in BotRelayPacket wire,
        out int streamStart,
        out BotRelayPacket app)
    {
        streamStart = -1;
        app = default;
        if (wire.IsEmpty)
            return false;

        int maxStart = math.min(
            BotRelayWireBytes.PipelinePrefixBytes + BotRelayWireBytes.PipelineEnvelopeScanBytes,
            wire.length - 4);

        int bestStrictStage = -1;
        int bestStrictStart = -1;
        BotRelayPacket bestStrictApp = default;
        int bestFallbackStart = -1;
        int bestFallbackScore = int.MinValue;
        BotRelayPacket bestFallbackApp = default;

        for (int candidate = BotRelayWireBytes.PipelinePrefixBytes; candidate <= maxStart; ++candidate)
        {
            int pos = candidate;
            if (pos + sizeof(ushort) > wire.length)
            {
                continue;
            }

            int size = wire.GetByte(pos) | (wire.GetByte(pos + 1) << 8);
            pos += sizeof(ushort);
            if (size < 1 || size > wire.length - pos)
            {
                continue;
            }

            BotRelayWireBytes.CopyAppFromOffset(in wire, pos, size, out var rawPayload);
            if (!__LooksLikePipelineInvitePayload(in rawPayload))
            {
                continue;
            }

            if (BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in rawPayload, out var strictInvite))
            {
                if (strictInvite.stage > bestStrictStage ||
                    (strictInvite.stage == bestStrictStage &&
                     (bestStrictStart < 0 || candidate < bestStrictStart)))
                {
                    bestStrictStage = strictInvite.stage;
                    bestStrictStart = candidate;
                    bestStrictApp = rawPayload;
                }

                continue;
            }

            if (!__TryExtractInviteFromPopEventsPayload(in rawPayload, out var candidateApp) ||
                candidateApp.IsEmpty ||
                !BotRelayInviteDecodeOps.IsValidInviteApp(in candidateApp))
            {
                continue;
            }

            if (!__TryReadExtractedInviteApp(in candidateApp, out _))
            {
                continue;
            }

            int score = __ScorePipelineInviteCandidate(in candidateApp, candidate, in wire);
            if (score > bestFallbackScore ||
                (score == bestFallbackScore && (bestFallbackStart < 0 || candidate < bestFallbackStart)))
            {
                bestFallbackScore = score;
                bestFallbackStart = candidate;
                bestFallbackApp = candidateApp;
            }
        }

        if (bestStrictStart >= 0)
        {
            streamStart = bestStrictStart;
            app = bestStrictApp;
            return true;
        }

        if (bestFallbackStart < 0)
            return false;

        streamStart = bestFallbackStart;
        app = bestFallbackApp;
        return true;
    }

    private static bool __LooksLikePipelineInvitePayload(in BotRelayPacket rawPayload)
    {
        if (rawPayload.IsEmpty)
        {
            return false;
        }

        if (__PayloadStartsWithInviteJunkPrefix(in rawPayload))
        {
            return true;
        }

        if (BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in rawPayload, out _))
        {
            return true;
        }

        if (!BotRelayWireBytes.TryReadRelayMessageTypeHeader(in rawPayload, out int type) ||
            type != (int)ReplyMessageType.Invite)
        {
            return false;
        }

        return BotRelayInviteDecodeOps.TryReadInviteFromApp(in rawPayload, out _);
    }

    private static bool __TryExtractInviteFromPopEventsPayload(in BotRelayPacket rawPayload, out BotRelayPacket app)
    {
        app = default;
        if (rawPayload.IsEmpty)
            return false;

        int maxInner = math.min(
            BotRelayWireBytes.InvitePopEventsPayloadInnerScanBytes,
            rawPayload.length - BotRelayMessageUtility.MinSendRelayInviteAppBytes);

        int bestDeepSendRelayInner = -1;
        int bestShallowSendRelayInner = -1;
        int bestReplyInner = -1;
        int bestJunkReplyInner = -1;
        int bestTrimReplyInner = -1;
        bool hasJunkPrefix = __PayloadStartsWithInviteJunkPrefix(in rawPayload);
        int junkInner = BotRelayWireBytes.InviteReplyPayloadMinInnerOffset;
        int junkReplyInner = BotRelayWireBytes.InvitePipelineJunkAnchoredInnerOffset;
        int scanStart = 0;
        for (int inner = scanStart; inner <= maxInner; ++inner)
        {
            if (hasJunkPrefix && inner < junkInner)
            {
                continue;
            }

            int length = rawPayload.length - inner;
            if (length < BotRelayMessageUtility.MinSendRelayInviteAppBytes)
            {
                break;
            }

            BotRelayWireBytes.CopyAppFromOffset(in rawPayload, inner, length, out var candidate);
            if (hasJunkPrefix &&
                inner == junkInner &&
                candidate.length > 2 &&
                candidate.GetByte(0) == 0x04 &&
                __TryReadTrimmedJunkReplyCandidate(in candidate, out _))
            {
                if (bestTrimReplyInner < 0)
                {
                    bestTrimReplyInner = inner + 2;
                }
            }

            if (!__TryReadInviteCandidate(in candidate, inner, out var invite, out bool isSendRelay))
            {
                continue;
            }

            if (invite.channel < 0 || invite.stage < 0)
            {
                continue;
            }

            if (isSendRelay)
            {
                if (inner >= junkInner)
                {
                    if (bestDeepSendRelayInner < 0 || inner > bestDeepSendRelayInner)
                    {
                        bestDeepSendRelayInner = inner;
                    }
                }
                else if (!hasJunkPrefix)
                {
                    if (bestShallowSendRelayInner < 0 || inner < bestShallowSendRelayInner)
                    {
                        bestShallowSendRelayInner = inner;
                    }
                }
            }
            else
            {
                int replyInnerThreshold = hasJunkPrefix ? junkReplyInner : junkInner;
                if (inner >= replyInnerThreshold)
                {
                    if (bestJunkReplyInner < 0 || inner < bestJunkReplyInner)
                    {
                        bestJunkReplyInner = inner;
                    }
                }
                else if (bestReplyInner < 0 || inner < bestReplyInner)
                {
                    bestReplyInner = inner;
                }
            }
        }

        int bestSendRelayInner = bestDeepSendRelayInner >= 0
            ? bestDeepSendRelayInner
            : bestShallowSendRelayInner;

        if (bestSendRelayInner < 0 &&
            hasJunkPrefix &&
            __TryExtractJunkAnchoredSendRelay(in rawPayload, out app))
        {
            return !app.IsEmpty;
        }

        if (hasJunkPrefix &&
            __TryReadReplyInviteAtInner(in rawPayload, junkReplyInner, out var junkReplyInvite) &&
            bestSendRelayInner >= junkInner &&
            __TryReadSendRelayInviteAtInner(in rawPayload, bestSendRelayInner, out var junkSendRelayInvite) &&
            junkSendRelayInvite.id < BotRelayInviteDecodeOps.MinPlausibleInviteSenderId &&
            junkSendRelayInvite.channel == 0 &&
            junkReplyInvite.channel >= 0 &&
            junkReplyInvite.stage >= 0)
        {
            bestSendRelayInner = -1;
        }

        int bestInner = -1;
        if (bestSendRelayInner >= 0 &&
            __TryReadSendRelayInviteAtInner(in rawPayload, bestSendRelayInner, out var preferredSendRelay) &&
            preferredSendRelay.channel > 0)
        {
            bestInner = bestSendRelayInner;
        }
        else if (bestTrimReplyInner >= 0)
        {
            bestInner = bestTrimReplyInner;
        }
        else if (bestSendRelayInner >= 0)
        {
            bestInner = bestSendRelayInner;
        }
        else if (bestJunkReplyInner >= 0)
        {
            bestInner = bestJunkReplyInner;
        }
        else
        {
            bestInner = bestReplyInner;
        }

        if (bestInner < 0)
            return false;

        BotRelayWireBytes.CopyAppFromOffset(
            in rawPayload,
            bestInner,
            rawPayload.length - bestInner,
            out app);
        return !app.IsEmpty;
    }

    private static bool __TryReadTrimmedJunkReplyCandidate(
        in BotRelayPacket candidate,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        var options = BotRelayMessageUtility.InviteReadOptions.Default;
        var trimmed = __SliceApp(in candidate, 2);
        if (BotRelayMessageUtility.TryReadInviteFromPacket(in trimmed, out invite, options))
        {
            return invite.channel >= 0 &&
                   invite.stage >= 0 &&
                   (BotRelayInviteDecodeOps.IsPlausibleInvite(in invite) ||
                    invite.id < BotRelayInviteDecodeOps.MinPlausibleInviteSenderId);
        }

        return BotRelayInviteDecodeOps.TryReadJunkAnchoredPipelineInvite(in trimmed, out invite);
    }

    private static BotRelayPacket __SliceApp(in BotRelayPacket app, int offset)
    {
        var slice = default(BotRelayPacket);
        if (offset < 0 || offset >= app.length)
        {
            return slice;
        }

        slice.length = app.length - offset;
        for (int i = 0; i < slice.length; ++i)
        {
            slice.SetByte(i, app.GetByte(offset + i));
        }

        return slice;
    }

    private static bool __TryReadSendRelayInviteAtInner(
        in BotRelayPacket rawPayload,
        int inner,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        if (inner < 0 || inner >= rawPayload.length)
        {
            return false;
        }

        BotRelayWireBytes.CopyAppFromOffset(
            in rawPayload,
            inner,
            rawPayload.length - inner,
            out var candidate);
        return BotRelayInviteDecodeOps.TryReadStructuralSendRelayInvite(in candidate, out invite);
    }

    private static bool __TryReadReplyInviteAtInner(
        in BotRelayPacket rawPayload,
        int inner,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        if (inner < 0 || inner >= rawPayload.length)
        {
            return false;
        }

        BotRelayWireBytes.CopyAppFromOffset(
            in rawPayload,
            inner,
            rawPayload.length - inner,
            out var candidate);
        var options = BotRelayMessageUtility.InviteReadOptions.Default;
        return BotRelayMessageUtility.TryReadInviteFromPacket(in candidate, out invite, options) &&
               invite.channel >= 0 &&
               invite.stage >= 0;
    }

    private static bool __TryReadJunkAnchoredInviteAtInner(
        in BotRelayPacket rawPayload,
        int inner,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        if (inner < 0 || inner >= rawPayload.length)
        {
            return false;
        }

        BotRelayWireBytes.CopyAppFromOffset(
            in rawPayload,
            inner,
            rawPayload.length - inner,
            out var candidate);
        return BotRelayInviteDecodeOps.TryReadJunkAnchoredPipelineInvite(in candidate, out invite);
    }

    private static bool __TryReadInviteCandidate(
        in BotRelayPacket candidate,
        int innerOffset,
        out ReplyMessages.Invite invite,
        out bool isSendRelay)
    {
        invite = default;
        isSendRelay = false;
        var options = BotRelayMessageUtility.InviteReadOptions.Default;
        if (!BotRelayMessageUtility.TryReadInviteFromPacket(in candidate, out invite, options))
        {
            return false;
        }

        if (invite.channel < 0 || invite.stage < 0)
        {
            return false;
        }

        isSendRelay = BotRelayWireBytes.TryReadRelayMessageTypeHeader(in candidate, out int type) &&
                      type == (int)ReplyMessageType.Invite;
        return BotRelayInviteDecodeOps.IsPlausibleInvite(in invite) ||
               invite.id < BotRelayInviteDecodeOps.MinPlausibleInviteSenderId ||
               BotRelayInviteDecodeOps.TryReadJunkAnchoredPipelineInvite(in candidate, out _);
    }

    private static bool __TryReadExtractedInviteApp(
        in BotRelayPacket candidate,
        out ReplyMessages.Invite invite)
    {
        return BotRelayInviteDecodeOps.TryReadInviteFromApp(in candidate, out invite) &&
               (BotRelayInviteDecodeOps.IsPlausibleInvite(in invite) ||
                invite.id < BotRelayInviteDecodeOps.MinPlausibleInviteSenderId ||
                BotRelayInviteDecodeOps.TryReadJunkAnchoredPipelineInvite(in candidate, out _));
    }

    private static bool __PayloadStartsWithInviteJunkPrefix(in BotRelayPacket payload)
    {
        if (payload.length < BotRelayWireBytes.InviteReplyPayloadMinInnerOffset)
        {
            return false;
        }

        return payload.GetByte(0) == 0x0B &&
               payload.GetByte(1) == 0x8E &&
               payload.GetByte(2) == 0x7F &&
               payload.GetByte(3) == 0x8E &&
               payload.GetByte(4) == 0xF9 &&
               payload.GetByte(5) == 0xA4;
    }

    /// <summary>
    /// Live junk-prefixed payloads: scan post-junk offsets for Invite type header + plausible sender.
    /// </summary>
    private static bool __TryExtractJunkAnchoredSendRelay(
        in BotRelayPacket rawPayload,
        out BotRelayPacket app)
    {
        app = default;
        if (!__PayloadStartsWithInviteJunkPrefix(in rawPayload))
        {
            return false;
        }

        int junkInner = BotRelayWireBytes.InvitePipelineJunkAnchoredInnerOffset;
        int maxInner = math.min(
            junkInner + 8,
            rawPayload.length - BotRelayMessageUtility.MinSendRelayInviteAppBytes);

        int bestInner = -1;
        for (int inner = junkInner; inner <= maxInner; ++inner)
        {
            int length = rawPayload.length - inner;
            if (length < BotRelayMessageUtility.MinSendRelayInviteAppBytes)
            {
                break;
            }

            BotRelayWireBytes.CopyAppFromOffset(in rawPayload, inner, length, out var candidate);
            if (!__TryReadJunkAnchoredSendRelayCandidate(in candidate))
            {
                continue;
            }

            if (bestInner < 0 || inner > bestInner)
            {
                bestInner = inner;
            }
        }

        if (bestInner < 0)
        {
            return false;
        }

        BotRelayWireBytes.CopyAppFromOffset(
            in rawPayload,
            bestInner,
            rawPayload.length - bestInner,
            out app);
        return !app.IsEmpty;
    }

    private static bool __TryReadJunkAnchoredSendRelayCandidate(in BotRelayPacket candidate)
    {
        if (!BotRelayInviteDecodeOps.TryReadStructuralSendRelayInvite(in candidate, out var invite))
        {
            return false;
        }

        if (invite.channel < 0 || invite.stage < 0)
        {
            return false;
        }

        return BotRelayInviteDecodeOps.IsPlausibleInvite(in invite) ||
               invite.id < BotRelayInviteDecodeOps.MinPlausibleInviteSenderId;
    }

    private static bool __TryResolveBestStreamStart(in BotRelayPacket wire, out int streamStart)
    {
        streamStart = 0;
        if (wire.IsEmpty)
            return false;

        if (!BotRelayWireBytes.LooksLikeConnectHandshake(in wire) &&
            !BotRelayWireBytes.LooksLikePipelineWire(in wire))
            return true;

        int bestStart = -1;
        int bestScore = int.MinValue;
        bool inviteShell = BotRelayInviteDecodeOps.LooksLikeInviteShell(in wire);

        if (inviteShell &&
            __TryResolveInvitePipelineStreamStart(in wire, out int inviteStreamStart, out var inviteApp) &&
            __IsAuthoritativePipelineInviteApp(in inviteApp) &&
            !__TryWalkHasJoinAtCanonicalConnectOffset(in wire))
        {
            streamStart = inviteStreamStart;
            return true;
        }

        int maxStart = math.min(
            BotRelayWireBytes.PipelinePrefixBytes + BotRelayWireBytes.PipelineEnvelopeScanBytes,
            wire.length - 4);

        for (int candidate = BotRelayWireBytes.PipelinePrefixBytes; candidate <= maxStart; ++candidate)
        {
            var scratch = default(BotRelayPopEventsWalkBatch);
            if (!__TryWalkPopEventsAppsAt(
                    in wire,
                    candidate,
                    ref scratch,
                    out bool consumed) ||
                scratch.appCount < 1)
            {
                continue;
            }

            int score = __ScoreStreamWalkBatch(in scratch, consumed, inviteShell);
            if (candidate == BotRelayWireBytes.ResolvePopEventsStreamStart(in wire) &&
                __BatchContainsJoinRelayMessage(in scratch))
            {
                score += 50_000;
            }

            if (score > bestScore)
            {
                bestStart = candidate;
                bestScore = score;
            }
        }

        if (bestStart >= 0)
        {
            streamStart = bestStart;
            return true;
        }

        return false;
    }

    private static bool __IsAuthoritativePipelineInviteApp(in BotRelayPacket app)
    {
        if (BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out _))
        {
            return true;
        }

        return BotRelayInviteDecodeOps.TryReadJunkAnchoredPipelineInvite(in app, out var junkInvite) &&
               junkInvite.channel > 0;
    }

    internal static bool __TryWalkHasJoinAtCanonicalConnectOffset(in BotRelayPacket wire)
    {
        var batch = default(BotRelayPopEventsWalkBatch);
        int streamStart = BotRelayWireBytes.ResolvePopEventsStreamStart(in wire);
        if (!__TryWalkPopEventsAppsAt(
                in wire,
                streamStart,
                ref batch,
                out _) ||
            batch.appCount < 1)
        {
            return false;
        }

        return __BatchContainsJoinRelayMessage(in batch);
    }

    private static bool __TryWalkPopEventsAppsAt(
        in BotRelayPacket wire,
        int streamStart,
        ref BotRelayPopEventsWalkBatch batch,
        out bool fullyConsumed)
    {
        batch = default;
        fullyConsumed = false;
        if (streamStart < 0 || streamStart >= wire.length)
            return false;

        int pos = streamStart;
        while (pos + sizeof(ushort) <= wire.length)
        {
            int size = wire.GetByte(pos) | (wire.GetByte(pos + 1) << 8);
            pos += sizeof(ushort);
            if (size < 1 || size > wire.length - pos)
            {
                if (batch.appCount > 0)
                {
                    // A candidate stream start may accidentally decode one frame from UTP
                    // pipeline header bytes. Keep the partial batch for scoring/fallback, but
                    // do not rank it as an exact PopEvents stream. The real stream start must
                    // consume the wire cleanly.
                    fullyConsumed = false;
                    return true;
                }

                return false;
            }

            BotRelayWireBytes.CopyAppFromOffset(in wire, pos, size, out var rawPayload);
            pos += size;

            if (!__TryNormalizeInboundApp(in rawPayload, out var app) || app.IsEmpty)
                continue;

            if (!batch.TryAdd(in app))
                return false;
        }

        fullyConsumed = true;
        return batch.appCount > 0;
    }

    private static bool __TryNormalizeInboundApp(in BotRelayPacket rawPayload, out BotRelayPacket app)
    {
        app = default;
        if (rawPayload.IsEmpty)
        {
            return false;
        }

        if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in rawPayload, out int relayType) &&
            relayType > (int)NetworkRelayMessageType.Connect &&
            relayType <= (int)NetworkRelayMessageType.Query)
        {
            app = rawPayload;
            return true;
        }

        if (__PayloadStartsWithInviteJunkPrefix(in rawPayload) &&
            __TryExtractInviteFromPopEventsPayload(in rawPayload, out app) &&
            !app.IsEmpty)
        {
            app = __FinalizeInviteForInbox(in app);
            if (__IsValidInboundApp(in app))
            {
                return true;
            }
        }

        if (BotRelayWireBytes.TryNormalizePopEventsPayload(in rawPayload, out app) &&
            !app.IsEmpty)
        {
            app = __FinalizeInviteForInbox(in app);
            if (__IsValidInboundApp(in app))
            {
                return true;
            }
        }

        if (__IsValidInboundApp(in rawPayload))
        {
            app = __FinalizeInviteForInbox(in rawPayload);
            return true;
        }

        return false;
    }

    private static BotRelayPacket __FinalizeInviteForInbox(in BotRelayPacket app)
    {
        // Canonicalization is allowed only when the normalized app itself explicitly identifies as
        // Invite. ClientMessagePlay contains arbitrary level/scene UTF-8 bytes; the permissive
        // legacy Invite readers can reinterpret an interior slice as a structurally valid Invite
        // and expand a small Play app into a larger synthetic type=104 packet.
        if (!BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type) ||
            type != (int)ReplyMessageType.Invite)
        {
            return app;
        }

        if (BotRelayMessageUtility.TryCanonicalizeInviteForInbox(in app, out var sendRelay) &&
            !sendRelay.IsEmpty)
        {
            return sendRelay;
        }

        return app;
    }

    private static bool __IsValidInboundApp(in BotRelayPacket app)
    {
        if (!BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type))
            return BotRelayInviteDecodeOps.IsValidInviteApp(in app);

        if (type == (int)NetworkRelayMessageType.Connect)
            return false;

        if (type >= 0 && type <= (int)NetworkRelayMessageType.Query)
            return true;

        if (type == (int)ReplyMessageType.Invite)
            return BotRelayInviteDecodeOps.IsValidInviteApp(in app);

        if (type > (int)NetworkRelayMessageType.Query + 1 && type <= (int)ReplyMessageType.PlayerProperty)
            return app.length >= BotRelayMessageUtility.SendRelayHeaderMinBytes + 2;

        return type > (int)NetworkRelayMessageType.Query;
    }

    internal static bool BatchContainsInvite(in BotRelayPopEventsWalkBatch batch)
    {
        for (int i = 0; i < batch.appCount; ++i)
        {
            var app = batch.GetApp(i);
            if (BotRelayInviteDecodeOps.IsValidInviteApp(in app))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool BatchContainsRelayMessageType(
        in BotRelayPopEventsWalkBatch batch,
        NetworkRelayMessageType expected)
    {
        int expectedValue = (int)expected;
        for (int i = 0; i < batch.appCount; ++i)
        {
            var app = batch.GetApp(i);
            if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type) &&
                type == expectedValue)
            {
                return true;
            }
        }

        return false;
    }

    private static bool __BatchContainsJoinRelayMessage(in BotRelayPopEventsWalkBatch batch)
    {
        for (int i = 0; i < batch.appCount; ++i)
        {
            var app = batch.GetApp(i);
            if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type) &&
                type == (int)NetworkRelayMessageType.Join)
            {
                return true;
            }
        }

        return false;
    }

    private static bool __BatchContainsValidInviteForStreamScoring(in BotRelayPopEventsWalkBatch batch)
    {
        for (int i = 0; i < batch.appCount; ++i)
        {
            var app = batch.GetApp(i);
            if (BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type) &&
                type == (int)ReplyMessageType.Invite &&
                BotRelayInviteDecodeOps.IsValidInviteApp(in app))
            {
                return true;
            }
        }

        return false;
    }

    private static bool __BatchContainsChannelRelayMessage(in BotRelayPopEventsWalkBatch batch)
    {
        for (int i = 0; i < batch.appCount; ++i)
        {
            var app = batch.GetApp(i);
            if (!BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type))
            {
                continue;
            }

            if (type > (int)NetworkRelayMessageType.Connect &&
                type <= (int)NetworkRelayMessageType.Query)
            {
                return true;
            }
        }

        return false;
    }

    private static int __ScorePipelineInviteCandidate(
        in BotRelayPacket app,
        int streamStart,
        in BotRelayPacket wire)
    {
        if (!BotRelayInviteDecodeOps.TryReadInviteFromApp(in app, out var invite))
        {
            return int.MinValue / 2 + streamStart;
        }

        int score = 0;
        if (BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in app, out _))
        {
            score += 200_000;
        }
        else if (BotRelayInviteDecodeOps.IsValidInviteApp(in app))
        {
            score += 50_000;
        }

        if (invite.channel > 0)
        {
            score += 5_000;
        }

        if (invite.stage > 0)
        {
            score += invite.stage;
        }

        if (BotRelayInviteDecodeOps.IsPlausibleInvite(in invite))
        {
            score += 2_000;
        }

        if (streamStart == BotRelayWireBytes.ResolvePopEventsStreamStart(in wire))
        {
            score += 1_000;
        }

        return score;
    }

    private static int __ScoreStreamWalkBatch(
        in BotRelayPopEventsWalkBatch batch,
        bool fullyConsumed,
        bool inviteShell)
    {
        int score = batch.appCount;
        if (fullyConsumed)
        {
            score += 100;
        }

        if (__BatchContainsValidInviteForStreamScoring(in batch))
        {
            score += 10_000;
        }
        else if (__BatchContainsJoinRelayMessage(in batch))
        {
            score += 20_000;
        }
        else if (__BatchContainsChannelRelayMessage(in batch))
        {
            score += 8_000;
        }
        else if (inviteShell)
        {
            score -= 5_000;
        }

        return score;
    }
}
