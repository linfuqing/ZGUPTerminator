#if UNITY_EDITOR
using UnityEditor;

[InitializeOnLoad]
internal static class LevelRecordingProjectSetup
{
    static LevelRecordingProjectSetup()
    {
        EditorApplication.delayCall += EnsureRecordingFolder;
    }

    private static void EnsureRecordingFolder()
    {
        var path = LevelRecordingSerializer.GetRecordingRootDirectory();
        if (!System.IO.Directory.Exists(path))
            System.IO.Directory.CreateDirectory(path);
    }
}
#endif
