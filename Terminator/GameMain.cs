using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Entities.Content;
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

public class GameLevelData : ILevelData
{
    private readonly uint UserID;

    public bool canRecoveryExtra
    {
        get; 
        private set;
    }
    
    public GameLevelData(uint userID, bool hasSweepCard)
    {
        UserID = userID;

        canRecoveryExtra = hasSweepCard;
    }
    
    public IEnumerator SubmitStage(
        int stage, 
        int time, 
        int damagePercentage, 
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
            UserID, 
            //ToStageFlag(flag),
            stage, 
            time, 
            damagePercentage, 
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
                
                int numItems = x.rewards == null ? 0 : x.rewards.Length;
                result.rewards = numItems > 0 ? new ILevelData.Item[numItems] : null;
                for (int i = 0; i < numItems; ++i)
                {
                    ref var source = ref x.rewards[i];
                    ref var destination = ref result.rewards[i];
                    destination.name = source.name;
                    destination.count = source.count;
                }
                
                onComplete(result);
            });
    }
        
    public IEnumerator SubmitLevel(
        //ILevelData.Flag flag, 
        int stage,
        int time, 
        int damagePercentage, 
        int hpPercentage, 
        int killCount, 
        int killBossCount, 
        int gold,
        Action<bool> onComplete)
    {
        return IUserData.instance.SubmitLevel(
            UserID, 
            //ToStageFlag(flag),
            stage, 
            time, 
            damagePercentage, 
            hpPercentage, 
            killCount, 
            killBossCount, 
            gold, 
            onComplete);
    }

    public IEnumerator Buy(Action<bool> onComplete)
    {
        if (!canRecoveryExtra)
        {
            var purchaseData = IPurchaseData.instance;
            if (purchaseData != null)
                return purchaseData.Buy(UserID, PurchaseType.SweepCard, 0, x =>
                {
                    canRecoveryExtra = x;

                    //LevelPlayerShared<LocalPlayer>.property.effectTargetRecoveryTimes = 2;

                    onComplete(x);
                });
            
        }

        onComplete(true);
        
        return null;
    }
    
    public IEnumerator BuyToSkip(Action<bool> onComplete)
    {
        var purchaseData = IPurchaseData.instance;
        if (purchaseData != null)
            return purchaseData.Buy(UserID, PurchaseType.AdvertisingFreeCard, 0, onComplete);

        onComplete(true);
        
        return null;
    }
    
    public IEnumerator Broadcast(Action<bool> onComplete)
    {
        var advertisementData = IAdvertisementData.instance;
        if(advertisementData != null)
            return advertisementData.Broadcast(UserID, AdvertisementType.Recovery, null, onComplete);
        
        onComplete(true);
        
        return null;
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
        private AssetManager.Writer __writer;

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
            path = AssetFileUtility.Combine(AssetFileUtility.persistentDataPath, path);
            return AssetFileUtility.Combine(path, AssetFileUtility.GetFileName(path));
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
            
            if(__writer.isCreated)
                __writer.Dispose();
        }

        public bool MoveNext()
        {
            float maximumDeltaTime = Time.maximumDeltaTime * 0.5f;
            long ticks = DateTime.Now.Ticks;
            while (index < __assetNames.Length)
            {
                if(__Load(__assetNames[index]))
                    ++index;
                
                if ((DateTime.Now.Ticks - ticks) * 1.0 / TimeSpan.TicksPerSecond > maximumDeltaTime)
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

                path = AssetFileUtility.Combine(__path, name);
            }
            else
            {
                folder = name.Remove(separatorIndex);

                filename = AssetFileUtility.GetFileNameWithoutExtension(name.Substring(separatorIndex + 1));

                separatorIndex = folder.LastIndexOf('/');
                if (separatorIndex != -1)
                    folder = folder.Substring(separatorIndex + 1);

                path = AssetFileUtility.Combine(__path, folder, filename);

                if (__folders == null)
                    __folders = new HashSet<string>();

                if (__folders.Add(folder))
                    AssetManager.LoadFrom(AssetFileUtility.Combine(folder, folder));
            }

            /*string assetName = string.IsNullOrEmpty(folder) ? filename : $"{folder}/{filename}";
            assetName = assetName.ToLower();
            //TODO: Check MD5
            if (AssetManager.Get(assetName, out var asset))
                return true;*/

            var text = __assetBundle.LoadAsset(filename) as TextAsset;
            if (text == null)
                return false;

            var bytes = text.GetData<byte>();
            //DestroyImmediate(text, true);

            if (__writer.isCreated && __writer.Folder != folder)
            {
                __writer.Dispose();

                __writer = default;
            }

            if (!__writer.isCreated)
                __writer = new AssetManager.Writer(folder, AssetManager);

            try
            {
                string assetName = string.IsNullOrEmpty(folder) ? filename : $"{folder}/{filename}";
                assetName = assetName.ToLower();

                AssetManager.AssetData assetData;
                using (var md5 = new MD5CryptoServiceProvider())
                    using (var stream = bytes.ToStream())
                    assetData.info.md5 = md5.ComputeHash(stream);

                if (!AssetManager.Get(assetName, out var asset) ||
                    !asset.data.info.md5.AsSpan().SequenceEqual(assetData.info.md5.AsSpan()))
                {
                    AssetManager.CreateDirectory(path);

                    using (var file = AssetFileUtility.Open(path, FileMode.Create, FileAccess.Write))
                        file.Write(bytes.AsReadOnlySpan());

                    assetData.type = AssetManager.AssetType.Uncompressed;
                    assetData.info.version = 0;
                    assetData.info.size = (uint)bytes.Length;
                    assetData.info.fileName = filename;
                    assetData.pack = AssetManager.AssetPack.Default;
                    assetData.dependencies = null;

                    __writer.Write(assetName, assetData);
                }

                //DestroyImmediate(text, true);
            }
            catch (Exception e)
            {
                Debug.LogException(e.InnerException ?? e);

                return false;
            }

            return true;
        }
    }
    
    private struct AssetUnzipper : IGameAssetUnzipper
    {
        public string filename => GameConstantManager.Get(ContentPackPath);
        
        public static string ToAssetPath(string path)
        {
            return AssetIterator.ToAssetPath(ref path);
        }

        public IEnumerator Execute(AssetBundle assetBundle, AssetManager.DownloadHandler downloadHandler)
        {
            var folders = new HashSet<string>();
            
            if(assetBundle == null)
                sceneArchiveAssetManager = new AssetManager(ToAssetPath(filename));
            else
            {
                using (var assetIterator = new AssetIterator(filename, assetBundle, folders))
                {
                    int index, count;
                    while (assetIterator.MoveNext())
                    {
                        yield return Resources.UnloadUnusedAssets();
                        
                        GC.Collect();

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

                    sceneArchiveAssetManager = assetIterator.AssetManager;
                }
            }

#if ENABLE_CONTENT_DELIVERY
            print("Start Initialize ContentDelivery");

            //string cdnURL = GameConstantManager.Get(GameConstantManager.KEY_CDN_URL);
            //if (string.IsNullOrEmpty(cdnURL))
            {
                ContentDeliveryGlobalState.Initialize(
                    null,
                    null,
                    null,
                    null);

                //var paths = new Dictionary<string, string>();
                string directory = Path.Combine(Application.persistentDataPath, filename);
                Func<string, string> remapFunc = x =>
                {
                    int separatorIndex = x.LastIndexOf('/');
                    string folder = separatorIndex == -1 ? string.Empty : x.Remove(separatorIndex);

                    if (!string.IsNullOrEmpty(folder) && folders.Add(folder))
                    {
                        string path = AssetFileUtility.Combine(folder, folder).ToLower();
                        Debug.Log($"Asset manager load from {path}");
                        
                        sceneArchiveAssetManager.LoadFrom(path);
                    }

                    string name = x.ToLower();
                    
                    return Path.Combine(directory, name);
                    /*if (!sceneArchiveAssetManager.GetAssetPath(name, out _, out ulong fileOffset, out string filePath))
                        Debug.LogError($"GetFileInfo {x} failed");

                    UnityEngine.Assertions.Assert.AreEqual(0, fileOffset);

                    if (string.IsNullOrEmpty(filePath))
                        return filePath;
                    
                    if (!paths.TryGetValue(filePath, out var result))
                    {
                        result = Path.Combine(directory, name);
                        paths[filePath] = result;
                    }

                    return result;*/
                    //return filePath;
                };

                ContentDeliveryGlobalState.PathRemapFunc = remapFunc;
                
                #if DEBUG
                ContentDeliveryGlobalState.LogFunc = Debug.Log;
                #endif
                
                var catalogPath = remapFunc(RuntimeContentManager.RelativeCatalogPath);
                if (sceneArchiveAssetManager.GetAssetPath(RuntimeContentManager.RelativeCatalogPath.ToLower(), out _,
                        out _, out string filePath))
                {
                    var folder = Path.GetDirectoryName(catalogPath);
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                        Directory.CreateDirectory(folder);

                    File.WriteAllBytes(catalogPath, AssetFileUtility.ReadAllBytes(filePath));
                }

                RuntimeContentManager.LoadLocalCatalogData(catalogPath,
                    RuntimeContentManager.DefaultContentFileNameFunc,
                    p => remapFunc(RuntimeContentManager.DefaultArchivePathFunc(p)));
            }
            /*else
                RuntimeContentSystem.LoadContentCatalog(
                    $"{cdnURL}/{Filename}",
                    Filename,
                    GameConstantManager.Get(ContentSet));*/
            
            print("End Initialize ContentDelivery");
#endif
        }
    }

    [Serializable]
    internal struct Chapter
    {
        [Serializable]
        public struct Stage
        {
            public string name;
            public string bossTitle;
            public string bossDescription;
            //public Vector3 playerOffset;
            public SpawnerAttribute.Scale spawnerAttribute;
        }
        
        public string name;
        
        public Stage[] stages;
    }

    public static readonly string LanguagePackageResourcePath = "LanguagePackageResourcePath";
    public static readonly string AssetPath = "AssetPath";
    public static readonly string AssetScenePath = "AssetScenePath";
    public static readonly string DefaultSceneName = "DefaultSceneName";
    public static readonly string DefaultLevelSceneName = "DefaultLevelSceneName";
    public static readonly string ContentSet = "ContentSet";
    public static readonly string ContentPackPath = "ContentPackPath";
    public static readonly string NoticePath = "NoticePath";
    
    public const string NAME_SPACE_USER_GROUP = "GameMainUserGroup";

    //public const string NAME_SPACE_SCENE = "GameMainScene";
    //public const string NAME_SPACE_LEVEL = "GameMainLevel";
    
    public const string NAME_SPACE_PLAYER_PREF_VERSION = "PlayerPrefVersion";

    public static int userType
    {
        get;

        private set;
    }

    public event Func<IEnumerator> onStart;

    [SerializeField] 
    internal Chapter[] _chapters;

    [SerializeField] 
    internal int _playerPrefVersion = 1;

    [SerializeField] 
    internal Vector2Int _userGroupRange = new Vector2Int(0, 1);

    private Coroutine __coroutine;
    
    private static bool __isActivated;

    public static uint userID
    {
        get;

        private set;
    }

    public static event Action<uint, bool> onUserLogin;

    public static AssetManager sceneArchiveAssetManager
    {
        get;

        private set;
    }

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

        int userGroup = PlayerPrefs.GetInt(NAME_SPACE_USER_GROUP, -1);
        if (userGroup == -1)
        {
            userGroup = UnityEngine.Random.Range(_userGroupRange.x, _userGroupRange.y + 1);
            
            PlayerPrefs.SetInt(NAME_SPACE_USER_GROUP, userGroup);
        }

        LevelShared.userGroup = userGroup;

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
                userType = x.type;
                
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
                    
                    if (chapter.stages != null)
                    {
                        LevelShared.Stage stage;
                        foreach (var chapterStage in chapter.stages)
                        {
                            stage.name = chapterStage.name;
                            stage.bossTitle = chapterStage.bossTitle;
                            stage.bossDescription = chapterStage.bossDescription;
                            //stage.playerOffset = chapterStage.playerOffset;
                            stage.spawnerAttributeScale = chapterStage.spawnerAttribute;
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

                ILevelData.instance = new GameLevelData(y, false);
                
                Login(y);
            });

        if (userID != 0)
        {
            var analytics = IAnalytics.instance as IAnalyticsEx;
            analytics?.StartLevel(defaultSceneName);

            if(stage > 0)
                yield return IUserData.instance.ApplyStage(userID, levelID, stage, __ApplyStage);
            else
                yield return IUserData.instance.ApplyLevel(userID, levelID, stage, __ApplyLevel);

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
            true,//activation != null, 
            defaultSceneName, 
            path, 
            GameConstantManager.Get(GameConstantManager.KEY_CDN_URL), 
            factory, 
            __OnAllAssetsLoaded(), 
            activation, 
            new IGameAssetUnzipper[] {new AssetUnzipper()}, 
            assetPaths);

        __coroutine = null;
    }

    private void __ApplyLevel(IUserData.LevelProperty property)
    {
        LevelShared.stage = property.stage;

        property.value.Apply<LocalPlayer>(0, 0);
    }

    private void __ApplyStage(IUserData.StageProperty property)
    {
        LevelShared.exp = property.cache.exp;
        LevelShared.expMax = property.cache.expMax;
        
        LevelShared.stage = property.stage;
        
        property.value.Apply<LocalPlayer>(0, property.cache.rage);
    }

    private void __OnConfirmCancel()
    {
        Application.Quit();
    }

    private IEnumerator __OnAllAssetsLoaded()
    {
        yield return null;

        var assetManager = GameAssetManager.instance.dataManager;

        string noticePath = GameConstantManager.Get(NoticePath);
        if (!string.IsNullOrEmpty(noticePath))
        {
            var assetBundleLoader = new AssetBundleLoader<GameObject>(noticePath.ToLower(), noticePath, assetManager);

            yield return assetBundleLoader;

            var notice = assetBundleLoader.value;
            if (notice != null)
            {
                notice = Instantiate(notice);
                DontDestroyOnLoad(notice);
                
                var manager = notice.GetComponentInChildren<GameManager>(true);
                if (manager != null)
                {
                    manager.QueryNotices(false);

                    while (manager.isLoading || manager.isNoticeShow)
                        yield return null;
                }
            }
        }

        InstanceManager.assetManager = assetManager;
    }
}
