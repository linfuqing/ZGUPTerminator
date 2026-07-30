using Unity.Burst;
using ZG;

/// <summary>
/// Strict decoder for the current Server-to-Bot Invite protocol.
///
/// The only accepted application payload is the shape written by
/// <c>NetworkRelayServerIdentity.SendRelay</c>:
/// <c>[Invite type][relay type][sender id]</c>, byte flush, then the complete
/// <c>ReplyMessages.Invite</c> body including its 80-byte client header.
/// </summary>
[BurstCompile]
internal static class BotRelayInviteDecodeOps
{
    private static bool IsPlausibleInvite(in ReplyMessages.Invite invite) =>
        invite.id != 0 &&
        invite.stage >= 0 &&
        invite.channel >= 0 &&
        (int)invite.type >= (int)NetworkRelayType.All;

    internal static bool IsStrictSendRelayInviteApp(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        return BotRelayMessageUtility.TryReadSendRelayInviteFromPacket(in app, out invite) &&
               IsPlausibleInvite(in invite);
    }
}
