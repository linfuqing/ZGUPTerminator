using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

[Serializable]
public struct UserRewardOptionData
{
    public float chance;
        
    public UserRewardData value;

    public UserRewardOptionData(string value)
    {
        var parameters = value.Split(':');
        this.value.name = parameters[0];
        this.value.type = (UserRewardType)int.Parse(parameters[1]);
        this.value.count = 2 < parameters.Length ? int.Parse(parameters[2]) : 1;
        chance = 3 < parameters.Length ? float.Parse(parameters[3]) : 1.0f;
    }
}

public struct UserStageReward
{
    [Flags]
    public enum Flag
    {
        Unlocked = 0x01, 
        Collected = 0x02
    }

    public enum Condition
    {
        Normal, 
        Once, 
        HPPercentage, 
        KillCount, 
        Time, 
        Gold
    }
    
    public string name;
    public uint id;
    public Flag flag;
    public Condition condition;
    public int conditionValue;
    public UserRewardData[] values;
}

public struct UserStage
{
    [Serializable]
    public struct RewardPool
    {
        public string name;
        
        public UserRewardOptionData[] options;
        
#if UNITY_EDITOR
        [CSVField]
        public string 奖池名字
        {
            set => name = value;
        }

        [CSVField]
        public string 奖池选项
        {
            set
            {
                var parameters = value.Split('/');
                
                int numParameters = parameters.Length;
                options = new UserRewardOptionData[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    options[i] = new UserRewardOptionData(parameters[i]);
            }
        }
#endif
    }
    
    public string name;
    public uint id;
    public int energy;
    public UserRewardData[] rewards;
    public UserStageReward.Flag[] rewardFlags;
    //public RewardPool[] rewardPools;
}

public partial interface IUserData
{
    public struct Item
    {
        public string name;
        public int count;

        public static Item[] Parse(string[] values, int startIndex, int length)
        {
            int count = length >> 1;
            if(0 == count)
                return Array.Empty<Item>();

            int index = startIndex;
            var items = new Item[count];
            for (int i = 0; i < count; ++i)
            {
                ref var item = ref items[i];
                item.name = values[index++];
                item.count = int.Parse(values[index++]);
            }

            return items;
        }

        public override string ToString()
        {
            return $"{name}{UserData.SEPARATOR}{count}";
        }
    }

    public struct StageCache
    {
        public uint seconds;
        public int rage;
        public int exp;
        public int expMax;
        public string[] skills;
        public Item[] items;

        public static readonly StageCache Empty = new StageCache(string.Empty);
        
        public bool isEmpty => exp == expMax && (skills == null || skills.Length < 1);

        public StageCache(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                seconds = 0;
                rage = 0;
                exp = 0;
                expMax = 0;

                skills = Array.Empty<string>();
                items = Array.Empty<Item>();

                return;
            }
            
            var values = value.Split(UserData.SEPARATOR);
            
            int length = values.Length;
            seconds = uint.Parse(values[--length]);
            //兼容老版本
            if (seconds == 0)
            {
                seconds = DateTimeUtility.GetSeconds();
                rage = 0;
            }
            else
                rage = int.Parse(values[--length]);
            
            exp = int.Parse(values[--length]);
            expMax = int.Parse(values[--length]);

            if (length > 0)
            {
                if (int.TryParse(values[length - 1], out int skillCount))
                {
                    skills = skillCount > 0 ? new string[skillCount] : null;
                    length -= skillCount + 1;
                    Array.Copy(values, length,  skills, 0, skillCount);
                    items = Item.Parse(values, 0, length);
                    Array.Resize(ref skills, skillCount);
                }
                else
                {
                    Array.Resize(ref values, length);
                    skills = values;
                    items = Array.Empty<Item>();
                }
            }
            else
            {
                skills = Array.Empty<string>();
                items = Array.Empty<Item>();
            }
        }

        public override string ToString()
        {
            int numSkills = skills == null ? 0 : skills.Length;
            string result = $"{numSkills}{UserData.SEPARATOR}{expMax}{UserData.SEPARATOR}{exp}{UserData.SEPARATOR}{rage}{UserData.SEPARATOR}{seconds}";
            if(numSkills > 0)
                result = $"{string.Join(UserData.SEPARATOR, skills)}{UserData.SEPARATOR}{result}";
            
            int numItems = items == null ? 0 : items.Length;
            if(numItems > 0)
                result = $"{string.Join(UserData.SEPARATOR, items)}{UserData.SEPARATOR}{result}";
            
            return result;
        }
    }

    public struct Stage
    {
        public int energy;
        public int levelEnergy;
        public StageCache cache;
        public UserStageReward[] rewards;
    }

    public struct StageProperty
    {
        public int stage;
        
        public Property value;

        public StageCache cache;
        
        public UserLevelStageData[] levelStages;
    }

    public struct StageResult
    {
        public int flag;

        public int totalEnergy;
        public int nextStageEnergy;
    }

    /// <summary>
    /// 查询关卡
    /// </summary>
    IEnumerator QueryStage(
        uint userID,
        uint stageID,
        Action<Stage> onComplete);

    /// <summary>
    /// 继续游戏
    /// </summary>
    IEnumerator ApplyStage(
        uint userID,
        uint levelID,
        int stage, 
        Action<StageProperty> onComplete);

    IEnumerator SubmitStage(
        uint userID,
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
        Item[] items,
        Action<StageResult> onComplete);

    /// <summary>
    /// 收集关卡奖励
    /// </summary>
    IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<Memory<UserReward>> onComplete);

    /// <summary>
    /// 一键收集全部关卡奖励
    /// </summary>
    IEnumerator CollectStageRewards(uint userID, Action<Memory<UserReward>> onComplete);

    IEnumerator ApplyReward(uint userID, string poolName, Action<Memory<UserReward>> onComplete);
}

public partial class UserData
{
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

    private const string NAME_SPACE_USER_STAGE_TIME = "UserStageTime";

    public static int GetStageTime(string levelName, int stage)
    {
        return PlayerPrefs.GetInt(GetStageNameSpace(NAME_SPACE_USER_STAGE_TIME, levelName, stage));
    }

    private const string NAME_SPACE_USER_STAGE_HP_PERCENTAGE = "UserStageHPPercentage";

    public static int GetStageHPPercentage(string levelName, int stage)
    {
        return PlayerPrefs.GetInt(GetStageNameSpace(NAME_SPACE_USER_STAGE_HP_PERCENTAGE, levelName, stage));
    }

    private const string NAME_SPACE_USER_STAGE_GOLD = "UserStageGold";

    public static int GetStageGold(string levelName, int stage)
    {
        return PlayerPrefs.GetInt(GetStageNameSpace(NAME_SPACE_USER_STAGE_GOLD, levelName, stage));
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

    public static readonly List<UserRewardData> Rewards = new List<UserRewardData>();

    public static void ApplyRewards(UserRewardOptionData[] options)
    {
        var randomSelector = new ZG.RandomSelector(0);
        foreach (var option in options)
        {   
            if(!randomSelector.Select(option.chance))
                continue;

            Rewards.Add(option.value);
        }
    }

    public static void ApplyReward(
        string poolName, 
        UserStage.RewardPool[] rewardPools)
    {
        foreach (var rewardPool in rewardPools)
        {
            if (rewardPool.name == poolName)
            {
                ApplyRewards(rewardPool.options);
                
                break;
            }
        }
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
        __SubmitStageFlag(levelName, stage, out string levelStartStageKey);
        
        PlayerPrefs.DeleteKey(levelStartStageKey);
    }

    private static void __SubmitStageFlag(string levelName, int stage, out string levelStartStageKey)
    {
        levelStartStageKey = $"{NAME_SPACE_USER_LEVEL_START_STAGE}{levelName}";
        int startStage = PlayerPrefs.GetInt(levelStartStageKey, -1);
        if (startStage == -1)
            return;
         
        string stageFlagKey;
        for (int i = startStage; i< stage; ++i)
        {
            stageFlagKey = GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, i);

            PlayerPrefs.SetInt(stageFlagKey,
                PlayerPrefs.GetInt(stageFlagKey) | (int)IUserData.StageFlag.Once);
        }
    }

    private static int __SubmitStageFlag(
        //IUserData.StageFlag value, 
        bool isPerfect, 
        string levelName, 
        int fromStage, 
        int toStage)
    {
        var value = isPerfect ? IUserData.StageFlag.Perfect : IUserData.StageFlag.Normal;

        int result = (int)value;
        
        string key;
        for (int i = fromStage; i < toStage; ++i)
        {
            key = GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, i);
            result = (int)value | PlayerPrefs.GetInt(key);
            PlayerPrefs.SetInt(key, result);
        }

        return result;
    }

    private static void __SetStageValue(string key, string levelName, int stage, int value, bool isGreaterOrLess)
    {
        key = GetStageNameSpace(key, levelName, stage);
        int origin = PlayerPrefs.GetInt(key, isGreaterOrLess ? 0 : value);
        if (isGreaterOrLess ? origin < value : origin >= value)
            PlayerPrefs.SetInt(key, value);
    }
    
    private static void __SetStageTime(string levelName, int stage, int value)
    {
        __SetStageValue(NAME_SPACE_USER_STAGE_TIME, levelName, stage, value, false);
    }

    private static void __SetStageHPPercentage(string levelName, int stage, int value)
    {
        __SetStageValue(NAME_SPACE_USER_STAGE_HP_PERCENTAGE, levelName, stage, value, true);
    }
    
    private static void __SetStageGold(string levelName, int stage, int value)
    {
        __SetStageValue(NAME_SPACE_USER_STAGE_GOLD, levelName, stage, value, true);
    }

    private static void __SetStageKillCount(string levelName, int stage, int value)
    {
        __SetStageValue(NAME_SPACE_USER_STAGE_KILL_COUNT, levelName, stage, value, true);
    }

    private static void __SetStageKillBossCount(string levelName, int stage, int value)
    {
        __SetStageValue(NAME_SPACE_USER_STAGE_KILL_BOSS_COUNT, levelName, stage, value, true);
    }
}