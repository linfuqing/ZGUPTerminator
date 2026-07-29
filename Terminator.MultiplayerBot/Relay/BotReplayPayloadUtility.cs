using Unity.Collections;
using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using ZG;

public static class BotReplayPayloadUtility
{
    public static bool TryGetReplyMessageType(in BotRelayPacket packet, out ReplyMessageType messageType)
    {
        messageType = default;
        if (packet.length < 2)
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
                messageType = (ReplyMessageType)reader.ReadPackedInt(StreamCompressionModel.Default);
                return messageType >= ReplyMessageType.Chat && messageType <= ReplyMessageType.PlayerProperty;
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
        if (meta.payloadLength < 2)
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
            messageType = (ReplyMessageType)reader.ReadPackedInt(StreamCompressionModel.Default);
            return messageType >= ReplyMessageType.Chat && messageType <= ReplyMessageType.PlayerProperty;
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
        if (meta.payloadLength == 0)
            return false;

        unsafe
        {
            var source = (byte*)blob.payloadBytes.GetUnsafePtr() + meta.payloadOffset;
            packet.CopyFromPtr(source, meta.payloadLength);
        }

        return !packet.IsEmpty;
    }
}
