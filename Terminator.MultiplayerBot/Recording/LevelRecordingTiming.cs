using System.Collections.Generic;
using Unity.Mathematics;
using ZG;

/// <summary>
/// Frame timestamps and replay span helpers (content vs wall-clock session).
/// </summary>
internal static class LevelRecordingTiming
{
    public static bool IsGameplayMessage(ReplyMessageType messageType)
    {
        return messageType == ReplyMessageType.Move ||
               messageType == ReplyMessageType.Camera ||
               messageType == ReplyMessageType.Damage ||
               messageType == ReplyMessageType.SelectSkill;
    }

    public static bool TryGetLastFrameTimestamp(IReadOnlyList<LevelRecordingFrame> frames, out float timestamp)
    {
        timestamp = 0f;
        if (frames == null || frames.Count == 0)
        {
            return false;
        }

        timestamp = frames[frames.Count - 1].timestamp;
        return true;
    }

    /// <summary>First Move/Camera/Damage/SelectSkill frame; falls back to first non-PlayerProperty frame.</summary>
    public static float ComputeGameplayTimestampBase(IReadOnlyList<LevelRecordingFrame> frames)
    {
        if (frames == null || frames.Count == 0)
        {
            return 0f;
        }

        float fallback = 0f;
        var hasFallback = false;
        for (int i = 0; i < frames.Count; ++i)
        {
            var payload = frames[i].payload;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType))
            {
                continue;
            }

            if (messageType == ReplyMessageType.PlayerProperty)
            {
                continue;
            }

            if (!hasFallback)
            {
                fallback = frames[i].timestamp;
                hasFallback = true;
            }

            if (IsGameplayMessage(messageType))
            {
                return frames[i].timestamp;
            }
        }

        return hasFallback ? fallback : 0f;
    }

    public static float ComputeGameplaySpan(IReadOnlyList<LevelRecordingFrame> frames, float timestampBase)
    {
        if (!TryGetLastFrameTimestamp(frames, out float lastTimestamp))
        {
            return 0f;
        }

        return math.max(0f, lastTimestamp - timestampBase);
    }

    public static float ResolveHeaderDuration(
        IReadOnlyList<LevelRecordingFrame> frames,
        float wallDurationSeconds)
    {
        if (TryGetLastFrameTimestamp(frames, out float lastTimestamp) && lastTimestamp > 0f)
        {
            return lastTimestamp;
        }

        return wallDurationSeconds;
    }
}
