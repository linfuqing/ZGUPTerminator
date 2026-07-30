using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using ZG;

/// <summary>
/// Objective post-hoc metrics from a captured session (no human timing required).
/// </summary>
internal static class LevelRecordingSessionAudit
{
    internal const int CollapsedSameTimestampMoveThreshold = 8;
    internal const int DuplicateSameTickMoveThreshold = 2;

    private sealed class PayloadContentComparer : IEqualityComparer<byte[]>
    {
        public static readonly PayloadContentComparer Instance = new PayloadContentComparer();

        public bool Equals(byte[] x, byte[] y)
        {
            if (object.ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null || x.Length != y.Length)
            {
                return false;
            }

            for (int i = 0; i < x.Length; ++i)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(byte[] payload)
        {
            if (payload == null)
            {
                return 0;
            }

            // Stable FNV-1a: deterministic across runtimes and no payload copies/strings.
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < payload.Length; ++i)
                {
                    hash = (hash ^ payload[i]) * 16777619u;
                }

                return (int)hash;
            }
        }
    }

    internal struct TypeSpan
    {
        public ReplyMessageType messageType;
        public int count;
        public float firstTimestamp;
        public float lastTimestamp;

        public float SpanSeconds => math.max(0f, lastTimestamp - firstTimestamp);
    }

    internal struct ClassifiedFrameSummary
    {
        public LevelRecordingRecordedPayloadKind kind;
        public int wireType;
        public int count;

        public string Label =>
            LevelRecordingPayloadClassification.FormatWireTypeLabel(wireType, kind);
    }

    internal struct Report
    {
        public int totalFrames;
        public int emptyFrames;
        public int unrecognizedFrames;
        public float wallDurationSeconds;
        public float lastFrameTimestamp;
        public float contentGapSeconds;
        public float gameplayTimestampBase;
        public float gameplaySpanSeconds;
        public float largestMoveGapSeconds;
        public int nonMonotonicTimestampCount;
        public int firstNonMonotonicFrameIndex;
        public float largestTimestampRegressionSeconds;
        public int moveFrameCount;
        public int maxMoveFramesAtSameTimestamp;
        public float maxMoveBatchTimestamp;
        public int maxIdenticalMoveFramesAtSameTimestamp;
        public float maxIdenticalMoveBatchTimestamp;
        public bool replyWriteGateOpenAtEnd;
        public int localChannelStatusAtEnd;
        public int remoteChannelStatusAtEnd;
        public List<TypeSpan> typeSpans;
        public List<ClassifiedFrameSummary> ancillarySpans;
    }

    public static Report Build(
        IReadOnlyList<LevelRecordingFrame> frames,
        float wallDurationSeconds,
        bool replyWriteGateOpenAtEnd,
        int localChannelStatusAtEnd,
        int remoteChannelStatusAtEnd)
    {
        var report = new Report
        {
            totalFrames = frames?.Count ?? 0,
            wallDurationSeconds = wallDurationSeconds,
            replyWriteGateOpenAtEnd = replyWriteGateOpenAtEnd,
            localChannelStatusAtEnd = localChannelStatusAtEnd,
            remoteChannelStatusAtEnd = remoteChannelStatusAtEnd,
            firstNonMonotonicFrameIndex = -1,
            typeSpans = new List<TypeSpan>(),
            ancillarySpans = new List<ClassifiedFrameSummary>()
        };

        if (frames == null || frames.Count == 0)
        {
            return report;
        }

        LevelRecordingTiming.TryGetLastFrameTimestamp(frames, out report.lastFrameTimestamp);
        report.contentGapSeconds = math.max(0f, wallDurationSeconds - report.lastFrameTimestamp);
        report.gameplayTimestampBase = LevelRecordingTiming.ComputeGameplayTimestampBase(frames);
        report.gameplaySpanSeconds = LevelRecordingTiming.ComputeGameplaySpan(
            frames,
            report.gameplayTimestampBase);

        var spanByType = new Dictionary<ReplyMessageType, TypeSpan>();
        var ancillaryByKey = new Dictionary<(LevelRecordingRecordedPayloadKind kind, int wireType), ClassifiedFrameSummary>();
        var movePayloadCountsAtTimestamp = new Dictionary<byte[], int>(PayloadContentComparer.Instance);
        float previousMoveTimestamp = -1f;
        int currentMoveTimestampBatch = 0;
        for (int i = 0; i < frames.Count; ++i)
        {
            if (i > 0 && frames[i].timestamp < frames[i - 1].timestamp)
            {
                float regression = frames[i - 1].timestamp - frames[i].timestamp;
                ++report.nonMonotonicTimestampCount;
                if (report.firstNonMonotonicFrameIndex < 0)
                {
                    report.firstNonMonotonicFrameIndex = i;
                }

                if (regression > report.largestTimestampRegressionSeconds)
                {
                    report.largestTimestampRegressionSeconds = regression;
                }
            }

            var payload = frames[i].payload;
            if (!LevelRecordingPayloadClassification.TryClassifyRecordedPayload(
                    payload,
                    out var kind,
                    out int wireType,
                    out var messageType))
            {
                report.unrecognizedFrames++;
                continue;
            }

            if (kind == LevelRecordingRecordedPayloadKind.Empty)
            {
                report.emptyFrames++;
                continue;
            }

            if (kind == LevelRecordingRecordedPayloadKind.Unrecognized)
            {
                report.unrecognizedFrames++;
                continue;
            }

            if (kind != LevelRecordingRecordedPayloadKind.GameplayReply)
            {
                __AccumulateAncillarySpan(ancillaryByKey, kind, wireType);
                continue;
            }

            if (!spanByType.TryGetValue(messageType, out var span))
            {
                span = new TypeSpan
                {
                    messageType = messageType,
                    count = 0,
                    firstTimestamp = frames[i].timestamp,
                    lastTimestamp = frames[i].timestamp
                };
            }

            span.count++;
            span.lastTimestamp = frames[i].timestamp;
            spanByType[messageType] = span;

            if (messageType == ReplyMessageType.Move)
            {
                report.moveFrameCount++;
                bool isSameTimestampBatch =
                    previousMoveTimestamp >= 0f &&
                    frames[i].timestamp == previousMoveTimestamp;
                if (previousMoveTimestamp >= 0f)
                {
                    float gap = frames[i].timestamp - previousMoveTimestamp;
                    if (gap > report.largestMoveGapSeconds)
                    {
                        report.largestMoveGapSeconds = gap;
                    }

                    currentMoveTimestampBatch = isSameTimestampBatch
                        ? currentMoveTimestampBatch + 1
                        : 1;
                }
                else
                {
                    currentMoveTimestampBatch = 1;
                }

                if (!isSameTimestampBatch)
                {
                    movePayloadCountsAtTimestamp.Clear();
                }

                int identicalPayloadCount = 1;
                if (movePayloadCountsAtTimestamp.TryGetValue(payload, out int previousPayloadCount))
                {
                    identicalPayloadCount = previousPayloadCount + 1;
                }

                movePayloadCountsAtTimestamp[payload] = identicalPayloadCount;

                if (currentMoveTimestampBatch > report.maxMoveFramesAtSameTimestamp)
                {
                    report.maxMoveFramesAtSameTimestamp = currentMoveTimestampBatch;
                    report.maxMoveBatchTimestamp = frames[i].timestamp;
                }

                if (identicalPayloadCount > report.maxIdenticalMoveFramesAtSameTimestamp)
                {
                    report.maxIdenticalMoveFramesAtSameTimestamp = identicalPayloadCount;
                    report.maxIdenticalMoveBatchTimestamp = frames[i].timestamp;
                }

                previousMoveTimestamp = frames[i].timestamp;
            }
        }

        foreach (var pair in spanByType)
        {
            report.typeSpans.Add(pair.Value);
        }

        report.typeSpans.Sort((a, b) => a.firstTimestamp.CompareTo(b.firstTimestamp));
        foreach (var pair in ancillaryByKey)
        {
            report.ancillarySpans.Add(pair.Value);
        }

        report.ancillarySpans.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));

        if (report.maxMoveFramesAtSameTimestamp >= CollapsedSameTimestampMoveThreshold)
        {
            BotReplayLog.Diag(
                $"[RecordingAudit] {report.maxMoveFramesAtSameTimestamp} Move frames share timestamp " +
                $"{report.maxMoveBatchTimestamp:F3}s — likely batch capture after EndWrite index reset; " +
                "re-record after network suppress fix or check Spread warning in console.");
        }

        return report;
    }

    private static void __AccumulateAncillarySpan(
        Dictionary<(LevelRecordingRecordedPayloadKind kind, int wireType), ClassifiedFrameSummary> ancillaryByKey,
        LevelRecordingRecordedPayloadKind kind,
        int wireType)
    {
        var key = (kind, wireType);
        if (!ancillaryByKey.TryGetValue(key, out var summary))
        {
            summary = new ClassifiedFrameSummary
            {
                kind = kind,
                wireType = wireType,
                count = 0
            };
        }

        summary.count++;
        ancillaryByKey[key] = summary;
    }

    public static void LogReport(in Report report, string phase)
    {
        var builder = new StringBuilder();
        builder.Append("[RecordingAudit] phase=").Append(phase);
        builder.Append(", frames=").Append(report.totalFrames);
        builder.Append(", empty=").Append(report.emptyFrames);
        builder.Append(", unrecognized=").Append(report.unrecognizedFrames);
        builder.Append(", wall=").Append(report.wallDurationSeconds.ToString("F3")).Append('s');
        builder.Append(", lastFrameTs=").Append(report.lastFrameTimestamp.ToString("F3")).Append('s');
        builder.Append(", contentGap=").Append(report.contentGapSeconds.ToString("F3")).Append('s');
        builder.Append(", gameplayBase=").Append(report.gameplayTimestampBase.ToString("F3")).Append('s');
        builder.Append(", gameplaySpan=").Append(report.gameplaySpanSeconds.ToString("F3")).Append('s');
        builder.Append(", moveCount=").Append(report.moveFrameCount);
        builder.Append(", largestMoveGap=").Append(report.largestMoveGapSeconds.ToString("F3")).Append('s');
        builder.Append(", nonMonotonicTs=").Append(report.nonMonotonicTimestampCount);
        builder.Append(", firstRegressionFrame=").Append(report.firstNonMonotonicFrameIndex);
        builder.Append(", largestRegression=").Append(report.largestTimestampRegressionSeconds.ToString("F3")).Append('s');
        builder.Append(", maxSameTimestampMove=").Append(report.maxMoveFramesAtSameTimestamp);
        builder.Append('@').Append(report.maxMoveBatchTimestamp.ToString("F3")).Append('s');
        builder.Append(", maxIdenticalSameTimestampMove=")
            .Append(report.maxIdenticalMoveFramesAtSameTimestamp);
        builder.Append('@').Append(report.maxIdenticalMoveBatchTimestamp.ToString("F3")).Append('s');
        builder.Append(", replyGateOpen=").Append(report.replyWriteGateOpenAtEnd);
        builder.Append(", localChannel=").Append(report.localChannelStatusAtEnd);
        builder.Append(", remoteChannel=").Append(report.remoteChannelStatusAtEnd);

        for (int i = 0; i < report.ancillarySpans.Count; ++i)
        {
            ClassifiedFrameSummary span = report.ancillarySpans[i];
            builder.Append(", ").Append(span.Label).Append('=').Append(span.count);
        }

        for (int i = 0; i < report.typeSpans.Count; ++i)
        {
            TypeSpan span = report.typeSpans[i];
            builder.Append(", ").Append(span.messageType).Append('=').Append(span.count);
            builder.Append('[').Append(span.firstTimestamp.ToString("F3"));
            builder.Append('-').Append(span.lastTimestamp.ToString("F3"));
            builder.Append(", span=").Append(span.SpanSeconds.ToString("F3")).Append('s').Append(']');
        }

        BotReplayLog.Info(builder.ToString());
    }

    public static bool IsReplyWriteGateOpen()
    {
        return LevelPlayerShared<RemotePlayer>.isOnline &&
               LevelPlayerShared<RemotePlayer>.channelStatus == LevelPlayerShared<LocalPlayer>.channelStatus;
    }
}
