using Unity.Burst;

internal static class BotRelayConnectCapture
{
    public static int ClientPacketCount => BotRelayManager.Instance.connectCaptureClientCount;

    public static int ServerPacketCount => BotRelayManager.Instance.connectCaptureServerCount;

    public static bool TryCopyServerWire(int index, out BotRelayPacket packet) =>
        __TryCopyWire(true, index, out packet);

    [BurstDiscard]
    public static void Begin() =>
        BotRelayManager.Instance.BeginConnectCapture();

    [BurstDiscard]
    public static BotRelayConnectWireState End() =>
        BotRelayManager.Instance.EndConnectCapture();

    [BurstDiscard]
    public static void Cancel() =>
        BotRelayManager.Instance.CancelConnectCapture();

    private static bool __TryCopyWire(bool serverSide, int index, out BotRelayPacket packet)
    {
        packet = default;
        ref var manager = ref BotRelayManager.Instance;
        int count = serverSide ? manager.connectCaptureServerCount : manager.connectCaptureClientCount;
        if (index < 0 || index >= count)
            return false;

        var lengths = serverSide ? manager.connectCaptureServerLengths : manager.connectCaptureClientLengths;
        var bytes = serverSide ? manager.connectCaptureServerBytes : manager.connectCaptureClientBytes;
        int length = lengths[index];
        packet.length = length;
        int offset = index * BotRelayPacket.MaxPayloadSize;
        for (int i = 0; i < length; ++i)
            packet.SetByte(i, bytes[offset + i]);
        return !packet.IsEmpty;
    }
}
