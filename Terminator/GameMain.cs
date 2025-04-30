using System;
using System.IO;
using System.Collections;
using System.Security.Cryptography;
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

public class GameLevelData : ILevelData
{
    private uint __userID;
    
    public GameLevelData(uint userID)
    {
        __userID = userID;
    }
    
    public IEnumerator SubmitStage(
        ILevelData.Flag flag, 
        int stage, 
        int killCount, 
        int gold, 
        int rage, 
        int exp, 
        int expMax, 
        string[] skills,
        Action<int> onComplete)
    {
        return IUserData.instance.SubmitStage(
            __userID, 
            ToStageFlag(flag),
            stage, 
            killCount, 
            gold, 
            rage, 
            exp, 
            expMax,
            skills, 
            onComplete);
    }
        
    public IEnumerator SubmitLevel(
        ILevelData.Flag flag, 
        int stage,
        int killCount, 
        int gold,
        Action<bool> onComplete)
    {
        return IUserData.instance.SubmitLevel(
            __userID, 
            ToStageFlag(flag),
            stage, 
            killCount, 
            gold, 
            onComplete);
    }

    private IUserData.StageFlag ToStageFlag(ILevelData.Flag flag)
    {
        IUserData.StageFlag stageFlag = 0;
        if((flag & ILevelData.Flag.HasBeenDamaged) != ILevelData.Flag.HasBeenDamaged)
            stageFlag |= IUserData.StageFlag.NoDamage;

        return stageFlag;
    }
}

public class GameMain : GameUser
{
    
    private class AssetIterator : IDisposable
    {
        private AssetBundle __assetBundle;
        private string[] __assetNames;
        private string __path;
        
        public int index
        {
            get;

            private set;
        }

        public int count => __assetNames.Length;

        public string assetName => __assetNames[Mathf.Clamp(index, 0, count - 1)];

        public readonly AssetManager AssetManager;

        public static string ToAssetPath(ref string path)
        {
            path = Path.Combine(Application.persistentDataPath, path);
            return Path.Combine(path, Path.GetFileName(path));
        }
        
        public static string ToAssetPath(string path)
        {
            return ToAssetPath(ref path);
        }
        
        public AssetIterator(string path, AssetBundle assetBundle)
        {
            __path = path;
            string assetPath = ToAssetPath(ref __path);
            AssetManager.CreateDirectory(assetPath);

            AssetManager = new AssetManager(assetPath);
            
            __assetBundle = assetBundle;
            
            __assetNames = assetBundle.GetAllAssetNames();

            index = 0;
        }

        public void Dispose()
        {
            __assetBundle.Unload(true);
        }

        public bool MoveNext()
        {
            float maximumDeltaTime = Time.maximumDeltaTime * 0.5f;
            long ticks = DateTime.Now.Ticks;
            while (index < __assetNames.Length)
            {
                if (__Load(__assetNames[index++]) && 
                    (DateTime.Now.Ticks - ticks) * 1.0 / TimeSpan.TicksPerSecond > maximumDeltaTime)
                    return true;
            }

            return false;
        }

        public void WaitForCompletion()
        {
            while (MoveNext()) ;
        }

        private bool __Load(string name)
        {
            string filename = Path.GetFileNameWithoutExtension(name);
            if (AssetManager.Get(filename, out _))
                return false;

            var text = __assetBundle.LoadAsset(filename) as TextAsset;
            var bytes = text == null ? null : text.bytes;
            if (bytes == null)
                return false;

            UnityEngine.Object.DestroyImmediate(text, true);

            File.WriteAllBytes(Path.Combine(__path, filename), bytes);

            AssetManager.AssetData assetData;
            assetData.type = AssetManager.AssetType.Uncompressed;
            assetData.info.version = 0;
            assetData.info.size = (uint)bytes.LongLength;
            assetData.info.fileName = filename;
            using (var md5 = new MD5CryptoServiceProvider())
                assetData.info.md5 = md5.ComputeHash(bytes);

            assetData.pack = AssetManager.AssetPack.Default;
            assetData.dependencies = null;

            using (var writer = new AssetManager.Writer(string.Empty, AssetManager))
                writer.Write(filename, assetData);

            return true;
        }
    }
    
    public struct AssetUnzipper : IGameAssetUnzipper
    {
        public static readonly string FileName = GameConstantManager.Get(ContentPackPath);
        
        public string filename => FileName;

        public IEnumerator Execute(AssetBundle assetBundle, AssetManager.DownloadHandler downloadHandler)
        {
            AssetManager assetManager;
            if(assetBundle == null)
                assetManager = new AssetManager(AssetIterator.ToAssetPath(FileName));
            else
            {
                using (var assetIterator = new AssetIterator(FileName, assetBundle))
                {
                    int index, count;
                    while (assetIterator.MoveNext())
                    {
                        yield return null;

                        if (downloadHandler != null)
                        {
                            index = assetIterator.index;
                            count = assetIterator.count;

                            downloadHandler(
                                assetIterator.assetName,
                                index * 1.0f / count,
                                0,
                                0,
                                0,
                                index,
                                count);
                        }
                    }

                    assetManager = assetIterator.AssetManager;
                }
            }
            
#if ENABLE_CONTENT_DELIVERY
            ContentDeliveryGlobalState.Initialize(
                null, 
                null, 
                null, 
                null);
                
            Func<string, string> remapFunc = x =>
            {
                string filename = Path.GetFileName(x);
                if (!assetManager.GetAssetPath(filename, out _, out ulong fileOffset, out string filePath))
                    Debug.LogError($"GetFileInfo {x} failed");

                UnityEngine.Assertions.Assert.AreEqual(0, fileOffset);

                return filePath;
            };

            ContentDeliveryGlobalState.PathRemapFunc = remapFunc;

            var catalogPath = remapFunc(RuntimeContentManager.RelativeCatalogPath);
            RuntimeContentManager.LoadLocalCatalogData(catalogPath,
                RuntimeContentManager.DefaultContentFileNameFunc,
                p => remapFunc(RuntimeContentManager.DefaultArchivePathFunc(p)));
#endif
        }
    }

    public event Func<IEnumerator> onStart;

    public static readonly string LanguagePackageResourcePath = "LanguagePackageResourcePath";
    public static readonly string AssetPath = "AssetPath";
    public static readonly string AssetScenePath = "AssetScenePath";
    public static readonly string DefaultSceneName = "DefaultSceneName";
    public static readonly string DefaultLevelSceneName = "DefaultLevelSceneName";
    public static readonly string ContentSet = "ContentSet";
    public static readonly string ContentPackPath = "ContentPackPath";

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

/*#if ENABLE_CONTENT_DELIVERY
        string contentPackPath = GameConstantManager.Get(ContentPackPath);
        var contentPack = AssetUtility.RetrievePack(contentPackPath);
        if (contentPack == null)
            RuntimeContentSystem.LoadContentCatalog(
                contentPackPath,
                    null,
                    GameConstantManager.Get(ContentSet));
        else
        {
            var progressbar = GameProgressbar.instance;
            progressbar.ShowProgressBar();

            string packName;
            var header = contentPack.header;
            if (header == null)
            {
                packName = contentPackPath;
            }
            else
            {
                while (!header.isDone)
                    yield return null;

                packName = header.name;
            }

            while (!contentPack.isDone)
            {
                progressbar.UpdateProgressBar(contentPack.downloadProgress);

                yield return null;
            }

            string filePath = header?.filePath;
            if (string.IsNullOrEmpty(filePath))
            {
                ContentDeliveryGlobalState.Initialize(
                    null, 
                    null, 
                    null, 
                    null);
                
                Func<string, string> remapFunc = x =>
                {
                    if (contentPack.GetFileInfo(x, out ulong fileOffset, out string filePath))
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
            else
                RuntimeContentSystem.LoadContentCatalog(
                     $"{GameAssetManager.GetURL(filePath)}/",
                    null,
                    GameConstantManager.Get(ContentSet));

            progressbar.ClearProgressBar();
        }

#endif*/

        Shared.onActivated += __OnActivated;
        onLogin.AddListener(__OnLogin);
        Login();
    }

    private void __OnActivated()
    {
        Shared.onActivated -= __OnActivated;
        
        //__defaultSceneName = GameConstantManager.Get(DefaultLevelSceneName);

        var analytics = IAnalytics.instance as IAnalyticsEx;
        analytics?.Activate(Shared.channelName, Shared.channelUser);
    }

    private void __OnLogin()
    {
        var assetManager = GameAssetManager.instance;
        assetManager.onConfirmCancel += __OnConfirmCancel;

        assetManager.StartCoroutine(__Init());
    }

    private IEnumerator __Init()
    {
        uint userID = 0;
        string defaultSceneName = null;
        GameSceneActivation activation = null;
        yield return IUserData.instance.QueryUser(
            Shared.channelName, 
            Shared.channelUser,
            (x, y) =>
            {
                (IAnalytics.instance as IAnalyticsEx)?.Login(y);
        
                switch (x)
                {
                    case IUserData.Status.Guide:
                        defaultSceneName = GameConstantManager.Get(DefaultLevelSceneName);

                        activation = new GameSceneActivation();
                
                        userID = y;
                        break;
                    default:
                        defaultSceneName = GameConstantManager.Get(DefaultSceneName);
                        break;
                }

                ILevelData.instance = new GameLevelData(y);
            });

        if (userID != 0)
        {
            var analytics = IAnalytics.instance as IAnalyticsEx;
            analytics?.StartLevel(defaultSceneName);

            yield return IUserData.instance.ApplyLevel(userID, 1, null);

            LevelPlayerShared.effectTargetRecovery = 1024.0f;
        }

        var assetPaths = new GameAssetManager.AssetPath[2];
        string path = GameConstantManager.Get(AssetPath);
        assetPaths[0] = new GameAssetManager.AssetPath(path);

        string scenePath = GameConstantManager.Get(AssetScenePath);
        assetPaths[1] = new GameAssetManager.AssetPath(scenePath, GameLanguage.overrideLanguage);
        yield return GameAssetManager.instance.Init(
            defaultSceneName, 
            path, 
            GameConstantManager.Get(GameConstantManager.KEY_CDN_URL), 
            factory, 
            activation, 
            new IGameAssetUnzipper[] {new AssetUnzipper()}, 
            assetPaths);
    }

    private void __OnConfirmCancel()
    {
        Application.Quit();
    }
}
