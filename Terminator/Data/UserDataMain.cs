using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public sealed partial class UserDataMain : MonoBehaviour
{
    [Flags]
    private enum Flag
    {
        PurchasesUnlockFirst = 0x0001, 
        PurchasesUnlock = 0x0002 | PurchasesUnlockFirst, 
        
        TalentsUnlockFirst = 0x0004,
        TalentsUnlock = 0x0008 | TalentsUnlockFirst, 

        CardsCreated = 0x0010, 
        CardsUnlockFirst = 0x0020, 
        CardsUnlock = 0x0040 | CardsUnlockFirst, 
        
        CardUnlockFirst = 0x0080, 
        CardUnlock = 0x0100 | CardUnlockFirst, 

        RolesCreated = 0x0200, 
        RolesUnlockFirst = 0x0400, 
        RolesUnlock = 0x0800 | RolesUnlockFirst, 
        
        //RoleUnlockFirst = 0x1000, 
        //RoleUnlock = 0x2000 | RoleUnlockFirst, 
        
        UnlockFirst = PurchasesUnlockFirst | TalentsUnlockFirst | CardsUnlockFirst | CardUnlockFirst | RolesUnlockFirst// | RoleUnlockFirst
    }
    
    private const string NAME_SPACE_USER_FLAG = "UserFlag";

    private static Flag flag
    {
        get => (Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_FLAG);
        
        set => PlayerPrefs.SetInt(NAME_SPACE_USER_FLAG, (int)value);
    }
    
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

 #if USER_DATA_VERSION_1
        public Stage[] stages;
#endif
        
        public string[] stageNames;
        
#if UNITY_EDITOR
        [CSVField]
        public string 章节名字
        {
            set => name = value;
        }
        
        [CSVField]
        public int 章节体力
        {
            set => energy = value;
        }
        
        [CSVField]
        public string 章节小关
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    stageNames = null;

                    return;
                }
                
                stageNames = value.Split('/');
            }
        }
#endif
    }

    [SerializeField]
    internal Level[] _levels;

#if UNITY_EDITOR
    [SerializeField, CSV("_levels", guidIndex = -1, nameIndex = 0)] 
    internal string _levelsPath;
#endif
    
    public IEnumerator QueryLevels(
        uint userID,
        Action<IUserData.Levels> onComplete)
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
            
            numStages = __GetStageCount(level);
            userLevel.stages = new UserStage[numStages];
            for (j = 0; j < numStages; ++j)
            {
                stage = __GetStage(level, j);
                userStage.name = stage.name;
                userStage.id = __ToID(stageIndex++);
                userStage.energy = stage.energy;
                
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
                            stageReward.conditionValue, 
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

                //userStage.rewardPools = stage.rewardPools;

                userLevel.stages[j] = userStage;
            }

            userLevels[i] = userLevel;
        }

        IUserData.Levels result;
        result.flag = (flag & Flag.UnlockFirst) == 0 ? 0 : IUserData.Levels.Flag.UnlockFirst;
        result.levels = userLevels;
        
        onComplete(result);
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID,
        Action<IUserData.Property> onComplete)
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

        var flag = UserDataMain.flag;
        bool isDirty = (flag & Flag.CardsUnlockFirst) == Flag.CardsUnlockFirst;
        if(isDirty)
            flag &= ~Flag.CardsUnlockFirst;

        if ((flag & Flag.TalentsUnlock) == 0 && (flag & Flag.CardsUnlock) != 0/*PlayerPrefs.GetInt(NAME_SPACE_USER_CARDS_CAPACITY) > 3*/)
        {
            flag |= Flag.TalentsUnlock;

            isDirty = true;
        }

        /*if ((flag & Flag.RolesUnlock) != 0 && (flag & Flag.RoleUnlock) == 0)
        {
            flag |= Flag.RoleUnlock;

            isDirty = true;
        }*/
        
        if(isDirty)
            UserDataMain.flag = flag;
        
        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = levelID;
        levelCache.stage = 0;
        levelCache.gold = 0;
        UserData.levelCache = levelCache;

        onComplete(__ApplyProperty(userID));
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
        if (__GetStageCount(level) == levelCache.stage)
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
            stage = __GetStage(level, i);
            key = $"{NAME_SPACE_USER_LEVEL_STAGE_FLAG}{stage.name}";
            if(PlayerPrefs.GetInt(key) != 0)
                continue;
            
            PlayerPrefs.SetInt(key, 1);
            
            __ApplyRewards(stage.directRewards, rewards);
        }

        UserReward reward;
        reward.name = null;
        reward.id = 0;
        reward.type = UserRewardType.Gold;
        reward.count = gold;
        rewards.Add(reward);

        __CollectLevelLegacy(isNextLevel, levelIndex, levelCache.stage);
        
        onComplete(rewards.ToArray());
    }

    private bool __ApplyEnergy(int value)
    {
        var timeUnix = DateTime.UtcNow - Utc1970;
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
    [SerializeField]
    internal string _defaultSceneName = "S1";
    
    public IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<User, UserEnergy> onComplete)
    {
        return UserDataMain.instance.QueryUser(channelName, channelUser, onComplete);
    }

    public IEnumerator QueryLevels(
        uint userID,
        Action<IUserData.Levels> onComplete)
    {
        return UserDataMain.instance.QueryLevels(userID, onComplete);
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID,
        Action<IUserData.Property> onComplete)
    {
        var userDataMain = UserDataMain.instance;
        if (userDataMain == null)
        {
            yield return null;
            
            LevelCache levelCache;
            levelCache.name = _defaultSceneName;
            levelCache.id = levelID;
            levelCache.gold = 0;
            levelCache.stage = 0;

            UserData.levelCache = levelCache;
        }
        else
            yield return userDataMain.ApplyLevel(userID, levelID, onComplete);
    }

    public IEnumerator CollectLevel(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectLevel(userID, onComplete);
    }
}