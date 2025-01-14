using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public sealed partial class UserDataMain : MonoBehaviour
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

    private const string NAME_SPACE_USER_GOLD = "UserGold";

    public static int gold
    {
        get => PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);

        set => PlayerPrefs.SetInt(NAME_SPACE_USER_GOLD, value);
    }
    
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
        user.gold = gold;
        //user.level = UserData.level;

        UserEnergy userEnergy;
        userEnergy.value = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY, _energy.max);
        userEnergy.max = _energy.max;
        userEnergy.unitTime = (uint)Mathf.RoundToInt(_energy.uintTime * 1000);
        userEnergy.tick = DateTime.UtcNow.Ticks;
        
        onComplete(user, userEnergy);
    }

    private const char SEPARATOR = ',';
    private const string NAME_SPACE_USER_SKILLS = "UserSkills";

    [SerializeField]
    internal string[] _defaultSkills;
    
    public IEnumerator QuerySkills(
        uint userID,
        Action<Memory<UserSkill>> onComplete)
    {
        yield return null;
        
        var skills = PlayerPrefs.GetString(NAME_SPACE_USER_SKILLS, null)?.Split(SEPARATOR);

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
    internal struct Weapon
    {
        public string name;
        public string skillName;
    }

    private const string NAME_SPACE_USER_WEAPON_SELECTED = "UserWeaponSelected";
    private const string NAME_SPACE_USER_WEAPONS = "UserWeapons";

    [SerializeField] 
    internal Weapon[] _weapons;

    private Dictionary<string, int> __weaponIndices;

    public IEnumerator QueryWeapons(
        uint userID,
        Action<Memory<UserWeapon>> onComplete)
    {
        yield return null;

        if (__weaponIndices == null)
        {
            __weaponIndices = new Dictionary<string, int>();
            int numWeapons = _weapons.Length;
            for (int i = 0; i < numWeapons; ++i)
                __weaponIndices.Add(_weapons[i].name, i);
        }

        string[] weaponNames = PlayerPrefs.GetString(NAME_SPACE_USER_WEAPONS, null)?.Split(SEPARATOR);
        int numWeaponNames = weaponNames == null ? 0 : weaponNames.Length,
            weaponSelectedID = PlayerPrefs.GetInt(NAME_SPACE_USER_WEAPON_SELECTED, -1),
            weaponIndex;
        Weapon weapon;
        UserWeapon userWeapon;
        var userWeapons = new UserWeapon[numWeaponNames];
        for(int i = 0; i < numWeaponNames; ++i)
        {
            weaponIndex = __weaponIndices[weaponNames[i]];
            weapon = _weapons[weaponIndex];
            userWeapon = userWeapons[i];
            userWeapon.name = weapon.name;
            userWeapon.id = __ToID(weaponIndex);
            userWeapon.flag = userWeapon.id == weaponSelectedID ? UserWeapon.Flag.Selected : 0;
            userWeapons[i] = userWeapon;
        }

        onComplete(userWeapons);
    }

    public IEnumerator SelectWeapon(
        uint userID,
        uint weaponID,
        Action<bool> onComplete)
    {
        yield return null;
        
        var weaponNames = PlayerPrefs.GetString(NAME_SPACE_USER_WEAPONS, null)?.Split(SEPARATOR);
        if (weaponNames == null || weaponNames.IndexOf(_weapons[__ToIndex(weaponID)].name) == -1)
        {
            onComplete(false);
            
            yield break;
        }

        PlayerPrefs.SetInt(NAME_SPACE_USER_WEAPON_SELECTED, (int)weaponID);
        
        onComplete(true);
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
        public int energy;

        public Stage[] stages;
        public string[] rewardSkills;
    }

    //private const string NAME_SPACE_USER_LEVEL_STAGE = "UserLevelStage";
    private const string NAME_SPACE_USER_LEVEL_STAGE_FLAG = "UserLevelStageFlag";

    [SerializeField]
    internal Level[] _levels;

    [SerializeField, CSV("_levels", guidIndex = -1, nameIndex = 0)] 
    internal string _levelsPath;

    public IEnumerator QueryLevels(
        uint userID,
        Action<Memory<UserLevel>> onComplete)
    {
        yield return null;

        int i, numLevels = _levels.Length;
        Level level;
        UserLevel userLevel;
        var userLevels = new UserLevel[numLevels];
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            userLevel.name = level.name;
            userLevel.id = __ToID(i);
            userLevel.energy = level.energy;
            //userLevel.userStage = PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE}{i}");
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

        int i, j, numStages, stageIndex = 0, numLevels = Mathf.Min(_levels.Length, UserData.level);
        Level level;
        UserStage userStage;
        Stage stage;
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            
            numStages = level.stages == null ? 0 : level.stages.Length;
            for (j = 0; j < numStages; ++j)
            {
                if (((UserStage.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{__ToID(stageIndex + j)}") &
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
                    userStage.id = __ToID(stageIndex);
                    userStage.flag = (UserStage.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{userStage.id}");
                    userStage.rewardType = stage.rewardType;
                    userStage.rewardCount = stage.rewardCount;

                    userStages[j] = userStage;

                    ++stageIndex;
                }
                
                onComplete(userStages);
                
                break;
            }
            
            stageIndex += numStages;
        }
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID,
        Action<bool> onComplete)
    {
        yield return null;

        int energy = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY) - _levels[__ToIndex(levelID)].energy;
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

        /*string key = $"{NAME_SPACE_USER_LEVEL_STAGE}{__ToID(stageIndex + stage)}";
        stage = Mathf.Max(stage, PlayerPrefs.GetInt(key));
        PlayerPrefs.SetInt(key, stage);*/
        
        int userLevel = UserData.level, levelIndex = __ToIndex(levelID);
        if (userLevel < levelIndex)
        {
            onComplete(false);
            
            yield break;
        }

        if (userLevel == levelIndex)
        {
            UserData.level = ++userLevel;

            var level = _levels[levelIndex];

            string source = PlayerPrefs.GetString(NAME_SPACE_USER_LEVEL_REWARD_SKILLS), destination = string.Join(',', level.rewardSkills);
            if (string.IsNullOrEmpty(source))
                source = destination;
            else
                source = $"{source},{destination}";

            PlayerPrefs.SetString(NAME_SPACE_USER_LEVEL_REWARD_SKILLS, source);
        }
        
        UserDataMain.gold += gold;

        int stageIndex = 0;
        for (int i = 0; i < levelIndex; ++i)
            stageIndex += _levels[i].stages.Length;

        string key;
        UserStage.Flag flag;
        for (int i = 0; i < stage; ++i)
        {
            key = $"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{__ToID(stageIndex + i)}";
            flag = (UserStage.Flag)PlayerPrefs.GetInt(key);
            if ((flag & UserStage.Flag.Unlock) != UserStage.Flag.Unlock)
            {
                flag |= UserStage.Flag.Unlock;
                
                PlayerPrefs.SetInt(key, (int)flag);
            }
        }

        onComplete(true);
    }

    public IEnumerator CollectLevel(
        uint userID,
        Action<string[]> onComplete)
    {
        yield return null;

        var destination = PlayerPrefs.GetString(NAME_SPACE_USER_LEVEL_REWARD_SKILLS, null);
        var skills = destination?.Split(SEPARATOR);
        if(skills != null && skills.Length > 0)
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
        
        string key = $"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{stageID}";
        var flag = (UserStage.Flag)PlayerPrefs.GetInt(key);
        if ((flag & UserStage.Flag.Unlock) != UserStage.Flag.Unlock ||
            (flag & UserStage.Flag.Collected) == UserStage.Flag.Collected)
        {
            onComplete(false);
            
            yield break;
        }

        int stageIndex = __ToIndex(stageID), numStages, numLevels = Mathf.Min(_levels.Length, UserData.level);
        Level level;
        Stage stage;
        for (int i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            numStages = level.stages == null ? 0 : level.stages.Length;
            if (stageIndex < numStages)
            {
                stage = level.stages[stageIndex];
                switch (stage.rewardType)
                {
                    case UserStage.RewardType.Gold:
                        gold += stage.rewardCount;
                        break;
                    case UserStage.RewardType.Weapon:
                        string source = PlayerPrefs.GetString(NAME_SPACE_USER_WEAPONS), destination = stage.name;
                        if (string.IsNullOrEmpty(source))
                            source = destination;
                        else
                            source = $"{source},{destination}";

                        PlayerPrefs.SetString(NAME_SPACE_USER_WEAPONS, source);
                        break;
                }

                flag |= UserStage.Flag.Collected;

                PlayerPrefs.SetInt(key, (int)flag);

                onComplete(true);
                
                yield break;
            }

            stageIndex -= numStages;
        }
        
        onComplete(false);
    }

    [Serializable]
    internal struct Talent
    {
        public string name;
        public UserTalent.RewardType rewardType;
        public int rewardCount;
        public int gold;
    }

    private const string NAME_SPACE_USER_TALENT_FLAG = "UserTalentFlag";

    [SerializeField]
    internal Talent[] _talents;

    [SerializeField, CSV("_talents", guidIndex = -1, nameIndex = 0)] 
    internal string _talentsPath;
    
    public IEnumerator QueryTalents(
        uint userID,
        Action<Memory<UserTalent>> onComplete)
    {
        yield return null;

        int numTalents = _talents.Length;
        Talent talent;
        UserTalent userTalent;
        var userTalents = new UserTalent[numTalents];
        for (int i = 0; i < numTalents; ++i)
        {
            talent = _talents[i];
            userTalent.name = talent.name;
            userTalent.id = __ToID(i);
            userTalent.flag = (UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{userTalent.id}");
            userTalent.rewardType = talent.rewardType;
            userTalent.rewardCount = talent.rewardCount;
            userTalent.gold = talent.gold;
            userTalents[i] = userTalent;
        }

        onComplete(userTalents);
    }

    public IEnumerator CollectTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        yield return null;

        string key = $"{NAME_SPACE_USER_TALENT_FLAG}{talentID}";
        var flag = (UserTalent.Flag)PlayerPrefs.GetInt(key);
        if ((flag & UserTalent.Flag.Collected) == UserTalent.Flag.Collected)
        {
            onComplete(false);
            
            yield break;
        }

        flag |= UserTalent.Flag.Collected;
        PlayerPrefs.SetInt(key, (int)flag);

        int gold = UserDataMain.gold;
        
        var talent = _talents[__ToIndex(talentID)];
        if (talent.gold > gold)
        {
            onComplete(false);
            
            yield break;
        }

        UserDataMain.gold = gold - talent.gold;

        onComplete(true);
    }

    private uint __ToID(int index) => (uint)(index + 1);
    
    private int __ToIndex(uint id) => (int)(id - 1);
    
    void Awake()
    {
        if (IUserData.instance == null)
        {
            gameObject.AddComponent<UserData>();

            UserData.level = int.MaxValue;
        }

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
        Action<Memory<UserTalent>> onComplete)
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