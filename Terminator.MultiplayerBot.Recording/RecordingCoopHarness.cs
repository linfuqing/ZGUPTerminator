using UnityEngine;
using UnityEngine.SceneManagement;
using ZG;

/// <summary>
/// Editor-only level recording harness (Terminator.MultiplayerBot.Recording, UNITY_EDITOR).
/// Not compiled into player/Server builds; AddComponent works in Editor Play Mode.
/// </summary>
public sealed class RecordingCoopHarness : MonoBehaviour
{
    public static RecordingCoopHarness Instance { get; private set; }

    /// <summary>ECS systems read this; do not rely on <see cref="Instance"/> alone (Awake/scene-load timing).</summary>
    internal static bool EcsHarnessActive { get; private set; }

    internal static bool EcsRecordingActive { get; private set; }

    [SerializeField] private BotConfig botConfig;

    private ReplyMessageRecorder __recorder;
    private LevelRecordingReplayPlayer __replayPlayer;
    private bool __harnessActive;
    private bool __stageMockActive;
    private bool __pendingReplayAfterLogin;

    private int __savedRemotePlayerCount;
    private bool __savedIsHost;
    private int __savedChannel;
    private uint __savedRemoteId;
    private int __savedRemoteChannelFlag;
    private LevelPlayerHeader __savedRemoteHeader;
    private RemotePlayer.Status __savedRemoteStatus;

    private ClientDataRecordingUtility.ConnectionSnapshot __connectionSnapshot;

    /// <summary>
    /// True while an active recording session should not spawn a visible remote level player.
    /// Replay keeps this false so PlayerProperty + Joined can drive the fake remote avatar.
    /// </summary>
    internal static bool SuppressRemoteLevelSpawn =>
        Instance != null && Instance.__recorder != null && Instance.__recorder.IsRecording;

    public bool IsHarnessActive => __harnessActive;
    internal bool IsStageMockActive => __stageMockActive;
    public bool IsRecording => __recorder != null && __recorder.IsRecording;
    public bool IsReplaying => __replayPlayer != null && __replayPlayer.IsPlaying;
    public ReplyMessageRecorder Recorder => __recorder;
    public LevelRecordingReplayPlayer ReplayPlayer => __replayPlayer;

    public void SetBotConfig(BotConfig config)
    {
        botConfig = config;
    }

    public static RecordingCoopHarness EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        var existing = __FindHarnessInLoadedObjects();
        if (existing != null)
        {
            existing.__TryAdoptAsInstance(force: true);
            return Instance;
        }

        var gameObject = new GameObject(nameof(RecordingCoopHarness));
        var harness = gameObject.AddComponent<RecordingCoopHarness>();
        if (Instance == null && harness != null)
            harness.__TryAdoptAsInstance(force: true);

        if (Instance == null)
        {
            existing = __FindHarnessInLoadedObjects();
            if (existing != null)
                existing.__TryAdoptAsInstance(force: true);
        }

        if (Instance == null)
        {
            Debug.LogError("[LevelRecording] RecordingCoopHarness.EnsureInstance failed to establish singleton.");
            if (gameObject != null)
                Destroy(gameObject);
        }

        return Instance;
    }

    private void Awake()
    {
        __TryAdoptAsInstance(force: false);
    }

    private void __TryAdoptAsInstance(bool force)
    {
        if (!force && Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);

        __EnsureComponents();
    }

    private void __EnsureComponents()
    {
        if (__recorder == null)
            __recorder = GetComponent<ReplyMessageRecorder>() ?? gameObject.AddComponent<ReplyMessageRecorder>();
        if (__replayPlayer == null)
            __replayPlayer = GetComponent<LevelRecordingReplayPlayer>() ?? gameObject.AddComponent<LevelRecordingReplayPlayer>();
        if (GetComponent<LevelRecordingOverlay>() == null)
            gameObject.AddComponent<LevelRecordingOverlay>();
    }

    private static RecordingCoopHarness __FindHarnessInLoadedObjects()
    {
        return Object.FindAnyObjectByType<RecordingCoopHarness>(FindObjectsInactive.Include);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= __OnSceneLoaded;

        if (Instance == this)
            Instance = null;

        DisableHarness();
    }

    public void PrepareAndLoadLoginScene()
    {
        EnsureInstance();
        StopReplay();
        __stageMockActive = false;
        __pendingReplayAfterLogin = false;
        EnableRecordingSession();
        __BeginRecordingIfNeeded();
        LoadLoginScene();
    }

    public void PrepareAndReplay(string filePath)
    {
        EnsureInstance();
        if (__recorder != null && __recorder.IsRecording)
            __recorder.EndRecording();

        __replayPlayer.Load(filePath);
        __stageMockActive = false;
        __pendingReplayAfterLogin = true;
        EnableRecordingSession();
        LoadLoginScene();
        BotReplayLog.Diag($"Replay prepared: {filePath}");
    }

    public void StopReplay()
    {
        __pendingReplayAfterLogin = false;
        __replayPlayer?.Clear();
    }

    private void __BeginRecordingIfNeeded()
    {
        if (__recorder == null)
        {
            return;
        }

        if (__recorder.IsRecording)
        {
            return;
        }

        if (__recorder.BeginRecording())
        {
            BotReplayLog.Diag("Recording armed before Login scene is ready.");
            Debug.Log("[LevelRecording] Recording started.");
        }
        else
        {
            Debug.LogError(
                "[LevelRecording] Recording did not start because the EndWrite capture journal was unavailable.");
        }
    }

    public bool TryFinishRecording(out LevelRecordingSession session)
    {
        session = null;
        if (__recorder == null || !__recorder.IsRecording)
            return false;

        __recorder.RefreshHeaderMetadata();
        __recorder.EndRecording();
        session = __recorder.Session;
        Debug.Log($"[LevelRecording] Recording finished. Frames: {session.frames.Count}");
        return true;
    }

    public void StopHarness()
    {
        if (__recorder != null && __recorder.IsRecording)
            __recorder.EndRecording();

        StopReplay();
        DisableHarness();
    }

    private void LoadLoginScene()
    {
        var sceneManager = UnityEngine.Object.FindObjectOfType<GameSceneManager>();
        if (sceneManager != null)
        {
            sceneManager.LoadToDefault();
            Debug.Log("[LevelRecording] Loading Login scene via GameSceneManager.LoadToDefault()");
        }
        else
        {
            Debug.LogWarning("[LevelRecording] GameSceneManager not found; harness enabled in current scene.");
            if (__pendingReplayAfterLogin)
            {
                __pendingReplayAfterLogin = false;
            }
            else
            {
                __BeginRecordingIfNeeded();
            }
        }
    }

    private void EnableRecordingSession()
    {
        if (__harnessActive)
            return;

        __connectionSnapshot = ClientDataRecordingUtility.SuppressNetwork();
        __EnsureCoopSeedBeforeLoginStart();

        __savedRemotePlayerCount = ReplyMessageShared.remotePlayerCount;
        __savedIsHost = ReplyMessageShared.isHost;
        __savedChannel = ReplyMessageShared.channel;
        __savedRemoteId = LevelPlayerShared<RemotePlayer>.id;
        __savedRemoteChannelFlag = LevelPlayerShared<RemotePlayer>.channelFlag;
        __savedRemoteHeader = LevelPlayerShared<RemotePlayer>.header;
        __savedRemoteStatus = RemotePlayer.status;

        __harnessActive = true;
        EcsHarnessActive = true;
        SceneManager.sceneLoaded -= __OnSceneLoaded;
        SceneManager.sceneLoaded += __OnSceneLoaded;
        LevelRecordingHarnessOps.TickHarnessMaintenance(forceRemotePresence: IsRecording);
        Debug.Log("[LevelRecording] Recording session enabled (network suppressed, coop mock deferred).");
    }

    internal static void NotifyRecordingStarted()
    {
        EcsRecordingActive = true;
        LevelRecordingHarnessOps.TickHarnessMaintenance(forceRemotePresence: true);
    }

    internal static void NotifyRecordingEnded()
    {
        EcsRecordingActive = false;
    }

    private void __OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!__harnessActive)
        {
            return;
        }

        LevelRecordingHarnessOps.TickHarnessMaintenance(forceRemotePresence: IsRecording);
    }

    /// <summary>
    /// LoginManager.__Start checks remotePlayerCount before ActivateStageCoopMock; without this it logs
    /// Single play and sets Disabled, which can race level spawn on replay.
    /// </summary>
    private static void __EnsureCoopSeedBeforeLoginStart()
    {
        if (ReplyMessageShared.remotePlayerCount < 1)
        {
            ReplyMessageShared.remotePlayerCount = 1;
        }

        ReplyMessageShared.isHost = false;

        if (!EcsRecordingActive || LevelRecordingSpawnGuard.RemoteDemotedForLevelSpawn)
        {
            return;
        }

        if (RemotePlayer.status == RemotePlayer.Status.Disabled)
        {
            var loginManager = LoginManager.instance;
            if (loginManager != null && loginManager.status == LoginManager.Status.Start)
            {
                if (RemotePlayer.SetStatus(RemotePlayer.Status.Waiting))
                {
                    LevelRecordingSubmitStageRootCause.NotifyDisabledPromotedToWaiting();
                }
            }
        }
    }

    internal void TryActivateStageMockFromSystem()
    {
        TryActivateStageMock();
    }

    internal void SyncRemoteChannelToLocalFromSystem()
    {
        SyncRemoteChannelToLocal();
    }

    /// <summary>
    /// Before LoginManager.Status.Start: keep Remote online so __Start client branch and __SubmitStage PlayerProperty gate pass.
    /// </summary>
    internal void EnsureRecordingRemotePresence()
    {
        if (!EcsRecordingActive || LevelRecordingSpawnGuard.RemoteDemotedForLevelSpawn)
        {
            return;
        }

        var loginManager = LoginManager.instance;
        if (loginManager == null)
        {
            return;
        }

        __EnsureCoopSeedBeforeLoginStart();

        var profile = (botConfig != null ? botConfig : LoadDefaultBotConfig()).GetRecordingFakeProfile();
        var header = botConfig != null
            ? botConfig.ToLevelPlayerHeader(profile)
            : DefaultHeader(profile);

        LevelPlayerShared<RemotePlayer>.id = profile.userID;
        LevelPlayerShared<RemotePlayer>.header = header;
        LevelPlayerShared<RemotePlayer>.channelFlag = (int)NetworkRelayChannelFlag.Online;
        SyncRemoteChannelToLocal();

        if (RemotePlayer.status == RemotePlayer.Status.Disabled &&
            loginManager.status == LoginManager.Status.Start)
        {
            RemotePlayer.SetStatus(RemotePlayer.Status.Waiting);
        }
    }

    private void TryActivateStageMock()
    {
        var loginManager = LoginManager.instance;
        if (loginManager == null || loginManager.status != LoginManager.Status.Start)
            return;

        ActivateStageCoopMock();

        if (__pendingReplayAfterLogin)
        {
            __pendingReplayAfterLogin = false;
            BotReplayLog.Diag("Replay session ready; enter the level to start playback.");
        }
    }

    private void ActivateStageCoopMock()
    {
        if (__stageMockActive)
            return;

        var profile = (botConfig != null ? botConfig : LoadDefaultBotConfig()).GetRecordingFakeProfile();
        var header = botConfig != null
            ? botConfig.ToLevelPlayerHeader(profile)
            : DefaultHeader(profile);

        __EnsureCoopSeedBeforeLoginStart();

        if (RemotePlayer.status == RemotePlayer.Status.Canceled)
            RemotePlayer.SetStatus(RemotePlayer.Status.Disabled);

        LevelPlayerShared<RemotePlayer>.id = profile.userID;
        LevelPlayerShared<RemotePlayer>.header = header;
        LevelPlayerShared<RemotePlayer>.channelFlag = (int)NetworkRelayChannelFlag.Online;
        SyncRemoteChannelToLocal();

        // LoginManager.__Start waits for Joined before loading the level scene (see ~L2316).
        // LevelPlayerSystem spawns the remote prefab when Joined at level entry (L399).
        // Recording: LevelRecordingSpawnGuardSystem demotes Joined→Disabled before LevelPlayerSystem.
        // PlayerProperty is applied once in LevelRecordingSpawnGuardSystem (UpdateBefore LevelPlayerSystem).
        if (ReplyMessageShared.remotePlayerCount < 1)
            ReplyMessageShared.remotePlayerCount = 1;

        RemotePlayer.SetStatus(RemotePlayer.Status.Joined);

        __stageMockActive = true;
        BotReplayLog.Diag(
            $"Stage coop mock activated. Fake remote userID={profile.userID} " +
            $"mode={(IsRecording ? "record" : __replayPlayer != null && __replayPlayer.IsLoaded ? "replay" : "mock")}");
    }

    private void DisableHarness()
    {
        if (!__harnessActive && !__stageMockActive)
            return;

        LevelPlayerShared<RemotePlayer>.id = __savedRemoteId;
        LevelPlayerShared<RemotePlayer>.header = __savedRemoteHeader;
        LevelPlayerShared<RemotePlayer>.channelFlag = __savedRemoteChannelFlag;
        RemotePlayer.SetStatus(__savedRemoteStatus);

        ReplyMessageShared.remotePlayerCount = __savedRemotePlayerCount;
        ReplyMessageShared.isHost = __savedIsHost;
        ReplyMessageShared.channel = __savedChannel;

        ClientDataRecordingUtility.RestoreNetwork(in __connectionSnapshot);

        __stageMockActive = false;
        __harnessActive = false;
        __pendingReplayAfterLogin = false;
        EcsHarnessActive = false;
        EcsRecordingActive = false;
        SceneManager.sceneLoaded -= __OnSceneLoaded;
        Debug.Log("[LevelRecording] Recording harness disabled.");
    }

    private void SyncRemoteChannelToLocal()
    {
        var localStatus = LevelPlayerShared<LocalPlayer>.channelStatus;
        var flag = (int)NetworkRelayChannelFlag.Online |
                   (localStatus << (int)NetworkRelayChannelFlag.ShiftToStatus);

        if (LevelPlayerShared<RemotePlayer>.channelFlag == flag)
            return;

        LevelPlayerShared<RemotePlayer>.channelFlag = flag;
    }

    private static BotConfig LoadDefaultBotConfig()
    {
        return Resources.Load<BotConfig>("Bot/BotConfig");
    }

    private static LevelPlayerHeader DefaultHeader(in BotConfig.BotProfile profile)
    {
        LevelPlayerHeader header;
        header.power = profile.power;
        header.name = new Unity.Collections.FixedString32Bytes(profile.userName ?? string.Empty);
        header.avatar = new Unity.Collections.FixedString32Bytes(profile.userAvatar ?? string.Empty);
        return header;
    }
}
