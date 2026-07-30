using Unity.Collections;
using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ZG;

public static class BotReplayPayloadUtility
{
    private static bool __TryReadCurrentReplyPayload(
        ref DataStreamReader reader,
        out ReplyMessageType messageType)
    {
        messageType = default;
        var model = StreamCompressionModel.Default;
        if (!__TryReadPackedInt(ref reader, in model, out int rawMessageType) ||
            !__TryReadPackedInt(ref reader, in model, out int relayType))
        {
            return false;
        }

        reader.Flush();
        if (reader.HasFailedReads ||
            relayType != (int)NetworkRelayType.Channel ||
            reader.GetBytesRead() >= reader.Length ||
            rawMessageType < (int)ReplyMessageType.Chat ||
            rawMessageType > (int)ReplyMessageType.PlayerProperty ||
            rawMessageType == (int)ReplyMessageType.Invite)
        {
            return false;
        }

        var parsedMessageType = (ReplyMessageType)rawMessageType;
        switch (parsedMessageType)
        {
            case ReplyMessageType.Chat:
                if (!__TryReadFixedString512(ref reader, out var chat) || chat.IsEmpty)
                    return false;
                break;
            case ReplyMessageType.Camera:
            {
                if (!__TryReadPackedFloat(ref reader, in model, out float x) ||
                    !__TryReadPackedFloat(ref reader, in model, out float y) ||
                    !math.isfinite(x) ||
                    !math.isfinite(y))
                {
                    return false;
                }
                break;
            }
            case ReplyMessageType.Move:
            {
                if (!__TryReadPackedInt(ref reader, in model, out int count) ||
                    count <= 0 ||
                    count > __RemainingBits(ref reader) / 3)
                {
                    return false;
                }

                for (int i = 0; i < count; ++i)
                {
                    if (!__TryReadPackedInt(ref reader, in model, out int type) ||
                        !__TryReadPackedFloat(ref reader, in model, out float x) ||
                        !__TryReadPackedFloat(ref reader, in model, out float y) ||
                        type < (int)RemotePosition.Type.Normal ||
                        type > (int)RemotePosition.Type.Warp ||
                        !math.isfinite(x) ||
                        !math.isfinite(y))
                    {
                        return false;
                    }
                }

                break;
            }
            case ReplyMessageType.Damage:
            {
                int count = 0;
                do
                {
                    if (!__TryReadPackedInt(ref reader, in model, out _) ||
                        !__TryReadPackedInt(ref reader, in model, out _) ||
                        !__TryReadPackedInt(ref reader, in model, out _) ||
                        !__TryReadPackedInt(ref reader, in model, out _) ||
                        ++count > BotRelayPacket.MaxPayloadSize)
                    {
                        return false;
                    }
                } while (reader.GetBytesRead() < reader.Length);
                break;
            }
            case ReplyMessageType.SelectSkill:
            {
                if (!__TryReadPackedInt(ref reader, in model, out int count) ||
                    count <= 0 ||
                    count > __RemainingBits(ref reader) / 4)
                {
                    return false;
                }

                for (int i = 0; i < count; ++i)
                {
                    if (!__TryReadPackedInt(ref reader, in model, out int index) ||
                        !__TryReadPackedInt(ref reader, in model, out int originIndex) ||
                        !__TryReadPackedInt(ref reader, in model, out int activeIndex) ||
                        !__TryReadPackedFloat(ref reader, in model, out float damageScale) ||
                        index < 0 ||
                        originIndex < -1 ||
                        activeIndex < -1 ||
                        !math.isfinite(damageScale))
                    {
                        return false;
                    }
                }

                break;
            }
            case ReplyMessageType.PlayerProperty:
                if (!BotRelayCodec.TryReadCurrentPlayerPropertyBody(ref reader, in model))
                    return false;
                break;
            default:
                return false;
        }

        if (reader.HasFailedReads || reader.GetBytesRead() != reader.Length)
            return false;

        messageType = parsedMessageType;
        return true;
    }

    private static int __RemainingBits(ref DataStreamReader reader) =>
        reader.Length * 8 - reader.GetBitsRead();

    private static bool __TryReadPackedInt(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out int value)
    {
        value = 0;
        if (!__CanReadPackedUInt(ref reader))
            return false;

        value = reader.ReadPackedInt(model);
        return !reader.HasFailedReads;
    }

    private static bool __TryReadPackedFloat(
        ref DataStreamReader reader,
        in StreamCompressionModel model,
        out float value)
    {
        value = 0f;
        int remainingBits = __RemainingBits(ref reader);
        if (remainingBits < 1)
            return false;

        var probe = reader;
        uint changed = probe.ReadRawBits(1);
        int requiredBits = changed == 0 ? 1 : 33;
        if (remainingBits < requiredBits)
            return false;

        value = reader.ReadPackedFloat(model);
        return !reader.HasFailedReads;
    }

    // Safe preflight for StreamCompressionModel.Default. DataStreamReader logs an error before it
    // exposes HasFailedReads when a Huffman bucket announces more raw bits than remain; replay and
    // RecordingAudit must reject malformed payloads without flooding the Unity console.
    private static bool __CanReadPackedUInt(ref DataStreamReader reader)
    {
        int remainingBits = __RemainingBits(ref reader);
        if (remainingBits < 2)
            return false;

        int peekBitCount = math.min(remainingBits, 6);
        var probe = reader;
        uint peek = probe.ReadRawBits(peekBitCount);

        int codeLength;
        int bucketBits;
        if ((peek & 0x3u) == 0u)
        {
            codeLength = 2;
            bucketBits = 0;
        }
        else if (peekBitCount >= 3 && (peek & 0x7u) == 0x2u)
        {
            codeLength = 3;
            bucketBits = 0;
        }
        else if (peekBitCount >= 3 && (peek & 0x7u) == 0x6u)
        {
            codeLength = 3;
            bucketBits = 1;
        }
        else if (peekBitCount >= 3 && (peek & 0x7u) == 0x1u)
        {
            codeLength = 3;
            bucketBits = 2;
        }
        else if (peekBitCount >= 4 && (peek & 0xFu) == 0x5u)
        {
            codeLength = 4;
            bucketBits = 3;
        }
        else if (peekBitCount >= 4 && (peek & 0xFu) == 0xDu)
        {
            codeLength = 4;
            bucketBits = 4;
        }
        else if (peekBitCount >= 4 && (peek & 0xFu) == 0x3u)
        {
            codeLength = 4;
            bucketBits = 6;
        }
        else if (peekBitCount >= 5 && (peek & 0x1Fu) == 0xBu)
        {
            codeLength = 5;
            bucketBits = 8;
        }
        else if (peekBitCount >= 5 && (peek & 0x1Fu) == 0x1Bu)
        {
            codeLength = 5;
            bucketBits = 10;
        }
        else if (peekBitCount >= 5 && (peek & 0x1Fu) == 0x7u)
        {
            codeLength = 5;
            bucketBits = 12;
        }
        else if (peekBitCount >= 6 && (peek & 0x3Fu) == 0x17u)
        {
            codeLength = 6;
            bucketBits = 15;
        }
        else if (peekBitCount >= 6 && (peek & 0x3Fu) == 0x37u)
        {
            codeLength = 6;
            bucketBits = 18;
        }
        else if (peekBitCount >= 6 && (peek & 0x3Fu) == 0xFu)
        {
            codeLength = 6;
            bucketBits = 21;
        }
        else if (peekBitCount >= 6 && (peek & 0x3Fu) == 0x2Fu)
        {
            codeLength = 6;
            bucketBits = 24;
        }
        else if (peekBitCount >= 6 && (peek & 0x3Fu) == 0x1Fu)
        {
            codeLength = 6;
            bucketBits = 27;
        }
        else if (peekBitCount >= 6 && (peek & 0x3Fu) == 0x3Fu)
        {
            codeLength = 6;
            bucketBits = 32;
        }
        else
        {
            return false;
        }

        return remainingBits >= codeLength + bucketBits;
    }

    private static bool __TryReadFixedString512(
        ref DataStreamReader reader,
        out FixedString512Bytes value)
    {
        value = default;
        var probe = reader;
        ushort utf8Length = probe.ReadUShort();
        if (probe.HasFailedReads ||
            utf8Length > FixedString512Bytes.UTF8MaxLengthInBytes ||
            probe.GetBytesRead() > probe.Length - utf8Length)
        {
            return false;
        }

        value = reader.ReadFixedString512();
        return !reader.HasFailedReads;
    }

    public static bool TryGetReplyMessageType(in BotRelayPacket packet, out ReplyMessageType messageType)
    {
        messageType = default;
        if (packet.length < 2 || packet.length > BotRelayPacket.MaxPayloadSize)
            return false;

        unsafe
        {
            fixed (byte* ptr = packet.data)
            {
                var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                    ptr,
                    packet.length,
                    Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                    ref bytes,
                    AtomicSafetyHandle.GetTempMemoryHandle());
#endif
                var reader = new DataStreamReader(bytes);
                return __TryReadCurrentReplyPayload(ref reader, out messageType);
            }
        }
    }

    public static bool TryGetReplyMessageType(BlobAssetReference<BotRecordingBlob> recording, int frameIndex, out ReplyMessageType messageType)
    {
        messageType = default;
        if (!recording.IsCreated)
            return false;

        ref var blob = ref recording.Value;
        if (frameIndex < 0 || frameIndex >= blob.frames.Length)
            return false;

        ref readonly var meta = ref blob.frames[frameIndex];
        if (meta.payloadLength < 2 ||
            meta.payloadLength > BotRelayPacket.MaxPayloadSize ||
            meta.payloadOffset < 0 ||
            meta.payloadOffset > blob.payloadBytes.Length - meta.payloadLength)
            return false;

        unsafe
        {
            var bytes = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                (byte*)blob.payloadBytes.GetUnsafePtr() + meta.payloadOffset,
                meta.payloadLength,
                Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref bytes,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            var reader = new DataStreamReader(bytes);
            return __TryReadCurrentReplyPayload(ref reader, out messageType);
        }
    }

    public static bool TryCopyFrameToPacket(BlobAssetReference<BotRecordingBlob> recording, int frameIndex, out BotRelayPacket packet)
    {
        packet = default;
        if (!recording.IsCreated)
            return false;

        ref var blob = ref recording.Value;
        if (frameIndex < 0 || frameIndex >= blob.frames.Length)
            return false;

        ref readonly var meta = ref blob.frames[frameIndex];
        if (meta.payloadLength <= 0 ||
            meta.payloadLength > BotRelayPacket.MaxPayloadSize ||
            meta.payloadOffset < 0 ||
            meta.payloadOffset > blob.payloadBytes.Length - meta.payloadLength)
            return false;

        unsafe
        {
            var source = (byte*)blob.payloadBytes.GetUnsafePtr() + meta.payloadOffset;
            packet.CopyFromPtr(source, meta.payloadLength);
        }

        return !packet.IsEmpty;
    }
}
