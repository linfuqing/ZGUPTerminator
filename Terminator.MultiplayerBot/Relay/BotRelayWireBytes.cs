using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Networking.Transport;
using ZG;

[BurstCompile]
internal static class BotRelayWireBytes
{
    public const int EmptyWireIndex = 0;
    private const int MaxPackedIntBytes = 8;

    public static bool IsEmpty(ref BotRelayWireBlob wire) => wire.length <= 0;

    public static bool Equals(ref BotRelayWireBlob left, ref BotRelayWireBlob right)
    {
        if (left.length != right.length)
            return false;

        if (left.length <= 0)
            return true;

        for (int i = 0; i < left.length; ++i)
        {
            if (left.bytes[i] != right.bytes[i])
                return false;
        }

        return true;
    }

    public static bool Equals(ref BotRelayWireBlob wire, in BotRelayPacket packet)
    {
        if (wire.length != packet.length)
            return false;

        if (wire.length <= 0)
            return true;

        for (int i = 0; i < wire.length; ++i)
        {
            if (wire.bytes[i] != packet.GetByte(i))
                return false;
        }

        return true;
    }

    public static bool Equals(in BotRelayPacket left, in BotRelayPacket right)
    {
        if (left.length != right.length)
            return false;

        if (left.length <= 0)
            return true;

        for (int i = 0; i < left.length; ++i)
        {
            if (left.GetByte(i) != right.GetByte(i))
                return false;
        }

        return true;
    }

    public static bool LooksLikeConnectHandshake(in BotRelayPacket wire)
    {
        if (wire.length < 5)
            return false;

        return wire.GetByte(0) == (byte)'U' &&
               wire.GetByte(1) == (byte)'T' &&
               wire.GetByte(2) == (byte)'P' &&
               wire.GetByte(3) == 0x01 &&
               (wire.GetByte(4) == 0x01 || wire.GetByte(4) == 0x02);
    }

    public static bool LooksLikeConnectHandshake(ref BotRelayWireBlob wire)
    {
        if (wire.length < 5)
            return false;

        return wire.bytes[0] == (byte)'U' &&
               wire.bytes[1] == (byte)'T' &&
               wire.bytes[2] == (byte)'P' &&
               wire.bytes[3] == 0x01 &&
               (wire.bytes[4] == 0x01 || wire.bytes[4] == 0x02);
    }

    public static bool LooksLikeCurrentServerHello(
        in BotRelayPacket wire,
        ref BotRelayWireBlob capturedHello) =>
        wire.length == capturedHello.length &&
        LooksLikeConnectHandshake(in wire) &&
        LooksLikeConnectHandshake(ref capturedHello);

    public static bool TryCopyWireToPacket(ref BotRelayWireBlob wire, out BotRelayPacket packet)
    {
        packet = default;
        if (wire.length <= 0 || wire.length > BotRelayPacket.MaxPayloadSize)
            return false;

        packet.length = wire.length;
        for (int i = 0; i < wire.length; ++i)
            packet.SetByte(i, wire.bytes[i]);
        return true;
    }

    public static bool TryApplySessionPatch(
        ref BotRelayPacket outbound,
        ref BotRelayWireBlob connectTemplate,
        ref BotRelayWireBlob capturedHello,
        in BotRelayPacket liveHello)
    {
        if (outbound.IsEmpty ||
            connectTemplate.length <= 0 ||
            capturedHello.length <= 0 ||
            liveHello.IsEmpty)
        {
            return false;
        }

        if (Equals(ref capturedHello, in liveHello))
            return true;

        int n = math.min(
            outbound.length,
            math.min(connectTemplate.length, math.min(capturedHello.length, liveHello.length)));

        for (int i = 0; i < n; ++i)
        {
            byte captured = capturedHello.bytes[i];
            byte live = liveHello.GetByte(i);
            if (captured == live)
                continue;

            byte connect = connectTemplate.bytes[i];
            byte value = outbound.GetByte(i);
            if (connect == value)
                continue;

            outbound.SetByte(i, (byte)(value + (live - captured)));
        }

        return true;
    }

    public static ref BotRelayWireBlob GetWire(ref BotRelayCatalogBlob catalog, int wireIndex)
    {
        if (wireIndex < 0 || wireIndex >= catalog.wires.Length)
            wireIndex = EmptyWireIndex;

        return ref catalog.wires[wireIndex];
    }

    public static ref BotRelayWireBlob GetServerHello(ref BotRelayCatalogBlob catalog) =>
        ref GetWire(ref catalog, catalog.serverHelloWireIndex);

    /// <summary>
    /// Resolves the only current PopEvents stream start from the app offset measured by
    /// <see cref="BotRelayWireCatalog"/> against a live Null-pipeline capture.
    /// </summary>
    public static bool TryGetCurrentPopEventsStreamStart(
        in BotRelayPacket wire,
        int inboundAppPayloadOffset,
        out int streamStart)
    {
        streamStart = -1;
        if (wire.IsEmpty ||
            inboundAppPayloadOffset < sizeof(ushort) ||
            inboundAppPayloadOffset >= wire.length)
        {
            return false;
        }

        int candidate = inboundAppPayloadOffset - sizeof(ushort);
        int size = wire.GetByte(candidate) | (wire.GetByte(candidate + 1) << 8);
        if (size < 1 || size > wire.length - inboundAppPayloadOffset)
            return false;

        streamStart = candidate;
        return true;
    }

    /// <summary>
    /// Extracts the first accepted app from the current measured Null-pipeline framing.
    /// The complete frame stream must validate through the end of the datagram.
    /// </summary>
    public static bool TryExtractCurrentFramedApp(
        in BotRelayPacket wire,
        int inboundAppPayloadOffset,
        out BotRelayPacket app) =>
        BotRelayPopEventsWalkOps.TryExtractFirstServerToBotApp(
            in wire,
            inboundAppPayloadOffset,
            out app);

    /// <summary>
    /// Validates an application payload that has already been peeled from its transport frame.
    /// This method never searches inside the supplied bytes.
    /// </summary>
    public static bool TryAcceptCurrentInboundApp(
        in BotRelayPacket raw,
        out BotRelayPacket app)
    {
        app = default;
        if (raw.IsEmpty || !TryReadRelayMessageTypeHeader(in raw, out int type))
            return false;

        if (type == (int)ReplyMessageType.Invite)
        {
            if (!BotRelayInviteDecodeOps.IsStrictSendRelayInviteApp(in raw, out _))
                return false;

            app = raw;
            return true;
        }

        if (type == (int)ClientMessageType.MatchStart)
        {
            if (!TryValidateMatchStartRelayApp(in raw))
                return false;

            app = raw;
            return true;
        }

        if (type == (int)ClientMessageType.Play)
        {
            if (!TryValidatePlayRelayApp(in raw))
                return false;

            app = raw;
            return true;
        }

        if (type >= (int)NetworkRelayMessageType.Connect &&
            type <= (int)NetworkRelayMessageType.Query)
        {
            if (!__TryValidateCurrentNetworkControlApp(in raw, type))
                return false;

            app = raw;
            return true;
        }

        if (type >= (int)ClientMessageType.ApplyFriend &&
            type <= (int)ClientMessageType.Error)
        {
            if (!__TryValidateCurrentClientRelayApp(in raw, type))
                return false;

            app = raw;
            return true;
        }

        if (type == (int)ReplyMessageType.PlayerProperty)
        {
            if (!__TryValidatePlayerPropertyRelayApp(in raw))
                return false;

            app = raw;
            return true;
        }

        if (type < (int)ReplyMessageType.Chat ||
            type > (int)ReplyMessageType.SelectSkill ||
            !__TryValidateCurrentSendRelayEnvelope(in raw, type))
        {
            return false;
        }

        app = raw;
        return true;
    }

    public static bool LooksLikeLinkAckCandidate(
        in BotRelayPacket wire,
        ref BotRelayCatalogBlob catalog)
    {
        if (wire.IsEmpty || wire.length > 64 || LooksLikeConnectHandshake(in wire))
            return false;

        if (TryExtractCurrentFramedApp(
                in wire,
                catalog.inboundAppPayloadOffset,
                out var app) &&
            !app.IsEmpty)
        {
            return false;
        }

        for (int i = 0; i < catalog.connectServerWires.Length; ++i)
        {
            ref var captured = ref GetWire(
                ref catalog,
                catalog.connectServerWires[i].wireIndex);
            if (captured.length == wire.length)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Connect-time calibration. The captured app must be the sole, complete
    /// <c>ushort length + app</c> frame at the end of the current Null-pipeline datagram.
    /// </summary>
    public static bool TryResolveInboundAppPayloadOffset(
        in BotRelayPacket wire,
        in BotRelayPacket appFrame,
        out int offset)
    {
        offset = -1;
        if (wire.IsEmpty ||
            appFrame.IsEmpty ||
            wire.length < sizeof(ushort) + appFrame.length ||
            !TryAcceptCurrentInboundApp(in appFrame, out _))
        {
            return false;
        }

        int limit = wire.length - appFrame.length;
        for (int candidate = sizeof(ushort); candidate <= limit; ++candidate)
        {
            int streamStart = candidate - sizeof(ushort);
            int declaredLength =
                wire.GetByte(streamStart) |
                (wire.GetByte(streamStart + 1) << 8);
            if (declaredLength != appFrame.length ||
                candidate + appFrame.length != wire.length)
            {
                continue;
            }

            bool matches = true;
            for (int i = 0; i < appFrame.length; ++i)
            {
                if (wire.GetByte(candidate + i) == appFrame.GetByte(i))
                    continue;

                matches = false;
                break;
            }

            if (!matches)
                continue;

            offset = candidate;
            return true;
        }

        return false;
    }

    internal static void CopyAppFromOffset(
        in BotRelayPacket wire,
        int offset,
        int length,
        out BotRelayPacket app)
    {
        app = default;
        if (offset < 0 ||
            length < 1 ||
            length > BotRelayPacket.MaxPayloadSize ||
            offset + length > wire.length)
        {
            return;
        }

        app.length = length;
        for (int i = 0; i < length; ++i)
            app.SetByte(i, wire.GetByte(offset + i));
    }

    /// <summary>
    /// Reads the first packed relay message type without interpreting or searching trailing bytes.
    /// </summary>
    public static bool TryReadRelayMessageTypeHeader(in BotRelayPacket packet, out int type) =>
        TryReadRelayMessageTypeHeader(in packet, StreamCompressionModel.Default, out type);

    public static bool TryReadRelayMessageTypeHeader(
        in BotRelayPacket packet,
        StreamCompressionModel model,
        out int type) =>
        __TryReadPackedRelayType(in packet, model, out type, out _) &&
        __IsRelayAppMessageType(type);

    public static bool TryValidateMatchStartRelayApp(in BotRelayPacket packet)
    {
        if (packet.IsEmpty)
            return false;

        unsafe
        {
            byte* buffer = stackalloc byte[packet.length];
            for (int i = 0; i < packet.length; ++i)
                buffer[i] = packet.GetByte(i);

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                packet.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            if (!BotRelayMessageUtility.TryReadSendRelayHeader(
                    ref reader,
                    in model,
                    out int type,
                    out NetworkRelayType relayType,
                    out uint senderID) ||
                type != (int)ClientMessageType.MatchStart ||
                relayType != NetworkRelayType.Channel ||
                senderID == 0 ||
                reader.HasFailedReads)
            {
                return false;
            }

            reader.Flush();
            var matchStart = new ClientMessageMatchStart(ref reader);
            return !reader.HasFailedReads &&
                   reader.GetBytesRead() == reader.Length &&
                   matchStart.userStageID != 0 &&
                   matchStart.levelID != 0 &&
                   matchStart.stage >= 0 &&
                   matchStart.sceneName.Length > 0;
        }
    }

    public static bool TryValidatePlayRelayApp(in BotRelayPacket packet)
    {
        if (packet.IsEmpty)
            return false;

        unsafe
        {
            byte* buffer = stackalloc byte[packet.length];
            for (int i = 0; i < packet.length; ++i)
                buffer[i] = packet.GetByte(i);

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                packet.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            if (!BotRelayMessageUtility.TryReadSendRelayHeader(
                    ref reader,
                    in model,
                    out int type,
                    out NetworkRelayType relayType,
                    out uint senderID) ||
                type != (int)ClientMessageType.Play ||
                relayType != NetworkRelayType.Channel ||
                senderID == 0 ||
                reader.HasFailedReads)
            {
                return false;
            }

            reader.Flush();
            var play = new ClientMessagePlay(ref reader);
            return !reader.HasFailedReads &&
                   reader.GetBytesRead() == reader.Length &&
                   play.levelID != 0 &&
                   play.stage >= 0 &&
                   play.sceneName.Length > 0;
        }
    }

    private static bool __TryValidateCurrentNetworkControlApp(
        in BotRelayPacket packet,
        int expectedType)
    {
        unsafe
        {
            byte* buffer = stackalloc byte[packet.length];
            for (int i = 0; i < packet.length; ++i)
                buffer[i] = packet.GetByte(i);

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                packet.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            int type = reader.ReadPackedInt(model);
            if (reader.HasFailedReads || type != expectedType)
                return false;

            switch ((NetworkRelayMessageType)type)
            {
                case NetworkRelayMessageType.Connect:
                case NetworkRelayMessageType.Status:
                case NetworkRelayMessageType.Add:
                {
                    int channelFlag = reader.ReadPackedInt(model);
                    uint userID = reader.ReadPackedUInt(model);
                    return channelFlag >= 0 &&
                           userID != 0 &&
                           BotRelayMessageUtility.IsAtEnd(ref reader);
                }
                case NetworkRelayMessageType.Disconnect:
                case NetworkRelayMessageType.Remove:
                {
                    uint userID = reader.ReadPackedUInt(model);
                    return userID != 0 &&
                           BotRelayMessageUtility.IsAtEnd(ref reader);
                }
                case NetworkRelayMessageType.Create:
                case NetworkRelayMessageType.Join:
                case NetworkRelayMessageType.Leave:
                case NetworkRelayMessageType.Drop:
                case NetworkRelayMessageType.Query:
                    return __TryReadCurrentChannelSnapshot(ref reader, in model);
                case NetworkRelayMessageType.JoinFailed:
                {
                    int channel = reader.ReadPackedInt(model);
                    return channel >= 0 &&
                           BotRelayMessageUtility.IsAtEnd(ref reader);
                }
                case NetworkRelayMessageType.Matching:
                case NetworkRelayMessageType.Mismatch:
                    return BotRelayMessageUtility.TryReadServerMatchStateNotification(
                        ref reader,
                        in model,
                        out _,
                        out _);
                case NetworkRelayMessageType.Match:
                    return BotRelayMessageUtility.TryReadServerMatchNotification(
                        ref reader,
                        in model,
                        out int matchID,
                        out int level) &&
                        matchID > 0 &&
                        level >= 0;
                default:
                    return false;
            }
        }
    }

    private static bool __TryReadCurrentChannelSnapshot(
        ref DataStreamReader reader,
        in StreamCompressionModel model)
    {
        int channel = reader.ReadPackedInt(model);
        int channelFlag = reader.ReadPackedInt(model);
        if (reader.HasFailedReads || channel < 0 || channelFlag < 0)
            return false;

        if (BotRelayMessageUtility.IsAtEnd(ref reader))
            return true;

        reader.Flush();
        uint remoteUserID = reader.ReadPackedUInt(model);
        return remoteUserID != 0 &&
               BotRelayMessageUtility.TryReadOptionalRemoteClientHeader(
                   remoteUserID,
                   ref reader,
                   model,
                   out _) &&
               BotRelayMessageUtility.IsAtEnd(ref reader);
    }

    private static bool __TryValidateCurrentClientRelayApp(
        in BotRelayPacket packet,
        int expectedType)
    {
        unsafe
        {
            byte* buffer = stackalloc byte[packet.length];
            for (int i = 0; i < packet.length; ++i)
                buffer[i] = packet.GetByte(i);

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                packet.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            if (!BotRelayMessageUtility.TryReadSendRelayHeader(
                    ref reader,
                    in model,
                    out int type,
                    out NetworkRelayType relayType,
                    out uint senderID) ||
                type != expectedType ||
                senderID == 0)
            {
                return false;
            }

            bool isApplyFriend = type == (int)ClientMessageType.ApplyFriend;
            if (isApplyFriend
                    ? (int)relayType <= (int)NetworkRelayType.Identity
                    : relayType != NetworkRelayType.Channel)
            {
                return false;
            }

            reader.Flush();
            switch ((ClientMessageType)type)
            {
                case ClientMessageType.ApplyMatch:
                case ClientMessageType.ApplyMatchFail:
                case ClientMessageType.RejectMatch:
                case ClientMessageType.Cancel:
                case ClientMessageType.Error:
                    return BotRelayMessageUtility.IsAtEnd(ref reader);
                case ClientMessageType.Page:
                    reader.ReadPackedInt(model);
                    return BotRelayMessageUtility.IsAtEnd(ref reader);
                case ClientMessageType.ChapterStage:
                    _ = new ClientMessageChapterStage(ref reader, model);
                    return BotRelayMessageUtility.IsAtEnd(ref reader);
                case ClientMessageType.ApplyFriend:
                    _ = new ClientHeader(senderID, ref reader, model);
                    reader.ReadFixedString512();
                    return BotRelayMessageUtility.IsAtEnd(ref reader);
                default:
                    return false;
            }
        }
    }

    private static bool __TryValidatePlayerPropertyRelayApp(in BotRelayPacket packet)
    {
        unsafe
        {
            byte* buffer = stackalloc byte[packet.length];
            for (int i = 0; i < packet.length; ++i)
                buffer[i] = packet.GetByte(i);

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                packet.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            if (!BotRelayMessageUtility.TryReadSendRelayHeader(
                    ref reader,
                    in model,
                    out int type,
                    out NetworkRelayType relayType,
                    out uint senderID) ||
                type != (int)ReplyMessageType.PlayerProperty ||
                relayType != NetworkRelayType.Channel ||
                senderID == 0)
            {
                return false;
            }

            reader.Flush();
            _ = new ClientMessagePlayerProperty(ref reader);
            return BotRelayMessageUtility.IsAtEnd(ref reader);
        }
    }

    private static bool __TryValidateCurrentSendRelayEnvelope(
        in BotRelayPacket packet,
        int expectedType)
    {
        unsafe
        {
            byte* buffer = stackalloc byte[packet.length];
            for (int i = 0; i < packet.length; ++i)
                buffer[i] = packet.GetByte(i);

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                packet.length,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            var model = StreamCompressionModel.Default;
            if (!BotRelayMessageUtility.TryReadSendRelayHeader(
                    ref reader,
                    in model,
                    out int type,
                    out NetworkRelayType relayType,
                    out uint senderID) ||
                type != expectedType ||
                (int)relayType < (int)NetworkRelayType.All ||
                senderID == 0)
            {
                return false;
            }

            reader.Flush();
            if (type == (int)ReplyMessageType.Chat)
            {
                _ = new ClientHeader(senderID, ref reader, model);
                reader.ReadFixedString512();
                return BotRelayMessageUtility.IsAtEnd(ref reader);
            }

            // Camera/Move/Damage/SelectSkill are current variable-size gameplay streams. The Bot
            // does not interpret them, but accepts only their canonical Channel envelope and a
            // non-empty body bounded by the outer PopEvents length frame.
            return relayType == NetworkRelayType.Channel &&
                   BotRelayMessageUtility.RemainingBytes(ref reader) > 0;
        }
    }

    private static bool __IsRelayAppMessageType(int type)
    {
        switch (type)
        {
            case (int)NetworkRelayMessageType.Connect:
            case (int)NetworkRelayMessageType.Disconnect:
            case (int)NetworkRelayMessageType.Status:
            case (int)NetworkRelayMessageType.Add:
            case (int)NetworkRelayMessageType.Remove:
            case (int)NetworkRelayMessageType.Create:
            case (int)NetworkRelayMessageType.Join:
            case (int)NetworkRelayMessageType.JoinFailed:
            case (int)NetworkRelayMessageType.Leave:
            case (int)NetworkRelayMessageType.Drop:
            case (int)NetworkRelayMessageType.Matching:
            case (int)NetworkRelayMessageType.Match:
            case (int)NetworkRelayMessageType.Mismatch:
            case (int)NetworkRelayMessageType.Query:
            case (int)ClientMessageType.ApplyFriend:
            case (int)ClientMessageType.ApplyMatch:
            case (int)ClientMessageType.ApplyMatchFail:
            case (int)ClientMessageType.RejectMatch:
            case (int)ClientMessageType.Page:
            case (int)ClientMessageType.ChapterStage:
            case (int)ClientMessageType.Play:
            case (int)ClientMessageType.Cancel:
            case (int)ClientMessageType.Error:
            case (int)ClientMessageType.MatchStart:
            case (int)ReplyMessageType.Chat:
            case (int)ReplyMessageType.Camera:
            case (int)ReplyMessageType.Move:
            case (int)ReplyMessageType.Damage:
            case (int)ReplyMessageType.Invite:
            case (int)ReplyMessageType.SelectSkill:
            case (int)ReplyMessageType.PlayerProperty:
                return true;
            default:
                return false;
        }
    }

    private static bool __TryReadPackedRelayType(
        in BotRelayPacket packet,
        StreamCompressionModel model,
        out int type,
        out int bytesRead)
    {
        type = -1;
        bytesRead = 0;
        if (packet.length < 1)
            return false;

        unsafe
        {
            int capacity = packet.length + MaxPackedIntBytes;
            byte* buffer = stackalloc byte[capacity];
            for (int i = 0; i < packet.length; ++i)
                buffer[i] = packet.GetByte(i);
            for (int i = packet.length; i < capacity; ++i)
                buffer[i] = 0;

            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                buffer,
                capacity,
                Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            type = reader.ReadPackedInt(model);
            if (reader.HasFailedReads)
            {
                type = -1;
                return false;
            }

            bytesRead = reader.GetBytesRead();
            if (bytesRead <= packet.length)
                return true;

            type = -1;
            bytesRead = 0;
            return false;
        }
    }

}
