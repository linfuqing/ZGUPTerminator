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
    private const string NAME_SPACE_USER_ENERGY_TIME = "UserEnergyTime";

    [SerializeField]
    internal Energy _energy;

    public IEnumerator QueryUser(
        string channelName, 
        string channelUser,
        Action<User, UserEnergy> onComplete)
    {
        yield return null;
        
        User user;
        user.id = UserData.id;
        user.gold = gold;
        //user.level = UserData.level;

        var dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        var timeUnix = DateTime.UtcNow - dateTime;

        int time = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY_TIME);
        if (time == 0)
        {
            time = (int)timeUnix.TotalSeconds;
            
            PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGY_TIME, time);
        }
        
        UserEnergy userEnergy;
        userEnergy.value = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY, _energy.max);
        userEnergy.max = _energy.max;
        userEnergy.unitTime = (uint)Mathf.RoundToInt(_energy.uintTime * 1000);
        userEnergy.tick = (uint)time * TimeSpan.TicksPerSecond + dateTime.Ticks;
        
        onComplete(user, userEnergy);
    }

    [Serializable]
    public struct Tip
    {
        public int max;
        public float uintTime;

        public int value => GetValue(out _);

        public int GetValue(int value, uint utcTime, out uint time)
        {
            var timeUnix = DateTime.UtcNow - Utc1970;

            uint now = (uint)timeUnix.TotalSeconds;
            
            time = now;
            if (uintTime > Mathf.Epsilon)
            {
                float tipFloat = (time - (utcTime > 0 ? utcTime : time)) / uintTime;
                int tipInt =  Mathf.FloorToInt(tipFloat);
                value += tipInt;

                time -= (uint)Mathf.RoundToInt((tipFloat - tipInt) * uintTime);
            }
        
            if (value >= max)
            {
                value = max;

                time = now;
            }

            return value;
        }

        public int GetValue(out uint time)
        {
            return GetValue(PlayerPrefs.GetInt(NAME_SPACE_USER_TIP), 
                (uint)PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME),
                out time);
        }
    }

    private const string NAME_SPACE_USER_TIP = "UserTip";
    private const string NAME_SPACE_USER_TIP_TIME = "UserTipTime";
    private static readonly DateTime Utc1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SerializeField]
    internal Tip _tip;

    public IEnumerator QueryTip(
        uint userID,
        Action<UserTip> onComplete)
    {
        yield return null;

        var timeUnix = DateTime.UtcNow - Utc1970;
        int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
        if (time == 0)
        {
            time = (int)timeUnix.TotalSeconds;
            
            PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time);
        }

        UserTip userTip;
        userTip.value = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP);
        userTip.max = _tip.max;
        userTip.unitTime = (uint)Mathf.RoundToInt(_tip.uintTime * 1000);
        userTip.tick = (uint)time * TimeSpan.TicksPerSecond + Utc1970.Ticks;
        
        onComplete(userTip);
    }

    public IEnumerator CollectTip(
        uint userID,
        Action<int> onComplete)
    {
        yield return null;

        /*var timeUnix = DateTime.UtcNow - Utc1970;
        
        uint now = (uint)timeUnix.TotalSeconds, time = now;
        int tip = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP);
        if (_tip.uintTime > Mathf.Epsilon)
        {
            float tipFloat = (time - (uint)PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME, (int)time)) / _tip.uintTime;
            int tipInt =  Mathf.FloorToInt(tipFloat);
            tip += tipInt;

            time -= (uint)Mathf.RoundToInt((tipFloat - tipInt) * _tip.uintTime);
        }
        
        if (tip >= _tip.max)
        {
            tip = _tip.max;

            time = now;
        }*/

        int tip = _tip.GetValue(out uint time);
        gold += tip;

        PlayerPrefs.SetInt(NAME_SPACE_USER_TIP, 0);
        PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, (int)time);

        onComplete(tip);
    }

    
    private const string NAME_SPACE_USER_SKILLS = "UserSkills";

    public IEnumerator QuerySkills(
        uint userID,
        Action<Memory<UserSkill>> onComplete)
    {
        yield return null;
        
        var skillString = PlayerPrefs.GetString(NAME_SPACE_USER_SKILLS);
        string[] skills;
        if (string.IsNullOrEmpty(skillString))
        {
            //skillString = string.Join(SEPARATOR, _defaultSkills);

            skills = null;
        }
        else
            skills = skillString.Split(UserData.SEPARATOR);

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

        var weaponString = PlayerPrefs.GetString(NAME_SPACE_USER_WEAPONS);
        var weaponNames = string.IsNullOrEmpty(weaponString) ? null : weaponString.Split(UserData.SEPARATOR);
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

        var weaponString = PlayerPrefs.GetString(NAME_SPACE_USER_WEAPONS);
        var weaponNames = string.IsNullOrEmpty(weaponString) ? null : weaponString.Split(UserData.SEPARATOR);
        if (weaponNames == null || weaponNames.IndexOf(_weapons[__ToIndex(weaponID)].name) == -1)
        {
            onComplete(false);
            
            yield break;
        }

        PlayerPrefs.SetInt(NAME_SPACE_USER_WEAPON_SELECTED, (int)weaponID);
        
        onComplete(true);
    }

    [Serializable]
    internal partial struct Stage
    {
        public string name;
        
        public UserStage_v0.RewardType rewardType;
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

    [SerializeField]
    internal Level[] _levels;

    [SerializeField, CSV("_levels", guidIndex = -1, nameIndex = 0)] 
    internal string _levelsPath;

    public IEnumerator QueryLevels(
        uint userID,
        Action<Memory<UserLevel>> onComplete)
    {
        yield return null;

        int i, levelIndex = UserData.level, numLevels = Mathf.Clamp(levelIndex + 1, 1, _levels.Length);
        Level level;
        UserLevel userLevel;
        var userLevels = new UserLevel[numLevels];
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            userLevel.name = level.name;
            userLevel.id = __ToID(i);
            userLevel.energy = level.energy;
            userLevel.rewardSkills = levelIndex > i ? null : level.rewardSkills;

            userLevels[i] = userLevel;
        }
        
        onComplete(userLevels);
    }

    private const string NAME_SPACE_USER_LEVEL_STAGE_FLAG = "UserLevelStageFlag";
    
    public IEnumerator QueryStages(
        uint userID,
        Action<Memory<UserStage_v0>> onComplete)
    {
        yield return null;

        int i, j, numStages, stageIndex = 0, 
            numLevels = Mathf.Min(UserData.level + 1, _levels.Length), 
            levelEnd = numLevels - 1;
        Level level;
        UserStage_v0 userStage;
        Stage stage;
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            
            numStages = level.stages == null ? 0 : level.stages.Length;
            for (j = 0; j < numStages; ++j)
            {
                if (((UserStage_v0.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{__ToID(stageIndex + j)}") &
                     UserStage_v0.Flag.Collected) != UserStage_v0.Flag.Collected)
                    break;
            }

            if (j < numStages || i == levelEnd)
            {
                var userStages = new UserStage_v0[numStages];
                for (j = 0; j < numStages; ++j)
                {
                    stage = level.stages[j];

                    userStage.name = stage.name;
                    userStage.id = __ToID(stageIndex);
                    userStage.flag = (UserStage_v0.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{userStage.id}");
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
        
        int userLevel = UserData.level, levelIndex = __ToIndex(levelID);
        if (userLevel < levelIndex)
        {
            onComplete(false);
            
            yield break;
        }
        
        var level = _levels[levelIndex];
        if (!__ApplyEnergy(level.energy))
        {
            onComplete(false);

            yield break;
        }
        
        UserData.LevelCache levelCache;
        levelCache.id = levelID;
        levelCache.stage = 0;
        levelCache.gold = 0;
        UserData.levelCache = levelCache;
        
        onComplete(true);
    }

    public IEnumerator CollectLevel(
        uint userID,
        Action<int, string[]> onComplete)
    {
        yield return null;

        var temp = UserData.levelCache;
        if (temp == null)
        {
            onComplete(0, Array.Empty<string>());
            
            yield break;
        }

        UserData.levelCache = null;

        var levelCache = temp.Value;
        
        int userLevel = UserData.level, levelIndex = __ToIndex(levelCache.id);
        if (userLevel < levelIndex)
        {
            onComplete(0, null);
            
            yield break;
        }

        string[] rewardSkills = Array.Empty<string>();
        if (userLevel == levelIndex)
        {
            var level = _levels[levelIndex];
            if ((level.stages == null ? 0 : level.stages.Length) == levelCache.stage)
            {
                UserData.level = ++userLevel;

                rewardSkills = level.rewardSkills;

                string source = PlayerPrefs.GetString(NAME_SPACE_USER_SKILLS),
                    destination = string.Join(UserData.SEPARATOR, rewardSkills);
                if (string.IsNullOrEmpty(source))
                    source = destination;
                else
                    source = $"{source}{UserData.SEPARATOR}{destination}";

                PlayerPrefs.SetString(NAME_SPACE_USER_SKILLS, source);
            }
        }

        int gold = levelCache.gold;
        UserDataMain.gold += gold;

        int stageIndex = 0;
        for (int i = 0; i < levelIndex; ++i)
            stageIndex += _levels[i].stages.Length;

        string key;
        UserStage_v0.Flag flag;
        for (int i = 0; i < levelCache.stage; ++i)
        {
            key = $"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{__ToID(stageIndex + i)}";
            flag = (UserStage_v0.Flag)PlayerPrefs.GetInt(key);
            if ((flag & UserStage_v0.Flag.Unlock) != UserStage_v0.Flag.Unlock)
            {
                flag |= UserStage_v0.Flag.Unlock;
                
                PlayerPrefs.SetInt(key, (int)flag);
            }
        }
        
        onComplete(gold, rewardSkills);
    }

    public IEnumerator CollectStage(
        uint userID,
        uint stageID,
        Action<bool> onComplete)
    {
        yield return null;
        
        string key = $"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{stageID}";
        var flag = (UserStage_v0.Flag)PlayerPrefs.GetInt(key);
        if ((flag & UserStage_v0.Flag.Unlock) != UserStage_v0.Flag.Unlock ||
            (flag & UserStage_v0.Flag.Collected) == UserStage_v0.Flag.Collected)
        {
            onComplete(false);
            
            yield break;
        }

        int stageIndex = __ToIndex(stageID), numStages, numLevels = Mathf.Min(_levels.Length, UserData.level + 1);
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
                    case UserStage_v0.RewardType.Gold:
                        gold += stage.rewardCount;
                        break;
                    case UserStage_v0.RewardType.Weapon:
                        string source = PlayerPrefs.GetString(NAME_SPACE_USER_WEAPONS), destination = stage.name;
                        if (string.IsNullOrEmpty(source))
                            source = destination;
                        else
                            source = $"{source},{destination}";

                        PlayerPrefs.SetString(NAME_SPACE_USER_WEAPONS, source);
                        break;
                }

                flag |= UserStage_v0.Flag.Collected;

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

        int gold = UserDataMain.gold;
        
        var talent = _talents[__ToIndex(talentID)];
        if (talent.gold > gold)
        {
            onComplete(false);
            
            yield break;
        }

        UserDataMain.gold = gold - talent.gold;

        flag |= UserTalent.Flag.Collected;
        PlayerPrefs.SetInt(key, (int)flag);

        onComplete(true);
    }

    private bool __ApplyEnergy(int value)
    {
        var timeUnix = DateTime.UtcNow - new DateTime(
            1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        uint now = (uint)timeUnix.TotalSeconds, time = now;
        int energy = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY, _energy.max);
        if (_energy.uintTime > Mathf.Epsilon)
        {
            float energyFloat = (time - (uint)PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY_TIME, (int)time)) /
                                _energy.uintTime;
            int energyInt =  Mathf.FloorToInt(energyFloat);
            energy += energyInt;

            time -= (uint)Mathf.RoundToInt((energyFloat - energyInt) * _energy.uintTime);
        }

        if (energy >= _energy.max)
        {
            energy = _energy.max;

            time = now;
        }
        
        energy -= value;
        if (energy < 0)
            return false;
        
        PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGY, energy);
        PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGY_TIME, (int)time);

        return true;
    }

    private uint __ToID(int index) => (uint)(index + 1);
    
    private int __ToIndex(uint id) => (int)(id - 1);
    
#if UNITY_EDITOR
    [SerializeField]
    internal bool _isDebugLevel = true;
#endif
    
    void Awake()
    {
        if (IUserData.instance == null)
        {
            gameObject.AddComponent<UserData>();

#if UNITY_EDITOR
            if(_isDebugLevel)
#endif
            UserData.level = int.MaxValue - 1;
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

    public IEnumerator QueryTip(
        uint userID,
        Action<UserTip> onComplete)
    {
        return UserDataMain.instance.QueryTip(userID, onComplete);
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
        Action<Memory<UserStage_v0>> onComplete)
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

    public IEnumerator CollectLevel(
        uint userID,
        Action<int, string[]> onComplete)
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

    public IEnumerator CollectTip(
        uint userID,
        Action<int> onComplete)
    {
        return UserDataMain.instance.CollectTip(userID, onComplete);
    }

    public IEnumerator SelectWeapon(
        uint userID,
        uint weaponID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.SelectWeapon(userID, weaponID, onComplete);
    }

}