using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using ZG;

internal enum LevelRecordingValidationSeverity : byte
{
    Ok = 0,
    Warning = 1,
    Error = 2
}

internal struct LevelRecordingValidationIssue
{
    public LevelRecordingValidationSeverity severity;
    public string code;
    public string message;
}

internal struct LevelRecordingFileValidationResult
{
    public string filePath;
    public bool parseOk;
    public string parseError;
    public LevelRecordingSessionAudit.Report audit;
    public int selectSkillCount;
    public int selectSkillValidCount;
    public int selectSkillInvalidCount;
    public int firstInvalidSelectSkillFrameIndex;
    public int malformedGameplayPayloadCount;
    public int firstMalformedGameplayFrameIndex;
    public bool hasParseablePlayerProperty;
    public List<LevelRecordingValidationIssue> issues;

    public LevelRecordingValidationSeverity WorstSeverity
    {
        get
        {
            var worst = LevelRecordingValidationSeverity.Ok;
            if (issues == null)
            {
                return worst;
            }

            for (int i = 0; i < issues.Count; ++i)
            {
                if (issues[i].severity > worst)
                {
                    worst = issues[i].severity;
                }
            }

            return worst;
        }
    }
}

/// <summary>
/// Offline checks for .bin recordings (batch Editor audit + EditMode regression).
/// </summary>
internal static class LevelRecordingFileValidator
{
    private const float MinDurationForGameplayExpectationSeconds = 15f;
    private const float MinExpectedMoveSpanRatio = 0.25f;
    private const float LargeContentGapSeconds = 15f;
    private const float LargeMoveGapSeconds = 15f;
    private const float CollapsedGameplaySpanSeconds = 0.001f;
    private const int MinFramesForLongSession = 10;
    // This is only a cheap preflight. The strict current PlayerProperty parser below remains
    // authoritative for every frame that is large enough to inspect.
    private const int MinPlayerPropertyPreflightBytes = 8;

    public static IEnumerable<string> EnumerateRecordingFiles(string rootDirectory)
    {
        if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.bin", SearchOption.AllDirectories))
        {
            yield return path;
        }
    }

    public static LevelRecordingFileValidationResult Validate(string filePath)
    {
        LevelRecordingSession session;
        try
        {
            session = LevelRecordingSerializer.Read(filePath);
        }
        catch (Exception exception)
        {
            var result = new LevelRecordingFileValidationResult
            {
                filePath = filePath,
                parseOk = false,
                parseError = exception.Message,
                issues = new List<LevelRecordingValidationIssue>()
            };
            __AddIssue(result.issues, LevelRecordingValidationSeverity.Error, "ParseFailed", exception.Message);
            return result;
        }

        return ValidateSession(session, filePath);
    }

    /// <summary>
    /// Validates an in-memory capture before serialization. This is also the single rule path
    /// used by file validation, so save-time and deployment-time gates cannot drift.
    /// </summary>
    public static LevelRecordingFileValidationResult ValidateSession(
        LevelRecordingSession session,
        string sourceName = "<memory>")
    {
        var result = new LevelRecordingFileValidationResult
        {
            filePath = sourceName,
            parseOk = session != null,
            issues = new List<LevelRecordingValidationIssue>()
        };

        if (session == null)
        {
            result.parseError = "Recording session is null.";
            __AddIssue(
                result.issues,
                LevelRecordingValidationSeverity.Error,
                "ParseFailed",
                result.parseError);
            return result;
        }

        float wallDuration = math.max(session.header.duration, 0f);
        result.audit = LevelRecordingSessionAudit.Build(
            session.frames,
            wallDuration,
            replyWriteGateOpenAtEnd: false,
            localChannelStatusAtEnd: 0,
            remoteChannelStatusAtEnd: 0);

        if (!string.IsNullOrEmpty(session.captureError))
        {
            __AddIssue(
                result.issues,
                LevelRecordingValidationSeverity.Error,
                "EndWriteCaptureFailed",
                session.captureError);
        }

        __ValidateStructure(session, in result.audit, result.issues);
        __ValidateGameplayPayloads(
            session.frames,
            result.issues,
            out result.malformedGameplayPayloadCount,
            out result.firstMalformedGameplayFrameIndex);
        __ValidatePlayerProperty(session.frames, result.issues, out result.hasParseablePlayerProperty);
        __ValidateGameplaySpan(in result.audit, wallDuration, result.issues);
        try
        {
            __ValidateSelectSkillSequence(
                session.frames,
                result.hasParseablePlayerProperty,
                out result.selectSkillCount,
                out result.selectSkillValidCount,
                out result.selectSkillInvalidCount,
                out result.firstInvalidSelectSkillFrameIndex,
                result.issues);
        }
        catch (Exception exception)
        {
            __AddIssue(
                result.issues,
                LevelRecordingValidationSeverity.Error,
                "SelectSkillValidationFailed",
                exception.Message);
        }

        return result;
    }

    private static void __ValidateStructure(
        LevelRecordingSession session,
        in LevelRecordingSessionAudit.Report audit,
        List<LevelRecordingValidationIssue> issues)
    {
        if (float.IsNaN(session.header.duration) ||
            float.IsInfinity(session.header.duration) ||
            session.header.duration < 0f)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "InvalidDuration",
                $"Header duration is invalid: {session.header.duration}.");
        }

        if (session.frames == null || session.frames.Count == 0)
        {
            __AddIssue(issues, LevelRecordingValidationSeverity.Error, "EmptyRecording", "No frames in file.");
            return;
        }

        if (session.header.frameCount != session.frames.Count)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "FrameCountMismatch",
                $"Header frameCount={session.header.frameCount} but read {session.frames.Count} frame(s).");
        }

        int oversizedPayloads = 0;
        int largestPayload = 0;
        for (int i = 0; i < session.frames.Count; ++i)
        {
            int length = session.frames[i].payload?.Length ?? 0;
            if (length > largestPayload)
            {
                largestPayload = length;
            }

            if (length > BotRelayPacket.MaxPayloadSize)
            {
                ++oversizedPayloads;
            }
        }

        if (oversizedPayloads > 0)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "PayloadExceedsRelayPacketCapacity",
                $"{oversizedPayloads} frame(s) exceed BotRelayPacket.MaxPayloadSize=" +
                $"{BotRelayPacket.MaxPayloadSize}B (largest={largestPayload}B) and cannot be replayed.");
        }

        if (audit.unrecognizedFrames > 0)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Warning,
                "UnrecognizedPayloads",
                $"{audit.unrecognizedFrames} frame(s) could not be classified (corrupt or unknown wire type).");
        }

        if (audit.emptyFrames > 0)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Warning,
                "EmptyPayloadFrames",
                $"{audit.emptyFrames} frame(s) have empty payload.");
        }

        if (audit.nonMonotonicTimestampCount > 0)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "NonMonotonicTimestamps",
                $"{audit.nonMonotonicTimestampCount} timestamp regression(s); first frameIndex=" +
                $"{audit.firstNonMonotonicFrameIndex}, largest={audit.largestTimestampRegressionSeconds:F3}s. " +
                "Replay consumes file order, so regressing timestamps collapse frames into a later tick.");
        }

        if (audit.wallDurationSeconds > 30f && session.frames.Count < MinFramesForLongSession)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "SparseFrames",
                $"duration={audit.wallDurationSeconds:F1}s but only {session.frames.Count} frame(s).");
        }

        if (audit.contentGapSeconds > LargeContentGapSeconds)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Warning,
                "LargeContentGap",
                $"lastFrameTs={audit.lastFrameTimestamp:F1}s vs duration={audit.wallDurationSeconds:F1}s " +
                $"(gap {audit.contentGapSeconds:F1}s).");
        }
    }

    private static void __ValidateGameplayPayloads(
        IReadOnlyList<LevelRecordingFrame> frames,
        List<LevelRecordingValidationIssue> issues,
        out int malformedCount,
        out int firstMalformedFrameIndex)
    {
        malformedCount = 0;
        firstMalformedFrameIndex = -1;
        if (frames == null)
        {
            return;
        }

        for (int i = 0; i < frames.Count; ++i)
        {
            var payload = frames[i].payload;
            if (payload == null ||
                payload.Length == 0 ||
                !LevelRecordingPayloadClassification.TryReadRecordedWireType(payload, out int wireType) ||
                !__IsInLevelReplayableWireType(wireType))
            {
                continue;
            }

            bool isValid = payload.Length <= BotRelayPacket.MaxPayloadSize;
            if (isValid)
            {
                var packet = default(BotRelayPacket);
                packet.length = payload.Length;
                for (int j = 0; j < payload.Length; ++j)
                {
                    packet.SetByte(j, payload[j]);
                }

                isValid =
                    BotReplayPayloadUtility.TryGetReplyMessageType(in packet, out var messageType) &&
                    (int)messageType == wireType;
            }

            if (isValid)
            {
                continue;
            }

            ++malformedCount;
            if (firstMalformedFrameIndex < 0)
            {
                firstMalformedFrameIndex = i;
            }
        }

        if (malformedCount > 0)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "MalformedGameplayPayload",
                $"{malformedCount} replayable gameplay frame(s) do not match the exact current body layout " +
                $"(first bad frameIndex={firstMalformedFrameIndex}).");
        }
    }

    private static bool __IsInLevelReplayableWireType(int wireType)
    {
        switch ((ReplyMessageType)wireType)
        {
            case ReplyMessageType.Chat:
            case ReplyMessageType.Camera:
            case ReplyMessageType.Move:
            case ReplyMessageType.Damage:
            case ReplyMessageType.SelectSkill:
                return true;
            default:
                return false;
        }
    }

    private static void __ValidatePlayerProperty(
        IReadOnlyList<LevelRecordingFrame> frames,
        List<LevelRecordingValidationIssue> issues,
        out bool hasParseablePlayerProperty)
    {
        hasParseablePlayerProperty = false;
        bool sawPlayerProperty = false;
        bool sawUndersizedPlayerProperty = false;
        bool sawUnparseablePlayerProperty = false;

        for (int i = 0; i < frames.Count; ++i)
        {
            var payload = frames[i].payload;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType) ||
                messageType != ReplyMessageType.PlayerProperty)
            {
                continue;
            }

            sawPlayerProperty = true;
            if (payload.Length < MinPlayerPropertyPreflightBytes)
            {
                sawUndersizedPlayerProperty = true;
                continue;
            }

            if (ReplyMessageReplayInjector.TryParsePlayerProperty(payload, out _))
            {
                hasParseablePlayerProperty = true;
                return;
            }

            sawUnparseablePlayerProperty = true;
        }

        if (!sawPlayerProperty)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "MissingPlayerProperty",
                "No PlayerProperty frame — bot cannot join remote with correct stats.");
            return;
        }

        if (sawUnparseablePlayerProperty)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "UnparseablePlayerProperty",
                "PlayerProperty frame does not match the exact current header/body layout.");
            return;
        }

        if (sawUndersizedPlayerProperty)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "UndersizedPlayerProperty",
                "PlayerProperty frame is too small to be a valid wire payload.");
            return;
        }

        __AddIssue(
            issues,
            LevelRecordingValidationSeverity.Error,
            "UnparseablePlayerProperty",
            "PlayerProperty frame could not be parsed.");
    }

    private static bool __TryValidateSelectSkillPayload(byte[] payload)
    {
        if (payload == null ||
            payload.Length < 8 ||
            payload.Length > 4096 ||
            !LevelRecordingPayloadUtility.TryGetRecordedBodySlice(payload, out int bodyOffset, out int bodyLength) ||
            bodyLength <= 0 ||
            bodyOffset + bodyLength > payload.Length)
        {
            return false;
        }

        return true;
    }

    private static void __ValidateGameplaySpan(
        in LevelRecordingSessionAudit.Report audit,
        float wallDuration,
        List<LevelRecordingValidationIssue> issues)
    {
        if (audit.largestMoveGapSeconds > LargeMoveGapSeconds)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Warning,
                "LargeMoveGap",
                $"Largest gap between Move frames is {audit.largestMoveGapSeconds:F1}s " +
                $"(threshold {LargeMoveGapSeconds:F0}s); verify that the idle interval is intentional.");
        }

        bool hasCollapsedSameTimestampMoveBatch =
            audit.maxMoveFramesAtSameTimestamp >=
            LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold;
        bool hasCollapsedIdenticalMoveBatch =
            audit.maxIdenticalMoveFramesAtSameTimestamp >=
            LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold;
        bool hasCollapsedGameplaySpan =
            audit.moveFrameCount >= LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold &&
            audit.gameplaySpanSeconds < CollapsedGameplaySpanSeconds;
        if (hasCollapsedSameTimestampMoveBatch ||
            hasCollapsedIdenticalMoveBatch ||
            hasCollapsedGameplaySpan)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "CollapsedMoveTimestamps",
                $"maxSameTimestampMove={audit.maxMoveFramesAtSameTimestamp} at " +
                $"{audit.maxMoveBatchTimestamp:F3}s, moveCount={audit.moveFrameCount}, " +
                $"maxIdenticalSameTimestampMove={audit.maxIdenticalMoveFramesAtSameTimestamp} at " +
                $"{audit.maxIdenticalMoveBatchTimestamp:F3}s, " +
                $"gameplaySpan={audit.gameplaySpanSeconds:F3}s " +
                "(collapsed capture backlog; re-record before deployment).");
            return;
        }

        if (audit.maxIdenticalMoveFramesAtSameTimestamp >=
            LevelRecordingSessionAudit.DuplicateSameTickMoveThreshold)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Warning,
                "DuplicateSameTickMove",
                $"{audit.maxIdenticalMoveFramesAtSameTimestamp} byte-identical Move frames share timestamp " +
                $"{audit.maxIdenticalMoveBatchTimestamp:F3}s. This is below the collapsed-batch threshold " +
                $"({LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold}) but should be reviewed.");
        }

        if (wallDuration >= MinDurationForGameplayExpectationSeconds && audit.moveFrameCount == 0)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Warning,
                "NoMoveFrames",
                $"duration={wallDuration:F1}s but no Move frames captured.");
        }

        if (wallDuration >= MinDurationForGameplayExpectationSeconds &&
            audit.moveFrameCount >= LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold)
        {
            float expectedMinSpan = wallDuration * MinExpectedMoveSpanRatio;
            if (audit.gameplaySpanSeconds < expectedMinSpan)
            {
                __AddIssue(
                    issues,
                    LevelRecordingValidationSeverity.Error,
                    "ShortMoveSpan",
                    $"gameplaySpan={audit.gameplaySpanSeconds:F1}s vs duration={wallDuration:F1}s " +
                    $"(expected at least ~{expectedMinSpan:F0}s of movement).");
            }
        }
    }

    private static void __ValidateSelectSkillSequence(
        IReadOnlyList<LevelRecordingFrame> frames,
        bool hasParseablePlayerProperty,
        out int selectSkillCount,
        out int selectSkillValidCount,
        out int selectSkillInvalidCount,
        out int firstInvalidSelectSkillFrameIndex,
        List<LevelRecordingValidationIssue> issues)
    {
        selectSkillCount = 0;
        selectSkillValidCount = 0;
        selectSkillInvalidCount = 0;
        firstInvalidSelectSkillFrameIndex = -1;

        if (frames == null || frames.Count == 0)
        {
            return;
        }

        var simulated = new List<int>();
        for (int i = 0; i < frames.Count; ++i)
        {
            var payload = frames[i].payload;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType) ||
                messageType != ReplyMessageType.SelectSkill)
            {
                continue;
            }

            if (!__TryValidateSelectSkillPayload(payload))
            {
                selectSkillCount++;
                selectSkillInvalidCount++;
                if (firstInvalidSelectSkillFrameIndex < 0)
                {
                    firstInvalidSelectSkillFrameIndex = i;
                }

                continue;
            }

            selectSkillCount++;
            try
            {
                if (LevelRecordingSelectSkillReplayOps.TryValidateAndSimulateWithBootstrap(payload, simulated))
                {
                    selectSkillValidCount++;
                    continue;
                }
            }
            catch (Exception)
            {
                selectSkillInvalidCount++;
                if (firstInvalidSelectSkillFrameIndex < 0)
                {
                    firstInvalidSelectSkillFrameIndex = i;
                }

                continue;
            }

            selectSkillInvalidCount++;
            if (firstInvalidSelectSkillFrameIndex < 0)
            {
                firstInvalidSelectSkillFrameIndex = i;
            }
        }

        if (selectSkillCount == 0)
        {
            return;
        }

        if (selectSkillInvalidCount > 0)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "SelectSkillChainBroken",
                $"{selectSkillInvalidCount}/{selectSkillCount} SelectSkill frame(s) fail origin chain " +
                $"(first bad frameIndex={firstInvalidSelectSkillFrameIndex}).");
        }

        if (selectSkillCount > 0 && !hasParseablePlayerProperty)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Warning,
                "SelectSkillWithoutPlayerProperty",
                "SelectSkill present but PlayerProperty missing/unparseable — replay baseline unreliable.");
        }
    }

    private static void __AddIssue(
        List<LevelRecordingValidationIssue> issues,
        LevelRecordingValidationSeverity severity,
        string code,
        string message)
    {
        issues.Add(new LevelRecordingValidationIssue
        {
            severity = severity,
            code = code,
            message = message
        });
    }
}
