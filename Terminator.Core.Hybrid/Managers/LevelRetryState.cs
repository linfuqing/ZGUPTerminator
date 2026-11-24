using System;
using UnityEngine;

[Serializable]
public struct StageRetryConfiguration
{
    public SpawnerAttribute.Scale spawnerAttributeScale;
    public SpawnerAttribute.Scale alternativeSpawnerAttributeScale;
    public int failureCountToUseAlternative;
    public int failureCount;

    public bool useAlternative => failureCountToUseAlternative > 0 &&
        failureCount >= failureCountToUseAlternative;
    public SpawnerAttribute.Scale scale => useAlternative
        ? alternativeSpawnerAttributeScale : spawnerAttributeScale;
}

// One battle entry starts from the server total. SubmitLevel sends an absolute snapshot.
public static class LevelRetryState
{
    private static uint __userID, __levelID;
    private static StageRetryConfiguration[] __stages;
    private static bool[] __hasRetried;
    private static Action<int> __onOfflineRetry;

    public static void Begin(uint userID, uint levelID, StageRetryConfiguration[] stages, Action<int> onOfflineRetry = null)
    {
        __userID = userID;
        __levelID = levelID;
        __stages = stages;
        __hasRetried = new bool[stages == null ? 0 : stages.Length];
        __onOfflineRetry = onOfflineRetry;
        // No retry journal or pending count is restored between battle entries.
        if (stages == null)
            return;
        for (int i = 0; i < stages.Length; ++i)
        {
            var config = stages[i];
            config.failureCount = config.failureCountToUseAlternative > 0 ? Math.Max(0, config.failureCount) : 0;
            stages[i] = config;
        }
    }

    public static bool TryGetConfiguration(int stage, out StageRetryConfiguration config)
    {
        if (__stages == null || stage < 0 || stage >= __stages.Length)
        {
            config = default;
            return false;
        }
        config = __stages[stage];
        return true;
    }

    public static bool RecordRetry(int stage, out StageRetryConfiguration config)
    {
        if (!TryGetConfiguration(stage, out config) || __hasRetried[stage])
            return false;
        __hasRetried[stage] = true;
        if (config.failureCountToUseAlternative > 0)
        {
            __onOfflineRetry?.Invoke(stage);
            if (config.failureCount < int.MaxValue)
                ++config.failureCount;
            __stages[stage] = config;
        }
        Debug.Log($"[小关重试] 大关ID={__levelID}, 小关={stage + 1}, 失败次数={config.failureCount}, " +
            $"阈值={config.failureCountToUseAlternative}, 使用备用={config.useAlternative}");
        return true;
    }

    public static bool TryGetFailureCount(uint userID, int stage, int hpPercentage, out int total)
    {
        total = 0;
        if (userID != __userID || !TryGetConfiguration(stage, out var config) || config.failureCountToUseAlternative <= 0)
            return false;
        // A current death adds one to the outgoing snapshot only, never to local state.
        // Repeated background saves therefore submit exactly the same value.
        total = config.failureCount;
        if (hpPercentage <= 0 && total < int.MaxValue)
            ++total;
        return true;
    }

    public static void MarkCompletedBefore(uint userID, int nextStage)
    {
        if (__stages == null || userID != __userID)
            return;
        for (int i = 0; i < Math.Min(nextStage, __stages.Length); ++i)
        {
            var config = __stages[i];
            config.failureCountToUseAlternative = 0;
            config.failureCount = 0;
            __stages[i] = config;
        }
    }
}
