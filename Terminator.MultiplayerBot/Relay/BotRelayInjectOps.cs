using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// App-layer relay inject helpers shared by the agent tick, the replay load job and the
/// replay tick job. The bot's outbound UTP wire does not reliably reach the server (that is
/// the reason injection was introduced for Join). Every bot-to-server message the host needs
/// to observe (Join / Status / PlayerProperty / replay reply frames) must therefore be pushed
/// straight into the NetworkRelayServer inject queue, which is drained into
/// NetworkRelayServerHandler.Read next frame.
/// </summary>
internal static class BotRelayInjectOps
{
    private const int MaxInjectBytes = BotRelayPacket.MaxPayloadSize;

    /// <summary>
    /// Inject a control message encoded as [packedInt type][packedInt arg?] exactly as the real
    /// client writes it (Join / Status / Mismatch).
    /// </summary>
    public static unsafe void InjectControlMessage(
        ref NativeQueue<BotRelayInject>.ParallelWriter writer,
        uint userID,
        int type,
        int arg,
        bool hasArg)
    {
        var model = StreamCompressionModel.Default;
        byte* scratch = stackalloc byte[32];
        var streamWriter = new DataStreamWriter(scratch, 32);
        streamWriter.WritePackedInt(type, model);
        if (hasArg)
            streamWriter.WritePackedInt(arg, model);

        if (streamWriter.HasFailedWrites)
            return;

        int length = streamWriter.Length;

        BotRelayInject inject;
        inject.id = userID;
        inject.packet = default;
        inject.packet.length = length;
        for (int i = 0; i < length; ++i)
            inject.packet.SetByte(i, scratch[i]);

        writer.Enqueue(inject);
    }

    /// <summary>
    /// Inject the client-side ApplyMatch reply header. Unlike relay controls, ApplyMatch is a
    /// client message with a reply header, so it must not be encoded as a NetworkRelayMessageType.
    /// </summary>
    public static unsafe void InjectApplyMatch(
        ref NativeQueue<BotRelayInject>.ParallelWriter writer,
        uint userID)
    {
        byte* scratch = stackalloc byte[32];
        var streamWriter = new DataStreamWriter(scratch, 32);
        BotRelayMessageUtility.WriteApplyMatch(ref streamWriter);

        if (streamWriter.HasFailedWrites)
            return;

        BotRelayInject inject;
        inject.id = userID;
        inject.packet = default;
        inject.packet.length = streamWriter.Length;
        for (int i = 0; i < streamWriter.Length; ++i)
            inject.packet.SetByte(i, scratch[i]);

        writer.Enqueue(inject);
    }

    /// <summary>
    /// Inject a fully composed current app packet ([packedInt type][packedInt relayType][body]).
    /// Used for PlayerProperty and replay reply frames; no outbound UTP representation is built.
    /// </summary>
    public static bool InjectAppPacket(
        ref NativeQueue<BotRelayInject>.ParallelWriter writer,
        uint userID,
        in BotRelayPacket packet)
    {
        int length = packet.length;
        if (length < 1 || length > MaxInjectBytes)
            return false;

        BotRelayInject inject;
        inject.id = userID;
        inject.packet = packet;

        writer.Enqueue(inject);
        return true;
    }
}
