using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;
using ZG;

public sealed class LevelRecordingSelectSkillReplayOpsTests
{
    [Test]
    public void SelectSkillReplay_UpgradeRequiresMatchingOrigin()
    {
        var state = new List<int> { 3 };

        var upgradePayload = __BuildSelectSkillPayload(new (int index, int origin, int active)[]
        {
            (5, 3, 0)
        });

        Assert.IsTrue(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(upgradePayload, state));
        Assert.AreEqual(5, state[0]);
    }

    [Test]
    public void SelectSkillReplay_WrongOrigin_IsRejected()
    {
        var state = new List<int> { 9 };

        var upgradePayload = __BuildSelectSkillPayload(new (int index, int origin, int active)[]
        {
            (5, 3, 0)
        });

        Assert.IsFalse(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(upgradePayload, state));
        Assert.AreEqual(9, state[0]);
    }

    [Test]
    public void SelectSkillReplay_ChainedSelections_SimulateInOrder()
    {
        var state = new List<int> { 1 };

        Assert.IsTrue(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(
            __BuildSelectSkillPayload(new[] { (2, 1, 0) }),
            state));
        Assert.IsTrue(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(
            __BuildSelectSkillPayload(new[] { (4, 2, 0) }),
            state));
        Assert.AreEqual(4, state[0]);
    }

    [Test]
    public void SelectSkillReplay_SkipMissingFirstSelection_BlocksSecond()
    {
        var state = new List<int> { 1 };

        Assert.IsFalse(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(
            __BuildSelectSkillPayload(new[] { (4, 2, 0) }),
            state));
    }

    [Test]
    public void SelectSkillReplay_WriterFinalBytePadding_IsAccepted()
    {
        byte[] payload = null;
        int selectedIndex = 0;
        int unpaddedBitLength = 0;
        for (int candidate = 1; candidate <= 64; ++candidate)
        {
            payload = __BuildSelectSkillPayload(
                new[] { (candidate, 3, 0) },
                NetworkRelayType.Channel,
                out unpaddedBitLength);
            if ((unpaddedBitLength & 7) != 0)
            {
                selectedIndex = candidate;
                break;
            }
        }

        Assert.AreNotEqual(0, unpaddedBitLength & 7, "Test payload must end in writer-owned padding bits.");
        var state = new List<int> { 3 };
        Assert.IsTrue(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(payload, state));
        Assert.AreEqual(selectedIndex, state[0]);
    }

    [Test]
    public void SelectSkillReplay_TrailingWholeByte_IsRejectedWithoutMutation()
    {
        var state = new List<int> { 3 };
        var payload = __AppendByte(__BuildSelectSkillPayload(new[] { (5, 3, 0) }), 0);

        Assert.IsFalse(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(payload, state));
        CollectionAssert.AreEqual(new[] { 3 }, state);
    }

    [Test]
    public void SelectSkillReplay_TruncatedSecondSkill_IsRejectedWithoutPartialMutation()
    {
        var state = new List<int> { 3 };
        var payload = __BuildSelectSkillPayload(new[]
        {
            (5, 3, 0),
            (8, 5, 0)
        });
        Array.Resize(ref payload, payload.Length - 1);

        Assert.IsFalse(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(payload, state));
        CollectionAssert.AreEqual(new[] { 3 }, state);
    }

    [Test]
    public void SelectSkillReplay_WrongRelayType_IsRejectedWithoutMutation()
    {
        var state = new List<int> { 3 };
        var payload = __BuildSelectSkillPayload(
            new[] { (5, 3, 0) },
            NetworkRelayType.All,
            out _);

        Assert.IsFalse(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulate(payload, state));
        CollectionAssert.AreEqual(new[] { 3 }, state);
    }

    [Test]
    public void SelectSkillReplayWithBootstrap_TrailingWholeByte_IsRejectedWithoutMutation()
    {
        var state = new List<int>();
        var payload = __AppendByte(__BuildSelectSkillPayload(new[] { (5, 3, 0) }), 0);

        Assert.IsFalse(LevelRecordingSelectSkillReplayOps.TryValidateAndSimulateWithBootstrap(payload, state));
        CollectionAssert.IsEmpty(state);
    }

    [Test]
    public void RecordedPayloadClassification_SelectSkillRequiresCompleteReplyHeader()
    {
        using var scratch = new NativeArray<byte>(16, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        writer.WritePackedInt((int)ReplyMessageType.SelectSkill, StreamCompressionModel.Default);
        writer.Flush();
        var payload = new byte[writer.Length];
        for (int i = 0; i < writer.Length; ++i)
        {
            payload[i] = scratch[i];
        }

        Assert.IsFalse(LevelRecordingPayloadClassification.TryClassifyRecordedPayload(
            payload,
            out _,
            out _,
            out _));
        Assert.IsFalse(LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out _));
    }

    [Test]
    public void RecordedPayloadUtility_ReturnsStrictReplyHeaderAndBodySlice()
    {
        var payload = __BuildSelectSkillPayload(new[] { (5, 3, 0) });

        Assert.IsTrue(LevelRecordingPayloadUtility.TryGetRecordedReplyHeader(
            payload,
            out var messageType,
            out var relayType,
            out int bodyOffset));
        Assert.AreEqual(ReplyMessageType.SelectSkill, messageType);
        Assert.AreEqual(NetworkRelayType.Channel, relayType);
        Assert.IsTrue(LevelRecordingPayloadUtility.TryGetRecordedBodySlice(
            payload,
            out int sliceOffset,
            out int bodyLength));
        Assert.AreEqual(bodyOffset, sliceOffset);
        Assert.AreEqual(payload.Length - bodyOffset, bodyLength);
    }

    [Test]
    public void RecordedPayloadClassification_GameplayWrongRelay_IsRejectedByClassificationAndUtility()
    {
        var payload = __BuildSelectSkillPayload(
            new[] { (5, 3, 0) },
            NetworkRelayType.All,
            out _);

        Assert.IsFalse(LevelRecordingPayloadClassification.TryClassifyRecordedPayload(
            payload,
            out _,
            out _,
            out _));
        Assert.IsFalse(LevelRecordingPayloadUtility.TryGetRecordedReplyHeader(
            payload,
            out _,
            out _,
            out _));
    }

    [Test]
    public void RecordedReplyHeader_InvalidRelayValue_IsRejected()
    {
        var payload = __BuildSelectSkillPayload(
            new[] { (5, 3, 0) },
            (NetworkRelayType)99,
            out _);

        Assert.IsFalse(LevelRecordingPayloadClassification.TryReadRecordedReplyHeader(
            payload,
            out _,
            out _,
            out _));
    }

    static byte[] __BuildSelectSkillPayload((int index, int origin, int active)[] skills)
    {
        return __BuildSelectSkillPayload(skills, NetworkRelayType.Channel, out _);
    }

    static byte[] __BuildSelectSkillPayload(
        (int index, int origin, int active)[] skills,
        NetworkRelayType relayType,
        out int unpaddedBitLength)
    {
        using var scratch = new NativeArray<byte>(128, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WriteReplyHeader((int)ReplyMessageType.SelectSkill, relayType);
        writer.WritePackedInt(skills.Length, model);
        for (int i = 0; i < skills.Length; ++i)
        {
            writer.WritePackedInt(skills[i].index, model);
            writer.WritePackedInt(skills[i].origin, model);
            writer.WritePackedInt(skills[i].active, model);
            writer.WritePackedFloat(1f, model);
        }

        unpaddedBitLength = writer.LengthInBits;
        writer.Flush();
        var payload = new byte[writer.Length];
        for (int i = 0; i < writer.Length; ++i)
        {
            payload[i] = scratch[i];
        }

        return payload;
    }

    static byte[] __AppendByte(byte[] payload, byte value)
    {
        var result = new byte[payload.Length + 1];
        Array.Copy(payload, result, payload.Length);
        result[result.Length - 1] = value;
        return result;
    }
}
