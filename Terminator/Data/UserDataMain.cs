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

    [Header("Main")]
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
    internal struct Level
    {
        public string name;
        public int energy;

        public Stage[] stages;
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

        bool isUnlock = true;
        int i, j, k, 
            numStageRewards, 
            numStages, 
            stageIndex = 0, 
            levelIndex = UserData.level, 
            numLevels = Mathf.Clamp(levelIndex + 1, 1, _levels.Length);
        StageReward stageReward;
        Level level;
        Stage stage;
        UserStage userStage;
        UserLevel userLevel;
        var userLevels = new UserLevel[numLevels];
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            userLevel.name = level.name;
            userLevel.id = __ToID(i);
            userLevel.energy = level.energy;
            
            numStages = level.stages.Length;
            userLevel.stages = new UserStage[numStages];
            for (j = 0; j < numStages; ++j)
            {
                stage = level.stages[j];
                userStage.name = stage.name;
                userStage.id = __ToID(stageIndex++);
                
                if (isUnlock)
                {
                    userStage.rewards = null;
                    numStageRewards = stage.indirectRewards.Length;
                    userStage.rewardFlags = new UserStageReward.Flag[numStageRewards];
                    for (k = 0; k < numStageRewards; ++k)
                    {
                        stageReward = stage.indirectRewards[k];
                        userStage.rewardFlags[k] = __GetStageRewardFlag(
                            stageReward.name,
                            level.name,
                            j,
                            stageReward.condition,
                            out _);
                    }
                    
                    isUnlock = (UserData.GetStageFlag(level.name, j) & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal;
                }
                else
                {
                    userStage.rewards = stage.directRewards;
                    userStage.rewardFlags = null;
                }

                userStage.rewardPools = stage.rewardPools;

                userLevel.stages[j] = userStage;
            }

            userLevels[i] = userLevel;
        }
        
        onComplete(userLevels);
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID,
        Action<UserPropertyData> onComplete)
    {
        yield return null;
        
        int userLevel = UserData.level, levelIndex = __ToIndex(levelID);
        if (userLevel < levelIndex)
        {
            onComplete(default);
            
            yield break;
        }
        
        var level = _levels[levelIndex];
        if (!__ApplyEnergy(level.energy))
        {
            onComplete(default);

            yield break;
        }
        
        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = levelID;
        levelCache.stage = 0;
        levelCache.gold = 0;
        UserData.levelCache = levelCache;

        UserPropertyData result;
        result.skills = Array.Empty<UserPropertyData.Skill>();
        result.skillVariables = Array.Empty<UserPropertyData.SkillVariable>();
        result.attributes = Array.Empty<UserAttributeData>();
        
        onComplete(result);
    }

    private const string NAME_SPACE_USER_LEVEL_STAGE_FLAG = "UserLevelStageFlag";

    public IEnumerator CollectLevel(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        yield return null;

        var temp = UserData.levelCache;
        if (temp == null)
        {
            onComplete(null);
            
            yield break;
        }

        UserData.levelCache = null;

        var levelCache = temp.Value;
        
        int userLevel = UserData.level, levelIndex = __ToIndex(levelCache.id);
        if (userLevel < levelIndex)
        {
            onComplete(null);
            
            yield break;
        }

        bool isNextLevel = false;
        var level = _levels[levelIndex];
        if ((level.stages == null ? 0 : level.stages.Length) == levelCache.stage)
        {
            UserData.SubmitStageFlag(level.name, levelCache.stage);
            
            if (userLevel == levelIndex)
            {
                UserData.level = ++userLevel;

                isNextLevel = true;
            }
        }

        int gold = levelCache.gold;
        UserDataMain.gold += gold;

        string key;
        Stage stage;
        var rewards = new List<UserReward>();
        for(int i = 0; i < levelCache.stage; ++i)
        {
            stage = level.stages[i];
            key = $"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{stage.name}";
            if(PlayerPrefs.GetInt(key) != 0)
                continue;
            
            PlayerPrefs.SetInt(key, 1);
            
            __ApplyRewards(stage.directRewards, rewards);
        }

        __CollectLevelLegacy(isNextLevel, levelIndex, levelCache.stage);
        
        onComplete(rewards.ToArray());
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

    public IEnumerator QueryLevels(
        uint userID,
        Action<Memory<UserLevel>> onComplete)
    {
        return UserDataMain.instance.QueryLevels(userID, onComplete);
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID,
        Action<UserPropertyData> onComplete)
    {
        return UserDataMain.instance.ApplyLevel(userID, levelID, onComplete);
    }

    public IEnumerator CollectLevel(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectLevel(userID, onComplete);
    }
}