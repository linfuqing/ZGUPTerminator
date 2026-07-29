using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZG;

internal static class LoginManagerRecordingUtility
{
    public static void FillHeader(ref LevelRecordingFileHeader header)
    {
        var loginManager = LoginManager.instance;
        if (loginManager == null)
            return;

        header.userStageID = loginManager.selectedUserStageID;

        if (header.userStageID == 0 && LevelPlayerShared<LocalPlayer>.channelStatus != 0)
            header.userStageID = (uint)LevelPlayerShared<LocalPlayer>.channelStatus;

        // levelID / stageIndex：LoginManager 无私开属性时留 0；索引以 userStageID 为主（见 BotReplayCatalogBuilder）。
        header.levelID = 0;
        header.stageIndex = 0;

        var activeScene = SceneManager.GetActiveScene().name;
        header.sceneName = string.IsNullOrEmpty(activeScene)
            ? default
            : new FixedString32Bytes(activeScene);
    }
}