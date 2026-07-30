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
    public void PlayerPropertyParser_AcceptsExactCurrentRecordedLayout()
    {
        var payload = __BuildPlayerPropertyFrame(0f).payload;
        Assert.IsTrue(ReplyMessageReplayInjector.TryParsePlayerProperty(payload, out _));
    }

    [TestCase(0x00)]
    [TestCase(0x7F)]
    public void PlayerPropertyParser_RejectsCompleteTrailingByte(int trailingByte)
    {
        var payload = __BuildPlayerPropertyFrame(0f).payload;
        var extended = new byte[payload.Length + 1];
        System.Array.Copy(payload, extended, payload.Length);
        extended[extended.Length - 1] = (byte)trailingByte;

        Assert.IsFalse(ReplyMessageReplayInjector.TryParsePlayerProperty(extended, out _));
    }

    [Test]
    public void PlayerPropertyParser_RejectsHeaderWithoutBody()
    {
        var payload = __BuildPlayerPropertyFrame(0f).payload;
        int bodyOffset = __GetRecordedBodyOffset(payload);
        var headerOnly = new byte[bodyOffset];
        System.Array.Copy(payload, headerOnly, bodyOffset);

        Assert.IsFalse(ReplyMessageReplayInjector.TryParsePlayerProperty(headerOnly, out _));
    }

    [Test]
    public void Validate_FlagsLegacySenderPrefixedPlayerProperty()
    {
        var frame = __BuildPlayerPropertyFrame(1f);
        frame.payload = __BuildLegacySenderPrefixedPlayerProperty(frame.payload);

        var result = __ValidateFrames(new List<LevelRecordingFrame> { frame }, duration: 1f);

        Assert.IsFalse(result.hasParseablePlayerProperty);
        Assert.IsTrue(__HasIssue(result, "UnparseablePlayerProperty"));
    }

    [Test]
    public void Validate_WarnsForSevenIdenticalSameTickMoves()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f)
        };
        for (int i = 0; i < LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold - 1; ++i)
        {
            frames.Add(__BuildFrame(5f, ReplyMessageType.Move));
        }

        frames.Add(__BuildFrame(6f, ReplyMessageType.Camera));
        var result = __ValidateFrames(frames, duration: 6f);
        Assert.AreEqual(7, result.audit.maxIdenticalMoveFramesAtSameTimestamp);
        Assert.IsFalse(__HasIssue(result, "CollapsedMoveTimestamps"));
        Assert.AreEqual(
            LevelRecordingValidationSeverity.Warning,
            __GetIssueSeverity(result, "DuplicateSameTickMove"));
    }

    [Test]
    public void Validate_WarnsForDuplicateSameTickMoveInsideOtherwiseHealthyRecording()
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
        Assert.IsFalse(__HasIssue(result, "CollapsedMoveTimestamps"));
        Assert.AreEqual(
            LevelRecordingValidationSeverity.Warning,
            __GetIssueSeverity(result, "DuplicateSameTickMove"));
        Assert.AreEqual(2, result.audit.maxMoveFramesAtSameTimestamp);
        Assert.AreEqual(2, result.audit.maxIdenticalMoveFramesAtSameTimestamp);
        Assert.AreEqual(10f, result.audit.maxMoveBatchTimestamp, 0.001f);
    }

    [Test]
    public void Validate_AcceptsTwoDifferentMovesAtSameTimestamp()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            __BuildMoveFrame(5f, 1f),
            __BuildMoveFrame(5f, 2f),
            __BuildFrame(6f, ReplyMessageType.Camera)
        };

        var result = __ValidateFrames(frames, duration: 6f);

        Assert.AreEqual(2, result.audit.maxMoveFramesAtSameTimestamp);
        Assert.AreEqual(1, result.audit.maxIdenticalMoveFramesAtSameTimestamp);
        Assert.IsFalse(__HasIssue(result, "CollapsedMoveTimestamps"));
        Assert.IsFalse(__HasIssue(result, "DuplicateSameTickMove"));
        Assert.AreEqual(LevelRecordingValidationSeverity.Ok, result.WorstSeverity);
    }

    [Test]
    public void Validate_FlagsEightDifferentMovesAtSameTimestampAsCaptureBacklog()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f)
        };
        for (int i = 0; i < LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold; ++i)
        {
            frames.Add(__BuildMoveFrame(5f, i));
        }

        frames.Add(__BuildFrame(6f, ReplyMessageType.Camera));
        var result = __ValidateFrames(frames, duration: 6f);

        Assert.AreEqual(8, result.audit.maxMoveFramesAtSameTimestamp);
        Assert.AreEqual(1, result.audit.maxIdenticalMoveFramesAtSameTimestamp);
        Assert.AreEqual(
            LevelRecordingValidationSeverity.Error,
            __GetIssueSeverity(result, "CollapsedMoveTimestamps"));
    }

    [Test]
    public void Validate_FlagsEightIdenticalMovesAtSameTimestampAsCollapsed()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f)
        };
        for (int i = 0; i < LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold; ++i)
        {
            frames.Add(__BuildFrame(5f, ReplyMessageType.Move));
        }

        frames.Add(__BuildFrame(6f, ReplyMessageType.Camera));
        var result = __ValidateFrames(frames, duration: 6f);

        Assert.AreEqual(8, result.audit.maxIdenticalMoveFramesAtSameTimestamp);
        Assert.AreEqual(
            LevelRecordingValidationSeverity.Error,
            __GetIssueSeverity(result, "CollapsedMoveTimestamps"));
        Assert.IsFalse(__HasIssue(result, "DuplicateSameTickMove"));
    }

    [Test]
    public void Validate_FlagsEightMovesCollapsedIntoSubMillisecondGameplaySpan()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f)
        };
        for (int i = 0; i < LevelRecordingSessionAudit.CollapsedSameTimestampMoveThreshold; ++i)
        {
            frames.Add(__BuildMoveFrame(5f + i * 0.0001f, i));
        }

        var result = __ValidateFrames(frames, duration: 5.001f);

        Assert.AreEqual(1, result.audit.maxMoveFramesAtSameTimestamp);
        Assert.Less(result.audit.gameplaySpanSeconds, 0.001f);
        Assert.AreEqual(
            LevelRecordingValidationSeverity.Error,
            __GetIssueSeverity(result, "CollapsedMoveTimestamps"));
    }

    [Test]
    public void Validate_FlagsNonMonotonicTimestamps()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            __BuildFrame(5f, ReplyMessageType.Move),
            __BuildFrame(4.5f, ReplyMessageType.Camera),
            __BuildFrame(20f, ReplyMessageType.Move)
        };

        var result = __ValidateFrames(frames, duration: 25f);
        Assert.IsTrue(__HasIssue(result, "NonMonotonicTimestamps"));
        Assert.AreEqual(1, result.audit.nonMonotonicTimestampCount);
        Assert.AreEqual(LevelRecordingValidationSeverity.Error, result.WorstSeverity);
    }

    [Test]
    public void Validate_FlagsEndWriteCaptureFailure()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            __BuildFrame(5f, ReplyMessageType.Move),
            __BuildFrame(20f, ReplyMessageType.Move)
        };

        var result = __ValidateFrames(
            frames,
            duration: 25f,
            captureError: "Synthetic EndWrite capture failure.");

        Assert.AreEqual(
            LevelRecordingValidationSeverity.Error,
            __GetIssueSeverity(result, "EndWriteCaptureFailed"));
        Assert.AreEqual(LevelRecordingValidationSeverity.Error, result.WorstSeverity);
    }

    [Test]
    public void Validate_WarnsAboutLargeMoveGap()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            __BuildFrame(5f, ReplyMessageType.Move),
            __BuildFrame(25f, ReplyMessageType.Move)
        };

        var result = __ValidateFrames(frames, duration: 30f);
        Assert.IsTrue(__HasIssue(result, "LargeMoveGap"));
        Assert.AreEqual(LevelRecordingValidationSeverity.Warning, result.WorstSeverity);
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
    public void Validate_FlagsMalformedCurrentGameplayBody()
    {
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            new LevelRecordingFrame
            {
                timestamp = 2f,
                payload = __BuildMalformedGameplayPayload(ReplyMessageType.Move)
            }
        };

        var result = __ValidateFrames(frames, duration: 3f);

        Assert.AreEqual(1, result.malformedGameplayPayloadCount);
        Assert.AreEqual(1, result.firstMalformedGameplayFrameIndex);
        Assert.IsTrue(__HasIssue(result, "MalformedGameplayPayload"));
    }

    [Test]
    public void Validate_FlagsSelectSkillTrailingWholeByte()
    {
        var selectSkillFrame = __BuildSelectSkillFrame(2f, (5, 3, 0));
        selectSkillFrame.payload = __AppendTrailingByte(selectSkillFrame.payload);
        var frames = new List<LevelRecordingFrame>
        {
            __BuildPlayerPropertyFrame(1f),
            selectSkillFrame
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

    [Test]
    public void SerializerVersion1_ReadAndReadHeader_FailClosed()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllBytes(
                path,
                new byte[]
                {
                    (byte)'T', (byte)'R', (byte)'B', (byte)'T',
                    1, 0
                });

            var headerError = Assert.Throws<System.IO.InvalidDataException>(
                () => LevelRecordingSerializer.ReadHeader(path));
            StringAssert.Contains("Unsupported recording version: 1", headerError.Message);

            var readError = Assert.Throws<System.IO.InvalidDataException>(
                () => LevelRecordingSerializer.Read(path));
            StringAssert.Contains("Unsupported recording version: 1", readError.Message);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public void SerializerVersion2_ReadRejectsBytesAfterDeclaredFrames()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            var header = new LevelRecordingFileHeader
            {
                userStageID = 26,
                duration = 1f
            };
            var frames = new List<LevelRecordingFrame>
            {
                new LevelRecordingFrame
                {
                    timestamp = 1f,
                    payload = new byte[] { 1, 2, 3 }
                }
            };
            LevelRecordingSerializer.Write(path, in header, frames);

            using (var stream = new System.IO.FileStream(
                       path,
                       System.IO.FileMode.Append,
                       System.IO.FileAccess.Write,
                       System.IO.FileShare.None))
            {
                stream.WriteByte(0xA5);
            }

            var error = Assert.Throws<System.IO.InvalidDataException>(
                () => LevelRecordingSerializer.Read(path));
            StringAssert.Contains("Unexpected trailing recording data: 1 byte(s).", error.Message);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public void SerializerWrite_NonMonotonicTimestamps_DoNotOverwriteExistingFile()
    {
        __AssertSerializerWriteFailsWithoutOverwriting(
            new List<LevelRecordingFrame>
            {
                new LevelRecordingFrame { timestamp = 1f, payload = new byte[] { 1 } },
                new LevelRecordingFrame { timestamp = 0.5f, payload = new byte[] { 2 } }
            },
            "regresses from");
    }

    [Test]
    public void SerializerWrite_InvalidTimestamp_DoesNotOverwriteExistingFile()
    {
        __AssertSerializerWriteFailsWithoutOverwriting(
            new List<LevelRecordingFrame>
            {
                new LevelRecordingFrame { timestamp = float.NaN, payload = new byte[] { 1 } }
            },
            "invalid timestamp");
    }

    [Test]
    public void RecordingBlobBuilder_RejectsOversizedFrameInsteadOfTruncating()
    {
        var session = new LevelRecordingSession();
        session.frames.Add(new LevelRecordingFrame
        {
            timestamp = 0f,
            payload = new byte[BotRelayPacket.MaxPayloadSize + 1]
        });

        var error = Assert.Throws<System.IO.InvalidDataException>(
            () => BotRecordingBlobBuilder.CreateFromSession(session));
        StringAssert.Contains("replay capacity", error.Message);
    }

    private static LevelRecordingFileValidationResult __ValidateFrames(
        List<LevelRecordingFrame> frames,
        float duration,
        string captureError = null)
    {
        var session = new LevelRecordingSession
        {
            header = new LevelRecordingFileHeader
            {
                userStageID = 144,
                duration = duration,
                frameCount = frames.Count
            },
            captureError = captureError
        };
        session.frames.AddRange(frames);
        return LevelRecordingFileValidator.ValidateSession(session, "synthetic-test");
    }

    private static void __AssertSerializerWriteFailsWithoutOverwriting(
        List<LevelRecordingFrame> frames,
        string expectedMessage)
    {
        var path = System.IO.Path.GetTempFileName();
        var originalBytes = new byte[] { 0x51, 0x47, 0x55, 0x41, 0x52, 0x44 };
        try
        {
            System.IO.File.WriteAllBytes(path, originalBytes);
            var header = new LevelRecordingFileHeader
            {
                userStageID = 26,
                duration = 2f
            };

            var error = Assert.Throws<System.IO.InvalidDataException>(
                () => LevelRecordingSerializer.Write(path, in header, frames));

            StringAssert.Contains(expectedMessage, error.Message);
            CollectionAssert.AreEqual(originalBytes, System.IO.File.ReadAllBytes(path));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
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

    private static LevelRecordingValidationSeverity __GetIssueSeverity(
        in LevelRecordingFileValidationResult result,
        string code)
    {
        for (int i = 0; i < result.issues.Count; ++i)
        {
            if (result.issues[i].code == code)
            {
                return result.issues[i].severity;
            }
        }

        Assert.Fail("Expected validation issue: " + code);
        return LevelRecordingValidationSeverity.Ok;
    }

    private static LevelRecordingFrame __BuildFrame(float timestamp, ReplyMessageType messageType)
    {
        return new LevelRecordingFrame
        {
            timestamp = timestamp,
            payload = __BuildPayload(messageType)
        };
    }

    private static LevelRecordingFrame __BuildMoveFrame(float timestamp, float positionX)
    {
        return new LevelRecordingFrame
        {
            timestamp = timestamp,
            payload = __BuildMovePayload(positionX)
        };
    }

    private static int __GetRecordedBodyOffset(byte[] payload)
    {
        using var source = new NativeArray<byte>(payload, Allocator.Temp);
        var reader = new DataStreamReader(source);
        var model = StreamCompressionModel.Default;
        Assert.AreEqual((int)ReplyMessageType.PlayerProperty, reader.ReadPackedInt(model));
        Assert.AreEqual((int)NetworkRelayType.Channel, reader.ReadPackedInt(model));
        reader.Flush();
        Assert.IsFalse(reader.HasFailedReads);
        return reader.GetBytesRead();
    }

    private static byte[] __BuildLegacySenderPrefixedPlayerProperty(byte[] currentPayload)
    {
        int bodyOffset = __GetRecordedBodyOffset(currentPayload);
        using var source = new NativeArray<byte>(currentPayload, Allocator.Temp);
        using var target = new NativeArray<byte>(currentPayload.Length + 16, Allocator.Temp);
        var writer = new DataStreamWriter(target);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)ReplyMessageType.PlayerProperty, model);
        writer.WritePackedInt((int)NetworkRelayType.Channel, model);
        writer.WritePackedUInt(1126954459u, model);
        writer.Flush();
        writer.WriteBytes(source.GetSubArray(bodyOffset, currentPayload.Length - bodyOffset));
        Assert.IsFalse(writer.HasFailedWrites);
        return target.GetSubArray(0, writer.Length).ToArray();
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
        Assert.IsTrue(
            ReplyMessageReplayInjector.TryParsePlayerProperty(payload, out _),
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
        if (extraBodyBytes == 4)
        {
            return __BuildCurrentGameplayPayload(messageType);
        }

        using var bytes = new NativeArray<byte>(16 + extraBodyBytes, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        writer.WriteReplyHeader((int)messageType, NetworkRelayType.Channel);
        for (int i = 0; i < extraBodyBytes; ++i)
        {
            writer.WriteByte(0);
        }

        var copy = new byte[writer.Length];
        bytes.GetSubArray(0, writer.Length).CopyTo(copy);
        return copy;
    }

    private static byte[] __BuildCurrentGameplayPayload(ReplyMessageType messageType)
    {
        using var bytes = new NativeArray<byte>(128, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        var model = StreamCompressionModel.Default;
        writer.WriteReplyHeader((int)messageType, NetworkRelayType.Channel);
        switch (messageType)
        {
            case ReplyMessageType.Chat:
                writer.WriteFixedString512(default);
                break;
            case ReplyMessageType.Camera:
                new RemoteCameraForward().Write(ref writer, model);
                break;
            case ReplyMessageType.Move:
                writer.WritePackedInt(1, model);
                new RemotePosition().Write(ref writer, model);
                break;
            case ReplyMessageType.Damage:
                new RemoteEffectTargetDamage().Write(ref writer, model);
                break;
            case ReplyMessageType.SelectSkill:
                writer.WritePackedInt(1, model);
                new LevelSkill
                {
                    index = 1,
                    originIndex = -1,
                    activeIndex = -1,
                    damageScale = 1f
                }.Write(ref writer, model);
                break;
            default:
                writer.WriteByte(0);
                break;
        }

        Assert.IsFalse(writer.HasFailedWrites);
        return bytes.GetSubArray(0, writer.Length).ToArray();
    }

    private static byte[] __BuildMovePayload(float positionX)
    {
        using var bytes = new NativeArray<byte>(128, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        var model = StreamCompressionModel.Default;
        writer.WriteReplyHeader((int)ReplyMessageType.Move, NetworkRelayType.Channel);
        writer.WritePackedInt(1, model);
        new RemotePosition
        {
            type = RemotePosition.Type.Normal,
            value = new Unity.Mathematics.float2(positionX, 0f)
        }.Write(ref writer, model);
        Assert.IsFalse(writer.HasFailedWrites);
        return bytes.GetSubArray(0, writer.Length).ToArray();
    }

    private static byte[] __BuildMalformedGameplayPayload(ReplyMessageType messageType)
    {
        using var bytes = new NativeArray<byte>(16, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        writer.WriteReplyHeader((int)messageType, NetworkRelayType.Channel);
        writer.WriteByte(0x7F);
        writer.WriteByte(0x7F);
        Assert.IsFalse(writer.HasFailedWrites);
        return bytes.GetSubArray(0, writer.Length).ToArray();
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

    private static byte[] __AppendTrailingByte(byte[] payload)
    {
        var result = new byte[payload.Length + 1];
        for (int i = 0; i < payload.Length; ++i)
        {
            result[i] = payload[i];
        }

        return result;
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
