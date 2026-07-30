using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Collections;

[Category("Regression")]
public sealed class BotReplayCatalogFolderAuthorityTests
{
    [Test]
    public void RecordingVersion1_ReadAndReadHeader_FailClosed()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(
                file,
                new byte[]
                {
                    (byte)'T', (byte)'R', (byte)'B', (byte)'T',
                    1, 0
                });

            var headerError = Assert.Throws<InvalidDataException>(
                () => LevelRecordingSerializer.ReadHeader(file));
            StringAssert.Contains("Unsupported recording version: 1", headerError.Message);

            var readError = Assert.Throws<InvalidDataException>(
                () => LevelRecordingSerializer.Read(file));
            StringAssert.Contains("Unsupported recording version: 1", readError.Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestCase("1_0", 1u, 0, true)]
    [TestCase("26_5", 26u, 5, true)]
    [TestCase("0_0", 0u, 0, false)]
    [TestCase("Level12-1.scene", 0u, 0, false)]
    [TestCase("12_3_copy", 0u, 0, false)]
    public void LevelStageFolderKey_OnlyAcceptsCanonicalLevelAndStage(
        string folderName,
        uint expectedLevelID,
        int expectedStageIndex,
        bool expectedParsed)
    {
        bool parsed = BotReplayCatalogBuilder.TryParseFolderKey(
            folderName,
            out var levelID,
            out var stageIndex);

        Assert.AreEqual(expectedParsed, parsed);
        Assert.AreEqual(expectedLevelID, levelID);
        Assert.AreEqual(expectedStageIndex, stageIndex);
    }

    [TestCase("26", 26u, true)]
    [TestCase("0", 0u, false)]
    [TestCase("26_0", 0u, false)]
    public void StageFolderKey_OnlyAcceptsPositiveStageID(string folderName, uint expectedStageID, bool expectedParsed)
    {
        bool parsed = BotReplayCatalogBuilder.TryParseStageFolderKey(folderName, out var stageID);

        Assert.AreEqual(expectedParsed, parsed);
        Assert.AreEqual(expectedStageID, stageID);
    }

    [TestCase("Level12-1.scene", true)]
    [TestCase("Arena_Main", false)]
    [TestCase("UNASSIGNED_LEVEL_STAGE", false)]
    public void SceneFolderKey_OnlyAcceptsSceneNameDirectory(string folderName, bool expectedParsed)
    {
        bool parsed = BotReplayCatalogBuilder.TryParseSceneFolderKey(folderName, out var sceneName);

        Assert.AreEqual(expectedParsed, parsed);
        if (expectedParsed)
            Assert.AreEqual(folderName, sceneName.ToString());
    }

    [Test]
    public void Catalog_UsesFolderKey_NeverRecordingHeaderMetadata()
    {
        const uint folderLevelID = 4294967294u;
        const int folderStageIndex = 2147483646;
        const string folderName = "4294967294_2147483646";

        var root = LevelRecordingSerializer.GetRecordingRootDirectory();
        var directory = Path.Combine(root, folderName);
        var file = Path.Combine(directory, "folder-authority-regression.bin");
        Assert.IsFalse(Directory.Exists(directory), "Reserved regression folder is unexpectedly occupied.");

        BotReplayCatalog catalog = default;
        try
        {
            Directory.CreateDirectory(directory);
            var header = new LevelRecordingFileHeader
            {
                // Deliberately incompatible values: these are legal recording-client metadata,
                // but they must never become catalog keys.
                userStageID = 158,
                levelID = 0,
                stageIndex = 0,
            };
            LevelRecordingSerializer.Write(
                file,
                in header,
                new List<LevelRecordingFrame>
                {
                    new LevelRecordingFrame { timestamp = 0f, payload = new byte[] { 1 } }
                });

            var storedHeader = LevelRecordingSerializer.ReadHeader(file);
            Assert.AreEqual(158u, storedHeader.userStageID);
            Assert.AreEqual(0u, storedHeader.levelID);
            Assert.AreEqual(0, storedHeader.stageIndex);

            catalog = BotReplayCatalogBuilder.Build(Allocator.TempJob);
            var random = Unity.Mathematics.Random.CreateFromIndex(1);
            Assert.IsTrue(
                BotReplayCatalogBuilder.TryPickIndex(
                    catalog.entries,
                    0,
                    folderLevelID,
                    folderStageIndex,
                    default,
                    ref random,
                    out var catalogIndex));

            var entry = catalog.entries[catalogIndex];
            Assert.AreEqual(folderLevelID, entry.levelID);
            Assert.AreEqual(folderStageIndex, entry.stageIndex);
        }
        finally
        {
            for (int i = 0; i < catalog.EntryCount; ++i)
            {
                var recording = catalog.entries[i].recording;
                if (recording.IsCreated)
                    recording.Dispose();
            }

            catalog.Dispose();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Catalog_IndexesSceneFolderFromDirectoryName()
    {
        const string folderName = "Level999-Test.scene";
        var root = LevelRecordingSerializer.GetRecordingRootDirectory();
        var directory = Path.Combine(root, folderName);
        var file = Path.Combine(directory, "scene-folder-regression.bin");
        Assert.IsFalse(Directory.Exists(directory), "Reserved regression folder is unexpectedly occupied.");

        BotReplayCatalog catalog = default;
        try
        {
            Directory.CreateDirectory(directory);
            var header = new LevelRecordingFileHeader
            {
                userStageID = 158,
                levelID = 999,
                stageIndex = 99,
                sceneName = new FixedString32Bytes("HeaderMustNotWin.scene")
            };
            LevelRecordingSerializer.Write(
                file,
                in header,
                new List<LevelRecordingFrame>
                {
                    new LevelRecordingFrame { timestamp = 0f, payload = new byte[] { 1 } }
                });

            catalog = BotReplayCatalogBuilder.Build(Allocator.TempJob);
            var expectedScene = new FixedString32Bytes(folderName);
            bool found = false;
            for (int i = 0; i < catalog.EntryCount; ++i)
            {
                var entry = catalog.entries[i];
                if (entry.sceneName.Equals(expectedScene))
                {
                    found = true;
                    Assert.AreEqual(0u, entry.levelID);
                    Assert.AreEqual(-1, entry.stageIndex);
                    break;
                }
            }

            Assert.IsTrue(found, "A canonical *.scene parent folder must be indexed by sceneName.");
        }
        finally
        {
            for (int i = 0; i < catalog.EntryCount; ++i)
            {
                var recording = catalog.entries[i].recording;
                if (recording.IsCreated)
                    recording.Dispose();
            }

            catalog.Dispose();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Lookup_PrefersStageFolderThenLevelStageThenClientPlaySceneFileName()
    {
        var entries = new NativeArray<BotReplayCatalogEntry>(3, Allocator.Temp);
        try
        {
            entries[0] = new BotReplayCatalogEntry
            {
                sceneName = new FixedString32Bytes("Level12-1.scene")
            };
            entries[1] = new BotReplayCatalogEntry
            {
                levelID = 14,
                stageIndex = 0
            };
            entries[2] = new BotReplayCatalogEntry
            {
                userStageID = 26
            };

            var playScenePath = new FixedString32Bytes("Assets/Scenes/Level12-1.scene");
            var random = Unity.Mathematics.Random.CreateFromIndex(1);

            Assert.IsTrue(BotReplayCatalogBuilder.TryPickIndex(
                entries, 26, 14, 0, playScenePath, ref random, out var catalogIndex));
            Assert.AreEqual(2, catalogIndex);

            Assert.IsTrue(BotReplayCatalogBuilder.TryPickIndex(
                entries, 0, 14, 0, playScenePath, ref random, out catalogIndex));
            Assert.AreEqual(1, catalogIndex);

            Assert.IsTrue(BotReplayCatalogBuilder.TryPickIndex(
                entries, 0, 0, -1, playScenePath, ref random, out catalogIndex));
            Assert.AreEqual(0, catalogIndex);

            // Matching still uses the same priority chain. Its StageID and LevelID_stage folders
            // are intentionally absent, so lookup naturally falls through to sceneName.
            Assert.IsTrue(BotReplayCatalogBuilder.TryPickIndex(
                entries, 210, 99, 0, playScenePath, ref random, out catalogIndex));
            Assert.AreEqual(0, catalogIndex);
        }
        finally
        {
            entries.Dispose();
        }
    }

}
