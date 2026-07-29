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
    private const float CollapsedGameplaySpanSeconds = 0.001f;
    private const int MinFramesForLongSession = 10;
    // Heuristic only — never call LevelPlayerProperty ctor here (DataStreamReader LogError spam on corrupt bins).
    private const int MinPlayerPropertyHeuristicBytes = 32;

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
        var result = new LevelRecordingFileValidationResult
        {
            filePath = filePath,
            issues = new List<LevelRecordingValidationIssue>()
        };

        LevelRecordingSession session;
        try
        {
            session = LevelRecordingSerializer.Read(filePath);
            result.parseOk = true;
        }
        catch (Exception exception)
        {
            result.parseOk = false;
            result.parseError = exception.Message;
            __AddIssue(result.issues, LevelRecordingValidationSeverity.Error, "ParseFailed", exception.Message);
            return result;
        }

        float wallDuration = math.max(session.header.duration, 0f);
        result.audit = LevelRecordingSessionAudit.Build(
            session.frames,
            wallDuration,
            replyWriteGateOpenAtEnd: false,
            localChannelStatusAtEnd: 0,
            remoteChannelStatusAtEnd: 0);

        __ValidateStructure(session, in result.audit, result.issues);
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

    private static void __ValidatePlayerProperty(
        IReadOnlyList<LevelRecordingFrame> frames,
        List<LevelRecordingValidationIssue> issues,
        out bool hasParseablePlayerProperty)
    {
        hasParseablePlayerProperty = false;
        bool sawPlayerProperty = false;
        bool sawUndersizedPlayerProperty = false;

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
            if (payload.Length < MinPlayerPropertyHeuristicBytes)
            {
                sawUndersizedPlayerProperty = true;
                continue;
            }

            hasParseablePlayerProperty = true;
            return;
        }

        if (hasParseablePlayerProperty)
        {
            return;
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
            "PlayerProperty-sized frame present but below heuristic threshold or missing.");
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
        bool hasSameTimestampMoveBatch = audit.maxMoveFramesAtSameTimestamp > 1;
        bool hasCollapsedGameplaySpan =
            audit.moveFrameCount > 1 &&
            audit.gameplaySpanSeconds < CollapsedGameplaySpanSeconds;
        if (hasSameTimestampMoveBatch || hasCollapsedGameplaySpan)
        {
            __AddIssue(
                issues,
                LevelRecordingValidationSeverity.Error,
                "CollapsedMoveTimestamps",
                $"maxSameTimestampMove={audit.maxMoveFramesAtSameTimestamp} at " +
                $"{audit.maxMoveBatchTimestamp:F3}s, moveCount={audit.moveFrameCount}, " +
                $"gameplaySpan={audit.gameplaySpanSeconds:F3}s " +
                "(likely EndWrite index reset batch capture; re-record before deployment).");
            return;
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
            audit.moveFrameCount > 1)
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
