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
    public event Func<IEnumerator> onStart;

    public static readonly string LanguagePackageResourcePath = "LanguagePackageResourcePath";
    public static readonly string AssetPath = "AssetPath";
    public static readonly string AssetScenePath = "AssetScenePath";
    public static readonly string DefaultSceneName = "DefaultSceneName";
    public static readonly string DefaultLevelSceneName = "DefaultLevelSceneName";
    public static readonly string InitialContentSet = "InitialContentSet";
    public static readonly string LocalCachePath = "LocalCachePath";
    public static readonly string RemoteUrlRoot = "RemoteUrlRoot";

    private string __defaultSceneName;

    public IAssetBundleFactory factory
    {
        get;

        set;
    }

    public override IGameUserData userData => IUserData.instance;

    IEnumerator Start()
    {
        while(!GameConstantManager.isInit)
        {
            yield return null;
        }

#if ENABLE_CONTENT_DELIVERY
        string localCachePath = GameConstantManager.Get(LocalCachePath);
        RuntimeContentSystem.LoadContentCatalog(
            GameConstantManager.Get(RemoteUrlRoot), 
            string.IsNullOrEmpty(localCachePath) ? null : Path.Combine(Application.persistentDataPath, localCachePath), 
            GameConstantManager.Get(InitialContentSet));
#endif
        
        yield return null;
        
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
            analytics.Activate(GameUser.Shared.channelName, GameUser.Shared.channelUser);
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
            __defaultSceneName = GameConstantManager.Get(DefaultSceneName);
            activation = new GameSceneActivation();
        }
        else
            activation = null;
        
        return GameAssetManager.instance.Init(
            __defaultSceneName, 
            GameConstantManager.Get(AssetScenePath), 
                GameConstantManager.Get(AssetPath), 
                    GameConstantManager.Get(GameConstantManager.KEY_CDN_URL), 
            factory, 
            activation);
    }

    private void __OnConfirmCancel()
    {
        Application.Quit();
    }
}
