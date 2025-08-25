using System;
using System.Collections;
using UnityEngine;
using ZG;

public struct UserLevel
{
    public string name;
    public uint id;
    public int energy;
    public UserStage[] stages;
}

public partial interface IUserData
{
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

    private const string NAME_SPACE_USER_LEVEL = "UserLevel";
    //private const string NAME_SPACE_USER_STAGE_FLAG = "UserStageFlag";

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
            
            yield break;
        }

        var temp = levelCache.Value;
        if (temp.stage > stage)
        {
            UnityEngine.Debug.LogError("WTF?");
            
            onComplete(false);
            
            yield break;
        }
        
        yield return null;
        
        __SubmitStageFlag(flag, temp.name, temp.stage, stage);
        
        __SetStageKillCount(temp.name, temp.stage, killCount);
        __SetStageKillBossCount(temp.name, temp.stage, killBossCount);

        temp.stage = stage;
        temp.gold = gold;
        temp.killCount = killCount;
        temp.killBossCount = killBossCount;
        UserData.levelCache = temp;
        
        onComplete(true);
        
        //return null;
    }
}