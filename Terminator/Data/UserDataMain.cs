using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UserDataMain : MonoBehaviour
{
    public static UserDataMain instance
    {
        get;

        private set;
    }
   
    
    [Serializable]
    internal struct Energy
    {
        public int max;
        public float uintTime;
    }

    public const string NAME_SPACE_USER_GOLD = "UserGold";
    private const string NAME_SPACE_USER_ENERGY = "UserEnergy";

    [SerializeField]
    internal Energy _energy;

    public IEnumerator QueryUser(
        string channelName, 
        string channelUser,
        Action<User, UserEnergy> onComplete)
    {
        yield return null;
        
        User user;
        user.id = 0;
        user.gold = PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);
        user.level = UserData.level;

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
        uint userID,
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

    public IEnumerator QueryWeapons(
        uint userID,
        Action<Memory<UserWeapon>> onComplete)
    {
        yield return null;
    }

    public IEnumerator QueryTalents(
        uint userID,
        Action<int, Memory<UserTalent>> onComplete)
    {
        yield return null;
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
        uint userID,
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

            userLevels[i] = userLevel;
        }
        
        if(onComplete != null)
            onComplete(userLevels);
    }

    
    public IEnumerator QueryStages(
        uint userID,
        Action<Memory<UserStage>> onComplete)
    {
        yield return null;

        int i, j, numStages, stageIndex = 0, numLevels = _levels.Length;
        Level level;
        UserStage userStage;
        Stage stage;
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            
            numStages = level.stages == null ? 0 : level.stages.Length;
            for (j = 0; j < numStages; ++j)
            {
                if (((UserStage.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{i}-{j}") &
                    UserStage.Flag.Collected) != UserStage.Flag.Collected)
                    break;
            }

            if (j < numStages)
            {
                var userStages = new UserStage[numStages];
                for (j = 0; j < numStages; ++j)
                {
                    stage = level.stages[j];

                    userStage.name = stage.name;
                    userStage.id = stageIndex++;
                    userStage.flag = (UserStage.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{i}-{j}");
                    userStage.rewardType = stage.rewardType;
                    userStage.rewardCount = stage.rewardCount;

                    userStages[j] = userStage;
                }
                
                onComplete(userStages);
            }
            else
                stageIndex += numStages;
        }
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

        int userLevel = UserData.level;
        if (userLevel == levelID)
        {
            UserData.level = ++userLevel;

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

    public IEnumerator CollectLevel(
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
        uint stageID,
        Action<bool> onComplete)
    {
        yield return null;
    }

    public IEnumerator CollectTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        yield return null;
    }

    public IEnumerator SelectWeapon(
        uint userID,
        uint weaponID,
        Action<bool> onComplete)
    {
        yield return null;
    }
    
    void Awake()
    {
        instance = this;
    }
}

public partial class UserData
{
    public IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<User, UserEnergy> onComplete)
    {
        return UserDataMain.instance.QueryUser(channelName, channelUser, onComplete);
    }

    public IEnumerator QuerySkills(
        uint userID,
        Action<Memory<UserSkill>> onComplete)
    {
        return UserDataMain.instance.QuerySkills(userID, onComplete);
    }

    public IEnumerator QueryWeapons(
        uint userID,
        Action<Memory<UserWeapon>> onComplete)
    {
        return UserDataMain.instance.QueryWeapons(userID, onComplete);
    }

    public IEnumerator QueryTalents(
        uint userID,
        Action<int, Memory<UserTalent>> onComplete)
    {
        return UserDataMain.instance.QueryTalents(userID, onComplete);
    }

    public IEnumerator QueryLevels(
        uint userID,
        Action<Memory<UserLevel>> onComplete)
    {
        return UserDataMain.instance.QueryLevels(userID, onComplete);
    }

    public IEnumerator QueryStages(
        uint userID,
        Action<Memory<UserStage>> onComplete)
    {
        return UserDataMain.instance.QueryStages(userID, onComplete);
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.ApplyLevel(userID, levelID, onComplete);
    }

    public IEnumerator SubmitLevel(
        uint userID,
        uint levelID,
        int stage,
        int gold,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.SubmitLevel(userID, levelID, stage, gold, onComplete);
    }

    public IEnumerator CollectLevel(
        uint userID,
        Action<string[]> onComplete)
    {
        return UserDataMain.instance.CollectLevel(userID, onComplete);
    }

    public IEnumerator CollectStage(
        uint userID,
        uint stageID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.CollectStage(userID, stageID, onComplete);
    }

    public IEnumerator CollectTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.CollectTalent(userID, talentID, onComplete);
    }

    public IEnumerator SelectWeapon(
        uint userID,
        uint weaponID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.SelectWeapon(userID, weaponID, onComplete);
    }

}