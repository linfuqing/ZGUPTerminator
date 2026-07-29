using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Applies inbound relay apps to <see cref="ReplyMessageShared"/> / <see cref="LevelPlayerShared{RemotePlayer}"/>
/// using the same field order as <c>ReplyMessages.Collect</c> (host Create / Join paths).
/// PlayMode host sim only — not used on the bot relay hot path (M6).
/// </summary>
internal static class BotRelayHostMessageParser
{
    public static bool TryReadCreateFields(in BotRelayPacket app, out int channel, out int channelFlag)
    {
        channel = 0;
        channelFlag = 0;
        if (app.IsEmpty || app.length < 2)
            return false;

        var bytes = new NativeArray<byte>(app.length, Allocator.Temp);
        try
        {
            for (int i = 0; i < app.length; ++i)
                bytes[i] = app.GetByte(i);

            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            int type = reader.ReadPackedInt(model);
            if ((NetworkRelayMessageType)type != NetworkRelayMessageType.Create)
                return false;

            if (reader.GetBytesRead() >= reader.Length)
                return false;

            channel = reader.ReadPackedInt(model);
            if (reader.GetBytesRead() >= reader.Length)
                return false;

            channelFlag = reader.ReadPackedInt(model);
            return true;
        }
        finally
        {
            bytes.Dispose();
        }
    }

    public static bool TryApplyForHost(in BotRelayPacket app)
    {
        if (app.IsEmpty || app.length < 2)
            return false;

        var bytes = new NativeArray<byte>(app.length, Allocator.Temp);
        try
        {
            for (int i = 0; i < app.length; ++i)
                bytes[i] = app.GetByte(i);

            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            int type = reader.ReadPackedInt(model);

            switch ((NetworkRelayMessageType)type)
            {
                case NetworkRelayMessageType.Create:
                    return __ApplyCreate(ref reader, in model);
                case NetworkRelayMessageType.Join:
                    return __ApplyJoin(ref reader, in model);
                case NetworkRelayMessageType.Status:
                    if (reader.Length - reader.GetBytesRead() < 1)
                        return false;
                    ReplyMessageShared.SetChannelStatus(reader.ReadPackedInt(model));
                    return true;
                default:
                    return false;
            }
        }
        finally
        {
            bytes.Dispose();
        }
    }

    private static bool __ApplyCreate(ref DataStreamReader reader, in StreamCompressionModel model)
    {
        if (reader.Length - reader.GetBytesRead() < 1)
            return false;

        ReplyMessageShared.isHost = true;
        ReplyMessageShared.remotePlayerCount = 0;
        ReplyMessageShared.channel = reader.ReadPackedInt(model);
        ReplyMessageShared.SetChannelFlag(reader.ReadPackedInt(model));
        return true;
    }

    private static bool __ApplyJoin(ref DataStreamReader reader, in StreamCompressionModel model)
    {
        if (reader.Length - reader.GetBytesRead() < 2)
            return false;

        int channel = reader.ReadPackedInt(model);
        int channelFlag = reader.ReadPackedInt(model);

        if (channel != ReplyMessageShared.channel)
            return false;

        if (reader.GetBytesRead() >= reader.Length)
            return false;

        reader.Flush();
        uint remoteUserId = reader.ReadPackedUInt(model);
        ++ReplyMessageShared.remotePlayerCount;
        LevelPlayerShared<RemotePlayer>.id = remoteUserId;
        LevelPlayerShared<RemotePlayer>.channelFlag = channelFlag;

        if (reader.Length - reader.GetBytesRead() < 1)
            return true;

        var remoteHeader = new ClientHeader(0, ref reader, model);
        LevelPlayerShared<RemotePlayer>.header = remoteHeader.ToLevelPlayerHeader();
        return true;
    }
}
