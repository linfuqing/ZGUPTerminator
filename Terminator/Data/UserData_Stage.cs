using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

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
        NoDamage, 
        KillCount
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
    public struct RewardPoolOption
    {
        public float chance;
        
        public UserRewardData value;

        public RewardPoolOption(string value)
        {
            var parameters = value.Split(':');
            this.value.name = parameters[0];
            this.value.type = (UserRewardType)int.Parse(parameters[1]);
            this.value.count = 2 < parameters.Length ? int.Parse(parameters[2]) : 1;
            chance = 3 < parameters.Length ? float.Parse(parameters[3]) : 1.0f;
        }
    }
    
    [Serializable]
    public struct RewardPool
    {
        public string name;
        
        public RewardPoolOption[] options;
        
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
                options = new RewardPoolOption[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    options[i] = new RewardPoolOption(parameters[i]);
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
    public struct StageCache
    {
        public int rage;
        public int exp;
        public int expMax;
        public string[] skills;

        public static readonly StageCache Empty = new StageCache(string.Empty);
        
        public bool isEmpty => exp == expMax && (skills == null || skills.Length < 1);

        public StageCache(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                rage = 0;
                exp = 0;
                expMax = 0;

                skills = Array.Empty<string>();

                return;
            }
            
            skills = value.Split(UserData.SEPARATOR);
            
            int length = skills.Length;
            rage = int.Parse(skills[--length]);
            exp = int.Parse(skills[--length]);
            expMax = int.Parse(skills[--length]);

            Array.Resize(ref skills, length);
        }

        public override string ToString()
        {
            string result = $"{expMax}{UserData.SEPARATOR}{exp}{UserData.SEPARATOR}{rage}";
            if(skills != null && skills.Length > 0)
                result = $"{string.Join(UserData.SEPARATOR, skills)}{UserData.SEPARATOR}{result}";
            
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
        
        public SpawnerAttribute.Scale[] spawnerAttributes;
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
        StageFlag flag,
        int stage,
        int killCount, 
        int killBossCount, 
        int gold,
        int rage, 
        int exp,
        int expMax,
        string[] skills,
        Action<int> onComplete);
    
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

    public static void ApplyReward(
        string poolName, 
        UserStage.RewardPool[] rewardPools)
    {
        bool isSelected;
        float chance, total;
        foreach (var rewardPool in rewardPools)
        {
            if (rewardPool.name == poolName)
            {
                isSelected = false;
                chance = UnityEngine.Random.value;
                total = 0.0f;
                foreach (var option in rewardPool.options)
                {
                    total += option.chance;
                    if (total > 1.0f)
                    {
                        total -= 1.0f;
                        
                        chance = UnityEngine.Random.value;

                        isSelected = false;
                    }
                    
                    if(isSelected || total < chance)
                        continue;

                    isSelected = true;

                    Rewards.Add(option.value);
                }
                
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

    public IEnumerator SubmitStage(
        uint userID,
        IUserData.StageFlag flag,
        int stage,
        int killCount, 
        int killBossCount, 
        int gold, 
        int rage, 
        int exp, 
        int expMax, 
        string[] skills,
        Action<int> onComplete)
    {
        yield return null;

        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            UnityEngine.Debug.LogError("WTF?");

            onComplete(0);
            
            yield break;
        }

        var temp = levelCache.Value;
        __SetStageKillCount(temp.name, temp.stage, killCount);

        __SetStageKillBossCount(temp.name, temp.stage, killBossCount);

        __SubmitStageFlag(temp.name, stage, out _);

        int result = 0;
        if (temp.stage < stage)
        {
            result = __SubmitStageFlag(flag, temp.name, temp.stage, stage);

            IUserData.StageCache stageCache;
            stageCache.rage = rage;
            stageCache.exp = exp;
            stageCache.expMax = expMax;
            stageCache.skills = skills;
            PlayerPrefs.SetString(GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE, temp.name, stage),
                stageCache.ToString());
            
            temp.stage = stage;
        }
        else
            result = (int)GetStageFlag(temp.name, temp.stage - 1);

        temp.gold = Mathf.Max(temp.gold, gold);
        temp.killCount = Mathf.Max(temp.killCount, killCount);
        temp.killBossCount = Mathf.Max(temp.killBossCount, killBossCount);
        UserData.levelCache = temp;
        
        onComplete(result);
        
        //return null;
    }

    
    private static void __SubmitStageFlag(string levelName, int stage, out string levelStartStageKey)
    {
        levelStartStageKey = $"{NAME_SPACE_USER_LEVEL_START_STAGE}{levelName}";
        int startStage = PlayerPrefs.GetInt(levelStartStageKey, -1);
        if (startStage == -1)
            return;
        
        string stageFlagKey;
        for (int i = startStage; i < stage; ++i)
        {
            stageFlagKey = GetStageNameSpace(NAME_SPACE_USER_STAGE_FLAG, levelName, i);

            PlayerPrefs.SetInt(stageFlagKey,
                PlayerPrefs.GetInt(stageFlagKey) | (int)IUserData.StageFlag.Once);
        }
    }

    private static int __SubmitStageFlag(
        IUserData.StageFlag value, 
        string levelName, 
        int fromStage, 
        int toStage)
    {
        value |= IUserData.StageFlag.Normal;

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

}