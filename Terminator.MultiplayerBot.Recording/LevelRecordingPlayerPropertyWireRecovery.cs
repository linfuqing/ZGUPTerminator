using Unity.Collections;
using ZG;

/// <summary>
/// Builds the recorded relay payload used by PlayerProperty validation fixtures.
/// Production recording only accepts payloads observed by the EndWrite capture journal.
/// </summary>
internal static class LevelRecordingPlayerPropertyWireRecovery
{
    public static bool TryBuildRecordedPayload(in LevelPlayerProperty property, out byte[] payload)
    {
        payload = null;
        if (LevelRecordingReplayPropertyOps.IsPropertyEmpty(in property))
        {
            return false;
        }

        using var scratch = new NativeArray<byte>(8192, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        writer.WriteReplyHeader((int)ReplyMessageType.PlayerProperty, NetworkRelayType.Channel);
        property.Write(ref writer, StreamCompressionModel.Default);

        if (writer.Length < 1)
        {
            return false;
        }

        payload = scratch.GetSubArray(0, writer.Length).ToArray();
        return payload.Length > 0;
    }
}
