using Unity.Burst;
using Unity.Mathematics;
using ZG;

/// <summary>
/// Authoritative squad-channel resolution for misaligned live pipeline Invites.
/// Priority: structured invite.channel → session/catalog Create/Join relay → pipeline inner
/// channel field → junk-anchor fallback (last resort).
/// </summary>
[BurstCompile]
internal static class BotRelayInviteChannelAuthority
{
    public const int UnsetChannel = -1;
    public const int MaxServerChannel = 31;

    public static bool TryResolveSquadChannel(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire,
        in BotSessionState session,
        ref BotRelayCatalogBlob catalog)
    {
        if (__TryResolveSquadChannelCore(ref invite, in app, in shellWire, in session))
        {
            return true;
        }

        if (__TryApplyCatalogHostChannel(ref invite, ref catalog))
        {
            return true;
        }

        return __TryApplyJunkAnchorFallback(ref invite, in app, in shellWire);
    }

    public static bool TryResolveSquadChannel(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire,
        ref BotRelayCatalogBlob catalog)
    {
        var session = default(BotSessionState);
        session.pendingHostSquadChannel = UnsetChannel;
        return TryResolveSquadChannel(ref invite, in app, in shellWire, in session, ref catalog);
    }

    public static bool TryResolveSquadChannel(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire,
        in BotSessionState session)
    {
        if (__TryResolveSquadChannelCore(ref invite, in app, in shellWire, in session))
        {
            return true;
        }

        return __TryApplyJunkAnchorFallback(ref invite, in app, in shellWire);
    }

    public static bool TryResolveSquadChannel(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire)
    {
        var session = default(BotSessionState);
        session.pendingHostSquadChannel = UnsetChannel;
        return TryResolveSquadChannel(ref invite, in app, in shellWire, in session);
    }

    private static bool __TryResolveSquadChannelCore(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire,
        in BotSessionState session)
    {
        // A structurally decoded positive channel is authoritative. In particular, a
        // targeted Server.SendRelay Invite has a flushed body boundary; byte scans over
        // its text/header payload can otherwise manufacture a false host channel (often 0).
        if (invite.channel > 0)
        {
            return true;
        }

        if (__TryApplySessionHostCreateChannel(ref invite, in session))
        {
            return true;
        }

        if (__TryApplyShellBatchHostChannel(ref invite, in shellWire))
        {
            return true;
        }

        if (__TryApplyAppScanHostChannel(ref invite, in app))
        {
            return true;
        }

        if (invite.channel > 0)
        {
            return true;
        }

        if (invite.channel == 0)
        {
            if (__TryApplyEmbeddedCreateChannel(ref invite, in app, in shellWire))
            {
                return true;
            }

            if (__TryUpgradeChannelZeroFromJunkAnchor(ref invite, in app, in shellWire))
            {
                return true;
            }

            return true;
        }

        if (__TryApplyPipelineInnerHostChannel(ref invite, in app))
        {
            return true;
        }

        return invite.channel >= 0;
    }

    /// <summary>
    /// Host Create relay channel embedded via <c>04 B9</c> prefix (including channel 0).
    /// </summary>
    private static bool __TryApplyEmbeddedCreateChannel(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire)
    {
        if (BotRelayWireBytes.TryScanPipelineEmbeddedHostCreateChannel(in app, out int embeddedChannel))
        {
            invite.channel = embeddedChannel;
            return true;
        }

        if (!shellWire.IsEmpty &&
            BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in shellWire, out var rawShellApp) &&
            BotRelayWireBytes.TryScanPipelineEmbeddedHostCreateChannel(in rawShellApp, out embeddedChannel))
        {
            invite.channel = embeddedChannel;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Structured squad channel 0 with no embedded Create prefix may still be junk-misaligned channel 1.
    /// </summary>
    private static bool __TryUpgradeChannelZeroFromJunkAnchor(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire)
    {
        if (BotRelayInviteDecodeOps.TryRecoverSquadChannelFromLivePipelinePrefix(in app, ref invite) &&
            invite.channel > 0)
        {
            return true;
        }

        if (!shellWire.IsEmpty &&
            BotRelayInviteDecodeOps.TryRecoverSquadChannelFromPipelineShell(in shellWire, ref invite) &&
            invite.channel > 0)
        {
            return true;
        }

        return false;
    }

    public static void RecordRemoteHostCreateOrJoin(
        ref BotSessionState session,
        int channel,
        uint remoteUserId)
    {
        if (channel < 0 || channel > MaxServerChannel)
        {
            return;
        }

        session.pendingHostSquadChannel = channel;
        if (remoteUserId != 0)
        {
            session.pendingHostUserId = remoteUserId;
        }
    }

    public static void ClearPendingHostChannel(ref BotSessionState session)
    {
        session.pendingHostSquadChannel = UnsetChannel;
        session.pendingHostUserId = 0;
    }

    private static bool __TryApplySessionHostCreateChannel(
        ref ReplyMessages.Invite invite,
        in BotSessionState session)
    {
        if (session.pendingHostSquadChannel < 0)
        {
            return false;
        }

        if (invite.id != 0 &&
            session.pendingHostUserId != 0 &&
            invite.id != session.pendingHostUserId)
        {
            return false;
        }

        invite.channel = session.pendingHostSquadChannel;
        return true;
    }

    /// <summary>
    /// Live pipeline shells embed the host Create relay channel at the fixed inner offset
    /// (see <see cref="BotRelayWireBytes.InvitePipelineJunkAnchoredInnerOffset"/>).
    /// </summary>
    private static bool __TryApplyPipelineInnerHostChannel(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app)
    {
        if (!BotRelayWireBytes.TryReadAuthoritativePipelineHostChannel(in app, out int channel))
        {
            return false;
        }

        invite.channel = channel;
        return true;
    }

    private static bool __TryApplyShellBatchHostChannel(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket shellWire)
    {
        if (!shellWire.IsEmpty &&
            BotRelayPopEventsWalkOps.TryExtractPipelineInviteShell(in shellWire, out var rawShellApp) &&
            BotRelayWireBytes.TryScanPipelineEmbeddedHostCreateChannel(in rawShellApp, out int embeddedChannel))
        {
            invite.channel = embeddedChannel;
            return true;
        }

        if (shellWire.IsEmpty)
        {
            return false;
        }

        var batch = default(BotRelayPopEventsWalkBatch);
        if (!BotRelayPopEventsWalkOps.TryWalkServerToBotApps(in shellWire, ref batch, out _) ||
            batch.appCount < 1)
        {
            return false;
        }

        for (int i = 0; i < batch.appCount; ++i)
        {
            var app = batch.GetApp(i);
            if (BotRelayWireBytes.TryScanRelayCreateOrJoinChannel(in app, out int channel))
            {
                invite.channel = channel;
                return true;
            }
        }

        return false;
    }

    private static bool __TryApplyAppScanHostChannel(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app)
    {
        if (BotRelayWireBytes.TryScanPipelineEmbeddedHostCreateChannel(in app, out int embeddedChannel))
        {
            invite.channel = embeddedChannel;
            return true;
        }

        if (!BotRelayWireBytes.TryScanRelayCreateOrJoinChannel(in app, out int channel))
        {
            return false;
        }

        invite.channel = channel;
        return true;
    }

    private static bool __TryApplyCatalogHostChannel(
        ref ReplyMessages.Invite invite,
        ref BotRelayCatalogBlob catalog)
    {
        if (!catalog.IsValid)
        {
            return false;
        }

        for (int i = 0; i < catalog.inboundMappings.Length; ++i)
        {
            var mapping = catalog.inboundMappings[i];
            ref var appWire = ref BotRelayWireBytes.GetWire(ref catalog, mapping.appIndex);
            if (!BotRelayWireBytes.TryCopyWireToPacket(ref appWire, out var app) ||
                app.IsEmpty)
            {
                continue;
            }

            if (BotRelayWireBytes.TryScanRelayCreateOrJoinChannel(in app, out int channel))
            {
                invite.channel = channel;
                return true;
            }
        }

        return false;
    }

    private static bool __TryApplyJunkAnchorFallback(
        ref ReplyMessages.Invite invite,
        in BotRelayPacket app,
        in BotRelayPacket shellWire)
    {
        if (BotRelayInviteDecodeOps.TryRecoverSquadChannelFromLivePipelinePrefix(in app, ref invite) &&
            invite.channel > 0)
        {
            return true;
        }

        if (BotRelayInviteDecodeOps.TryRecoverSquadChannelFromPipelineShell(in shellWire, ref invite) &&
            invite.channel > 0)
        {
            return true;
        }

        return BotRelayInviteDecodeOps.TryRecoverSquadChannelFromCompactReplyPrefix(ref invite) &&
               invite.channel >= 0;
    }
}
