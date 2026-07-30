using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using NUnit.Framework;

[Category("Deployment")]
[Category("Recording")]
public sealed class LevelRecordingDeploymentAuditTests
{
    [Test]
    public void DeployedRecordingRoot_AllFilesPassStrictAudit()
    {
        string root = LevelRecordingSerializer.GetRecordingRootDirectory();
        Assert.IsTrue(
            System.IO.Directory.Exists(root),
            "Recording deployment root is missing: " + root);

        var results = LevelRecordingBatchAuditRunner.Scan(root, showProgress: false);
        var summary = LevelRecordingBatchAuditRunner.Summarize(results);
        UnityEngine.Debug.Log(
            "[RecordingAudit] Deployment total=" + summary.totalFiles +
            " ok=" + summary.okCount +
            " warning=" + summary.warningCount +
            " error=" + summary.errorCount +
            " scanFailed=" + summary.scanFailures);

        Assert.Greater(summary.totalFiles, 0, "No deployed recordings were found.");
        Assert.AreEqual(0, summary.scanFailures, "At least one deployed recording could not be scanned.");
        Assert.AreEqual(
            0,
            summary.errorCount,
            "At least one deployed recording failed the strict audit. " + __DescribeErrors(results));
    }

    private static string __DescribeErrors(
        IReadOnlyList<LevelRecordingFileValidationResult> results)
    {
        var codeCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var examples = new List<string>();
        for (int resultIndex = 0; resultIndex < results.Count; ++resultIndex)
        {
            var result = results[resultIndex];
            if (result.WorstSeverity != LevelRecordingValidationSeverity.Error)
            {
                continue;
            }

            if (examples.Count < 5)
            {
                string directory = Path.GetFileName(Path.GetDirectoryName(result.filePath));
                examples.Add(Path.Combine(directory ?? string.Empty, Path.GetFileName(result.filePath)));
            }

            if (result.issues == null)
            {
                continue;
            }

            for (int issueIndex = 0; issueIndex < result.issues.Count; ++issueIndex)
            {
                var issue = result.issues[issueIndex];
                if (issue.severity != LevelRecordingValidationSeverity.Error)
                {
                    continue;
                }

                codeCounts.TryGetValue(issue.code, out int count);
                codeCounts[issue.code] = count + 1;
            }
        }

        var message = new StringBuilder("errorCodes=[");
        bool isFirst = true;
        foreach (var pair in codeCounts)
        {
            if (!isFirst)
            {
                message.Append(", ");
            }

            isFirst = false;
            message.Append(pair.Key).Append('=').Append(pair.Value);
        }

        message.Append("], examples=[").Append(string.Join(", ", examples)).Append(']');
        return message.ToString();
    }
}
