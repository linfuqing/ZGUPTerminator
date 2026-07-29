using Unity.Collections;
using ZG;

internal static class ReplyMessageReplayInjector
{
    public static bool TryParsePlayerProperty(byte[] recordedPayload, out LevelPlayerProperty property)
    {
        return TryParsePlayerProperty(recordedPayload, out property, logFailures: true);
    }

    public static bool TryParsePlayerProperty(
        byte[] recordedPayload,
        out LevelPlayerProperty property,
        bool logFailures)
    {
        property = default;
        if (recordedPayload == null || recordedPayload.Length == 0)
        {
            return false;
        }

        if (__TryParsePlayerPropertyAfterHeaderSkip(recordedPayload, skipSenderId: false, logFailures, out property))
        {
            return true;
        }

        return __TryParsePlayerPropertyAfterHeaderSkip(recordedPayload, skipSenderId: true, logFailures, out property);
    }

    private static bool __TryParsePlayerPropertyAfterHeaderSkip(
        byte[] recordedPayload,
        bool skipSenderId,
        bool logFailures,
        out LevelPlayerProperty property)
    {
        property = default;
        using var source = new NativeArray<byte>(recordedPayload, Allocator.Temp);
        var reader = new DataStreamReader(source);
        if (!LevelRecordingEditorReplayPayloadUtility.TrySkipRecordedReplyHeader(ref reader))
            return false;

        if (skipSenderId)
        {
            var streamCompressionModel = StreamCompressionModel.Default;
            if (reader.GetBytesRead() >= reader.Length)
                return false;

            reader.ReadPackedUInt(streamCompressionModel);
            reader.Flush();
            if (reader.GetBytesRead() >= reader.Length)
                return false;
        }

        try
        {
            property = new LevelPlayerProperty(ref reader, StreamCompressionModel.Default);
            return true;
        }
        catch (System.Exception exception)
        {
            if (!skipSenderId)
            {
                return false;
            }

            if (logFailures)
            {
                BotReplayLog.Warn(
                    $"Skip invalid PlayerProperty frame ({recordedPayload.Length} bytes): {exception.Message}");
            }

            return false;
        }
    }

    public static bool TryApplyPlayerProperty(byte[] recordedPayload, bool promoteJoined = true)
    {
        if (!TryParsePlayerProperty(recordedPayload, out var property))
            return false;

        ApplySharedProperty(in property, promoteJoined);
        return true;
    }

    public static void ApplySharedProperty(in LevelPlayerProperty property, bool promoteJoined)
    {
        LevelPlayerShared<RemotePlayer>.property = property;

        // LevelPlayerSystem promotes Joined→StandBy after spawn; do not demote during in-level replay.
        if (promoteJoined && RemotePlayer.status != RemotePlayer.Status.StandBy)
            RemotePlayer.SetStatus(RemotePlayer.Status.Joined);
    }
}
