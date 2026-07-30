#if UNITY_EDITOR

using System.IO;

using UnityEditor;

using UnityEngine;



public sealed class LevelRecordingEditorWindow : EditorWindow

{

    private BotConfig __botConfig;

    private string __replayFilePath;

    private string __status = "Idle";

    private string __lastSavedPath;



    [MenuItem("Tools/MultiplayerBot/Level Recording")]

    public static void Open()

    {

        GetWindow<LevelRecordingEditorWindow>("Level Recording");

    }



    private void OnEnable()

    {

        if (__botConfig == null)

            __botConfig = Resources.Load<BotConfig>("Bot/BotConfig");



        if (string.IsNullOrEmpty(__replayFilePath))

            __replayFilePath = LevelRecordingSerializer.GetRecordingRootDirectory();

    }



    private void OnGUI()

    {

        EditorGUILayout.LabelField("关卡联机录制", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(

            "录制：Play Mode →「开始录制」→ Login 进关 →「完成录制」保存\n" +

            "回放：选择 .bin →「开始回放」→ Login 进关 → Remote 按录制轨迹行动",

            MessageType.Info);



        __botConfig = (BotConfig)EditorGUILayout.ObjectField("Bot Config", __botConfig, typeof(BotConfig), false);



        EditorGUILayout.Space();

        DrawRecordingSection();

        EditorGUILayout.Space();

        DrawReplaySection();



        EditorGUILayout.Space();

        EditorGUILayout.LabelField("状态", __status);



        if (!string.IsNullOrEmpty(__lastSavedPath))

            EditorGUILayout.LabelField("上次保存", __lastSavedPath);



        if (!Application.isPlaying)

            EditorGUILayout.HelpBox("请先进入 Play Mode。", MessageType.Warning);



        EditorGUILayout.Space();

        DrawSessionPreview();

    }



    private void DrawRecordingSection()

    {

        EditorGUILayout.LabelField("录制", EditorStyles.boldLabel);



        using (new EditorGUI.DisabledScope(!Application.isPlaying))

        {

            var harness = Application.isPlaying ? RecordingCoopHarness.Instance : null;

            var isRecording = harness != null && harness.IsRecording;

            var isReplaying = harness != null && harness.IsReplaying;



            using (new EditorGUI.DisabledScope(isReplaying))

            {

                if (!isRecording)

                {

                    if (GUILayout.Button("开始录制", GUILayout.Height(28)))

                        StartRecording();

                }

                else if (GUILayout.Button("完成录制", GUILayout.Height(28)))

                {

                    FinishRecording();

                }

            }



            if (harness != null && GUILayout.Button("停止 Harness（不保存）"))

                harness.StopHarness();

        }

    }



    private void DrawReplaySection()

    {

        EditorGUILayout.LabelField("回放", EditorStyles.boldLabel);



        EditorGUILayout.BeginHorizontal();

        __replayFilePath = EditorGUILayout.TextField("录制文件", __replayFilePath);

        if (GUILayout.Button("浏览", GUILayout.Width(56)))

        {

            var picked = EditorUtility.OpenFilePanel(

                "选择关卡录制",

                LevelRecordingSerializer.GetRecordingRootDirectory(),

                "bin");

            if (!string.IsNullOrEmpty(picked))

                __replayFilePath = picked;

        }

        EditorGUILayout.EndHorizontal();



        using (new EditorGUI.DisabledScope(!Application.isPlaying))

        {

            var harness = Application.isPlaying ? RecordingCoopHarness.Instance : null;

            var replay = harness?.ReplayPlayer;

            var isRecording = harness != null && harness.IsRecording;

            var isReplaying = replay != null && replay.IsPlaying;

            var isLoaded = replay != null && replay.IsLoaded;



            using (new EditorGUI.DisabledScope(isRecording))

            {

                if (!isReplaying && !isLoaded)

                {

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(__replayFilePath) || !File.Exists(__replayFilePath)))

                    {

                        if (GUILayout.Button("开始回放", GUILayout.Height(28)))

                            StartReplay(__replayFilePath);

                    }

                }

                else if (GUILayout.Button("停止回放", GUILayout.Height(28)))

                {

                    harness?.StopHarness();

                    __status = "回放已停止。";

                }

            }



            if (replay != null && replay.Session != null && (isReplaying || isLoaded))

            {

                EditorGUILayout.LabelField("回放文件", Path.GetFileName(__replayFilePath));

                EditorGUILayout.LabelField("帧数 / 时长",

                    $"{replay.Session.frames.Count} / {replay.Session.header.duration:F1}s");

                if (isReplaying)

                    EditorGUILayout.LabelField("进度", $"{replay.PlaybackTime:F1}s");

            }

        }

    }



    private void StartRecording()

    {

        Directory.CreateDirectory(LevelRecordingSerializer.GetRecordingRootDirectory());



        var harness = RecordingCoopHarness.EnsureInstance();
        if (harness == null)
        {
            __status = "RecordingCoopHarness 初始化失败。";
            return;
        }

        if (__botConfig != null)
            harness.SetBotConfig(__botConfig);

        harness.PrepareAndLoadLoginScene();

        __status = "等待 Login 场景 / 录制中…";

        __lastSavedPath = null;

    }



    private void StartReplay(string filePath)

    {

        if (!File.Exists(filePath))

        {

            __status = "录制文件不存在。";

            return;

        }



        LevelRecordingSession session;

        try

        {

            session = LevelRecordingSerializer.Read(filePath);

        }

        catch (IOException exception)

        {

            __status = $"读取失败: {exception.Message}";

            return;

        }



        if (session.frames.Count == 0)

        {

            __status = "录制文件为空。";

            EditorUtility.DisplayDialog("Level Recording", "录制文件没有帧数据。", "OK");

            return;

        }



        var harness = RecordingCoopHarness.EnsureInstance();
        if (harness == null)
        {
            __status = "RecordingCoopHarness 初始化失败。";
            return;
        }

        if (__botConfig != null)
            harness.SetBotConfig(__botConfig);

        harness.PrepareAndReplay(filePath);

        __status = $"回放已加载 {session.frames.Count} 帧，请进关…";

    }



    private void FinishRecording()

    {

        var harness = RecordingCoopHarness.Instance;

        if (harness == null || !harness.TryFinishRecording(out var session))

        {

            __status = "没有进行中的录制。";

            return;

        }



        if (session.frames.Count == 0)

        {

            __status = "录制为空，未保存。";

            EditorUtility.DisplayDialog("Level Recording", "未捕获到 sendBuffer 帧，请确认已进关并产生联机同步。", "OK");

            return;

        }

        session.header.frameCount = session.frames.Count;
        var validation = LevelRecordingFileValidator.ValidateSession(
            session,
            sourceName: "Level Recording save gate");
        if (validation.WorstSeverity == LevelRecordingValidationSeverity.Error)
        {
            var details = new System.Text.StringBuilder();
            for (int i = 0; i < validation.issues.Count; ++i)
            {
                var issue = validation.issues[i];
                if (issue.severity != LevelRecordingValidationSeverity.Error)
                {
                    continue;
                }

                if (details.Length > 0)
                {
                    details.AppendLine();
                }

                details.Append(issue.code).Append(": ").Append(issue.message);
            }

            __status = "Recording validation failed; file was not written.";
            EditorUtility.DisplayDialog(
                "Level Recording - Invalid Capture",
                "The recording contains blocking errors and was not saved.\n\n" + details,
                "OK");
            return;
        }



        var defaultPath = LevelRecordingSerializer.BuildDefaultFilePath(session.header);

        var savePath = EditorUtility.SaveFilePanel(

            "保存关卡录制",

            Path.GetDirectoryName(defaultPath),

            Path.GetFileName(defaultPath),

            "bin");



        if (string.IsNullOrEmpty(savePath))

        {

            __status = "已取消保存。";

            return;

        }



        var recordingRoot = Path.GetFullPath(LevelRecordingSerializer.GetRecordingRootDirectory())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var selectedDirectory = Path.GetFullPath(Path.GetDirectoryName(savePath) ?? string.Empty);
        var selectedFolderName = Path.GetFileName(selectedDirectory);
        bool isStageFolder = BotReplayCatalogBuilder.TryParseStageFolderKey(selectedFolderName, out _);
        bool isLevelStageFolder = BotReplayCatalogBuilder.TryParseFolderKey(
            selectedFolderName,
            out _,
            out _);
        bool isSceneFolder = BotReplayCatalogBuilder.TryParseSceneFolderKey(selectedFolderName, out _);
        if (!selectedDirectory.StartsWith(recordingRoot, System.StringComparison.OrdinalIgnoreCase) ||
            (!isStageFolder && !isLevelStageFolder && !isSceneFolder))
        {
            __status = "Save cancelled: select a supported Recordings folder key.";
            EditorUtility.DisplayDialog(
                "Level Recording",
                "Replay files must be saved directly under StreamingAssets/Recordings/{userStageID}/, " +
                "{levelID}_{stageIndex}/, or {sceneName}/. TRBT header metadata is not used for playback lookup.",
                "OK");
            return;
        }

        LevelRecordingSerializer.Write(savePath, session.header, session.frames);

        __lastSavedPath = savePath;

        __replayFilePath = savePath;

        __status = $"已保存 {session.frames.Count} 帧";



        if (savePath.StartsWith(Application.dataPath))

            AssetDatabase.Refresh();

    }



    private void DrawSessionPreview()

    {

        if (!Application.isPlaying || RecordingCoopHarness.Instance?.Recorder?.Session == null)

            return;



        var session = RecordingCoopHarness.Instance.Recorder.Session;

        if (!session.isActive && session.frames.Count == 0)

            return;



        EditorGUILayout.LabelField("当前录制会话", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("userStageID", session.header.userStageID.ToString());

        EditorGUILayout.LabelField("levelID / stage", $"{session.header.levelID} / {session.header.stageIndex}");

        EditorGUILayout.LabelField("帧数", session.isActive ? session.frames.Count.ToString() : session.header.frameCount.ToString());

        EditorGUILayout.LabelField("时长", session.ElapsedTime.ToString("F1") + "s");

    }

}

#endif


