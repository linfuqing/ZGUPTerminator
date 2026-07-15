using System;

/// <summary>
/// Build-time / runtime shared format: parent Unity scene (Selection in BuildContent)
/// → Content archive ids needed for MEMFS materialization.
/// </summary>
public static class SceneArchiveDependencies
{
    public const string FileName = "scene_archive_dependencies.json";

    /// <summary>Path relative to content pack root (same folder as archive_dependencies.bin).</summary>
    public static string RelativePath => $"ContentArchives/{FileName}";

    [Serializable]
    public class File
    {
        public string catalog;
        public SceneEntry[] scenes;
    }

    [Serializable]
    public class SceneEntry
    {
        /// <summary>Parent Unity scene asset name (matches LoadScene / GameSceneActivation).</summary>
        public string sceneName;

        /// <summary>Parent scene asset GUID (optional, for debugging).</summary>
        public string sceneGuid;

        /// <summary>Archive ids under ContentArchives/ to materialize.</summary>
        public string[] archives;
    }
}
