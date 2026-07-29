using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

public sealed class LevelRecordingReplayPlayer : MonoBehaviour
{
    private LevelRecordingSession __session;
    private int __nextFrameIndex;
    private float __replayClockOrigin;
    private float __timestampBase;
    private bool __isPlaying;
    private bool __waitingForLevel;
    private bool __skillBaselineReady;
    private bool __loggedInjectFailure;

    public bool IsPlaying => __isPlaying;
    public bool IsLoaded => __session != null && __session.frames.Count > 0;
    public bool IsWaitingForLevel => __waitingForLevel;
    public bool IsSkillBaselineReady => __skillBaselineReady;
    public LevelRecordingSession Session => __session;
    public float PlaybackTime => __isPlaying ? Time.unscaledTime - __replayClockOrigin : 0f;

    public float GameplayDuration =>
        __session != null && __session.frames.Count > 0
            ? LevelRecordingTiming.ComputeGameplaySpan(__session.frames, __timestampBase)
            : 0f;

    private void OnEnable()
    {
        LevelRecordingReplayBridge.Register(this);
    }

    private void OnDisable()
    {
        LevelRecordingReplayBridge.Unregister(this);
    }

    public void Load(string filePath)
    {
        Stop();
        NetworkClientReplayInjector.ResetDebugState();
        LevelRecordingReplayState.Reset();
        LevelRecordingReplayPropertyOps.ResetCache();
        __session = LevelRecordingSerializer.Read(filePath);
        __waitingForLevel = true;
        __loggedInjectFailure = false;
        __skillBaselineReady = false;
        if (LevelRecordingReplayPropertyOps.TryEnsureCachedFromRecording())
        {
            LevelRecordingReplayPropertyOps.TryGetCachedProperty(out _);
        }
        else
        {
            BotReplayLog.Warn(
                "Replay file has no parseable PlayerProperty frame. " +
                LevelRecordingReplayPropertyOps.DescribeFrameTypes(__session));
        }
    }

    public bool TryApplyPlayerPropertyFrame()
    {
        return LevelRecordingReplayPropertyOps.TryApplySharedFromRecording(logWhenApplied: true);
    }

    private bool __TryEnsureRecordingPlayerPropertyIndexed()
    {
        if (!LevelRecordingReplayPropertyOps.TryEnsureCachedFromRecording())
            return false;

        int propertyFrameIndex = LevelRecordingReplayPropertyOps.PlayerPropertyFrameIndex;
        if (propertyFrameIndex >= 0 && __nextFrameIndex <= propertyFrameIndex)
            __nextFrameIndex = propertyFrameIndex + 1;

        return true;
    }

    public void BeginWhenLevelReady()
    {
        if (__session == null || __session.frames.Count == 0 || __isPlaying)
            return;

        __waitingForLevel = false;

        if (!ClientDataRecordingUtility.EnsureDriverForReplayInjection())
        {
            BotReplayLog.Warn(
                "NetworkClientDriver not ready; replay inject may be blocked until ClientData initializes.");
        }

        LevelRecordingHarnessOps.SyncRemoteChannelForWireWrite();

        if (LevelRecordingReplayPropertyOps.TryEnsureCachedFromRecording())
        {
            __TryEnsureRecordingPlayerPropertyIndexed();
        }

        if (!LevelRecordingRemotePropertySync.TryApplyRecordingPropertyToRemoteEntity(
                LevelRecordingReplayState.MutableSimulatedActiveSkillValues))
        {
            BotReplayLog.Warn(
                "Could not sync spawned remote entity from recording property; SelectSkill frames may fail validation.");
        }

        __skillBaselineReady = true;
        __timestampBase = LevelRecordingTiming.ComputeGameplayTimestampBase(__session.frames);
        if (__nextFrameIndex <= 0)
            __nextFrameIndex = 0;

        SkipStartupFramesBeforeGameplay();
        __replayClockOrigin = Time.unscaledTime;
        __isPlaying = true;
        __loggedInjectFailure = false;
        BotReplayLog.Diag(
            $"Replay started (timestamp base {__timestampBase:F2}s, gameplaySpan={GameplayDuration:F2}s, " +
            $"nextFrame={__nextFrameIndex}/{__session.frames.Count}).");
    }

    public void Stop()
    {
        __isPlaying = false;
        __waitingForLevel = false;
        __nextFrameIndex = 0;
        __timestampBase = 0f;
        LevelRecordingReplayPropertyOps.ResetCache();
        __skillBaselineReady = false;
        LevelRecordingReplayState.Reset();
    }

    public void Clear()
    {
        Stop();
        __session = null;
    }

    public void InjectDueFramesForCurrentTime()
    {
        if (!__isPlaying || __session == null || !__skillBaselineReady)
            return;

        var elapsed = Time.unscaledTime - __replayClockOrigin;
        var remotePlayerId = GetRemotePlayerId();
        var injected = 0;
        var dueCount = 0;

        while (__nextFrameIndex < __session.frames.Count)
        {
            var frame = __session.frames[__nextFrameIndex];
            if (frame.timestamp - __timestampBase > elapsed)
                break;

            ++dueCount;
            var payload = frame.payload;
            if (payload != null && payload.Length > 0 &&
                (!LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType) ||
                 messageType != ReplyMessageType.PlayerProperty))
            {
                if (NetworkClientReplayInjector.TryInjectInbound(remotePlayerId, payload))
                {
                    ++injected;
                }
                else if (!__loggedInjectFailure)
                {
                    __loggedInjectFailure = true;
                    LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out messageType);
                    var counters = NetworkClientReplayInjector.GetDebugCounters();
                    BotReplayLog.Warn(
                        $"Inject failed for {messageType} " +
                        $"(frame {__nextFrameIndex}, remoteId={remotePlayerId}, " +
                        $"inject ok {counters.successes}/{counters.attempts}).");
                }
            }

            ++__nextFrameIndex;
        }

        if (__nextFrameIndex >= __session.frames.Count)
        {
            __isPlaying = false;
            var counters = NetworkClientReplayInjector.GetDebugCounters();
            BotReplayLog.Info(
                "[RecordingAudit] phase=ReplayFinished, " +
                $"playbackElapsed={elapsed:F3}s, expectedGameplaySpan={GameplayDuration:F3}s, " +
                $"timestampBase={__timestampBase:F3}s, framesConsumed={__nextFrameIndex}/{__session.frames.Count}, " +
                $"injectOk={counters.successes}/{counters.attempts}, " +
                $"delta={math.abs(elapsed - GameplayDuration):F3}s");
        }
    }

    public static bool IsLevelReadyForReplay()
    {
        // StandBy is only set after LevelPlayerSystem spawn + Apply; strongest ready signal.
        if (RemotePlayer.status == RemotePlayer.Status.StandBy)
        {
            return true;
        }

        if (!HasSpawnedRemotePlayerEntity())
        {
            return false;
        }

        if (RemotePlayer.status != RemotePlayer.Status.Joined)
        {
            return false;
        }

        return LevelShared.unscaledDeltaTime > Mathf.Epsilon;
    }

    internal static bool HasSpawnedRemotePlayerEntity() => __HasSpawnedRemotePlayerEntity();

    private static bool __HasSpawnedRemotePlayerEntity()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return false;
        }

        using var query = world.EntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<RemotePlayer>(),
            ComponentType.ReadOnly<RemoteIdentity>());
        return !query.IsEmpty;
    }

    private void SkipStartupFramesBeforeGameplay()
    {
        while (__nextFrameIndex < __session.frames.Count)
        {
            var frame = __session.frames[__nextFrameIndex];
            if (frame.timestamp >= __timestampBase)
                break;

            var payload = frame.payload;
            if (payload != null && payload.Length > 0 &&
                LevelRecordingPayloadUtility.TryGetReplyMessageType(payload, out var messageType) &&
                messageType == ReplyMessageType.PlayerProperty)
            {
                ++__nextFrameIndex;
                continue;
            }

            ++__nextFrameIndex;
        }
    }

    private uint GetRemotePlayerId()
    {
        var remotePlayerId = LevelPlayerShared<RemotePlayer>.id;
        if (remotePlayerId != 0)
            return remotePlayerId;

        var botConfig = Resources.Load<BotConfig>("Bot/BotConfig");
        if (botConfig != null)
            return botConfig.GetRecordingFakeProfile().userID;

        return 999000001u;
    }
}
