using Unity.Collections;
using ZG;

internal static class BotRelayMessageParser
{
    public static void ParseTransportPayload(
        in BotRelayPacket packet,
        ref BotRelayInbox inbox,
        ref BotRelayConnection connection,
        in ClientHeader selfHeader,
        ref bool sentInitialStatus)
    {
        if (packet.IsEmpty)
        {
            inbox.NotifyConnected(ref connection, in selfHeader, ref sentInitialStatus);
            return;
        }

        if (packet.length <= 0)
            return;

        using var payload = new NativeArray<byte>(packet.length, Allocator.Temp);
        var packetLocal = packet;
        if (!packetLocal.TryCopyTo(payload))
            return;

        inbox.ParseRelayFrame(payload, in selfHeader);
    }
}
