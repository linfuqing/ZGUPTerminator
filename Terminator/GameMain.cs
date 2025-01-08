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
        return ContentDeliveryGlobalState.CurrentContentUpdateState < ContentDeliveryGlobalState.ContentUpdateState.ContentReady;
    }

    void IEnumerator.Reset()
    {
            
    }
        
    object IEnumerator.Current => null;
}

public class GameMain : MonoBehaviour
{
    public event Func<IEnumerator> onStart;

    public static readonly string LanguagePackageResourcePath = "LanguagePackageResourcePath";
    public static readonly string AssetPath = "AssetPath";
    public static readonly string AssetScenePath = "AssetScenePath";
    public static readonly string DefaultSceneName = "DefaultSceneName";
    public static readonly string InitialContentSet = "InitialContentSet";
    public static readonly string LocalCachePath = "LocalCachePath";
    public static readonly string RemoteUrlRoot = "RemoteUrlRoot";

    public IAssetBundleFactory factory
    {
        get;

        set;
    }

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
        
        var assetManager = GameAssetManager.instance;
        assetManager.onConfirmCancel += __OnConfirmCancel;

        assetManager.StartCoroutine(__Init());
    }

    private IEnumerator __Init()
    {
        return GameAssetManager.instance.Init(
            GameConstantManager.Get(DefaultSceneName), 
            GameConstantManager.Get(AssetScenePath), 
                GameConstantManager.Get(AssetPath), 
                    GameConstantManager.Get(GameConstantManager.KEY_CDN_URL), 
            factory, 
            null/*new GameSceneActivation()*/);
    }

    private void __OnConfirmCancel()
    {
        Application.Quit();
    }
}
