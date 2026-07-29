using NUnit.Framework;
using ZG;

[Category("Recording")]
[Category("Regression")]
[TestFixture]
public sealed class LevelRecordingSessionAuditTests
{
    [Test]
    public void Build_ComputesGameplaySpanAndContentGap_FromSyntheticFrames()
    {
        var frames = new System.Collections.Generic.List<LevelRecordingFrame>
        {
            new LevelRecordingFrame { timestamp = 1.465f, payload = new byte[440] },
            new LevelRecordingFrame { timestamp = 9.912f, payload = __BuildPayload(ReplyMessageType.Move) },
            new LevelRecordingFrame { timestamp = 12.854f, payload = __BuildPayload(ReplyMessageType.Move) }
        };

        var report = LevelRecordingSessionAudit.Build(
            frames,
            wallDurationSeconds: 26.892f,
            replyWriteGateOpenAtEnd: true,
            localChannelStatusAtEnd: 144,
            remoteChannelStatusAtEnd: 144);

        Assert.AreEqual(26.892f, report.wallDurationSeconds, 0.001f);
        Assert.AreEqual(12.854f, report.lastFrameTimestamp, 0.001f);
        Assert.AreEqual(14.038f, report.contentGapSeconds, 0.01f);
        Assert.AreEqual(9.912f, report.gameplayTimestampBase, 0.001f);
        Assert.AreEqual(2.942f, report.gameplaySpanSeconds, 0.01f);
        Assert.AreEqual(2, report.moveFrameCount);
        Assert.AreEqual(1, report.maxMoveFramesAtSameTimestamp);
    }

    [Test]
    public void Build_ReportsLargestLocalSameTimestampMoveBatch()
    {
        var frames = new System.Collections.Generic.List<LevelRecordingFrame>
        {
            new LevelRecordingFrame { timestamp = 1f, payload = new byte[440] },
            new LevelRecordingFrame { timestamp = 5f, payload = __BuildPayload(ReplyMessageType.Move) },
            new LevelRecordingFrame { timestamp = 10f, payload = __BuildPayload(ReplyMessageType.Move) },
            new LevelRecordingFrame { timestamp = 10f, payload = __BuildPayload(ReplyMessageType.Move) },
            new LevelRecordingFrame { timestamp = 20f, payload = __BuildPayload(ReplyMessageType.Move) }
        };

        var report = LevelRecordingSessionAudit.Build(
            frames,
            wallDurationSeconds: 25f,
            replyWriteGateOpenAtEnd: false,
            localChannelStatusAtEnd: 0,
            remoteChannelStatusAtEnd: 0);

        Assert.AreEqual(2, report.maxMoveFramesAtSameTimestamp);
        Assert.AreEqual(10f, report.maxMoveBatchTimestamp, 0.001f);
    }

    private static byte[] __BuildPayload(ReplyMessageType messageType)
    {
        using var bytes = new Unity.Collections.NativeArray<byte>(16, Unity.Collections.Allocator.Temp);
        var writer = new Unity.Collections.DataStreamWriter(bytes);
        writer.WritePackedInt((int)messageType, Unity.Collections.StreamCompressionModel.Default);
        writer.WritePackedInt(0, Unity.Collections.StreamCompressionModel.Default);
        var copy = new byte[writer.Length];
        bytes.GetSubArray(0, writer.Length).CopyTo(copy);
        return copy;
    }
}
