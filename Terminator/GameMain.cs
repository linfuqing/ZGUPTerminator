using System;
using System.IO;
using System.Collections;
using Unity.Entities.Content;
using UnityEngine;
using ZG;

public class GameSceneActivation : IEnumerator
{
    public bool MoveNext()
    {
#if ENABLE_CONTENT_DELIVERY
        return ContentDeliveryGlobalState.CurrentContentUpdateState < ContentDeliveryGlobalState.ContentUpdateState.ContentReady;
#else
        return false;
#endif
    }

    void IEnumerator.Reset()
    {
            
    }
        
    object IEnumerator.Current => null;
}

public class GameMain : GameUser
{
    private class LevelData : ILevelData
    {
        private uint __id;

        public LevelData(uint id)
        {
            __id = id;
        }
        
        public IEnumerator SubmitLevel(
            int stage,
            int gold,
            Action<bool> onComplete)
        {
            return IUserData.instance.SubmitLevel(__id, stage, gold, onComplete);
        }
    }

    public event Func<IEnumerator> onStart;

    public static readonly string LanguagePackageResourcePath = "LanguagePackageResourcePath";
    public static readonly string AssetPath = "AssetPath";
    public static readonly string AssetScenePath = "AssetScenePath";
    public static readonly string DefaultSceneName = "DefaultSceneName";
    public static readonly string DefaultLevelSceneName = "DefaultLevelSceneName";
    public static readonly string InitialContentSet = "InitialContentSet";
    public static readonly string LocalCachePath = "LocalCachePath";
    public static readonly string RemoteUrlRoot = "RemoteUrlRoot";

    private uint __id;
    private string __defaultSceneName;

    public IAssetBundleFactory factory
    {
        get;

        set;
    }

    public override IGameUserData userData => IUserData.instance;

    IEnumerator Start()
    {
        Application.targetFrameRate = 60;
        
        while(!GameConstantManager.isInit)
        {
            yield return null;
        }

        yield return GameAssetManager.InitLanguage(
            GameConstantManager.Get(LanguagePackageResourcePath),
            GameConstantManager.Get(GameConstantManager.KEY_CDN_URL), 
            factory, 
            DontDestroyOnLoad);
        
        var onStarts = this.onStart?.GetInvocationList();
        if (onStarts != null)
        {
            foreach (var onStart in onStarts)
                yield return ((Func<IEnumerator>)onStart)();
        }

#if ENABLE_CONTENT_DELIVERY
        string remoteUrlRoot = GameConstantManager.Get(RemoteUrlRoot), 
            localCachePath = GameConstantManager.Get(LocalCachePath);
        if (string.IsNullOrEmpty(remoteUrlRoot))
        {
            var pack = AssetUtility.RetrievePack(localCachePath);
            if (pack == null)
                RuntimeContentSystem.LoadContentCatalog(null, localCachePath, null);
            else
            {
                var progressbar = GameProgressbar.instance;
                progressbar.ShowProgressBar();

                string packName;
                var header = pack.header;
                if (header == null)
                    packName = localCachePath;
                else
                {
                    while (!header.isDone)
                        yield return null;

                    packName = header.name;
                }

                while (!pack.isDone)
                {
                    progressbar.UpdateProgressBar(pack.downloadProgress);

                    yield return null;
                }

                progressbar.ClearProgressBar();

                ContentDeliveryGlobalState.Initialize(
                    null, 
                    null, 
                    null, 
                    null);

                Func<string, string> remapFunc = x =>
                {
                    if (pack.GetFileInfo(x, out ulong fileOffset, out string filePath))
                    {
                        AssetUtility.UpdatePack(packName, ref filePath, ref fileOffset);
                        
                        Debug.Log($"UpdatePack {filePath}");
                    }
                    else
                        Debug.LogError($"GetFileInfo {x} failed");

                    UnityEngine.Assertions.Assert.AreEqual(0, fileOffset);

                    return filePath;
                };

                ContentDeliveryGlobalState.PathRemapFunc = remapFunc;
                
                var catalogPath = remapFunc(RuntimeContentManager.RelativeCatalogPath);
                RuntimeContentManager.LoadLocalCatalogData(catalogPath,
                        RuntimeContentManager.DefaultContentFileNameFunc,
                        p => remapFunc(RuntimeContentManager.DefaultArchivePathFunc(p)));
            }
        }
        else
        {
            RuntimeContentSystem.LoadContentCatalog(
                remoteUrlRoot,
                string.IsNullOrEmpty(localCachePath)
                    ? null
                    : Path.Combine(Application.persistentDataPath, localCachePath),
                GameConstantManager.Get(InitialContentSet));
        }
#endif

        Shared.onActivated += __OnActivated;
        onLogin.AddListener(__OnLogin);
        Login();
    }

    private void __OnActivated()
    {
        Shared.onActivated -= __OnActivated;
        
        __defaultSceneName = GameConstantManager.Get(DefaultLevelSceneName);

        var analytics = IAnalytics.instance as IAnalyticsEx;
        if(analytics != null)
            analytics.Activate(Shared.channelName, Shared.channelUser);
    }

    private void __OnLogin()
    {
        var assetManager = GameAssetManager.instance;
        assetManager.onConfirmCancel += __OnConfirmCancel;

        assetManager.StartCoroutine(__Init());
    }

    private IEnumerator __Init()
    {
        GameSceneActivation activation;
        if (string.IsNullOrEmpty(__defaultSceneName))
        {
            activation = null;
            __defaultSceneName = GameConstantManager.Get(DefaultSceneName);
        }
        else
        {
            yield return IUserData.instance.QueryUser(Shared.channelName, Shared.channelUser, __OnApplyLevel);
            yield return IUserData.instance.QuerySkills(__id, __OnApplySkills);
            
            activation = new GameSceneActivation();
        }

        yield return GameAssetManager.instance.Init(
            __defaultSceneName, 
            GameConstantManager.Get(AssetScenePath), 
                GameConstantManager.Get(AssetPath), 
                    GameConstantManager.Get(GameConstantManager.KEY_CDN_URL), 
            factory, 
            activation);
    }

    private void __OnApplyLevel(uint id)
    {
        __id = id;
        ILevelData.instance = new LevelData(id);
    }

    private void __OnApplySkills(Memory<UserSkill> skills)
    {
        ref var skillGroups = ref LevelPlayerShared.skillGroups;
        skillGroups.Clear();

        LevelPlayerSkillGroup skillGroup;
        foreach (var skill in skills.Span)
        {
            skillGroup.name = skill.name;
            skillGroups.Add(skillGroup);
        }

    }

    private void __OnConfirmCancel()
    {
        Application.Quit();
    }
}
