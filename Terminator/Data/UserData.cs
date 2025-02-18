using System;
using System.Collections;
using UnityEngine;

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

public struct UserStage_v0
{
    [Flags]
    public enum Flag
    {
        Unlock = 0x01, 
        Collected = 0x02
    }

    public enum RewardType
    {
        Gold, 
        Weapon
    }
        
    public string name;
    public uint id;
    //public int levelID;
    public Flag flag;
    public RewardType rewardType;
    public int rewardCount;
}

public struct UserLevel
{
    public string name;
    public uint id;
    public int energy;
    //public int userStage;

    public string[] rewardSkills;
}

public struct UserWeapon
{
    [Flags]
    public enum Flag
    {
        Selected = 0x01
    }
    
    public string name;
    public uint id;
    public Flag flag;
}

public struct UserTalent
{
    public enum RewardType
    {
        Hp, 
        Attack, 
        Defence
    }
    
    [Flags]
    public enum Flag
    {
        Collected = 0x01
    }

    public string name;
    public uint id;
    public Flag flag;
    public RewardType rewardType;
    public int rewardCount;
    public int gold;
}

public struct UserSkill
{
    public string name;
}

public partial interface IUserData : IGameUserData
{
    public static IUserData instance;

    IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<uint> onComplete);
    
    IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<User, UserEnergy> onComplete);
    
    IEnumerator QueryTip(
        uint userID, 
        Action<UserTip> onComplete);

    IEnumerator QuerySkills(
        uint userID, 
        Action<Memory<UserSkill>> onComplete);

    IEnumerator QueryWeapons(
        uint userID, 
        Action<Memory<UserWeapon>> onComplete);
    
    IEnumerator QueryTalents(
        uint userID, 
        Action<Memory<UserTalent>> onComplete);

    IEnumerator QueryStages(
        uint userID,
        Action<Memory<UserStage_v0>> onComplete);
    
    IEnumerator QueryLevels(
        uint userID, 
        Action<Memory<UserLevel>> onComplete);
    
    IEnumerator ApplyLevel(
        uint userID,
        uint levelID, 
        Action<bool> onComplete);

    IEnumerator SubmitLevel(
        uint userID,
        int stage, 
        int gold, 
        int exp, 
        int expMax, 
        string[] skills,
        Action<bool> onComplete);

    IEnumerator CollectLevel(
        uint userID,
        Action<int, string[]> onComplete);

    IEnumerator CollectStage(
        uint userID,
        uint stageID, 
        Action<bool> onComplete);
    
    IEnumerator CollectTalent(
        uint userID,
        uint talentID, 
        Action<bool> onComplete);
    
    IEnumerator CollectTip(
        uint userID,
        Action<int> onComplete);

    IEnumerator SelectWeapon(
        uint userID,
        uint weaponID, 
        Action<bool> onComplete);
}

public partial class UserData : MonoBehaviour, IUserData
{
    public struct LevelCache
    {
        public uint id;
        public int stage;
        public int gold;

        public LevelCache(string value)
        {
            var values = value.Split(SEPARATOR);
            id = uint.Parse(values[0]);
            stage = int.Parse(values[1]);
            gold = int.Parse(values[2]);
        }

        public override string ToString()
        {
            return $"{id}{SEPARATOR}{stage}{SEPARATOR}{gold}";
        }
    }

    private const string NAME_SPACE_USER_ID = "UserLevelID";
    
    private const string NAME_SPACE_USER_LEVEL_CACHE = "UserLevelCache";
    private const string NAME_SPACE_USER_LEVEL = "UserLevel";
    private const string NAME_SPACE_USER_STAGE_CACHE = "UserStageCache";

    private const string NAME_SPACE_USER_ENERGY_TIME = "UserEnergyTime";
    
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
            if(value == null)
                PlayerPrefs.DeleteKey(NAME_SPACE_USER_LEVEL_CACHE);
            else
                PlayerPrefs.SetString(NAME_SPACE_USER_LEVEL_CACHE, value.Value.ToString());
        }
    }

    public static IUserData.StageCache GetStageCache(uint levelID, int stage)
    {
        return new IUserData.StageCache(PlayerPrefs.GetString($"{NAME_SPACE_USER_STAGE_CACHE}-{levelID}-{stage}"));
    }

    public IEnumerator QueryUser(
        string channelName,
        string channelUser,
        Action<uint> onComplete)
    {
        yield return null;

        LevelCache levelCache;
        levelCache.id = 1;
        levelCache.gold = 0;
        levelCache.stage = 0;

        UserData.levelCache = levelCache;

        onComplete(id);
    }
    
    //[SerializeField]
    //internal string[] _defaultSkills;
    
    public IEnumerator SubmitLevel(
        uint userID,
        int stage,
        int gold,
        int exp, 
        int expMax, 
        string[] skills,
        Action<bool> onComplete)
    {
        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            onComplete(false);
            
            yield break;
        }

        var temp = levelCache.Value;
        temp.stage = stage;
        temp.gold = gold;
        UserData.levelCache = temp;

        IUserData.StageCache stageCache;
        stageCache.exp = exp;
        stageCache.expMax = expMax;
        stageCache.skills = skills;
        PlayerPrefs.SetString($"{NAME_SPACE_USER_STAGE_CACHE}{temp.id}-{stage}", stageCache.ToString());
        
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
    
    void Awake()
    {
        IUserData.instance = this;
    }
}
