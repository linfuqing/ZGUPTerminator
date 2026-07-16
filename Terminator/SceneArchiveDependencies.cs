using System;

/// <summary>
/// Build-time / runtime shared format: parent Unity scene (Selection in BuildContent)
/// → files to materialize under PathRemap (ContentArchives + EntityScenes).
/// </summary>
public static class SceneArchiveDependencies
{
    public const string FileName = "scene_archive_dependencies";

    public const string Extension = "json";

    public static string RelativePathWithoutExtension => $"ContentArchives/{FileName}";

    /// <summary>Path relative to content pack root (same folder as archive_dependencies.bin).</summary>
    public static string RelativePath => $"{RelativePathWithoutExtension}.{Extension}";
    
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

        /// <summary>Archive ids under ContentArchives/ (Hybrid / WeakObject content).</summary>
        public string[] archives;

        /// <summary>
        /// Relative paths under content pack as PathRemap sees them
        /// (e.g. EntityScenes/{guid}.entityheader, EntityScenes/{guid}.0.entities).
        /// Required for SubScene load even when archives is empty.
        /// </summary>
        public string[] entityScenes;
    }
}
