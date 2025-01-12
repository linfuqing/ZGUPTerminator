using Mono.Cecil;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct User
{
    public uint id;
    public int gold;
    public int level;
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
    //public int id;
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
    public int userStage;

    public UserStage[] stages;
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
        Damage
    }
    
    [Flags]
    public enum Flag
    {
        Collected = 0x01
    }

    public string name;
    public Flag flag;
    public RewardType rewardType;
    public int rewardCount;
    public int gold;
}

public struct UserSkill
{
    public string name;
}

public interface IUserData
{
    public static IUserData instance;

    IEnumerator QueryUser(
        string channel, 
        string channelUser, 
        Action<User, UserEnergy> onComplete);
    
    IEnumerator QuerySkills(
        uint id, 
        Action<Memory<UserSkill>> onComplete);

    IEnumerator QueryLevels(
        uint id, 
        Action<Memory<UserLevel>> onComplete);
    
    IEnumerator QueryWeapons(
        uint id, 
        Action<Memory<UserWeapon>> onComplete);
    
    IEnumerator QueryTalents(
        uint id, 
        Action<int, Memory<UserTalent>> onComplete);
    
    IEnumerator ApplyLevel(
        uint userID,
        uint levelID, 
        Action<bool> onComplete);

    IEnumerator SubmitLevel(
        uint userID,
        uint levelID, 
        int stage, 
        int gold, 
        Action<bool> onComplete);

    IEnumerator CollectLevel(
        uint userID,
        Action<string[]> onComplete);

    IEnumerator CollectStage(
        uint userID,
        uint levelID, 
        int stage, 
        Action<bool> onComplete);
    
    IEnumerator CollectTalent(
        uint userID,
        uint talentID, 
        Action<bool> onComplete);
    
    IEnumerator SelectWeapon(
        uint userID,
        uint weaponID, 
        Action<bool> onComplete);
}

public class UserData : MonoBehaviour//, IUserData
{
    [Serializable]
    internal struct Energy
    {
        public int max;
        public float uintTime;
    }

    private const string NAME_SPACE_USER_GOLD = "User";
    private const string NAME_SPACE_USER_LEVEL = "UserLevel";
    private const string NAME_SPACE_USER_ENERGY = "UserEnergy";

    [SerializeField]
    internal Energy _energy;

    public IEnumerator QueryUser(
        string channel,
        string channelUser,
        Action<User, UserEnergy> onComplete)
    {
        yield return null;

        User user;
        user.id = 0;
        user.gold = PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);
        user.level = PlayerPrefs.GetInt(NAME_SPACE_USER_LEVEL);

        UserEnergy userEnergy;
        userEnergy.value = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY);
        userEnergy.max = _energy.max;
        userEnergy.unitTime = (uint)Mathf.RoundToInt(_energy.uintTime * 1000);
        userEnergy.tick = DateTime.UtcNow.Ticks;
        
        onComplete(user, userEnergy);
    }

    [SerializeField]
    internal string[] _defaultSkills;
    private const string NAME_SPACE_USER_SKILLS = "UserSkills";

    public IEnumerator QuerySkills(
        uint id,
        Action<Memory<UserSkill>> onComplete)
    {
        yield return null;
        
        var skills = PlayerPrefs.GetString(NAME_SPACE_USER_SKILLS).Split(',');

        int numSkills = skills == null ? 0 : skills.Length;
        UserSkill userSkill;
        var userSkills = new UserSkill[numSkills];
        for (int i = 0; i < numSkills; ++i)
        {
            userSkill.name = skills[i];

            userSkills[i] = userSkill;
        }

        onComplete(userSkills);
    }

    [Serializable]
    internal struct Stage
    {
        public string name;
        public UserStage.RewardType rewardType;
        public int rewardCount;
    }
    
    [Serializable]
    internal struct Level
    {
        public string name;
        public uint id;
        public int energy;

        public Stage[] stages;
        public string[] rewardSkills;
    }

    private const string NAME_SPACE_USER_LEVEL_STAGE = "UserLevelStage";
    private const string NAME_SPACE_USER_LEVEL_STAGE_FLAG = "UserLevelStageFlag";

    [SerializeField]
    internal Level[] _levels;

    public IEnumerator QueryLevels(
        uint id,
        Action<Memory<UserLevel>> onComplete)
    {
        yield return null;

        int i, j, numStages, numLevels = _levels.Length;
        Level level;
        UserLevel userLevel;
        UserStage userStage;
        Stage stage;
        var userLevels = new UserLevel[numLevels];
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            userLevel.name = level.name;
            userLevel.id = (uint)i + 1;
            userLevel.energy = level.energy;
            userLevel.userStage = PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE}{i}");
            userLevel.rewardSkills = level.rewardSkills;

            numStages = level.stages == null ? 0 : level.stages.Length;
            userLevel.stages = new UserStage[numStages];
            for (j = 0; j < numStages; ++j)
            {
                stage = level.stages[j];

                userStage.name = stage.name;
                userStage.flag = (UserStage.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{i}-{j}");
                userStage.rewardType = stage.rewardType;
                userStage.rewardCount = stage.rewardCount;

                userLevel.stages[j] = userStage;
            }

            userLevels[i] = userLevel;
        }
        
        if(onComplete != null)
            onComplete(userLevels);
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID,
        Action<bool> onComplete)
    {
        yield return null;

        int energy = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY) - _levels[levelID].energy;
        if (energy < 0)
        {
            if (onComplete != null)
                onComplete(false);

            yield break;
        }
        
        PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGY, energy);
        
        if (onComplete != null)
            onComplete(true);
    }

    private const string NAME_SPACE_USER_LEVEL_REWARD_SKILLS = "UserLevelStageRewardSkills";

    public IEnumerator SubmitLevel(
        uint userID,
        uint levelID,
        int stage,
        int gold,
        Action<bool> onComplete)
    {
        yield return null;
        
        string key = $"{NAME_SPACE_USER_LEVEL_STAGE}{levelID}-{stage}";
        stage = Mathf.Max(stage, PlayerPrefs.GetInt(key));
        PlayerPrefs.SetInt(key, stage);
        
        gold += PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);
        PlayerPrefs.SetInt(NAME_SPACE_USER_GOLD, gold);

        int userLevel = PlayerPrefs.GetInt(NAME_SPACE_USER_LEVEL);
        if (userLevel == levelID)
        {
            PlayerPrefs.SetInt(NAME_SPACE_USER_LEVEL, ++userLevel);

            var level = _levels[levelID];

            string source = PlayerPrefs.GetString(NAME_SPACE_USER_LEVEL_REWARD_SKILLS), destination = string.Join(',', level.rewardSkills);
            if (string.IsNullOrEmpty(source))
                source = destination;
            else
                source = $"{source},{destination}";

            PlayerPrefs.SetString(NAME_SPACE_USER_LEVEL_REWARD_SKILLS, source);
        }

        if (onComplete != null)
            onComplete(true);
    }

    IEnumerator CollectLevel(
        uint userID,
        Action<string[]> onComplete)
    {
        yield return null;

        var destination = PlayerPrefs.GetString(NAME_SPACE_USER_LEVEL_REWARD_SKILLS);
        var skills = destination.Split(',');
        if(skills.Length > 0)
        {
            string source = PlayerPrefs.GetString(NAME_SPACE_USER_SKILLS);
            if (string.IsNullOrEmpty(source))
                source = destination;
            else
                source = $"{source},{destination}";

            PlayerPrefs.SetString(NAME_SPACE_USER_SKILLS, source);
            PlayerPrefs.DeleteKey(NAME_SPACE_USER_LEVEL_REWARD_SKILLS);
        }

        onComplete(skills);
    }

    public IEnumerator CollectStage(
        uint userID,
        uint levelID,
        int stage,
        Action<bool> onComplete)
    {
        yield return null;
    }

}
