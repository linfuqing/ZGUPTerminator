#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class LevelRecordingBatchAuditWindow : EditorWindow
{
    private string __rootDirectory;
    private Vector2 __scroll;
    private bool __showOk;
    private bool __showWarning;
    private bool __showError = true;
    private bool __scanPending;
    private readonly List<LevelRecordingFileValidationResult> __results = new List<LevelRecordingFileValidationResult>();

    [MenuItem("Tools/MultiplayerBot/Recording Batch Audit")]
    public static void Open()
    {
        GetWindow<LevelRecordingBatchAuditWindow>("Recording Audit");
    }

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(__rootDirectory))
        {
            __rootDirectory = LevelRecordingSerializer.GetRecordingRootDirectory();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("录制文件批量检测", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "扫描 .bin 的 PlayerProperty、Move 时间轴、SelectSkill 链等。Error = 建议重录；" +
            "Warning = 可疑但未必影响回放；OK = 无 Error/Warning。默认只显示 Error。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        __rootDirectory = EditorGUILayout.TextField("Root", __rootDirectory);
        if (GUILayout.Button("...", GUILayout.Width(28)))
        {
            var picked = EditorUtility.OpenFolderPanel("Recordings Root", __rootDirectory, string.Empty);
            if (!string.IsNullOrEmpty(picked))
            {
                __rootDirectory = picked;
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("列表筛选", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        __showError = EditorGUILayout.ToggleLeft("Error", __showError, GUILayout.Width(56));
        __showWarning = EditorGUILayout.ToggleLeft("Warning", __showWarning, GUILayout.Width(72));
        __showOk = EditorGUILayout.ToggleLeft("OK", __showOk, GUILayout.Width(48));
        EditorGUILayout.EndHorizontal();

        BotReplayLog.VerboseDiagnostics = EditorGUILayout.ToggleLeft(
            "Play Mode 详细 RecordingDiag 日志",
            BotReplayLog.VerboseDiagnostics);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(__scanPending))
        {
            if (GUILayout.Button(__scanPending ? "Scanning..." : "Scan All", GUILayout.Height(28)))
            {
                __QueueScan();
            }
        }

        if (GUILayout.Button("Open Root", GUILayout.Height(28)))
        {
            EditorUtility.RevealInFinder(__rootDirectory);
        }

        EditorGUILayout.EndHorizontal();

        if (__results.Count == 0)
        {
            EditorGUILayout.HelpBox("点击 Scan All 开始检测。", MessageType.None);
            return;
        }

        var summary = LevelRecordingBatchAuditRunner.Summarize(__results);
        int visibleCount = __CountVisibleResults(__results);
        EditorGUILayout.LabelField(
            $"Scanned {summary.totalFiles} file(s): OK={summary.okCount}, " +
            $"Warning={summary.warningCount}, Error={summary.errorCount}" +
            (visibleCount != summary.totalFiles ? $" — showing {visibleCount}" : string.Empty),
            EditorStyles.boldLabel);

        __scroll = EditorGUILayout.BeginScrollView(__scroll);
        for (int i = 0; i < __results.Count; ++i)
        {
            __DrawResult(__results[i]);
        }

        EditorGUILayout.EndScrollView();
    }

    private void __QueueScan()
    {
        if (string.IsNullOrEmpty(__rootDirectory) || !Directory.Exists(__rootDirectory))
        {
            EditorUtility.DisplayDialog("Recording Audit", "Directory not found:\n" + __rootDirectory, "OK");
            return;
        }

        __scanPending = true;
        EditorApplication.delayCall -= __RunScanDeferred;
        EditorApplication.delayCall += __RunScanDeferred;
    }

    private void __RunScanDeferred()
    {
        EditorApplication.delayCall -= __RunScanDeferred;
        __scanPending = false;
        try
        {
            if (string.IsNullOrEmpty(__rootDirectory) || !Directory.Exists(__rootDirectory))
            {
                EditorUtility.DisplayDialog("Recording Audit", "Directory not found:\n" + __rootDirectory, "OK");
                return;
            }

            __results.Clear();
            __results.AddRange(LevelRecordingBatchAuditRunner.Scan(__rootDirectory, showProgress: true));
            var summary = LevelRecordingBatchAuditRunner.Summarize(__results);
            Debug.Log(
                "[RecordingAudit] BatchScan complete " +
                $"root={__rootDirectory}, total={summary.totalFiles}, ok={summary.okCount}, " +
                $"warning={summary.warningCount}, error={summary.errorCount}, scanFailed={summary.scanFailures}.");
            if (__results.Count == 0)
            {
                EditorUtility.DisplayDialog("Recording Audit", "No .bin files found under:\n" + __rootDirectory, "OK");
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[RecordingAudit] BatchScan failed: " + exception);
            EditorUtility.DisplayDialog("Recording Audit", "Scan failed:\n" + exception.Message, "OK");
        }
        finally
        {
            Repaint();
        }
    }

    private void __DrawResult(LevelRecordingFileValidationResult result)
    {
        if (!__ShouldShowResult(result))
        {
            return;
        }

        Color color;
        if (result.WorstSeverity == LevelRecordingValidationSeverity.Error)
        {
            color = new Color(1f, 0.55f, 0.55f);
        }
        else if (result.WorstSeverity == LevelRecordingValidationSeverity.Warning)
        {
            color = new Color(1f, 0.9f, 0.55f);
        }
        else
        {
            color = new Color(0.75f, 1f, 0.75f);
        }

        var relativePath = __ToRelativePath(result.filePath);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        var old = GUI.backgroundColor;
        GUI.backgroundColor = color;
        GUILayout.Label(result.WorstSeverity.ToString(), GUILayout.Width(64));
        GUI.backgroundColor = old;
        EditorGUILayout.LabelField(relativePath, EditorStyles.boldLabel);
        if (GUILayout.Button("Reveal", GUILayout.Width(56)))
        {
            EditorUtility.RevealInFinder(result.filePath);
        }

        EditorGUILayout.EndHorizontal();

        if (!result.parseOk)
        {
            EditorGUILayout.LabelField(result.parseError);
            EditorGUILayout.EndVertical();
            return;
        }

        var audit = result.audit;
        EditorGUILayout.LabelField(
            $"frames={audit.totalFrames}, duration={audit.wallDurationSeconds:F1}s, " +
            $"gameplaySpan={audit.gameplaySpanSeconds:F1}s, move={audit.moveFrameCount}, " +
            $"maxSameTimestampMove={audit.maxMoveFramesAtSameTimestamp}, " +
            $"SelectSkill={result.selectSkillValidCount}/{result.selectSkillCount}");

        if (audit.ancillarySpans != null && audit.ancillarySpans.Count > 0)
        {
            var ancillary = string.Empty;
            for (int i = 0; i < audit.ancillarySpans.Count; ++i)
            {
                if (i > 0)
                {
                    ancillary += ", ";
                }

                ancillary += audit.ancillarySpans[i].Label + "=" + audit.ancillarySpans[i].count;
            }

            EditorGUILayout.LabelField("Handshake / ancillary: " + ancillary);
        }

        if (result.issues != null)
        {
            for (int i = 0; i < result.issues.Count; ++i)
            {
                var issue = result.issues[i];
                if (!__ShouldShowIssue(issue))
                {
                    continue;
                }

                EditorGUILayout.LabelField("[" + issue.code + "] " + issue.message);
            }
        }

        EditorGUILayout.EndVertical();
    }

    private bool __ShouldShowResult(LevelRecordingFileValidationResult result)
    {
        switch (result.WorstSeverity)
        {
            case LevelRecordingValidationSeverity.Error:
                return __showError;
            case LevelRecordingValidationSeverity.Warning:
                return __showWarning;
            default:
                return __showOk;
        }
    }

    private bool __ShouldShowIssue(LevelRecordingValidationIssue issue)
    {
        switch (issue.severity)
        {
            case LevelRecordingValidationSeverity.Error:
                return __showError;
            case LevelRecordingValidationSeverity.Warning:
                return __showWarning;
            default:
                return __showOk;
        }
    }

    private int __CountVisibleResults(IReadOnlyList<LevelRecordingFileValidationResult> results)
    {
        int count = 0;
        for (int i = 0; i < results.Count; ++i)
        {
            if (__ShouldShowResult(results[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static string __ToRelativePath(string path)
    {
        var dataPath = Application.dataPath;
        if (path.StartsWith(dataPath))
        {
            return "Assets" + path.Substring(dataPath.Length).Replace('\\', '/');
        }

        return path;
    }
}

#endif
