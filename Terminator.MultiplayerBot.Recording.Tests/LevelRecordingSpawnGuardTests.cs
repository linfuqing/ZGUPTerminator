using NUnit.Framework;

using ZG;

[Category("Recording")]
[Category("Regression")]
[TestFixture]
public sealed class LevelRecordingSpawnGuardTests
{
    [Test]
    public void ShouldSuppressRemoteSpawn_OnlyWhenRecordingAndJoined()
    {
        Assert.IsTrue(LevelRecordingSpawnGuard.ShouldSuppressRemoteSpawn(
            true, RemotePlayer.Status.Joined));
        Assert.IsFalse(LevelRecordingSpawnGuard.ShouldSuppressRemoteSpawn(
            false, RemotePlayer.Status.Joined));
        Assert.IsFalse(LevelRecordingSpawnGuard.ShouldSuppressRemoteSpawn(
            true, RemotePlayer.Status.Disabled));
        Assert.IsFalse(LevelRecordingSpawnGuard.ShouldSuppressRemoteSpawn(
            true, RemotePlayer.Status.StandBy));
    }

    [Test]
    public void TryDemoteJoinedForRecording_TransitionsJoinedToDisabled()
    {
        var previous = RemotePlayer.status;
        try
        {
            LevelRecordingSpawnGuard.ResetSpawnDemotionState();
            RemotePlayer.SetStatus(RemotePlayer.Status.Joined);
            Assert.IsTrue(LevelRecordingSpawnGuard.TryDemoteJoinedForRecording());
            Assert.AreEqual(RemotePlayer.Status.Disabled, RemotePlayer.status);
            Assert.IsTrue(LevelRecordingSpawnGuard.RemoteDemotedForLevelSpawn);
        }
        finally
        {
            LevelRecordingSpawnGuard.ResetSpawnDemotionState();
            RemotePlayer.SetStatus(previous);
        }
    }

    [Test]
    public void SuppressRemoteLevelSpawn_IsFalseWhenNotRecording()
    {
        Assert.IsFalse(RecordingCoopHarness.SuppressRemoteLevelSpawn);
    }

    [Test]
    public void ShouldPromoteJoinedForReplay_WhenDisabledOrWaitingWhileWaitingForLevel()
    {
        Assert.IsTrue(LevelRecordingReplaySpawnEnsure.ShouldPromoteJoinedForReplay(
            RemotePlayer.Status.Disabled, replayLoaded: true, waitingForLevel: true));
        Assert.IsTrue(LevelRecordingReplaySpawnEnsure.ShouldPromoteJoinedForReplay(
            RemotePlayer.Status.Waiting, replayLoaded: true, waitingForLevel: true));
        Assert.IsFalse(LevelRecordingReplaySpawnEnsure.ShouldPromoteJoinedForReplay(
            RemotePlayer.Status.StandBy, replayLoaded: true, waitingForLevel: true));
        Assert.IsFalse(LevelRecordingReplaySpawnEnsure.ShouldPromoteJoinedForReplay(
            RemotePlayer.Status.Joined, replayLoaded: true, waitingForLevel: true));
        Assert.IsFalse(LevelRecordingReplaySpawnEnsure.ShouldPromoteJoinedForReplay(
            RemotePlayer.Status.Disabled, replayLoaded: false, waitingForLevel: true));
        Assert.IsFalse(LevelRecordingReplaySpawnEnsure.ShouldPromoteJoinedForReplay(
            RemotePlayer.Status.Disabled, replayLoaded: true, waitingForLevel: false));
    }

    [Test]
    public void TryPromoteJoinedForReplay_TransitionsDisabledToJoined()
    {
        var previous = RemotePlayer.status;
        try
        {
            RemotePlayer.SetStatus(RemotePlayer.Status.Disabled);
            Assert.IsTrue(LevelRecordingReplaySpawnEnsure.TryPromoteJoinedForReplay(
                RemotePlayer.Status.Disabled, replayLoaded: true, waitingForLevel: true));
            Assert.AreEqual(RemotePlayer.Status.Joined, RemotePlayer.status);
        }
        finally
        {
            RemotePlayer.SetStatus(previous);
        }
    }
}
