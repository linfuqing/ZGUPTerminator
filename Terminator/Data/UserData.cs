using System;
using System.Collections;
using UnityEngine;

public enum UserRewardType
{
    PurchasePoolKey, 
    Card, 
    Role, 
    Accessory, 
    Item, 
    Diamond, 
    Gold, 
    Energy
}

public enum UserAttributeType
{
    Hp, 
    Attack, 
    Defence
}

[Serializable]
public struct UserAttributeData
{
    public UserAttributeType type;
    public UserVariableType variableType;
    public float value;
}

[Serializable]
public struct UserRewardData
{
    public string name;
    
    public UserRewardType type;

    public int count;
}

public struct User
{
    public uint id;
    public int gold;
    //public int level;
}

public struct UserTip
{
    public int value;
    public int max;
    public uint unitTime;
    public long tick;
}

public struct UserEnergy
{
    public int value;
    public int max;
    public uint unitTime;
    public long tick;
}

public partial struct UserLevel
{
    public string name;
    public uint id;
    public int energy;
}

public struct UserTalent
{
    [Flags]
    public enum Flag
    {
        Collected = 0x01
    }

    public string name;
    public uint id;
    public Flag flag;
    public UserAttributeData attribute;
    public int gold;
}

public partial interface IUserData : IGameUserData
{
    [Flags]
    public enum StageFlag
    {
        Normal = 0x01, 
        Once = 0x02, 
        NoDamage = 0x03
    }

    public static IUserData instance;

    IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<uint> onComplete);
    
    IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<User, UserEnergy> onComplete);
    
    IEnumerator QueryLevels(
        uint userID, 
        Action<Memory<UserLevel>> onComplete);
    
    IEnumerator ApplyLevel(
        uint userID,
        uint levelID, 
        Action<UserPropertyData> onComplete);

    IEnumerator SubmitLevel(
        uint userID,
        StageFlag flag,
        int stage, 
        int gold, 
        Action<bool> onComplete);

    IEnumerator CollectLevel(
        uint userID,
        Action<Memory<UserRewardData>> onComplete);
}

public partial class UserData : MonoBehaviour, IUserData
{
    public struct LevelCache
    {
        public string name;
        public uint id;
        public int stage;
        public int gold;

        public LevelCache(string value)
        {
            var values = value.Split(SEPARATOR);
            name = values[0];
            id = uint.Parse(values[1]);
            stage = int.Parse(values[2]);
            gold = int.Parse(values[3]);
        }

        public override string ToString()
        {
            return $"{name}{SEPARATOR}{id}{SEPARATOR}{stage}{SEPARATOR}{gold}";
        }
    }

    private const string NAME_SPACE_USER_ID = "UserLevelID";
    
    private const string NAME_SPACE_USER_LEVEL = "UserLevel";
    //private const string NAME_SPACE_USER_STAGE_FLAG = "UserStageFlag";

    public const char SEPARATOR = ',';
    
    public static uint id
    {
        get
        {
            int id = PlayerPrefs.GetInt(NAME_SPACE_USER_ID);
            if (id == 0)
            {
                id = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                
                PlayerPrefs.SetInt(NAME_SPACE_USER_ID, id);
            }

            return (uint)id;
        }
    }

    public static int level
    {
        get => PlayerPrefs.GetInt(NAME_SPACE_USER_LEVEL);
        
        set => PlayerPrefs.SetInt(NAME_SPACE_USER_LEVEL, value);
    }
    
    private const string NAME_SPACE_USER_STAGE_CACHE = "UserStageCache";
    private const string NAME_SPACE_USER_LEVEL_CACHE = "UserLevelCache";
    
    public static LevelCache? levelCache
    {
        get
        {
            var value = PlayerPrefs.GetString(NAME_SPACE_USER_LEVEL_CACHE);
            if (string.IsNullOrEmpty(value))
                return null;

            return new LevelCache(value);
        }

        set
        {
            if (value == null)
                PlayerPrefs.DeleteKey(NAME_SPACE_USER_LEVEL_CACHE);
            else
                PlayerPrefs.SetString(NAME_SPACE_USER_LEVEL_CACHE, value.Value.ToString());
        }
    }

    public static string GetStageNameSpace(string nameSpace, string levelName, int stage)
    {
        return $"{nameSpace}{levelName}{SEPARATOR}{stage}";
    }

    public static IUserData.StageCache GetStageCache(string levelName, int stage)
    {
        return new IUserData.StageCache(
            PlayerPrefs.GetString(GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE, levelName, stage)));
    }

    public IEnumerator QueryUser(
        string channelName,
        string channelUser,
        Action<uint> onComplete)
    {
        yield return null;

        LevelCache levelCache;
        levelCache.name = string.Empty;
        levelCache.id = 1;
        levelCache.gold = 0;
        levelCache.stage = 0;

        UserData.levelCache = levelCache;

        onComplete(id);
    }
    
    public IEnumerator SubmitLevel(
        uint userID,
        IUserData.StageFlag flag,
        int stage,
        int gold,
        Action<bool> onComplete)
    {
        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            onComplete(false);
            
            yield break;
        }

        var temp = levelCache.Value;

        __SubmitStageFlag(flag, temp.name, temp.stage, stage);

        temp.stage = stage;
        temp.gold = gold;
        UserData.levelCache = temp;
        
        onComplete(true);
    }

    public IEnumerator Activate(
        string code,
        string channel,
        string channelUser,
        Action<IGameUserData.UserStatus> onComplete)
    {
        yield return null;

        onComplete(PlayerPrefs.GetInt(NAME_SPACE_USER_ID) == 0
            ? IGameUserData.UserStatus.New
            : IGameUserData.UserStatus.Ok);
    }

    public IEnumerator Check(
        string channel,
        string channelUser,
        Action<IGameUserData.UserStatus> onComplete)
    {
        yield return null;

        onComplete(PlayerPrefs.GetInt(NAME_SPACE_USER_ID) == 0
            ? IGameUserData.UserStatus.New
            : IGameUserData.UserStatus.Ok);
    }

    public IEnumerator Bind(
        int userID,
        string channelUser,
        string channel,
        Action<bool?> onComplete)
    {
        yield return null;
    }

    public IEnumerator Unbind(
        string channel,
        string channelUser,
        Action<bool?> onComplete)
    {
        yield return null;
    }

    private const string NAME_SPACE_USER_STAGE_FLAG = "UserStageFlag";
    private const string NAME_SPACE_USER_STAGE_CACHE_TIMES = "UserStageCacheTimes";
    
    public static IUserData.StageFlag GetStageFlag(string levelName, int stage)
    {
        return (IUserData.StageFlag)PlayerPrefs.GetInt(GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, stage));
    }

    public static void ApplyStageFlag(string levelName, int stage)
    {
        string key = GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE_TIMES, levelName, stage);
        int times = PlayerPrefs.GetInt(key);
        PlayerPrefs.SetInt(key, ++times);
    }

    public static void SubmitStageFlag(string levelName, int stage)
    {
        string stageCacheTimesKey, stageFlagKey;
        for (int i = 0; i < stage; ++i)
        {
            stageCacheTimesKey = GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE_TIMES, levelName, i);
            if (PlayerPrefs.GetInt(stageCacheTimesKey) < 2)
            {
                stageFlagKey = GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, i);

                PlayerPrefs.SetInt(stageFlagKey,
                    PlayerPrefs.GetInt(stageFlagKey) | (int)IUserData.StageFlag.Once);
            }

            //PlayerPrefs.DeleteKey(stageCacheTimesKey);
            //PlayerPrefs.DeleteKey(GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE, levelName, i));
        }
    }

    private static void __SubmitStageFlag(IUserData.StageFlag value, string levelName, int fromStage, int toStage)
    {
        value |= IUserData.StageFlag.Normal;
        
        string key;
        for (int i = fromStage; i < toStage; ++i)
        {
            key = GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, i);
            PlayerPrefs.SetInt(key, (int)value | PlayerPrefs.GetInt(key));
        }
    }
    
    void Awake()
    {
        IUserData.instance = this;
    }
}
