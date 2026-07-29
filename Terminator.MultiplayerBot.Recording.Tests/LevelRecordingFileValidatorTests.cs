using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

[Category("Recording")]
[Category("Regression")]
[TestFixture]
public sealed class LevelRecordingFileValidatorTests
{
    [Test]
    public void Validate_FlagsMissingPlayerProperty()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildFrame(1f, ReplyMessageType.Move)
        };

        var result = __ValidateFrames(frames, duration: 30f);
        Assert.AreEqual(LevelRecordingValidationSeverity.Error, result.WorstSeverity);
        Assert.IsTrue(__HasIssue(result, "MissingPlayerProperty"));
    }

    [Test]
    public void Validate_FlagsCollapsedMoveTimestamps()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            __BuildFrame(10f, ReplyMessageType.Move),
            __BuildFrame(10f, ReplyMessageType.Move),
            __BuildFrame(10f, ReplyMessageType.Move)
        };

        var result = __ValidateFrames(frames, duration: 30f);
        Assert.IsTrue(__HasIssue(result, "CollapsedMoveTimestamps"));
    }

    [Test]
    public void Validate_FlagsCollapsedMoveTimestampBatchInsideOtherwiseLongRecording()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            __BuildFrame(5f, ReplyMessageType.Move),
            __BuildFrame(10f, ReplyMessageType.Move),
            __BuildFrame(10f, ReplyMessageType.Move),
            __BuildFrame(20f, ReplyMessageType.Move)
        };

        var result = __ValidateFrames(frames, duration: 25f);
        Assert.IsTrue(__HasIssue(result, "CollapsedMoveTimestamps"));
        Assert.AreEqual(2, result.audit.maxMoveFramesAtSameTimestamp);
        Assert.AreEqual(10f, result.audit.maxMoveBatchTimestamp, 0.001f);
    }

    [Test]
    public void Validate_FlagsBrokenSelectSkillChain()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            __BuildSelectSkillFrame(2f, (5, 3, 0)),
            __BuildSelectSkillFrame(3f, (8, 99, 0))
        };

        var result = __ValidateFrames(frames, duration: 10f);
        Assert.IsTrue(__HasIssue(result, "SelectSkillChainBroken"));
        Assert.AreEqual(1, result.selectSkillInvalidCount);
    }

    [Test]
    public void Validate_ClassifiesInviteWithoutUnrecognizedPayloadWarning()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildInviteFrame(0.5f),
            __BuildPlayerPropertyFrame(1f),
            __BuildFrame(5f, ReplyMessageType.Move),
            __BuildFrame(20f, ReplyMessageType.Move)
        };

        var result = __ValidateFrames(frames, duration: 25f);
        Assert.IsFalse(__HasIssue(result, "UnrecognizedPayloads"));
        Assert.IsFalse(__HasIssue(result, "UnknownPayloads"));
        Assert.AreEqual(1, result.audit.ancillarySpans.Count);
        Assert.AreEqual(nameof(ReplyMessageType.Invite), result.audit.ancillarySpans[0].Label);
        Assert.AreEqual(1, result.audit.ancillarySpans[0].count);
    }

    [Test]
    public void Validate_AcceptsHealthyMoveSpan()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            __BuildFrame(5f, ReplyMessageType.Move),
            __BuildFrame(20f, ReplyMessageType.Move)
        };

        var result = __ValidateFrames(frames, duration: 25f);
        if (result.WorstSeverity != LevelRecordingValidationSeverity.Ok)
        {
            var details = string.Empty;
            for (int i = 0; i < result.issues.Count; ++i)
            {
                details += result.issues[i].code + "; ";
            }

            Assert.AreEqual(LevelRecordingValidationSeverity.Ok, result.WorstSeverity, details);
        }
    }

    [Test]
    public void Validate_FlagsFrameAboveRelayPacketCapacity()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            new LevelRecordingFrame
            {
                timestamp = 2f,
                payload = __BuildPayload(ReplyMessageType.Move, BotRelayPacket.MaxPayloadSize)
            }
        };

        var result = __ValidateFrames(frames, duration: 2f);
        Assert.IsTrue(__HasIssue(result, "PayloadExceedsRelayPacketCapacity"));
        Assert.AreEqual(LevelRecordingValidationSeverity.Error, result.WorstSeverity);
    }

    private static LevelRecordingFileValidationResult __ValidateFrames(
        List<LevelRecordingFrame> frames,
        float duration)
    {
        var path = System.IO.Path.Combine(
            UnityEngine.Application.temporaryCachePath,
            "LevelRecordingFileValidatorTests.bin");
        var header = new LevelRecordingFileHeader
        {
            userStageID = 144,
            duration = duration,
            frameCount = frames.Count
        };
        LevelRecordingSerializer.Write(path, in header, frames);
        return LevelRecordingFileValidator.Validate(path);
    }

    private static bool __HasIssue(in LevelRecordingFileValidationResult result, string code)
    {
        for (int i = 0; i < result.issues.Count; ++i)
        {
            if (result.issues[i].code == code)
            {
                return true;
            }
        }

        return false;
    }

    private static LevelRecordingFrame __BuildFrame(float timestamp, ReplyMessageType messageType)
    {
        return new LevelRecordingFrame
        {
            timestamp = timestamp,
            payload = __BuildPayload(messageType)
        };
    }

    private static LevelRecordingFrame __BuildPlayerPropertyFrame(float timestamp)
    {
        var property = new LevelPlayerProperty
        {
            effectTargetHP = 1000,
            effectRage = 1,
            effectDamageScale = 0.25f,
            instanceName = "AuditTestBotPropertyFrame"
        };
        property.maskSkillNames.Add("MaskSkillNameForAuditTest");
        Assert.IsTrue(
            LevelRecordingPlayerPropertyWireRecovery.TryBuildRecordedPayload(in property, out var payload));
        Assert.GreaterOrEqual(payload.Length, 32, "Test PlayerProperty payload should pass heuristic threshold.");
        Assert.IsTrue(
            ReplyMessageReplayInjector.TryParsePlayerProperty(payload, out _, logFailures: false),
            "PlayerProperty roundtrip failed (" + payload.Length + " bytes).");

        return new LevelRecordingFrame
        {
            timestamp = timestamp,
            payload = payload
        };
    }

    private static LevelRecordingFrame __BuildSelectSkillFrame(
        float timestamp,
        (int index, int origin, int active) skill)
    {
        return new LevelRecordingFrame
        {
            timestamp = timestamp,
            payload = __BuildSelectSkillPayload(new[] { skill })
        };
    }

    private static LevelRecordingFrame __BuildInviteFrame(float timestamp)
    {
        using var scratch = new NativeArray<byte>(128, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        writer.WriteReplyHeader((int)ReplyMessageType.Invite, NetworkRelayType.All);
        var payload = new byte[writer.Length];
        for (int i = 0; i < writer.Length; ++i)
        {
            payload[i] = scratch[i];
        }

        return new LevelRecordingFrame
        {
            timestamp = timestamp,
            payload = payload
        };
    }

    private static byte[] __BuildPayload(ReplyMessageType messageType, int extraBodyBytes = 4)
    {
        using var bytes = new NativeArray<byte>(16 + extraBodyBytes, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        writer.WritePackedInt((int)messageType, StreamCompressionModel.Default);
        writer.WritePackedInt(0, StreamCompressionModel.Default);
        for (int i = 0; i < extraBodyBytes; ++i)
        {
            writer.WriteByte(0);
        }

        var copy = new byte[writer.Length];
        bytes.GetSubArray(0, writer.Length).CopyTo(copy);
        return copy;
    }

    private static byte[] __BuildSelectSkillPayload((int index, int origin, int active)[] skills)
    {
        using var scratch = new NativeArray<byte>(128, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WriteReplyHeader((int)ReplyMessageType.SelectSkill, NetworkRelayType.Channel);
        writer.WritePackedInt(skills.Length, model);
        for (int i = 0; i < skills.Length; ++i)
        {
            writer.WritePackedInt(skills[i].index, model);
            writer.WritePackedInt(skills[i].origin, model);
            writer.WritePackedInt(skills[i].active, model);
            writer.WritePackedFloat(1f, model);
        }

        writer.Flush();
        var payload = new byte[writer.Length];
        for (int i = 0; i < writer.Length; ++i)
        {
            payload[i] = scratch[i];
        }

        return payload;
    }

    [Test]
    public void BatchScan_SvnResourceRecordings_DoesNotThrow()
    {
        const string root = @"D:\SVN\Project\Terminator_Resource\Assets\StreamingAssets\Recordings";
        if (!System.IO.Directory.Exists(root))
        {
            Assert.Ignore("SVN recordings root not present on this machine.");
        }

        List<LevelRecordingFileValidationResult> results = null;
        Assert.DoesNotThrow(() =>
        {
            results = LevelRecordingBatchAuditRunner.Scan(root, showProgress: false);
        });

        Assert.IsNotNull(results);
        Assert.Greater(results.Count, 0);
        var summary = LevelRecordingBatchAuditRunner.Summarize(results);
        UnityEngine.Debug.Log(
            "[RecordingAudit] BatchScan test total=" + summary.totalFiles +
            " ok=" + summary.okCount +
            " warning=" + summary.warningCount +
            " error=" + summary.errorCount +
            " scanFailed=" + summary.scanFailures);
        Assert.AreEqual(0, summary.scanFailures, "Batch scan should not throw on individual files.");
    }
}
