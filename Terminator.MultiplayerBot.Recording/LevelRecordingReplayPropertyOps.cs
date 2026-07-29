using Unity.Entities;
using Unity.Mathematics;
using ZG;

/// <summary>
/// Applies recording PlayerProperty onto <see cref="LevelPlayerShared{RemotePlayer}.property"/>
/// immediately before <see cref="LevelPlayerSystem"/> remote spawn.
/// </summary>
internal static class LevelRecordingReplayPropertyOps
{
    private static bool s_hasCachedProperty;
    private static LevelPlayerProperty s_cachedProperty;
    private static int s_playerPropertyFrameIndex = -1;
    private static bool s_appliedBeforeRemoteSpawn;
    private static bool s_warnedMissingPropertyAtSpawn;

    public static int PlayerPropertyFrameIndex => s_playerPropertyFrameIndex;

    public static void ResetCache()
    {
        s_hasCachedProperty = false;
        s_cachedProperty = default;
        s_playerPropertyFrameIndex = -1;
        s_appliedBeforeRemoteSpawn = false;
        s_warnedMissingPropertyAtSpawn = false;
    }

    public static bool IsPropertyEmpty(in LevelPlayerProperty property)
    {
        return property.activeSkills.Length == 0 &&
               property.skillGroups.Length == 0 &&
               property.instanceName.IsEmpty &&
               property.effectTargetHP == 0;
    }

    public static bool TryGetCachedProperty(out LevelPlayerProperty property)
    {
        property = s_cachedProperty;
        return s_hasCachedProperty;
    }

    public static bool TryApplySharedFromRecording(bool logWhenApplied)
    {
        if (!TryEnsureCachedFromRecording())
        {
            return false;
        }

        ReplyMessageReplayInjector.ApplySharedProperty(in s_cachedProperty, promoteJoined: false);
        if (logWhenApplied)
        {
            BotReplayLog.Diag(
                $"Applied PlayerProperty from recording " +
                $"(activeSkills={s_cachedProperty.activeSkills.Length}, " +
                $"instance={s_cachedProperty.instanceName}).");
        }

        return true;
    }

    public static bool TryApplySharedBeforeRemoteSpawn()
    {
        if (s_appliedBeforeRemoteSpawn)
        {
            return s_hasCachedProperty;
        }

        if (!TryEnsureCachedFromRecording())
        {
            if (!s_warnedMissingPropertyAtSpawn)
            {
                s_warnedMissingPropertyAtSpawn = true;
                BotReplayLog.Warn(
                    "Replay file has no valid PlayerProperty frame; remote spawn will use prefab defaults. Re-record after this fix to capture bot property.");
            }

            return false;
        }

        ReplyMessageReplayInjector.ApplySharedProperty(in s_cachedProperty, promoteJoined: false);
        s_appliedBeforeRemoteSpawn = true;
        BotReplayLog.Diag(
            $"Applied RemotePlayer.property before LevelPlayerSystem spawn " +
            $"(activeSkills={s_cachedProperty.activeSkills.Length}, " +
            $"instance={s_cachedProperty.instanceName}).");
        return true;
    }

    public static bool TryResolveRecordingProperty(out LevelPlayerProperty property)
    {
        if (TryGetCachedProperty(out property))
        {
            return !IsPropertyEmpty(in property);
        }

        if (!TryEnsureCachedFromRecording())
        {
            property = default;
            return false;
        }

        property = s_cachedProperty;
        return !IsPropertyEmpty(in property);
    }

    public static bool IsRemoteSpawnPending()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return false;
        }

        using var query = world.EntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<LevelPlayer>(),
            ComponentType.Exclude<ThirdPersonPlayer>());
        return !query.IsEmpty;
    }

    public static bool ShouldApplyBeforeLevelPlayerSpawn(RemotePlayer.Status status)
    {
        if (status != RemotePlayer.Status.Joined)
        {
            return false;
        }

        if (LevelRecordingReplayPlayer.HasSpawnedRemotePlayerEntity())
        {
            return false;
        }

        if (LevelShared.unscaledDeltaTime < math.FLT_MIN_NORMAL)
        {
            return false;
        }

        return IsRemoteSpawnPending();
    }

    public static bool TryEnsureCachedFromRecording()
    {
        if (s_hasCachedProperty)
        {
            return true;
        }

        var session = LevelRecordingReplayBridge.ReplaySession;
        if (session == null && RecordingCoopHarness.Instance != null)
        {
            session = RecordingCoopHarness.Instance.ReplayPlayer?.Session;
        }

        if (session == null || session.frames.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < session.frames.Count; ++i)
        {
            var payload = session.frames[i].payload;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType) ||
                messageType != ReplyMessageType.PlayerProperty)
            {
                continue;
            }

            if (!ReplyMessageReplayInjector.TryParsePlayerProperty(payload, out s_cachedProperty))
            {
                continue;
            }

            s_hasCachedProperty = true;
            s_playerPropertyFrameIndex = i;
            return true;
        }

        return false;
    }

    public static string DescribeFrameTypes(LevelRecordingSession session)
    {
        if (session == null || session.frames == null || session.frames.Count == 0)
        {
            return "frames=0";
        }

        var gameplayCounts = new System.Collections.Generic.Dictionary<ReplyMessageType, int>();
        var ancillaryCounts = new System.Collections.Generic.Dictionary<string, int>();
        int empty = 0;
        int unrecognized = 0;
        for (int i = 0; i < session.frames.Count; ++i)
        {
            var payload = session.frames[i].payload;
            if (!LevelRecordingPayloadClassification.TryClassifyRecordedPayload(
                    payload,
                    out var kind,
                    out int wireType,
                    out var messageType))
            {
                unrecognized++;
                continue;
            }

            if (kind == LevelRecordingRecordedPayloadKind.Empty)
            {
                empty++;
                continue;
            }

            if (kind == LevelRecordingRecordedPayloadKind.Unrecognized)
            {
                unrecognized++;
                continue;
            }

            if (kind == LevelRecordingRecordedPayloadKind.GameplayReply)
            {
                if (!gameplayCounts.ContainsKey(messageType))
                {
                    gameplayCounts[messageType] = 0;
                }

                gameplayCounts[messageType]++;
                continue;
            }

            string label = LevelRecordingPayloadClassification.FormatWireTypeLabel(wireType, kind);
            if (!ancillaryCounts.ContainsKey(label))
            {
                ancillaryCounts[label] = 0;
            }

            ancillaryCounts[label]++;
        }

        var parts = new System.Collections.Generic.List<string>();
        foreach (var pair in gameplayCounts)
        {
            parts.Add($"{pair.Key}={pair.Value}");
        }

        foreach (var pair in ancillaryCounts)
        {
            parts.Add($"{pair.Key}={pair.Value}");
        }

        if (empty > 0)
        {
            parts.Add($"Empty={empty}");
        }

        if (unrecognized > 0)
        {
            parts.Add($"Unrecognized={unrecognized}");
        }

        return $"frameTypes: {string.Join(", ", parts)}";
    }
}
