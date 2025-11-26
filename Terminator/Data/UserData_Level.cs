using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using ZG;

public struct UserLevel
{
    public string name;
    public uint id;
    //public int energy;
    public UserStage[] stages;
}

[Serializable]
public struct UserLevelStageData
{
    public SpawnerAttribute.Scale spawnerAttributeScale;
    public LevelQuest[] quests;

    public LevelShared.Stage ToShared()
    {
        LevelShared.Stage result;
        result.spawnerAttributeScale = spawnerAttributeScale;
        result.quests = default;
        if (quests != null)
        {
            foreach (var quest in quests)
                result.quests.Add(quest);
        }

        return result;
    }
}

public partial interface IUserData
{
    [Flags]
    public enum StageFlag
    {
        Normal = 0x01, 
        Once = 0x02 | Normal, 
        Perfect = 0x04 | Normal
    }

    public struct LevelStage
    {
        public uint levelID;
        public int stage;

        public UserReward[] rewards;
    }

    public struct LevelChapters
    {
        [Flags]
        public enum Flag
        {
            UnlockFirst = 0x01
        }

        public Flag flag;
        
        public int stageRewardCount;

        public UserLevel[] levels;
    }

    public struct LevelTicket
    {
        public string name;

        /// <summary>
        /// 门票数量
        /// </summary>
        public int count;

        /// <summary>
        /// 下一次解锁新关卡需要的章节数
        /// </summary>
        public int chapter;
        
        /// <summary>
        /// 对应Level的名字
        /// </summary>
        public string[] levelNames;
    }

    public struct LevelTickets
    {
        public enum Flag
        {
            Unlock = 0x01, 
            UnlockFirst = 0x02 | Unlock
        }

        public Flag flag;
        public LevelTicket[] tickets;
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
        public int hpMax;
        public LayerMaskAndTagsAuthoring spawnerLayerMaskAndTags;
        public Skill[] skills;
        public UserAttributeData[] attributes;
    }
    
    public struct LevelProperty
    {
        public int stage;
        public Property value;
        public UserLevelStageData[] levelStages;
    }

    IEnumerator ApplyLevel(
        uint userID,
        uint levelID, 
        int closestStage, 
        Action<LevelProperty> onComplete);

    IEnumerator SubmitLevel(
        uint userID,
        //StageFlag flag,
        int stage, 
        int time, 
        int hpPercentage, 
        int killCount, 
        int killBossCount, 
        int gold, 
        Action<bool> onComplete);

    IEnumerator CollectLevel(
        uint userID,
        Action<LevelStage> onComplete);

    IEnumerator SweepLevel(
        uint userID,
        uint levelID,
        Action<Memory<UserReward>> onComplete);
    
    IEnumerator QueryLevelChapters(
        uint userID, 
        Action<LevelChapters> onComplete);

    /// <summary>
    /// 查找门票
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryLevelTickets(
        uint userID,
        Action<LevelTickets> onComplete);
}


public partial class UserData
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

    private const string NAME_SPACE_USER_CHAPTER = "UserChapter";
    //private const string NAME_SPACE_USER_STAGE_FLAG = "UserStageFlag";

    public static int chapter
    {
        get => PlayerPrefs.GetInt(NAME_SPACE_USER_CHAPTER);
        
        set => PlayerPrefs.SetInt(NAME_SPACE_USER_CHAPTER, value);
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

    public IEnumerator SubmitLevel(
        uint userID,
        //IUserData.StageFlag flag,
        int stage,
        int time, 
        int hpPercentage,
        int killCount, 
        int killBossCount, 
        int gold,
        Action<bool> onComplete)
    {
        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            Debug.LogError("WTF?");

            onComplete(false);
            
            yield break;
        }

        var temp = levelCache.Value;
        if (temp.stage > stage)
        {
            //UnityEngine.Debug.LogError("WTF?");
            
            onComplete(false);
            
            yield break;
        }
        
        yield return null;
        
        __SetStageKillCount(temp.name, temp.stage, killCount - temp.killCount);
        __SetStageKillBossCount(temp.name, temp.stage, killBossCount - temp.killBossCount);
        __SetStageGold(temp.name, temp.stage, gold);

        if (temp.stage < stage)
        {
            __SetStageHPPercentage(temp.name, temp.stage, hpPercentage);
            __SetStageTime(temp.name, temp.stage, time);
            
            __SubmitStageFlag(hpPercentage == 100, /*flag, */temp.name, temp.stage, stage);

            temp.stage = stage;
        }

        temp.killCount = killCount;
        temp.killBossCount = killBossCount;
        temp.gold = gold;
        UserData.levelCache = temp;
        
        onComplete(true);
        
        //return null;
    }
}