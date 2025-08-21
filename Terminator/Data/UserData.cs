using System;
using System.Collections;
using UnityEngine;

public enum UserRewardType
{
    PurchasePoolKey, 
    CardsCapacity, 
    Card, 
    Role, 
    Accessory, 
    Item, 
    Diamond, 
    Gold, 
    Energy, 
    EnergyMax, 
    ActiveDay, 
    ActiveWeek
}

public enum UserSkillType
{
    Individual, 
    Group
}

public enum UserAttributeType
{
    None, 
    Hp, 
    Attack, 
    Defence, 
    Recovery, 
}

[Serializable]
public struct UserAttributeData
{
    public UserAttributeType type;
    public float value;
}

[Serializable]
public struct UserRewardData
{
    public string name;
    
    public UserRewardType type;

    public int count;

    public UserRewardData(string text)
    {
        var parameters = text.Split('*');

        name = parameters[0];
        type = (UserRewardType)int.Parse(parameters[1]);
        count = int.Parse(parameters[2]);
    }
}

public struct UserReward
{
    public string name;

    public uint id;
    
    public UserRewardType type;

    public int count;
}

public struct User
{
    public uint id;
    public int gold;
    //public int level;
}

public struct UserEnergy
{
    public int value;
    public int max;
    public uint unitTime;
    public long tick;

    public int current =>
        Mathf.Min(value + (int)((DateTime.UtcNow.Ticks - tick) / (TimeSpan.TicksPerMillisecond * unitTime)));
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
    public int gold;
    public float skillGroupDamage;
    public UserAttributeData attribute;
}

public partial interface IUserData : IGameUserData
{
    public enum Status
    {
        Normal, 
        Guide
    }
    
    [Flags]
    public enum StageFlag
    {
        Normal = 0x01, 
        Once = 0x02 | Normal, 
        NoDamage = 0x04 | Normal
    }

    public struct Levels
    {
        [Flags]
        public enum Flag
        {
            UnlockFirst = 0x01
        }

        public Flag flag;
        
        public UserLevel[] levels;
    }

    public struct Skill
    {
        public UserSkillType type;
        public string name;
        public float damage;
    }

    public struct Property
    {
        public string name;
        public int spawnerLayerMask;
        public Skill[] skills;
        public UserAttributeData[] attributes;
    }

    public struct LevelProperty
    {
        public int stage;
        public Property value;
        public SpawnerAttribute.Scale[] spawnerAttributes;
    }

    public static IUserData instance;

    IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<Status, uint> onComplete);
    
    IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<User, UserEnergy> onComplete);
    
    IEnumerator QueryLevels(
        uint userID, 
        Action<Levels> onComplete);
    
    IEnumerator ApplyLevel(
        uint userID,
        uint levelID, 
        int closestStage, 
        Action<LevelProperty> onComplete);

    IEnumerator SubmitLevel(
        uint userID,
        StageFlag flag,
        int stage, 
        int killCount, 
        int killBossCount, 
        int gold, 
        Action<bool> onComplete);

    IEnumerator CollectLevel(
        uint userID,
        Action<Memory<UserReward>> onComplete);
}

public partial class UserData : MonoBehaviour, IUserData
{
    public struct LevelCache
    {
        public string name;
        public uint id;
        public int stage;
        public int gold;
        public int killCount;
        public int killBossCount;

        public LevelCache(string value)
        {
            var values = value.Split(SEPARATOR);
            name = values[0];
            id = uint.Parse(values[1]);
            stage = int.Parse(values[2]);
            gold = int.Parse(values[3]);
            killCount = int.Parse(values[4]);
            killBossCount = int.Parse(values[5]);
        }

        public override string ToString()
        {
            return $"{name}{SEPARATOR}{id}{SEPARATOR}{stage}{SEPARATOR}{gold}{SEPARATOR}{killCount}{SEPARATOR}{killBossCount}";
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

    private const string NAME_SPACE_USER_STAGE_CACHE = "UserStageCache";
    
    public static IUserData.StageCache GetStageCache(string levelName, int stage)
    {
        return new IUserData.StageCache(
            PlayerPrefs.GetString(GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE, levelName, stage)));
    }

    private const string NAME_SPACE_USER_STAGE_KILL_COUNT = "UserStageKillCount";
    
    public static int GetStageKillCount(string levelName, int stage)
    {
        return PlayerPrefs.GetInt(GetStageNameSpace(NAME_SPACE_USER_STAGE_KILL_COUNT, levelName, stage));
    }

    private const string NAME_SPACE_USER_STAGE_KILL_BOSS_COUNT = "UserStageKillBossCount";
    
    public static int GetStageKillBossCount(string levelName, int stage)
    {
        return PlayerPrefs.GetInt(GetStageNameSpace(NAME_SPACE_USER_STAGE_KILL_BOSS_COUNT, levelName, stage));
    }
    
    public IEnumerator QueryUser(
        string channelName,
        string channelUser,
        Action<IUserData.Status, uint> onComplete)
    {
        yield return null;

        onComplete(level > 0 ? IUserData.Status.Normal : IUserData.Status.Guide, id);
    }
    
    public IEnumerator SubmitLevel(
        uint userID,
        IUserData.StageFlag flag,
        int stage,
        int killCount, 
        int killBossCount, 
        int gold,
        Action<bool> onComplete)
    {
        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            UnityEngine.Debug.LogError("WTF?");

            onComplete(false);
            
            return null;
        }

        var temp = levelCache.Value;
        if (temp.stage > stage)
        {
            UnityEngine.Debug.LogError("WTF?");
            
            onComplete(false);
            
            return null;
        }
        
        __SubmitStageFlag(flag, temp.name, temp.stage, stage);
        
        __SetStageKillCount(temp.name, temp.stage, killCount);
        __SetStageKillBossCount(temp.name, temp.stage, killBossCount);

        temp.stage = stage;
        temp.gold = gold;
        temp.killCount = killCount;
        temp.killBossCount = killBossCount;
        UserData.levelCache = temp;
        
        onComplete(true);
        
        return null;
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
    private const string NAME_SPACE_USER_LEVEL_START_STAGE = "UserLevelStartStage";
    
    public static IUserData.StageFlag GetStageFlag(string levelName, int stage)
    {
        return (IUserData.StageFlag)PlayerPrefs.GetInt(GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, stage));
    }

    public static void StartStage(string levelName, int stage)
    {
        //string key = GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE_TIMES, levelName, stage);
        //int times = PlayerPrefs.GetInt(key);
        //PlayerPrefs.SetInt(key, ++times);
        
        PlayerPrefs.SetInt($"{NAME_SPACE_USER_LEVEL_START_STAGE}{levelName}", stage);
    }

    public static void EndStage(string levelName, int stage)
    {
        string levelStartStageKey = $"{NAME_SPACE_USER_LEVEL_START_STAGE}{levelName}";
        int startStage = PlayerPrefs.GetInt(levelStartStageKey);
        if (startStage == 0)
        {
            string stageFlagKey;
            for (int i = 0; i < stage; ++i)
            {
                stageFlagKey = GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, i);

                PlayerPrefs.SetInt(stageFlagKey,
                    PlayerPrefs.GetInt(stageFlagKey) | (int)IUserData.StageFlag.Once);
            }
        }
        PlayerPrefs.DeleteKey(levelStartStageKey);
    }

    private static void __SubmitStageFlag(
        IUserData.StageFlag value, 
        string levelName, 
        int fromStage, 
        int toStage)
    {
        value |= IUserData.StageFlag.Normal;
        
        string key;
        for (int i = fromStage; i < toStage; ++i)
        {
            key = GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, i);
            PlayerPrefs.SetInt(key, (int)value | PlayerPrefs.GetInt(key));
        }
    }
    
    private static void __SetStageKillCount(string levelName, int stage, int value)
    {
        string key = GetStageNameSpace(NAME_SPACE_USER_STAGE_KILL_COUNT, levelName, stage);
        int origin = PlayerPrefs.GetInt(key);
        if (origin < value)
            PlayerPrefs.SetInt(key, value);
    }

    private static void __SetStageKillBossCount(string levelName, int stage, int value)
    {
        string key = GetStageNameSpace(NAME_SPACE_USER_STAGE_KILL_BOSS_COUNT, levelName, stage);
        int origin = PlayerPrefs.GetInt(key);
        if (origin < value)
            PlayerPrefs.SetInt(key, value);
    }

    void Awake()
    {
        IUserData.instance = this;
    }
}
