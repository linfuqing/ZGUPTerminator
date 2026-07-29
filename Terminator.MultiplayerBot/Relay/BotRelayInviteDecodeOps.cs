using Unity.Burst;
using Unity.Mathematics;
using ZG;

/// <summary>
/// Single authoritative Server→Bot Invite wire decoder shared by RouteSend peel and DrainInbound.
/// See Documentation~/MultiplayerBot/联机机器人-ServerToBot全帧解码.md (Burst-safe strict validation).
/// </summary>
[BurstCompile]
internal static class BotRelayInviteDecodeOps
{
    public const int InviteShellMinWireBytes = 80;
    public const int InviteShellMaxWireBytes = 160;

    public const int DecodePathNone = 0;
    public const int DecodePathPipeline = 1;
    public const int DecodePathMappingExact = 2;
    public const int DecodePathMappingOffset = 3;
    public const int DecodePathCatalogOffset = 4;

    public static bool LooksLikeInviteShell(in BotRelayPacket wire) =>
        wire.length >= InviteShellMinWireBytes &&
        wire.length <= InviteShellMaxWireBytes &&
        BotRelayWireBytes.LooksLikePipelineWire(in wire);

    /// <summary>
    /// Structural SendRelay Invite inside a pipeline shell. When canonicalize fails, generic
    /// PopEventsWalk mis-parses the same frame — invite must not fall through to generic walk.
    /// </summary>
    public static bool ShouldBlockGenericShellWalk(in BotRelayPacket wire)
    {
        if (!LooksLikeInviteShell(in wire))
        {
            return false;
        }

        if (!BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out var rawApp) ||
            rawApp.IsEmpty)
        {
            return false;
        }

        if (!TryReadInviteFromApp(in rawApp, out _))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Invite-sized pipeline shells that are not structural invites (Join ack, ChapterStage, Play).
    /// </summary>
    public static bool ShouldFallThroughToGenericShellWalk(in BotRelayPacket wire) =>
        LooksLikeInviteShell(in wire) && !ShouldBlockGenericShellWalk(in wire);

    public static bool TryDecodeServerToBotInviteWire(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket app,
        out int decodePath)
    {
        app = default;
        decodePath = DecodePathNone;
        if (!LooksLikeInviteShell(in wire))
            return false;

        if (BotRelayWireBytes.LooksLikePipelineWire(in wire) &&
            BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in wire, out var rawApp) &&
            __TryFinalizeInviteApp(in rawApp, in wire, ref catalog, out app))
        {
            decodePath = DecodePathPipeline;
            return true;
        }

        if (catalog.IsValid &&
            __TryDecodeViaMappingExact(in wire, ref catalog, out var mappedApp) &&
            __TryFinalizeInviteApp(in mappedApp, in wire, ref catalog, out app))
        {
            decodePath = DecodePathMappingExact;
            return true;
        }

        if (catalog.IsValid &&
            __TryDecodeViaMappingOffset(in wire, ref catalog, out var offsetMappedApp) &&
            __TryFinalizeInviteApp(in offsetMappedApp, in wire, ref catalog, out app))
        {
            decodePath = DecodePathMappingOffset;
            return true;
        }

        if (catalog.IsValid &&
            catalog.inboundAppPayloadOffset >= 0 &&
            BotRelayWireBytes.TryExtractCatalogInboundApp(
                in wire,
                catalog.inboundAppPayloadOffset,
                out var catalogApp) &&
            __TryFinalizeInviteApp(in catalogApp, in wire, ref catalog, out app))
        {
            decodePath = DecodePathCatalogOffset;
            return true;
        }

        return false;
    }

    public static bool TryFinalizeInviteAppForRoutePeel(
        in BotRelayPacket raw,
        in BotRelayPacket shellWire,
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket app)
    {
        return __TryFinalizeInviteApp(in raw, in shellWire, ref catalog, out app);
    }

    private static bool __TryFinalizeInviteApp(
        in BotRelayPacket raw,
        in BotRelayPacket shellWire,
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket app)
    {
        app = default;
        if (!IsValidInviteApp(in raw))
        {
            return false;
        }

        if (BotRelayMessageUtility.TryCanonicalizeInviteAppToSendRelay(in raw, in shellWire, ref catalog, out app))
        {
            return true;
        }

        if (!TryGetFirstConnectClientWire(ref catalog, out var connectWire))
        {
            return false;
        }

        return BotRelayMessageUtility.TryCanonicalizeInviteAppToSendRelay(in raw, in connectWire, ref catalog, out app);
    }

    internal static bool TryGetFirstConnectClientWire(
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket wire)
    {
        wire = default;
        if (!catalog.IsValid || catalog.connectClientWires.Length < 1)
        {
            return false;
        }

        ref var wireBlob = ref BotRelayWireBytes.GetWire(ref catalog, catalog.connectClientWires[0].wireIndex);
        return BotRelayWireBytes.TryCopyWireToPacket(ref wireBlob, out wire) && !wire.IsEmpty;
    }

    /// <summary>Live player/bot user ids are nine-digit scale; misaligned false positives are much smaller.</summary>
    internal const uint MinPlausibleInviteSenderId = 100_000_000u;

    public static bool IsValidInviteApp(in BotRelayPacket app)
    {
        if (app.IsEmpty)
            return false;

        if (IsStrictSendRelayInviteApp(in app, out _))
            return true;

        if (!__TryReadStructuralInviteFromApp(in app, out var invite))
            return false;

        if (IsPlausibleInvite(in invite))
            return true;

        return __IsRecoverablePipelineInvite(in app, in invite);
    }

    internal static bool TryReadInviteFromApp(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        return BotRelayMessageUtility.TryReadInviteFromPacket(
            in app,
            out invite,
            BotRelayMessageUtility.InviteReadOptions.Default) &&
               invite.channel >= 0 &&
               invite.stage >= 0;
    }

    internal static bool TryReadStructuralSendRelayInvite(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        var options = BotRelayMessageUtility.InviteReadOptions.Default;
        if (!BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(in app, out invite, options))
            return false;

        if (invite.channel < 0 || invite.stage < 0)
            return false;

        if (!BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type) ||
            type != (int)ReplyMessageType.Invite)
            return false;

        return true;
    }

    /// <summary>
    /// Live 104B junk-prefixed pipeline: after trimming packed Invite(104), Reply-shaped prefix may carry the
    /// server squad index in the compact sender field while body squad channel reads 0.
    /// </summary>
    internal static bool TryRecoverSquadChannelFromCompactReplyPrefix(ref ReplyMessages.Invite invite)
    {
        if (invite.channel != 0)
        {
            return false;
        }

        if (invite.id <= 0 || invite.id >= MinPlausibleInviteSenderId)
        {
            return false;
        }

        const int maxServerChannel = 31;
        if (invite.id > maxServerChannel)
        {
            return false;
        }

        invite.channel = (int)invite.id;
        return true;
    }

    internal static bool TryRecoverSquadChannelFromLivePipelinePrefix(
        in BotRelayPacket app,
        ref ReplyMessages.Invite invite)
    {
        if (invite.channel > 0)
        {
            return false;
        }

        if (app.length >= 2 &&
            app.GetByte(0) == 0xA2 &&
            app.GetByte(1) == 0x01)
        {
            invite.channel = 1;
            return true;
        }

        if (app.length >= 2 &&
            app.GetByte(0) == 0x0C &&
            app.GetByte(1) == 0x00)
        {
            invite.channel = 1;
            return true;
        }

        int inner = BotRelayWireBytes.InvitePipelineJunkAnchoredInnerOffset;
        if (__TryReadJunkAnchorChannelAt(in app, inner, ref invite))
        {
            return true;
        }

        if (inner > 0 && __TryReadJunkAnchorChannelAt(in app, inner - 1, ref invite))
        {
            return true;
        }

        return TryRecoverSquadChannelFromCompactReplyPrefix(ref invite);
    }

    private static bool __TryReadJunkAnchorChannelAt(
        in BotRelayPacket app,
        int offset,
        ref ReplyMessages.Invite invite)
    {
        if (offset < 0 || offset + 1 >= app.length)
        {
            return false;
        }

        byte b0 = app.GetByte(offset);
        byte b1 = app.GetByte(offset + 1);
        if ((b0 == 0xA2 && b1 == 0x01) || (b0 == 0x0C && b1 == 0x00))
        {
            invite.channel = 1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Live pipeline shell carries <c>A2 01</c> junk anchor when host <c>CreateOrJoin</c> used server channel 1;
    /// peeled app body may still read squad channel 0.
    /// </summary>
    internal static bool TryRecoverSquadChannelFromPipelineShell(
        in BotRelayPacket shellWire,
        ref ReplyMessages.Invite invite)
    {
        if (invite.channel != 0 || shellWire.IsEmpty)
        {
            return false;
        }

        if (BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in shellWire, out var rawShellApp) &&
            !rawShellApp.IsEmpty)
        {
            var probe = invite;
            if (TryRecoverSquadChannelFromLivePipelinePrefix(in rawShellApp, ref probe) &&
                probe.channel != 0)
            {
                invite.channel = probe.channel;
                return true;
            }
        }

        int inner = BotRelayWireBytes.InvitePipelineJunkAnchoredInnerOffset;
        int streamStart = BotRelayWireBytes.ResolvePopEventsStreamStart(in shellWire);
        int anchorInWire = streamStart + sizeof(ushort) + inner;
        if (anchorInWire + 1 < shellWire.length &&
            shellWire.GetByte(anchorInWire) == 0x0C &&
            shellWire.GetByte(anchorInWire + 1) == 0x00)
        {
            invite.channel = 1;
            return true;
        }

        int scanStart = math.max(streamStart + sizeof(ushort), inner - 4);
        int scanEnd = math.min(shellWire.length - 1, streamStart + sizeof(ushort) + inner + 32);
        for (int i = scanStart; i < scanEnd; ++i)
        {
            if (shellWire.GetByte(i) == 0xA2 && shellWire.GetByte(i + 1) == 0x01)
            {
                invite.channel = 1;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Live junk-prefixed pipeline slice at inner+8: [0xA2][0x01][Invite body…] after trimming packed Invite(104).
    /// Standard Reply/SendRelay readers misalign on this capture; recover squad channel 1 structurally.
    /// </summary>
    internal static bool TryReadJunkAnchoredPipelineInvite(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        if (app.IsEmpty || app.length < BotRelayMessageUtility.MinSendRelayInviteAppBytes)
        {
            return false;
        }

        bool isA2Anchor = app.GetByte(0) == 0xA2 && app.GetByte(1) == 0x01;
        bool is0cAnchor = app.GetByte(0) == 0x0C && app.GetByte(1) == 0x00;
        if (!isA2Anchor && !is0cAnchor)
        {
            return false;
        }

        invite.type = NetworkRelayType.All;
        invite.id = 0;
        invite.channel = 0;
        invite.levelID = 0;
        invite.stage = 0;

        if (isA2Anchor)
        {
            invite.channel = 1;
        }
        else if (is0cAnchor)
        {
            invite.channel = 1;
        }

        var options = BotRelayMessageUtility.InviteReadOptions.Default;
        var body = default(BotRelayPacket);
        body.length = app.length - 2;
        for (int i = 0; i < body.length; ++i)
        {
            body.SetByte(i, app.GetByte(i + 2));
        }

        if (BotRelayMessageUtility.TryReadReplyPayloadInviteFromPacket(in body, out var bodyInvite, options))
        {
            if (bodyInvite.stage >= 0)
            {
                invite.stage = bodyInvite.stage;
            }

            if (bodyInvite.levelID != 0)
            {
                invite.levelID = bodyInvite.levelID;
            }

            if (bodyInvite.channel >= 0)
            {
                invite.channel = bodyInvite.channel;
            }

            invite.text = bodyInvite.text;
            invite.header = bodyInvite.header;
        }

        return invite.channel >= 0 && invite.stage >= 0;
    }

    internal static bool TryRecoverInviteSenderFromPipelineShell(
        in BotRelayPacket shellWire,
        out uint senderId)
    {
        senderId = 0;
        if (shellWire.IsEmpty)
            return false;

        int maxOffset = math.min(shellWire.length - 1, 48);
        uint bestSender = 0;
        for (int offset = 1; offset <= maxOffset; ++offset)
        {
            if (!BotRelayWireBytes.TryReadPackedUIntAtOffset(in shellWire, offset, out uint candidate))
            {
                continue;
            }

            if (candidate >= MinPlausibleInviteSenderId && candidate < 2_000_000_000u)
            {
                bestSender = candidate;
            }
        }

        if (bestSender == 0)
        {
            return false;
        }

        senderId = bestSender;
        return true;
    }

    internal static bool TryRecoverInviteSenderFromStructuralInviteApp(
        in BotRelayPacket app,
        out uint senderId)
    {
        senderId = 0;
        if (app.IsEmpty)
        {
            return false;
        }

        var options = BotRelayMessageUtility.InviteReadOptions.Default;
        if (BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(in app, out var sendRelay, options) &&
            sendRelay.id >= MinPlausibleInviteSenderId)
        {
            senderId = sendRelay.id;
            return true;
        }

        if (__PayloadStartsWithInviteJunkPrefix(in app))
        {
            const int junkBytes = 6;
            if (app.length > junkBytes)
            {
                BotRelayWireBytes.CopyAppFromOffset(
                    in app,
                    junkBytes,
                    app.length - junkBytes,
                    out var afterJunk);
                if (BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(in afterJunk, out sendRelay, options) &&
                    sendRelay.id >= MinPlausibleInviteSenderId)
                {
                    senderId = sendRelay.id;
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool TryRecoverInviteSenderFromAsciiDigitTail(
        in BotRelayPacket app,
        out uint senderId)
    {
        senderId = 0;
        if (app.IsEmpty)
        {
            return false;
        }

        int bestStart = -1;
        int bestLength = 0;
        int runStart = -1;
        int runLength = 0;
        int scanEnd = math.min(app.length, 96);
        for (int i = 0; i < scanEnd; ++i)
        {
            byte b = app.GetByte(i);
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                if (runLength == 0)
                {
                    runStart = i;
                }

                runLength++;
            }
            else
            {
                if (runLength > bestLength)
                {
                    bestStart = runStart;
                    bestLength = runLength;
                }

                runLength = 0;
            }
        }

        if (runLength > bestLength)
        {
            bestStart = runStart;
            bestLength = runLength;
        }

        if (bestLength < 9 || bestStart < 0)
        {
            return false;
        }

        uint parsed = 0;
        for (int i = 0; i < bestLength; ++i)
        {
            byte b = app.GetByte(bestStart + i);
            parsed = parsed * 10u + (uint)(b - (byte)'0');
        }

        if (parsed < MinPlausibleInviteSenderId || parsed >= 2_000_000_000u)
        {
            return false;
        }

        senderId = parsed;
        return true;
    }

    internal static bool TryRecoverInviteSenderFromInviteApp(
        in BotRelayPacket app,
        out uint senderId) =>
        TryRecoverInviteSenderFromStructuralInviteApp(in app, out senderId);

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

    private static bool __IsRecoverablePipelineInvite(
        in BotRelayPacket app,
        in ReplyMessages.Invite invite) =>
        invite.channel >= 0 &&
        invite.stage >= 0 &&
        (IsPlausibleInvite(in invite) ||
         TryReadJunkAnchoredPipelineInvite(in app, out _) ||
         TryReadStructuralSendRelayInvite(in app, out _) ||
         BotRelayMessageUtility.TryReadReplyPayloadInviteFromPacket(
             in app,
             out _,
             BotRelayMessageUtility.InviteReadOptions.Default));

    internal static bool IsPlausibleInvite(in ReplyMessages.Invite invite) =>
        invite.channel >= 0 &&
        invite.stage >= 0 &&
        invite.id >= MinPlausibleInviteSenderId &&
        invite.id < 2_000_000_000u &&
        (int)invite.type >= 0 &&
        (int)invite.type <= (int)NetworkRelayType.Channel;

    // SendRelay's outer relay field is a delivery route. For a targeted Invite the
    // server writes the recipient's RelayType (for example 1215), not All/Channel.
    // Keep the conventional enum check for heuristic recovery, but accept any non-negative
    // route here once the complete SendRelay structure has been decoded.
    private static bool __IsPlausibleServerRoutedInvite(in ReplyMessages.Invite invite) =>
        invite.channel >= 0 &&
        invite.stage >= 0 &&
        invite.id >= MinPlausibleInviteSenderId &&
        invite.id < 2_000_000_000u &&
        (int)invite.type >= 0;

    internal static bool IsStrictSendRelayInviteApp(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        var options = BotRelayMessageUtility.InviteReadOptions.Default;
        if (!BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(in app, out invite, options))
            return false;

        if (!__IsPlausibleServerRoutedInvite(in invite))
            return false;

        if (!BotRelayWireBytes.TryReadRelayMessageTypeHeader(in app, out int type) ||
            type != (int)ReplyMessageType.Invite)
            return false;

        return invite.channel >= 0 && invite.stage >= 0;
    }

    private static bool __TryReadStructuralInviteFromApp(in BotRelayPacket app, out ReplyMessages.Invite invite)
    {
        return TryReadInviteFromApp(in app, out invite);
    }

    internal static string DescribeDecodePath(int decodePath) =>
        decodePath switch
        {
            DecodePathPipeline => "pipelineInvite",
            DecodePathMappingExact => "mappingExact",
            DecodePathMappingOffset => "mappingOffset",
            DecodePathCatalogOffset => "catalogOffset",
            _ => "none"
        };

    private static bool __TryDecodeViaMappingExact(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket app)
    {
        app = default;
        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
        {
            var mapping = catalog.inboundMappings[i];
            ref var templateWire = ref BotRelayWireBytes.GetWire(ref catalog, mapping.wireIndex);
            if (!BotRelayWireBytes.Equals(ref templateWire, in wire))
                continue;

            ref var appWire = ref BotRelayWireBytes.GetWire(ref catalog, mapping.appIndex);
            if (!BotRelayWireBytes.TryCopyWireToPacket(ref appWire, out app) || app.IsEmpty)
                return false;

            return IsValidInviteApp(in app);
        }

        return false;
    }

    private static bool __TryDecodeViaMappingOffset(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog,
        out BotRelayPacket app)
    {
        app = default;
        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
        {
            var mapping = catalog.inboundMappings[i];
            ref var templateWireBlob = ref BotRelayWireBytes.GetWire(ref catalog, mapping.wireIndex);
            ref var templateAppWire = ref BotRelayWireBytes.GetWire(ref catalog, mapping.appIndex);
            if (templateWireBlob.length != wire.length)
                continue;

            if (!BotRelayWireBytes.TryCopyWireToPacket(ref templateAppWire, out var templateApp) ||
                templateApp.IsEmpty ||
                !BotRelayWireBytes.TryReadRelayMessageTypeHeader(in templateApp, out int templateType) ||
                templateType != (int)ReplyMessageType.Invite)
                continue;

            if (!BotRelayWireBytes.TryCopyWireToPacket(ref templateWireBlob, out var templateWire) ||
                templateWire.IsEmpty ||
                !BotRelayWireBytes.TryResolveInboundAppPayloadOffset(
                    in templateWire,
                    in templateApp,
                    out int offset))
                continue;

            if (!BotRelayWireBytes.TryExtractCatalogInboundApp(in wire, offset, out app) || app.IsEmpty)
                continue;

            return IsValidInviteApp(in app);
        }

        return false;
    }
}
