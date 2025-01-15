using System;
using System.Collections;
using System.Collections.Generic;
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

public struct UserStage
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

public interface IUserData : IGameUserData
{
    public static IUserData instance;

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
        Action<Memory<UserStage>> onComplete);
    
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
            var values = value.Split(',');
            id = uint.Parse(values[0]);
            stage = int.Parse(values[1]);
            gold = int.Parse(values[2]);
        }

        public override string ToString()
        {
            return $"{id},{stage},{gold}";
        }
    }
    
    private const string NAME_SPACE_USER_LEVEL_CACHE = "UserLevelCache";
    private const string NAME_SPACE_USER_LEVEL = "UserLevel";

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

    public IEnumerator SubmitLevel(
        uint userID,
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

        onComplete(level > 0
            ? IGameUserData.UserStatus.Ok
            : IGameUserData.UserStatus.New);
    }

    public IEnumerator Check(
        string channel,
        string channelUser,
        Action<IGameUserData.UserStatus> onComplete)
    {
        yield return null;

        onComplete(level > 0
            ? IGameUserData.UserStatus.Ok
            : IGameUserData.UserStatus.New);
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
