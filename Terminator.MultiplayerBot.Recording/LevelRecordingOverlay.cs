using UnityEngine;



/// <summary>

/// Play Mode 内简易录制/回放提示（配合 Editor Window 或独立使用）。

/// </summary>

public sealed class LevelRecordingOverlay : MonoBehaviour

{

    private void OnGUI()

    {

        var harness = RecordingCoopHarness.Instance;

        if (harness == null)

            return;



        if (harness.IsRecording)

            DrawRecording(harness);

        else if (harness.ReplayPlayer != null && (harness.ReplayPlayer.IsPlaying || harness.ReplayPlayer.IsLoaded))

            DrawReplay(harness);

    }



    private static void DrawRecording(RecordingCoopHarness harness)

    {

        var session = harness.Recorder.Session;

        var rect = new Rect(10, 10, 360, 80);

        GUI.Box(rect, GUIContent.none);

        GUILayout.BeginArea(rect);

        GUILayout.Label("关卡录制中", BoldLabel());

        GUILayout.Label($"帧: {session.frames.Count}  时长: {session.ElapsedTime:F1}s");

        GUILayout.Label("在 Tools → MultiplayerBot → Level Recording 点击「完成录制」");

        GUILayout.EndArea();

    }



    private static void DrawReplay(RecordingCoopHarness harness)

    {

        var replay = harness.ReplayPlayer;

        var session = replay.Session;

        var rect = new Rect(10, 10, 400, 96);

        GUI.Box(rect, GUIContent.none);

        GUILayout.BeginArea(rect);

        GUILayout.Label(replay.IsPlaying ? "关卡回放中" : "关卡回放已加载", BoldLabel());

        if (session != null)

        {

            GUILayout.Label($"Level {session.header.levelID} Stage {session.header.stageIndex}  帧: {session.frames.Count}");

            if (replay.IsPlaying)
            {
                GUILayout.Label(
                    $"进度: {replay.PlaybackTime:F1}s / {replay.GameplayDuration:F1}s (文件 {session.header.duration:F1}s)");
            }
            else if (replay.IsWaitingForLevel)
                GUILayout.Label("等待关卡加载完成 (Remote StandBy)…");
            else
                GUILayout.Label("进关后自动开始回放 Remote 行为");

        }

        GUILayout.Label("Tools → MultiplayerBot → Level Recording 点击「停止回放」");

        GUILayout.EndArea();

    }



    private static GUIStyle BoldLabel()

    {

        return new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

    }

}


