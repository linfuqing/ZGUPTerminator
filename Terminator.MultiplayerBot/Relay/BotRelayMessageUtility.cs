using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Protocol reads/writes shared by the Burst bot relay path.
/// Invite parsing intentionally supports only the current complete SendRelay layout.
/// </summary>
internal static class BotRelayMessageUtility
{
    public const int ClientHeaderFixedBytes = 80;
    public const int SendRelayHeaderMinBytes = 3;
    // Packed sender ids have variable width; the complete-body parser below is authoritative.
    // This lower bound exists only to avoid unsafe reader attempts before the fixed header.
    public const int MinSendRelayInviteAppBytes =
        SendRelayHeaderMinBytes + ClientHeaderFixedBytes + sizeof(ushort);

    public static void WriteSendRelayHeader(
        ref DataStreamWriter writer,
        int messageType,
        NetworkRelayType relayType,
        uint senderId,
        in StreamCompressionModel model)
    {
        writer.WritePackedInt(messageType, model);
        writer.WritePackedInt((int)relayType, model);
        writer.WritePackedUInt(senderId, model);
    }

    public static bool TryReadSendRelayHeader(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out int messageType,
        out NetworkRelayType relayType,
        out uint senderId)
    {
        messageType = 0;
        relayType = default;
        senderId = 0;
        if (reader.Length - reader.GetBytesRead() < SendRelayHeaderMinBytes)
            return false;

        messageType = reader.ReadPackedInt(model);
        relayType = (NetworkRelayType)reader.ReadPackedInt(model);
        senderId = reader.ReadPackedUInt(model);
        return !reader.HasFailedReads;
    }

    public static void WriteInviteBody(
        ref DataStreamWriter writer,
        uint levelID,
        int stage,
        int squadChannel,
        in FixedString512Bytes text,
        in FixedBytes80 header,
        in StreamCompressionModel model)
    {
        writer.WritePackedUInt(levelID, model);
        writer.WritePackedInt(stage, model);
        writer.WritePackedInt(squadChannel, model);
        writer.WriteFixedString512(text);
        header.Write(ref writer);
    }

    public static void WriteSendRelayInvite(
        ref DataStreamWriter writer,
        NetworkRelayType relayType,
        uint senderId,
        uint levelID,
        int stage,
        int squadChannel,
        in FixedString512Bytes text,
        in FixedBytes80 header,
        in StreamCompressionModel model)
    {
        WriteSendRelayHeader(
            ref writer,
            (int)ReplyMessageType.Invite,
            relayType,
            senderId,
            in model);

        // NetworkRelayServerIdentity.SendRelay writes the envelope and then copies the
        // byte-aligned body left by ReplyMessages.Invite.WriteReplyHeader.
        writer.Flush();
        WriteInviteBody(
            ref writer,
            levelID,
            stage,
            squadChannel,
            in text,
            in header,
            in model);
    }

    private static bool TryReadSendRelayInvite(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        var originalReader = reader;
        if (!TryReadSendRelayHeader(
                ref reader,
                in model,
                out int messageType,
                out NetworkRelayType relayType,
                out uint senderId) ||
            messageType != (int)ReplyMessageType.Invite ||
            (int)relayType < (int)NetworkRelayType.All ||
            senderId == 0)
        {
            reader = originalReader;
            return false;
        }

        // This flush is part of the current protocol. A continuous packed fallback would accept
        // a different field layout and is deliberately not supported.
        reader.Flush();
        if (!__TryReadCompleteInviteBody(
                ref reader,
                relayType,
                senderId,
                in model,
                out invite))
        {
            reader = originalReader;
            invite = default;
            return false;
        }

        return true;
    }

    private static bool __TryReadCompleteInviteBody(
        ref DataStreamReader reader,
        NetworkRelayType relayType,
        uint senderId,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        invite.type = relayType;
        invite.id = senderId;

        invite.levelID = reader.ReadPackedUInt(model);
        invite.stage = reader.ReadPackedInt(model);
        invite.channel = reader.ReadPackedInt(model);
        if (reader.HasFailedReads || invite.stage < 0 || invite.channel < 0)
            return false;

        if (!__TryReadFixedString512(ref reader, out invite.text))
            return false;

        if (reader.Length - reader.GetBytesRead() != ClientHeaderFixedBytes)
            return false;

        invite.header = new FixedBytes80(ref reader);
        return !reader.HasFailedReads &&
               reader.GetBytesRead() == reader.Length;
    }

    private static bool __TryReadFixedString512(
        ref DataStreamReader reader,
        out FixedString512Bytes text)
    {
        text = default;
        int remaining = reader.Length - reader.GetBytesRead();
        if (remaining < sizeof(ushort) + ClientHeaderFixedBytes)
            return false;

        int saved = reader.GetBytesRead();
        ushort utf8Length = reader.ReadUShort();
        if (reader.HasFailedReads)
            return false;

        reader.SeekSet(saved);
        const int maxUtf8Bytes = 509;
        if (utf8Length > maxUtf8Bytes ||
            remaining != sizeof(ushort) + utf8Length + ClientHeaderFixedBytes)
        {
            return false;
        }

        text = reader.ReadFixedString512();
        return !reader.HasFailedReads;
    }

    public static bool TryReadSendRelayInviteFromPacket(
        in BotRelayPacket app,
        out ReplyMessages.Invite invite) =>
        TryReadSendRelayInviteFromPacket(
            in app,
            StreamCompressionModel.Default,
            out invite);

    public static bool TryReadSendRelayInviteFromPacket(
        in BotRelayPacket app,
        in StreamCompressionModel model,
        out ReplyMessages.Invite invite)
    {
        invite = default;
        if (app.IsEmpty || app.length < MinSendRelayInviteAppBytes)
            return false;

        unsafe
        {
            byte* buffer = stackalloc byte[app.length];
            for (int i = 0; i < app.length; ++i)
                buffer[i] = app.GetByte(i);

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                app.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            return TryReadSendRelayInvite(
                ref reader,
                in model,
                out invite);
        }
    }

    public static void WriteClientMatchToSend(
        ref DataStreamWriter writer,
        in NetworkRelayMatch match,
        in StreamCompressionModel model)
    {
        writer.WritePackedInt((int)NetworkRelayMessageType.Match, model);
        match.Write(ref writer, in model);
    }

    public static bool TryReadServerMatchNotification(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out int matchId,
        out int level)
    {
        matchId = reader.ReadPackedInt(model);
        level = reader.ReadPackedInt(model);
        return !reader.HasFailedReads &&
               reader.GetBytesRead() == reader.Length;
    }

    public static bool TryReadServerMatchStateNotification(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out int matchId,
        out uint sourceId)
    {
        matchId = reader.ReadPackedInt(model);
        sourceId = 0;
        if (reader.HasFailedReads || matchId <= 0)
            return false;

        // Server sends [type, match] to the identity and [type, match, sourceID] to its channel.
        // Packed fields can share the final byte, so probe the optional non-zero source on a copy
        // before accepting the bodyless form.
        var bodylessReader = reader;
        int remainingBits = reader.Length * 8 - reader.GetBitsRead();
        if (remainingBits >= 2)
        {
            uint candidateSource = reader.ReadPackedUInt(model);
            if (!reader.HasFailedReads &&
                candidateSource != 0 &&
                IsAtEnd(ref reader))
            {
                sourceId = candidateSource;
                return true;
            }
        }

        reader = bodylessReader;
        return IsAtEnd(ref reader);
    }

    public static void WriteApplyMatch(ref DataStreamWriter writer)
    {
        writer.WriteReplyHeader((int)ClientMessageType.ApplyMatch, NetworkRelayType.Channel);
    }

    public static int RemainingBytes(ref DataStreamReader reader) =>
        reader.Length - reader.GetBytesRead();

    public static bool IsAtEnd(ref DataStreamReader reader) =>
        !reader.HasFailedReads &&
        reader.GetBytesRead() == reader.Length;

    public static int DecodeChannelStatus(int channelFlag) =>
        (int)((uint)channelFlag >> (int)NetworkRelayChannelFlag.ShiftToStatus);

    public static bool TryReadOptionalRemoteClientHeader(
        uint remoteUserID,
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out ClientHeader header)
    {
        header = default;
        if (RemainingBytes(ref reader) < ClientHeaderFixedBytes)
            return false;

        header = new ClientHeader(remoteUserID, ref reader, model);
        return !reader.HasFailedReads;
    }
}
