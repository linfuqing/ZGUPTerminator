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
        return __TryValidateAndSimulate(payload, simulatedActiveSkillValues, false);
    }

    /// <summary>
    /// Offline audit: infer per-slot baseline from first upgrade origin when PlayerProperty baseline is unavailable.
    /// </summary>
    public static bool TryValidateAndSimulateWithBootstrap(byte[] payload, List<int> simulatedActiveSkillValues)
    {
        return __TryValidateAndSimulate(payload, simulatedActiveSkillValues, true);
    }

    private static bool __TryValidateAndSimulate(
        byte[] payload,
        List<int> simulatedActiveSkillValues,
        bool allowBootstrap)
    {
        if (payload == null || payload.Length == 0 || simulatedActiveSkillValues == null ||
            !LevelRecordingPayloadClassification.TryClassifyRecordedPayload(
                payload,
                out var kind,
                out _,
                out var classifiedMessageType))
        {
            return false;
        }

        if (kind != LevelRecordingRecordedPayloadKind.GameplayReply ||
            classifiedMessageType != ReplyMessageType.SelectSkill)
        {
            return true;
        }

        if (!LevelRecordingPayloadUtility.TryGetRecordedReplyHeader(
                payload,
                out var messageType,
                out var relayType,
                out int bodyOffset) ||
            messageType != ReplyMessageType.SelectSkill ||
            relayType != NetworkRelayType.Channel ||
            bodyOffset >= payload.Length)
        {
            return false;
        }

        int bodyLength = payload.Length - bodyOffset;
        using var body = new NativeArray<byte>(bodyLength, Allocator.Temp);
        NativeArray<byte>.Copy(payload, bodyOffset, body, 0, bodyLength);

        var reader = new DataStreamReader(body);
        var model = StreamCompressionModel.Default;
        int numSkills = reader.ReadPackedInt(model);
        if (reader.HasFailedReads || numSkills <= 0)
        {
            return false;
        }

        var simulated = new List<int>(simulatedActiveSkillValues);
        for (int i = 0; i < numSkills; ++i)
        {
            var skill = new LevelSkill(ref reader, model);
            if (reader.HasFailedReads ||
                (allowBootstrap && !__TryBootstrapSlot(skill.originIndex, skill.activeIndex, simulated)) ||
                !__TrySimulateOne(skill.index, skill.originIndex, skill.activeIndex, simulated))
            {
                return false;
            }
        }

        // GetBytesRead counts the currently consumed partial byte, so writer-owned padding bits
        // in the final byte remain legal while any complete trailing byte is rejected.
        if (reader.HasFailedReads || reader.GetBytesRead() != reader.Length)
        {
            return false;
        }

        simulatedActiveSkillValues.Clear();
        simulatedActiveSkillValues.AddRange(simulated);
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
