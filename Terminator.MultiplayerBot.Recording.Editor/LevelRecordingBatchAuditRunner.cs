#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class LevelRecordingBatchAuditRunner
{
    internal struct ScanSummary
    {
        public int totalFiles;
        public int okCount;
        public int warningCount;
        public int errorCount;
        public int scanFailures;
    }

    public static List<LevelRecordingFileValidationResult> Scan(
        string rootDirectory,
        bool showProgress = true)
    {
        var results = new List<LevelRecordingFileValidationResult>();
        if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return results;
        }

        var paths = new List<string>(LevelRecordingFileValidator.EnumerateRecordingFiles(rootDirectory));
        try
        {
            for (int i = 0; i < paths.Count; ++i)
            {
                if (showProgress &&
                    EditorUtility.DisplayCancelableProgressBar(
                        "Recording Audit",
                        paths[i],
                        paths.Count <= 1 ? 1f : (float)(i + 1) / paths.Count))
                {
                    break;
                }

                try
                {
                    results.Add(LevelRecordingFileValidator.Validate(paths[i]));
                }
                catch (Exception exception)
                {
                    results.Add(__BuildScanFailure(paths[i], exception.Message));
                }
            }
        }
        finally
        {
            if (showProgress)
            {
                EditorUtility.ClearProgressBar();
            }
        }

        results.Sort(__CompareResults);
        return results;
    }

    public static ScanSummary Summarize(IReadOnlyList<LevelRecordingFileValidationResult> results)
    {
        var summary = new ScanSummary
        {
            totalFiles = results?.Count ?? 0
        };

        if (results == null)
        {
            return summary;
        }

        for (int i = 0; i < results.Count; ++i)
        {
            if (__HasIssueCode(results[i], "ScanFailed"))
            {
                summary.scanFailures++;
            }

            switch (results[i].WorstSeverity)
            {
                case LevelRecordingValidationSeverity.Error:
                    summary.errorCount++;
                    break;
                case LevelRecordingValidationSeverity.Warning:
                    summary.warningCount++;
                    break;
                default:
                    summary.okCount++;
                    break;
            }
        }

        return summary;
    }

    private static LevelRecordingFileValidationResult __BuildScanFailure(string path, string message)
    {
        return new LevelRecordingFileValidationResult
        {
            filePath = path,
            parseOk = false,
            parseError = message,
            issues = new List<LevelRecordingValidationIssue>
            {
                new LevelRecordingValidationIssue
                {
                    severity = LevelRecordingValidationSeverity.Error,
                    code = "ScanFailed",
                    message = message
                }
            }
        };
    }

    private static bool __HasIssueCode(LevelRecordingFileValidationResult result, string code)
    {
        if (result.issues == null)
        {
            return false;
        }

        for (int i = 0; i < result.issues.Count; ++i)
        {
            if (result.issues[i].code == code)
            {
                return true;
            }
        }

        return false;
    }

    private static int __CompareResults(
        LevelRecordingFileValidationResult a,
        LevelRecordingFileValidationResult b)
    {
        int severity = b.WorstSeverity.CompareTo(a.WorstSeverity);
        if (severity != 0)
        {
            return severity;
        }

        return string.CompareOrdinal(a.filePath, b.filePath);
    }
}

#endif
