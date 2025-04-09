using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class UserDataMain
{
    [Serializable]
    public struct Tip_v0
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
    
    [Header("Legacy")]

    [SerializeField]
    internal Tip_v0 _tip_v0;

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
        userTip.max = _tip_v0.max;
        userTip.unitTime = (uint)Mathf.RoundToInt(_tip_v0.uintTime * 1000);
        userTip.tick = (uint)time * TimeSpan.TicksPerSecond + Utc1970.Ticks;
        
        onComplete(userTip);
    }

    public IEnumerator CollectTip(
        uint userID,
        Action<int> onComplete)
    {
        yield return null;

        int tip = _tip_v0.GetValue(out uint time);
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
        if (weaponNames == null || Array.IndexOf(weaponNames, _weapons[__ToIndex(weaponID)].name) == -1)
        {
            onComplete(false);
            
            yield break;
        }

        PlayerPrefs.SetInt(NAME_SPACE_USER_WEAPON_SELECTED, (int)weaponID);
        
        onComplete(true);
    }

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

                    userStage.rewardType = UserStage_v0.RewardType.Gold;
                    userStage.rewardCount = 0;
                    foreach (var reward in stage.indirectRewards)
                    {
                        foreach (var value in reward.values)
                        {
                            switch (value.type)
                            {
                                case UserRewardType.Gold:
                                    userStage.rewardType = UserStage_v0.RewardType.Gold;
                                    userStage.rewardCount = value.count;
                                    userStages[j] = userStage;

                                    break;
                                case UserRewardType.Accessory:
                                    userStage.rewardType = UserStage_v0.RewardType.Weapon;
                                    userStage.rewardCount = value.count;
                                    userStages[j] = userStage;

                                    break;
                            }
                        }
                    }

                    userStages[j] = userStage;

                    ++stageIndex;
                }
                
                onComplete(userStages);
                
                break;
            }
            
            stageIndex += numStages;
        }
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
                foreach (var reward in stage.indirectRewards)
                {
                    foreach (var value in reward.values)
                    {
                        switch (value.type)
                        {
                            case UserRewardType.Gold:
                                gold += value.count;
                                break;
                            case UserRewardType.Accessory:
                                string source = PlayerPrefs.GetString(NAME_SPACE_USER_WEAPONS), destination = stage.name;
                                if (string.IsNullOrEmpty(source))
                                    source = destination;
                                else
                                    source = $"{source},{destination}";

                                PlayerPrefs.SetString(NAME_SPACE_USER_WEAPONS, source);
                                break;
                        }
                    }
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

    private void __CollectLevelLegacy(bool isNextLevel, int levelIndex, int stage)
    {
        var level = _levels[levelIndex];
        if (isNextLevel)
        {
            for (int i = 0; i < stage; ++i)
            {
                foreach (var reward in level.stages[i].directRewards)
                {
                    switch (reward.type)
                    {
                        case UserRewardType.Card:
                            string source = PlayerPrefs.GetString(NAME_SPACE_USER_SKILLS),
                                destination = reward.name;//string.Join(UserData.SEPARATOR, rewardSkills);
                            if (string.IsNullOrEmpty(source))
                                source = destination;
                            else
                                source = $"{source}{UserData.SEPARATOR}{destination}";

                            PlayerPrefs.SetString(NAME_SPACE_USER_SKILLS, source);
                            break;
                    }
                }
            }
        }
        
        int stageIndex = 0;
        for (int i = 0; i < levelIndex; ++i)
            stageIndex += _levels[i].stages.Length;

        string key;
        UserStage_v0.Flag flag;
        for (int i = 0; i < stage; ++i)
        {
            key = $"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{__ToID(stageIndex + i)}";
            flag = (UserStage_v0.Flag)PlayerPrefs.GetInt(key);
            if ((flag & UserStage_v0.Flag.Unlock) != UserStage_v0.Flag.Unlock)
            {
                flag |= UserStage_v0.Flag.Unlock;
                
                PlayerPrefs.SetInt(key, (int)flag);
            }
        }
    }
}

public partial class UserData
{
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

    public IEnumerator QueryStages(
        uint userID,
        Action<Memory<UserStage_v0>> onComplete)
    {
        return UserDataMain.instance.QueryStages(userID, onComplete);
    }

    public IEnumerator CollectStage(
        uint userID,
        uint stageID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.CollectStage(userID, stageID, onComplete);
    }

    public IEnumerator SelectWeapon(
        uint userID,
        uint weaponID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.SelectWeapon(userID, weaponID, onComplete);
    }
    
    public IEnumerator CollectTip(
        uint userID,
        Action<int> onComplete)
    {
        return UserDataMain.instance.CollectTip(userID, onComplete);
    }
}