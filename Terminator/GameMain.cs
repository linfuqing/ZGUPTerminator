using System;
using System.IO;
using System.Collections;
using Unity.Entities.Content;
using UnityEngine;
using ZG;

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

    IEnumerator Start()
    {
        while(!GameConstantManager.isInit)
        {
            yield return null;
        }

        yield return null;

        string language = GameLanguage.overrideLanguage, 
            persistentDataPath = Path.Combine(Application.persistentDataPath, language), 
            languagePackageResourcePath = GameConstantManager.Get(LanguagePackageResourcePath), 
            languagePackageResourceFolder = Path.GetDirectoryName(languagePackageResourcePath), 
            languagePackageResourceFilename = Path.GetFileName(languagePackageResourceFolder), 
            languagePackageResourceFilepath = Path.Combine(languagePackageResourceFolder, languagePackageResourceFilename);
        var assetManager = new AssetManager(Path.Combine(
            persistentDataPath, 
            languagePackageResourceFilepath), null);
        
        var assetPath = new AssetPath(GameAssetManager.GetStreamingAssetsURL(Path.Combine(language, languagePackageResourceFilepath)), string.Empty, null);
        yield return assetManager.GetOrDownload(null, null, assetPath);

        string url = GameConstantManager.Get(GameConstantManager.KEY_CDN_URL);
        if (!string.IsNullOrEmpty(url))
        {
            assetPath = new AssetPath(
                $"{url}/{language}/{languagePackageResourceFolder}/{languagePackageResourceFilename}", 
                string.Empty, 
                null);
            yield return assetManager.GetOrDownload(null, null, assetPath);
        }

        string languagePackageResourceName = Path.GetFileName(languagePackageResourcePath);
        var loader = new AssetBundleLoader<GameObject>(languagePackageResourceName.ToLower(), languagePackageResourceName, assetManager);

        yield return loader;

        var languagePackage = Instantiate(loader.value);

        DontDestroyOnLoad(languagePackage);
        
        var onStarts = this.onStart?.GetInvocationList();
        if (onStarts != null)
        {
            foreach (var onStart in onStarts)
                yield return ((Func<IEnumerator>)onStart)();
        }

#if ENABLE_CONTENT_DELIVERY
        GameProgressbar.instance.ShowProgressBar();
        
        string localCachePath = GameConstantManager.Get(LocalCachePath);
        RuntimeContentSystem.LoadContentCatalog(
            GameConstantManager.Get(RemoteUrlRoot), 
            string.IsNullOrEmpty(localCachePath) ? null : Path.Combine(Application.persistentDataPath, localCachePath), 
            GameConstantManager.Get(InitialContentSet));
        
        ContentDeliveryGlobalState.RegisterForContentUpdateCompletion(__OnContentUpdateCompletion);
#else
        var gameAssetManager = GameAssetManager.instance;
        gameAssetManager.onConfirmCancel += __OnConfirmCancel;

        gameAssetManager.StartCoroutine(__Init());
#endif
    }

    private IEnumerator __Init()
    {
        return GameAssetManager.instance.Init(
            GameConstantManager.Get(DefaultSceneName), 
            GameConstantManager.Get(AssetScenePath), 
                GameConstantManager.Get(AssetPath), 
                    GameConstantManager.Get(GameConstantManager.KEY_CDN_URL));
    }

    private void __OnContentUpdateCompletion(ContentDeliveryGlobalState.ContentUpdateState contentUpdateState)
    {
        if (contentUpdateState < ContentDeliveryGlobalState.ContentUpdateState.ContentReady)
            return;
        
        GameProgressbar.instance.ClearProgressBar();
        
        var assetManager = GameAssetManager.instance;
        assetManager.onConfirmCancel += __OnConfirmCancel;

        assetManager.StartCoroutine(__Init());
    }

    private void __OnConfirmCancel()
    {
        Application.Quit();
    }
}
