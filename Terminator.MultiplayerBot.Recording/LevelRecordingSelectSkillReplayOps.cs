using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using ZG;

/// <summary>
/// Validates SelectSkill replay payloads against the remote player's active skill slots so we
/// never inject a frame that would trip LevelSkill.Apply's originIndex guard.
/// </summary>
internal static class LevelRecordingSelectSkillReplayOps
{
    public static bool TryValidateAndSimulate(byte[] payload, List<int> simulatedActiveSkillValues)
    {
        if (payload == null || payload.Length < 2 || simulatedActiveSkillValues == null)
        {
            return false;
        }

        if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType) ||
            messageType != ReplyMessageType.SelectSkill)
        {
            return true;
        }

        if (!LevelRecordingPayloadUtility.TryGetRecordedBodySlice(payload, out int bodyOffset, out int bodyLength))
        {
            return false;
        }

        using var body = new NativeArray<byte>(bodyLength, Allocator.Temp);
        NativeArray<byte>.Copy(payload, bodyOffset, body, 0, bodyLength);

        var reader = new DataStreamReader(body);
        var model = StreamCompressionModel.Default;
        int numSkills = reader.ReadPackedInt(model);
        if (numSkills <= 0)
        {
            return false;
        }

        for (int i = 0; i < numSkills; ++i)
        {
            int index = reader.ReadPackedInt(model);
            int originIndex = reader.ReadPackedInt(model);
            int activeIndex = reader.ReadPackedInt(model);
            reader.ReadPackedFloat(model);

            if (!__TrySimulateOne(index, originIndex, activeIndex, simulatedActiveSkillValues))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Offline audit: infer per-slot baseline from first upgrade origin when PlayerProperty baseline is unavailable.
    /// </summary>
    public static bool TryValidateAndSimulateWithBootstrap(byte[] payload, List<int> simulatedActiveSkillValues)
    {
        if (payload == null || payload.Length < 2 || simulatedActiveSkillValues == null)
        {
            return false;
        }

        if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType) ||
            messageType != ReplyMessageType.SelectSkill)
        {
            return true;
        }

        if (!LevelRecordingPayloadUtility.TryGetRecordedBodySlice(payload, out int bodyOffset, out int bodyLength))
        {
            return false;
        }

        using var body = new NativeArray<byte>(bodyLength, Allocator.Temp);
        NativeArray<byte>.Copy(payload, bodyOffset, body, 0, bodyLength);

        var reader = new DataStreamReader(body);
        var model = StreamCompressionModel.Default;
        int numSkills = reader.ReadPackedInt(model);
        if (numSkills <= 0)
        {
            return false;
        }

        for (int i = 0; i < numSkills; ++i)
        {
            int index = reader.ReadPackedInt(model);
            int originIndex = reader.ReadPackedInt(model);
            int activeIndex = reader.ReadPackedInt(model);
            reader.ReadPackedFloat(model);

            if (!__TryBootstrapSlot(originIndex, activeIndex, simulatedActiveSkillValues))
            {
                return false;
            }

            if (!__TrySimulateOne(index, originIndex, activeIndex, simulatedActiveSkillValues))
            {
                return false;
            }
        }

        return true;
    }

    private static bool __TryBootstrapSlot(
        int originIndex,
        int activeIndex,
        List<int> simulatedActiveSkillValues)
    {
        if (activeIndex < 0)
        {
            return true;
        }

        while (simulatedActiveSkillValues.Count <= activeIndex)
        {
            simulatedActiveSkillValues.Add(-1);
        }

        if (simulatedActiveSkillValues[activeIndex] < 0)
        {
            simulatedActiveSkillValues[activeIndex] = originIndex;
        }

        return true;
    }

    public static void SeedFromActiveIndices(in DynamicBuffer<SkillActiveIndex> activeIndices, List<int> simulatedActiveSkillValues)
    {
        simulatedActiveSkillValues.Clear();
        for (int i = 0; i < activeIndices.Length; ++i)
            simulatedActiveSkillValues.Add(activeIndices[i].value);
    }

    private static bool __TrySimulateOne(
        int index,
        int originIndex,
        int activeIndex,
        List<int> simulatedActiveSkillValues)
    {
        if (activeIndex == -1)
        {
            simulatedActiveSkillValues.Add(index);
            return true;
        }

        if (activeIndex < 0 || activeIndex >= simulatedActiveSkillValues.Count)
            return false;

        if (originIndex != simulatedActiveSkillValues[activeIndex])
            return false;

        simulatedActiveSkillValues[activeIndex] = index;
        return true;
    }
}
