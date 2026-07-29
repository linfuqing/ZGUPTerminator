using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

public static class BotReplayCatalogBuilder
{
    public static BotReplayCatalog Build(Allocator allocator)
    {
        var managedEntries = new List<BotReplayCatalogEntry>();
        var seenPaths = new HashSet<string>();

        var root = LevelRecordingSerializer.GetRecordingRootDirectory();
        if (!Directory.Exists(root))
        {
            Debug.LogWarning($"[BotReplayCatalog] Recording root not found: {root}");
            return new BotReplayCatalog { entries = new NativeArray<BotReplayCatalogEntry>(0, allocator) };
        }

        int ignoredDirectoryCount = 0;
        foreach (var directory in Directory.GetDirectories(root))
        {
            var folderName = Path.GetFileName(directory);
            if (TryParseStageFolderKey(folderName, out var userStageID))
            {
                __TryLoadDirectory(directory, userStageID, 0, -1, default, managedEntries, seenPaths);
            }
            else if (TryParseFolderKey(folderName, out var levelID, out var stageIndex))
            {
                __TryLoadDirectory(directory, 0, levelID, stageIndex, default, managedEntries, seenPaths);
            }
            else if (TryParseSceneFolderKey(folderName, out var sceneName))
            {
                __TryLoadDirectory(directory, 0, 0, -1, sceneName, managedEntries, seenPaths);
            }
            else
            {
                ++ignoredDirectoryCount;
            }
        }

        if (ignoredDirectoryCount > 0)
        {
            Debug.LogWarning(
                $"[BotReplayCatalog] Ignored {ignoredDirectoryCount} invalid directory(s); recordings must live in " +
                "Recordings/{userStageID}/, Recordings/{levelID}_{stageIndex}/, or Recordings/{sceneName}/.");
        }

        if (managedEntries.Count == 0)
        {
            Debug.LogWarning("[BotReplayCatalog] No recordings found.");
            return new BotReplayCatalog { entries = new NativeArray<BotReplayCatalogEntry>(0, allocator) };
        }

        var entries = new NativeArray<BotReplayCatalogEntry>(managedEntries.Count, allocator);
        for (int i = 0; i < managedEntries.Count; ++i)
            entries[i] = managedEntries[i];

        Debug.Log($"[BotReplayCatalog] Loaded {entries.Length} folder-indexed recording(s) from disk.");
        return new BotReplayCatalog { entries = entries };
    }

    /// <summary>
    /// Selects replay material using parent-directory keys only, in this order:
    /// {userStageID}, {levelID}_{stageIndex}, then Path.GetFileName(sceneName).
    /// TRBT Header IDs and sceneName never participate in lookup.
    /// </summary>
    public static bool TryPickIndex(
        in NativeArray<BotReplayCatalogEntry> entries,
        uint userStageID,
        uint levelID,
        int stageIndex,
        in FixedString32Bytes sceneName,
        ref Unity.Mathematics.Random random,
        out int catalogIndex)
    {
        catalogIndex = -1;
        if (!entries.IsCreated || entries.Length == 0)
            return false;

        using var matches = new NativeList<int>(Allocator.Temp);
        if (userStageID != 0)
        {
            for (int i = 0; i < entries.Length; ++i)
            {
                ref readonly var entry = ref entries.ElementAt(i);
                if (entry.userStageID == userStageID)
                    matches.Add(i);
            }
        }

        if (matches.Length == 0 && levelID != 0 && stageIndex >= 0)
        {
            for (int i = 0; i < entries.Length; ++i)
            {
                ref readonly var entry = ref entries.ElementAt(i);
                if (entry.levelID == levelID && entry.stageIndex == stageIndex)
                    matches.Add(i);
            }
        }

        var sceneFileName = GetSceneFileName(in sceneName);
        if (matches.Length == 0 && !sceneFileName.IsEmpty)
        {
            for (int i = 0; i < entries.Length; ++i)
            {
                ref readonly var entry = ref entries.ElementAt(i);
                // Equivalent to Path.GetFileName(play.sceneName) ==
                // Path.GetFileName($"Recordings/{sceneName}"). Keep both operands
                // normalized because the client payload may contain a path.
                var recordingSceneFileName = GetSceneFileName(in entry.sceneName);
                if (!recordingSceneFileName.IsEmpty && recordingSceneFileName.Equals(sceneFileName))
                    matches.Add(i);
            }
        }

        if (matches.Length == 0)
            return false;

        catalogIndex = matches[random.NextInt(0, matches.Length)];
        return true;
    }

    private static void __TryLoadDirectory(
        string directory,
        uint userStageID,
        uint levelID,
        int stageIndex,
        in FixedString32Bytes sceneName,
        List<BotReplayCatalogEntry> output,
        HashSet<string> seenPaths)
    {
        foreach (var file in Directory.GetFiles(directory, "*.bin"))
        {
            if (!seenPaths.Add(file))
                continue;

            try
            {
                var blob = BotRecordingBlobBuilder.CreateFromFile(file);
                if (!blob.IsCreated)
                    continue;

                output.Add(new BotReplayCatalogEntry
                {
                    userStageID = userStageID,
                    levelID = levelID,
                    stageIndex = stageIndex,
                    sceneName = sceneName,
                    recording = blob
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BotReplayCatalog] Skip {file}: {exception.Message}");
            }
        }
    }

    internal static bool TryParseStageFolderKey(string folderName, out uint userStageID)
    {
        return uint.TryParse(folderName, out userStageID) && userStageID != 0;
    }

    internal static bool TryParseFolderKey(string folderName, out uint levelID, out int stageIndex)
    {
        levelID = 0;
        stageIndex = 0;
        if (string.IsNullOrEmpty(folderName))
            return false;

        int separator = folderName.IndexOf('_');
        if (separator <= 0 || separator != folderName.LastIndexOf('_') || separator >= folderName.Length - 1)
            return false;

        if (!uint.TryParse(folderName.Substring(0, separator), out levelID) || levelID == 0 ||
            !int.TryParse(folderName.Substring(separator + 1), out stageIndex) || stageIndex < 0)
        {
            levelID = 0;
            stageIndex = 0;
            return false;
        }

        return true;
    }

    internal static bool TryParseSceneFolderKey(string folderName, out FixedString32Bytes sceneName)
    {
        sceneName = default;
        if (string.IsNullOrEmpty(folderName) ||
            !folderName.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ||
            folderName.Length > FixedString32Bytes.UTF8MaxLengthInBytes)
        {
            return false;
        }

        sceneName = new FixedString32Bytes(folderName);
        return true;
    }

    internal static FixedString32Bytes GetSceneFileName(in FixedString32Bytes sceneName)
    {
        int firstFileNameByte = 0;
        for (int i = 0; i < sceneName.Length; ++i)
        {
            byte value = sceneName[i];
            if (value == (byte)'/' || value == (byte)'\\')
                firstFileNameByte = i + 1;
        }

        int fileNameLength = sceneName.Length - firstFileNameByte;
        if (fileNameLength <= 0 || fileNameLength > FixedString32Bytes.UTF8MaxLengthInBytes)
            return default;

        var result = default(FixedString32Bytes);
        if (!result.TryResize(fileNameLength, NativeArrayOptions.UninitializedMemory))
            return default;

        for (int i = 0; i < fileNameLength; ++i)
            result[i] = sceneName[firstFileNameByte + i];

        return result;
    }
}
