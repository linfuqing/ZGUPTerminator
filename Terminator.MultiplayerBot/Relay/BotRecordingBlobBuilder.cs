using System;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public static class BotRecordingBlobBuilder
{
    public static BlobAssetReference<BotRecordingBlob> CreateFromFile(string filePath)
    {
        var session = LevelRecordingSerializer.Read(filePath);
        return CreateFromSession(session);
    }

    public static BlobAssetReference<BotRecordingBlob> CreateFromSession(LevelRecordingSession session)
    {
        if (session == null || session.frames.Count == 0)
            return default;

        int totalPayloadBytes = 0;
        for (int i = 0; i < session.frames.Count; ++i)
        {
            var payload = session.frames[i].payload;
            if (payload == null || payload.Length == 0)
                continue;

            if (payload.Length > BotRelayPacket.MaxPayloadSize)
            {
                throw new InvalidDataException(
                    $"Frame {i} payload is {payload.Length}B; replay capacity is " +
                    $"{BotRelayPacket.MaxPayloadSize}B.");
            }

            totalPayloadBytes = checked(totalPayloadBytes + payload.Length);
        }

        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<BotRecordingBlob>();

        var framesArray = builder.Allocate(ref root.frames, session.frames.Count);
        var payloadArray = builder.Allocate(ref root.payloadBytes, Math.Max(totalPayloadBytes, 1));

        int offset = 0;
        for (int i = 0; i < session.frames.Count; ++i)
        {
            var frame = session.frames[i];
            var payload = frame.payload ?? Array.Empty<byte>();
            int length = payload.Length;

            framesArray[i] = new BotReplayFrameMeta
            {
                timestamp = frame.timestamp,
                payloadOffset = offset,
                payloadLength = (ushort)length
            };

            for (int j = 0; j < length; ++j)
                payloadArray[offset + j] = payload[j];

            offset += length;
        }

        return builder.CreateBlobAssetReference<BotRecordingBlob>(Allocator.Persistent);
    }
}
