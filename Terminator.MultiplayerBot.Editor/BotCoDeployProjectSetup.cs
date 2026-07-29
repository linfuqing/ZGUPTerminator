#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BotCoDeployProjectSetup
{

    [MenuItem("Tools/MultiplayerBot/Add Bot Co-Deploy To Open Scene")]
    public static void AddCoDeployToOpenScene()
    {
        if (Object.FindObjectOfType<BotCoDeployBootstrap>() != null)
        {
            Debug.Log("[Bot] BotCoDeployBootstrap already exists in scene.");
            return;
        }

        var config = EnsureBotConfigAssetInternal();
        var go = new GameObject("BotCoDeployBootstrap");
        var bootstrap = go.AddComponent<BotCoDeployBootstrap>();
        var serialized = new SerializedObject(bootstrap);
        serialized.FindProperty("config").objectReferenceValue = config;
        if (config != null)
        {
            var configSerialized = new SerializedObject(config);
            configSerialized.FindProperty("enableCoDeploy").boolValue = true;
            configSerialized.ApplyModifiedPropertiesWithoutUndo();
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[Bot] Added BotCoDeployBootstrap. Save scene and ensure NetworkRelayServerManager is present.");
    }

    [MenuItem("Tools/MultiplayerBot/Ensure Bot Config Asset")]
    public static void EnsureBotConfigAssetMenu()
    {
        EnsureBotConfigAssetInternal();
    }

    private static BotConfig EnsureBotConfigAssetInternal()
    {
        const string folder = "Assets/Resources/Bot";
        const string path = folder + "/BotConfig.asset";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Resources", "Bot");

        var config = AssetDatabase.LoadAssetAtPath<BotConfig>(path);
        if (config != null)
            return config;

        config = ScriptableObject.CreateInstance<BotConfig>();
        config.botProfiles = new[]
        {
            new BotConfig.BotProfile
            {
                userID = 999000001,
                userName = "Bot001",
                userAvatar = string.Empty,
                power = 1000,
                matchLevel = 0
            },
            new BotConfig.BotProfile
            {
                userID = 999000002,
                userName = "Bot002",
                userAvatar = string.Empty,
                power = 1000,
                matchLevel = 0
            }
        };
        config.enableCoDeploy = true;
        AssetDatabase.CreateAsset(config, path);
        AssetDatabase.SaveAssets();
        return config;
    }
}
#endif
