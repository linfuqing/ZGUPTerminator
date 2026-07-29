using UnityEngine;
using ZG;

/// <summary>
/// ECS-callable recording harness steps (no MonoBehaviour Update/LateUpdate).
/// </summary>
internal static class LevelRecordingHarnessOps
{
    private static bool s_loggedCoopSeedRepair;

    public static void ResetRecordingDiagnostics()
    {
        s_loggedCoopSeedRepair = false;
        LevelRecordingSpawnGuard.ResetSpawnDemotionState();
        LevelRecordingHandshakeDiagnostics.Reset();
        LevelRecordingSubmitStageRootCause.Reset();
    }

    /// <summary>
    /// After NetworkClient PopEvents (Connect may reset remotePlayerCount). Before capture.
    /// </summary>
    public static void RepairCoopGateAfterNetworkPop()
    {
        if (!RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        __RepairCoopGateForSubmitStage();
    }

    public static void TickCoopMock()
    {
        if (!RecordingCoopHarness.EcsHarnessActive)
        {
            return;
        }

        TickHarnessMaintenance(forceRemotePresence: RecordingCoopHarness.EcsRecordingActive);

        var harness = RecordingCoopHarness.Instance;
        if (harness == null)
        {
            return;
        }

        TickRecordingRemotePresenceForLogin();

        if (harness.IsStageMockActive)
        {
            __MaintainMockJoinedDuringLoginStart();
        }
        else
        {
            harness.TryActivateStageMockFromSystem();
        }
    }

    /// <summary>
    /// Coop seed + Remote online gate for LoginManager.__Start / __SubmitStage.
    /// Runs from Init, Presentation capture, scene load, and harness enable (not only CoopMock).
    /// </summary>
    public static void TickHarnessMaintenance(bool forceRemotePresence)
    {
        if (!RecordingCoopHarness.EcsHarnessActive)
        {
            return;
        }

        __MaintainHarnessCoopSeed(RecordingCoopHarness.EcsRecordingActive);

        if (!forceRemotePresence)
        {
            return;
        }

        __RepairCoopGateForSubmitStage();

        LevelRecordingHandshakeDiagnostics.TickDuringRecording();
    }

    /// <summary>
    /// Fake remote header/channel for Login handshake only (not every capture tick).
    /// </summary>
    public static void TickRecordingRemotePresenceForLogin()
    {
        if (!RecordingCoopHarness.EcsHarnessActive || !RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        var loginManager = LoginManager.instance;
        if (loginManager == null)
        {
            return;
        }

        if (LevelRecordingSpawnGuard.RemoteDemotedForLevelSpawn)
        {
            return;
        }

        var harness = RecordingCoopHarness.Instance;
        if (harness != null)
        {
            harness.EnsureRecordingRemotePresence();
        }
    }

    public static void TickWireSyncAndReplay()
    {
        SyncRemoteChannelForWireWrite();

        if (!ClientDataRecordingUtility.EnsureDriverForReplayInjection())
        {
            return;
        }

        LevelRecordingReplayBridge.TryBeginWhenLevelReady();
        LevelRecordingReplayBridge.TryInjectDueFrames();
    }

    public static void TickRecordingCaptureBeforeNetworkApply()
    {
        if (!RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        TickHarnessMaintenance(forceRemotePresence: true);

        var harness = RecordingCoopHarness.Instance;
        if (harness != null && harness.IsStageMockActive)
        {
            harness.SyncRemoteChannelToLocalFromSystem();
        }

        if (harness == null || !harness.IsRecording)
        {
            return;
        }

        var recorder = harness.Recorder;
        if (recorder == null)
        {
            return;
        }

        recorder.RefreshHeaderMetadata();
        recorder.CaptureEndOfFrame();
    }

    public static void TickRecordingCaptureAfterReplyWrite()
    {
        if (!RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        var harness = RecordingCoopHarness.Instance;
        if (harness == null || !harness.IsRecording)
        {
            return;
        }

        harness.Recorder?.CaptureEndOfFrame();
    }

    public static void SuppressLiveNetworkDuringHarness()
    {
        if (!RecordingCoopHarness.EcsHarnessActive)
        {
            return;
        }

        if (IClientData.instance is not ClientData clientData)
        {
            return;
        }

        var driver = clientData.driver;
        if (!driver.isCreated)
        {
            return;
        }

        if (driver.instance.connectionState == Unity.Networking.Transport.NetworkConnection.State.Connected)
        {
            driver.instance.Shutdown();
        }
    }

    /// <summary>
    /// ReplyServer Connect clears <see cref="ReplyMessageShared.remotePlayerCount"/>; restore coop gate for __Start / __SubmitStage.
    /// </summary>
    private static void __MaintainHarnessCoopSeed(bool isRecording)
    {
        if (ReplyMessageShared.remotePlayerCount < 1)
        {
            ReplyMessageShared.remotePlayerCount = 1;
            if (isRecording)
            {
                LevelRecordingSubmitStageRootCause.NotifyRemotePlayerCountRepaired();
            }

            if (isRecording && !s_loggedCoopSeedRepair)
            {
                s_loggedCoopSeedRepair = true;
                BotReplayLog.Diag(
                    "remotePlayerCount was 0 (Reply Connect reset); re-seeded for coop recording.");
            }
        }

        if (ReplyMessageShared.isHost)
        {
            ReplyMessageShared.isHost = false;
        }
    }

    /// <summary>
    /// LoginManager.__SubmitStage only EndWrites PlayerProperty when Remote != Disabled.
    /// SinglePlay (remotePlayerCount==0 at __Start) sets Disabled and never sends.
    /// </summary>
    private static void __RepairCoopGateForSubmitStage()
    {
        if (!RecordingCoopHarness.EcsRecordingActive)
        {
            return;
        }

        if (LevelRecordingSubmitStageRootCause.HasNativePlayerPropertyCapture)
        {
            return;
        }

        if (LevelRecordingSpawnGuard.RemoteDemotedForLevelSpawn)
        {
            return;
        }

        __MaintainHarnessCoopSeed(isRecording: true);

        if (RemotePlayer.status != RemotePlayer.Status.Disabled)
        {
            return;
        }

        var loginManager = LoginManager.instance;
        if (loginManager == null || loginManager.status != LoginManager.Status.Start)
        {
            return;
        }

        if (RemotePlayer.SetStatus(RemotePlayer.Status.Waiting))
        {
            LevelRecordingSubmitStageRootCause.NotifyDisabledPromotedToWaiting();
        }
    }

    /// <summary>
    /// Align remote channelFlag before ReplyMessageSystem write/collect (record + replay).
    /// </summary>
    public static void SyncRemoteChannelForWireWrite()
    {
        var harness = RecordingCoopHarness.Instance;
        if (harness == null || !harness.IsStageMockActive)
        {
            return;
        }

        harness.SyncRemoteChannelToLocalFromSystem();
    }

    /// <summary>
    /// Client __Start sets Remote Waiting then waits for Joined (inbound PlayerProperty).
    /// Mock has no inbound wire; promote Joined while LoginManager.Status.Start.
    /// </summary>
    private static void __MaintainMockJoinedDuringLoginStart()
    {
        var loginManager = LoginManager.instance;
        if (loginManager == null || loginManager.status != LoginManager.Status.Start)
        {
            return;
        }

        if (RemotePlayer.status == RemotePlayer.Status.Waiting)
        {
            RemotePlayer.SetStatus(RemotePlayer.Status.Joined);
        }
    }
}
