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

    static byte[] __BuildSelectSkillPayload((int index, int origin, int active)[] skills)
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
}
