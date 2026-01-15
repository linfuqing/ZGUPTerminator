using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Entities.Content;
using UnityEditor;
using UnityEngine;
using ZG;

public class GameRewardData : IRewardData
{
    private uint __userID;

    public GameRewardData(uint userID)
    {
        __userID = userID;
    }

    public IEnumerator ApplyReward(
        string poolName, 
        Action<IRewardData.Rewards> onComplete)
    {
        return IUserData.instance.ApplyReward(
            __userID,
            poolName,
            x =>
            {
                if (x.IsEmpty)
                {
                    onComplete(default);
                        
                    return;
                }
                    
                IRewardData.Rewards result;
                result.poolName = poolName;

                int numValues = x.Length;
                result.values = new IRewardData.Reward[numValues];
                for (int i = 0; i < numValues; ++i)
                {
                    ref var source = ref x.Span[i];
                    ref var destination = ref result.values[i];
                        
                    destination.name = source.name;
                    destination.count = UserRewardType.Accessory == source.type ? 1 : source.count;
                }

                onComplete(result);
            });
    }
}

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
        int stage, 
        int time, 
        int hpPercentage, 
        int killCount, 
        int killBossCount, 
        int gold, 
        int rage, 
        int exp, 
        int expMax, 
        string[] skills,
        ILevelData.Item[] inputs,
        Action<ILevelData.StageResult> onComplete)
    {
        int numItems = inputs == null ? 0 : inputs.Length;
        IUserData.Item[] outputs = numItems > 0 ? new IUserData.Item[numItems] : null;
        for (int i = 0; i < numItems; ++i)
        {
            ref var input = ref inputs[i];
            ref var output = ref outputs[i];
            output.name = input.name;
            output.count = input.count;
        }
        
        return IUserData.instance.SubmitStage(
            __userID, 
            //ToStageFlag(flag),
            stage, 
            time, 
            hpPercentage, 
            killCount, 
            killBossCount, 
            gold, 
            rage, 
            exp, 
            expMax,
            skills,
            outputs, 
            x =>
            {
                ILevelData.StageResult result;
                result.rankFlag = x.flag;
                result.energyStage = x.nextStageEnergy;
                result.energyMax = x.totalEnergy;
                
                onComplete(result);
            });
    }
        
    public IEnumerator SubmitLevel(
        //ILevelData.Flag flag, 
        int stage,
        int time, 
        int hpPercentage, 
        int killCount, 
        int killBossCount, 
        int gold,
        Action<bool> onComplete)
    {
        return IUserData.instance.SubmitLevel(
            __userID, 
            //ToStageFlag(flag),
            stage, 
            time, 
            hpPercentage, 
            killCount, 
            killBossCount, 
            gold, 
            onComplete);
    }

    /*private IUserData.StageFlag ToStageFlag(ILevelData.Flag flag)
    {
        IUserData.StageFlag stageFlag = 0;
        if((flag & ILevelData.Flag.HasBeenDamaged) != ILevelData.Flag.HasBeenDamaged)
            stageFlag |= IUserData.StageFlag.NoDamage;

        return stageFlag;
    }*/
}

public class GameMain : GameUser
{
    private class AssetIterator : IDisposable
    {
        private string __path;
        private string[] __assetNames;
        private HashSet<string> __folders;
        private AssetBundle __assetBundle;
        
        public readonly AssetManager AssetManager;

        public int index
        {
            get;

            private set;
        }

        public int count => __assetNames.Length;

        public string assetName => __assetNames[Mathf.Clamp(index, 0, count - 1)];

        public static string ToAssetPath(ref string path)
        {
            path = Path.Combine(Application.persistentDataPath, path);
            return Path.Combine(path, Path.GetFileName(path));
        }
        
        public static string ToAssetPath(string path)
        {
            return ToAssetPath(ref path);
        }
        
        public AssetIterator(string path, AssetBundle assetBundle, HashSet<string> folders)
        {
            __path = path;
            string assetPath = ToAssetPath(ref __path);
            
            AssetManager.CreateDirectory(assetPath);

            AssetManager = new AssetManager(assetPath);
            
            __assetBundle = assetBundle;
            
            __assetNames = assetBundle.GetAllAssetNames();
            
            __folders = folders;

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
            //assets/
            name = name.Substring(7);
            int separatorIndex = name.LastIndexOf('/');
            string folder, filename, path;
            if (separatorIndex == -1)
            {
                folder = string.Empty;
                
                filename = name;
                
                path = Path.Combine(__path, name);
            }
            else
            {
                folder = name.Remove(separatorIndex);

                filename = Path.GetFileNameWithoutExtension(name.Substring(separatorIndex + 1));
                
                separatorIndex = folder.LastIndexOf('/');
                if (separatorIndex != -1)
                    folder = folder.Substring(separatorIndex + 1);

                path = Path.Combine(__path, folder, filename);
                
                if (__folders == null)
                    __folders = new HashSet<string>();
                
                if(__folders.Add(folder))
                    AssetManager.LoadFrom(Path.Combine(folder, folder));
            }

            string assetName = string.IsNullOrEmpty(folder) ? filename : $"{folder}/{filename}";
            assetName = assetName.ToLower();
            if (AssetManager.Get(assetName, out _))
                return false;

            var text = __assetBundle.LoadAsset(filename) as TextAsset;
            var bytes = text == null ? null : text.bytes;
            if (bytes == null)
                return false;

            DestroyImmediate(text, true);

            AssetManager.CreateDirectory(path);
            
            File.WriteAllBytes(path, bytes);

            AssetManager.AssetData assetData;
            assetData.type = AssetManager.AssetType.Uncompressed;
            assetData.info.version = 0;
            assetData.info.size = (uint)bytes.LongLength;
            assetData.info.fileName = filename;
            using (var md5 = new MD5CryptoServiceProvider())
                assetData.info.md5 = md5.ComputeHash(bytes);

            assetData.pack = AssetManager.AssetPack.Default;
            assetData.dependencies = null;

            using (var writer = new AssetManager.Writer(folder, AssetManager))
                writer.Write(assetName, assetData);

            return true;
        }
    }
    
    private struct AssetUnzipper : IGameAssetUnzipper
    {
        public static readonly string Filename = GameConstantManager.Get(ContentPackPath);
        
        public string filename => Filename;

        public IEnumerator Execute(AssetBundle assetBundle, AssetManager.DownloadHandler downloadHandler)
        {
            var folders = new HashSet<string>();
            
            AssetManager assetManager;
            if(assetBundle == null)
                assetManager = new AssetManager(AssetIterator.ToAssetPath(Filename));
            else
            {
                using (var assetIterator = new AssetIterator(Filename, assetBundle, folders))
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
                                1.0f,
                                0,
                                (ulong)index,
                                (ulong)count,
                                index,
                                count);
                        }
                    }

                    assetManager = assetIterator.AssetManager;
                }
            }
            
#if ENABLE_CONTENT_DELIVERY
            //string cdnURL = GameConstantManager.Get(GameConstantManager.KEY_CDN_URL);
            //if (string.IsNullOrEmpty(cdnURL))
            {
                ContentDeliveryGlobalState.Initialize(
                    null,
                    null,
                    null,
                    null);

                Func<string, string> remapFunc = x =>
                {
                    int separatorIndex = x.LastIndexOf('/');
                    string folder = separatorIndex == -1 ? string.Empty : x.Remove(separatorIndex);

                    if (!string.IsNullOrEmpty(folder) && folders.Add(folder))
                    {
                        string path = Path.Combine(folder, folder).ToLower();
                        Debug.Log($"Asset manager load from {path}");
                        
                        assetManager.LoadFrom(path);
                    }

                    if (!assetManager.GetAssetPath(x.ToLower(), out _, out ulong fileOffset, out string filePath))
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
            /*else
                RuntimeContentSystem.LoadContentCatalog(
                    $"{cdnURL}/{Filename}",
                    Filename,
                    GameConstantManager.Get(ContentSet));*/
#endif
        }
    }

    [Serializable]
    internal struct Chapter
    {
        public string name;
        
        public SpawnerAttribute.Scale[] spawnerAttributes;
    }

    public event Func<IEnumerator> onStart;

    public static readonly string LanguagePackageResourcePath = "LanguagePackageResourcePath";
    public static readonly string AssetPath = "AssetPath";
    public static readonly string AssetScenePath = "AssetScenePath";
    public static readonly string DefaultSceneName = "DefaultSceneName";
    public static readonly string DefaultLevelSceneName = "DefaultLevelSceneName";
    public static readonly string ContentSet = "ContentSet";
    public static readonly string ContentPackPath = "ContentPackPath";

    //public const string NAME_SPACE_SCENE = "GameMainScene";
    //public const string NAME_SPACE_LEVEL = "GameMainLevel";
    
    public const string NAME_SPACE_PLAYER_PREF_VERSION = "PlayerPrefVersion";

    [SerializeField] 
    internal Chapter[] _chapters;

    [SerializeField] 
    internal SpawnerAttribute.Scale[] _defaultSpawnerAttributes;
    
    [SerializeField] 
    internal int _playerPrefVersion = 1;

    private Coroutine __coroutine;
    
    private static bool __isActivated;

    public static uint userID
    {
        get;

        private set;
    }

    public static event Action<uint, bool> onUserLogin;

    public IAssetBundleFactory factory
    {
        get;

        set;
    }

    public override IGameUserData userData => IUserData.instance;
    
    /*public static int GetSceneTimes(string sceneName)
    {
        return PlayerPrefs.GetInt(__GetSceneNameSpace(sceneName));
    }

    public static void IncrementSceneTimes(string sceneName)
    {
        PlayerPrefs.SetInt(__GetSceneNameSpace(sceneName), GetSceneTimes(sceneName) + 1);
    }

    public static int GetLevelTimes(string levelName)
    {
        return PlayerPrefs.GetInt(__GetLevelNameSpace(levelName));
    }

    public static void IncrementLevelTimes(string levelName)
    {
        PlayerPrefs.SetInt(__GetLevelNameSpace(levelName), GetLevelTimes(levelName) + 1);
    }

    private static string __GetLevelNameSpace(string levelName)
    {
        return NAME_SPACE_LEVEL + levelName;
    }

    private static string __GetSceneNameSpace(string sceneName)
    {
        return NAME_SPACE_SCENE + sceneName;
    }*/
    public static void Login(uint userID)
    {
        if (userID == GameMain.userID)
            return;
        
        (IAnalytics.instance as IAnalyticsEx)?.Login(userID);

        if(onUserLogin != null)
            onUserLogin(userID, __isActivated);

        __isActivated = false;
        
        GameMain.userID = userID;
    }

    IEnumerator Start()
    {
        if (PlayerPrefs.GetInt(NAME_SPACE_PLAYER_PREF_VERSION) < _playerPrefVersion)
        {
            PlayerPrefs.DeleteAll();
            
            PlayerPrefs.SetInt(NAME_SPACE_PLAYER_PREF_VERSION, _playerPrefVersion);
        }
        
        //PlayerSettings.WebGL.threadsSupport
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        //UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 2;
        
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

        var manager = GameManager.instance;
        if (manager != null)
        {
            manager.QueryNotices(false);

            while (manager.isLoading || manager.isNoticeShow)
                yield return null;
        }
        
        (IAnalytics.instance as IAnalyticsEx)?.Init();
        
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

        var assetManager = GameAssetManager.instance;
        if (assetManager != null)
            assetManager.onConfirmCancel += __OnConfirmCancel;

        Shared.onActivated += __OnActivated;
        onLogin.AddListener(__OnLogin);
        Login();
    }

    private void __OnActivated()
    {
        Shared.onActivated -= __OnActivated;

        __isActivated = true;
        
        //__defaultSceneName = GameConstantManager.Get(DefaultLevelSceneName);

        var analytics = IAnalytics.instance as IAnalyticsEx;
        analytics?.Activate(Shared.channelName, Shared.channelUser);
    }

    private void __OnLogin()
    {
        //var assetManager = GameAssetManager.instance;
        //assetManager.onConfirmCancel += __OnConfirmCancel;

        __coroutine = GameAssetManager.instance?.StartCoroutine(__Init(__coroutine));
    }

    private IEnumerator __Init(Coroutine coroutine)
    {
        if(coroutine != null)
            yield return coroutine;
        
        uint userID = 0, levelID = 0;
        int stage = -1;
        string defaultSceneName = null;
        GameSceneActivation activation = null;
        yield return IUserData.instance.QueryUser(
            Shared.channelName, 
            Shared.channelUser,
            (x, y) =>
            {
                (IAnalytics.instance as IAnalyticsEx)?.Login(y);

                if (x.levelID == 0 || x.chapter >= _chapters.Length)
                    defaultSceneName = GameConstantManager.Get(DefaultSceneName);
                else
                {
                    levelID = x.levelID;
                    stage = x.stage;
                    
                    var chapter = _chapters[x.chapter];

                    defaultSceneName = chapter.name;
                    
                    LevelShared.stages.Clear();
                    
                    if (chapter.spawnerAttributes != null)
                    {
                        LevelShared.Stage stage;
                        foreach (var spawnerAttribute in chapter.spawnerAttributes)
                        {
                            stage.spawnerAttributeScale = spawnerAttribute;
                            stage.quests = default;
                            LevelShared.stages.Add(stage);
                        }
                    }
                    
                    activation = new GameSceneActivation();
                    
                    userID = y;
                }
        
                /*switch (x)
                {
                    case IUserData.Status.Guide:
                        if (__isActivated)
                        {
                            defaultSceneName = GameConstantManager.Get(DefaultLevelSceneName);

                            //IncrementSceneTimes(defaultSceneName);

                            activation = new GameSceneActivation();

                            userID = y;
                        }
                        else
                            defaultSceneName = GameConstantManager.Get(DefaultSceneName);

                        break;
                    default:
                        defaultSceneName = GameConstantManager.Get(DefaultSceneName);
                        break;
                }*/

                ILevelData.instance = new GameLevelData(y);
                
                Login(y);
            });

        if (userID != 0)
        {
            var analytics = IAnalytics.instance as IAnalyticsEx;
            analytics?.StartLevel(defaultSceneName);

            if(stage > 0)
                yield return IUserData.instance.ApplyStage(userID, levelID, stage, __ApplyStage);
            else
                yield return IUserData.instance.ApplyLevel(userID, levelID, stage, null);

            IRewardData.instance = new GameRewardData(userID);
            
            /*LevelShared.stages.Clear();

            if (_defaultSpawnerAttributes != null)
            {
                LevelShared.Stage stage;
                foreach (var defaultSpawnerAttribute in _defaultSpawnerAttributes)
                {
                    stage.spawnerAttributeScale = defaultSpawnerAttribute;
                    stage.quests = default;
                    LevelShared.stages.Add(stage);
                }
            }*/
        }

        var assetPaths = new GameAssetManager.AssetPath[2];
        string path = GameConstantManager.Get(AssetPath);
        assetPaths[0] = new GameAssetManager.AssetPath(path);

        string scenePath = GameConstantManager.Get(AssetScenePath);
        assetPaths[1] = new GameAssetManager.AssetPath(scenePath, GameLanguage.overrideLanguage);
        yield return GameAssetManager.instance.Init(
            activation != null, 
            defaultSceneName, 
            path, 
            GameConstantManager.Get(GameConstantManager.KEY_CDN_URL), 
            factory, 
            activation, 
            new IGameAssetUnzipper[] {new AssetUnzipper()}, 
            assetPaths);

        __coroutine = null;
    }

    private void __ApplyStage(IUserData.StageProperty property)
    {
        LevelPlayerShared.effectRage = property.cache.rage;

        LevelShared.exp = property.cache.exp;
        LevelShared.expMax = property.cache.expMax;
        
        LevelShared.stage = property.stage;
    }

    private void __OnConfirmCancel()
    {
        Application.Quit();
    }
}
