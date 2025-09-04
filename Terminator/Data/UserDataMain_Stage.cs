using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    internal struct Stage
    {
        [Flags]
        public enum Flag
        {
            DontCache = 0x01
        }
        
        public string name;

        public Flag flag;

        public int energy;

        public SpawnerAttribute.Scale spawnerAttribute;
        
        public UserRewardData[] directRewards;
        
        public StageReward[] indirectRewards;
        
#if UNITY_EDITOR
        [CSVField]
        public string 小关名字
        {
            set => name = value;
        }
        
        [CSVField]
        public int 小关标签
        {
            set => flag = (Flag)value;
        }

        [CSVField]
        public int 小关体力
        {
            set => energy = value;
        }

        [CSVField]
        public float 小关速度比率
        {
            set => spawnerAttribute.speedScale = value;
        }
        
        [CSVField]
        public float 小关伤害比率
        {
            set => spawnerAttribute.damageScale = value;
        }
        
        [CSVField]
        public float 小关血量比率
        {
            set => spawnerAttribute.hp = value;
        }
        
        [CSVField]
        public float 小关进度比率
        {
            set => spawnerAttribute.level = value;
        }
        
        [CSVField]
        public float 小关经验比率
        {
            set => spawnerAttribute.exp = value;
        }
        
        [CSVField]
        public float 小关金币比率
        {
            set => spawnerAttribute.gold = value;
        }

        [CSVField]
        public string 小关直接奖励
        {
            set
            {
                if(string.IsNullOrEmpty(value))
                {
                    directRewards = null;
                    return;
                }

                var parameters = value.Split('/');
                
                int numParameters = parameters.Length;
                directRewards = new UserRewardData[numParameters];

                string parameter;
                string[] values;
                for (int i = 0; i < numParameters; ++i)
                {
                    parameter = parameters[i];
                    values = parameter.Split(':');
                    
                    ref var directReward = ref directRewards[i];
                    directReward.name = values[0];
                    directReward.type = (UserRewardType)int.Parse(values[1]);
                    directReward.count = int.Parse(values[2]);
                }
            }
        }
        
        [CSVField]
        public string 小关间接奖励
        {
            set
            {
                var parameters = value.Split('/');
                
                int numParameters = parameters.Length;
                indirectRewards = new StageReward[numParameters];

                int i, j, numValues;
                string parameter;
                string[] values;
                for (i = 0; i < numParameters; ++i)
                {
                    parameter = parameters[i];
                    values = parameter.Split(':');
                    
                    ref var indirectReward = ref indirectRewards[i];
                    indirectReward.name = values[0];
                    indirectReward.condition = (UserStageReward.Condition)int.Parse(values[1]);
                    if (values.Length > 3)
                    {
                        indirectReward.conditionValue = int.Parse(values[2]);
                        
                        values = values[3].Split('+');
                    }
                    else
                    {
                        indirectReward.conditionValue = 0;
                        
                        values = values[2].Split('+');
                    }

                    numValues = values.Length;

                    indirectReward.values = new UserRewardData[numValues];
                    for (j = 0; j < numValues; ++j)
                        indirectReward.values[j] = new UserRewardData(values[j]);
                }
            }
        }
#endif
    }

    [Header("Stage")]

    [SerializeField]
    internal Stage[] _stages;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_stages", guidIndex = -1, nameIndex = 0)] 
    internal string _stagesPath;
#endif

    [Serializable]
    internal struct StageReward
    {
        public string name;
        public UserStageReward.Condition condition;
        public int conditionValue;
        public UserRewardData[] values;
    }

    public IEnumerator QueryStage(
        uint userID,
        uint stageID, 
        Action<IUserData.Stage> onComplete)
    {
        yield return __CreateEnumerator();
        
        var levelStage = __GetLevelStageInfo(stageID);
        //if (__TryGetStage(stageID, out int targetStage, out int levelIndex, out int rewardIndex))
        {
            IUserData.Stage result;
            var level = _levels[levelStage.levelIndex];

            var stage = __GetStage(level, levelStage.stageIndex);

            result.energy = stage.energy;
            result.levelEnergy = __GetStage(level, __GetDontCacheStage(level, levelStage.stageIndex)).energy;
            result.cache = (stage.flag & Stage.Flag.DontCache) == Stage.Flag.DontCache ? IUserData.StageCache.Empty : UserData.GetStageCache(level.name, levelStage.stageIndex);

            /*int i, numSkillNames = result.cache.skills == null ? 0 : result.cache.skills.Length;
            result.skillGroupNames = numSkillNames > 0 ? new string[numSkillNames] : null;

            for (i = 0; i < numSkillNames; ++i)
                result.skillGroupNames[i] = __GetSkillGroupName(result.cache.skills[i]);*/
            
            int numStageRewards = stage.indirectRewards.Length;
            result.rewards = new UserStageReward[numStageRewards];
            
            int i;
            StageReward stageReward;
            UserStageReward userStageReward;
            for (i = 0; i < numStageRewards; ++i)
            {
                stageReward = stage.indirectRewards[i];
                userStageReward.name = stageReward.name;
                userStageReward.id = levelStage.rewardID;
                userStageReward.flag = __GetStageRewardFlag(
                    stageReward.name, 
                    level.name, 
                    levelStage.stageIndex,
                    stageReward.conditionValue, 
                    stageReward.condition, 
                    out _);
                userStageReward.condition = stageReward.condition;
                userStageReward.conditionValue = stageReward.conditionValue;
                userStageReward.values = stageReward.values;

                result.rewards[i] = userStageReward;
            }
            
            onComplete(result);

            yield break;
        }

        onComplete(default);
    }

    public IEnumerator ApplyStage(
        uint userID,
        uint levelID,
        int stage, 
        Action<IUserData.StageProperty> onComplete)
    {
        yield return __CreateEnumerator();

        /*if (!__TryGetStage(stageID, out int stageIndex, out int levelIndex, out _))
        {
            onComplete(default);
            
            yield break;
        }*/

        int levelIndex = __ToIndex(levelID);
        var level = _levels[levelIndex];
        
        IUserData.StageCache stageCache;
        int numStages = __GetStageCount(level), energy;
        if (numStages > stage)
        {
            var temp = __GetStage(level, stage);
            energy = temp.energy;

            stageCache = (temp.flag & Stage.Flag.DontCache) == Stage.Flag.DontCache
                ? IUserData.StageCache.Empty
                : UserData.GetStageCache(level.name, stage);
        }
        else
        {
            energy = 0;
            
            stageCache = IUserData.StageCache.Empty;
        }

        if (0 == stage && !__ApplyLevel(level.name, energy))
        {
            onComplete(default);
            
            yield break;
        }
        
        __SubmitStageFlag();

        __AppendQuest(UserQuest.Type.Stage, 1);

        //UserData.StartStage(level.name, stageIndex);

        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = __ToID(levelIndex);
        levelCache.stage = stage;
        levelCache.gold = 0;
        levelCache.killCount = 0;
        levelCache.killBossCount = 0;
        UserData.levelCache = levelCache;
        
        IUserData.StageProperty stageProperty;
        stageProperty.stage = stage;
        stageProperty.cache = stageCache;
        stageProperty.value = __ApplyProperty(
            userID, 
            stageProperty.cache.skills);

        stageProperty.spawnerAttributes = new SpawnerAttribute.Scale[numStages];
        for (int i = 0; i < numStages; ++i)
            stageProperty.spawnerAttributes[i] = __GetStage(level, i).spawnerAttribute;
        
        onComplete(stageProperty);
    }

    private const string NAME_SPACE_USER_STAGE_REWARD_FLAG = "userStageRewardFlag";

    public IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        int i, j, 
            stageRewardIndex = __ToIndex(stageRewardID), 
            numStages, 
            numStageRewards, 
            numChapters = Mathf.Min(_levelChapters.Length, UserData.chapter + 1);
        Level level;
        Stage stage;
        List<UserReward> rewards = null;
        for (i = 0; i < numChapters; ++i)
        {
            level = _levels[__GetLevelIndex(_levelChapters[i].name)];
            numStages = __GetStageCount(level);
            for (j = 0; j < numStages; ++j)
            {
                stage = __GetStage(level, j);

                numStageRewards = stage.indirectRewards.Length;
                if (stageRewardIndex < numStageRewards)
                {
                    if (rewards == null)
                        rewards = new List<UserReward>();
                    
                    if (__ApplyStageRewards(level.name, 
                            j, 
                            stage.indirectRewards[stageRewardIndex], 
                            rewards))
                    {
                        onComplete(rewards.ToArray());

                        yield break;
                    }

                    break;
                }

                stageRewardIndex -= numStageRewards;
            }
        }
        
        onComplete(null);
    }

    public IEnumerator CollectStageRewards(uint userID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        bool result = false;
        int i,
            j,
            k,
            numStages,
            numStageRewards,
            numChapters = Mathf.Min(_levelChapters.Length, UserData.chapter + 1);
        Level level;
        Stage stage;
        StageReward stageReward;
        List<UserReward> rewards = null;
        for (i = 0; i < numChapters; ++i)
        {
            level = _levels[__GetLevelIndex(_levelChapters[i].name)];
            numStages = __GetStageCount(level);
            for (j = 0; j < numStages; ++j)
            {
                stage = __GetStage(level, j);

                numStageRewards = stage.indirectRewards.Length;
                for (k = 0; k < numStageRewards; ++k)
                {
                    stageReward = stage.indirectRewards[k];

                    if (rewards == null)
                        rewards = new List<UserReward>();
                    
                    result |= __ApplyStageRewards(level.name, j, stageReward, rewards);
                }
            }
        }

        onComplete(result ? rewards.ToArray() : null);
    }

    [SerializeField]
    internal UserStage.RewardPool[] _rewardPools;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_rewardPools", guidIndex = -1, nameIndex = 0)] 
    internal string _rewardPoolsPath;
#endif

    public IEnumerator ApplyReward(uint userID, string poolName, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        UserData.ApplyReward(poolName, _rewardPools);

        var rewards = new List<UserReward>();
        
        __ApplyRewards(rewards);
        
        onComplete(rewards.Count > 0 ? rewards.ToArray() : null);
    }
    
    private static int __GetStageCount(in Level level)
    {
#if USER_DATA_VERSION_1
        return level.stages.Length;
#else
        return level.stageNames.Length;
#endif
    }
    
    private Stage __GetStage(in Level level, int stage)
    {
#if USER_DATA_VERSION_1
        return level.stages[stage];
#else
        return _stages[__GetStageIndex(level.stageNames[stage])];
#endif
    }
    
    private Dictionary<string, int> __stageNameToIndices;
    
    private int __GetStageIndex(string name)
    {
        if (__stageNameToIndices == null)
        {
            __stageNameToIndices = new Dictionary<string, int>();

            int numStages = _stages.Length;
            for(int i = 0; i < numStages; ++i)
                __stageNameToIndices.Add(_stages[i].name, i);
        }

        return __stageNameToIndices.TryGetValue(name, out int index) ? index : -1;
    }

    private UserStageReward.Flag __GetStageRewardFlag(
        string stageRewardName,
        string levelName, 
        int stage, 
        int conditionValue, 
        UserStageReward.Condition condition, 
        out string key)
    {
        key = UserData.GetStageNameSpace(NAME_SPACE_USER_STAGE_REWARD_FLAG, levelName, stage);
        key = $"{key}{UserData.SEPARATOR}{stageRewardName}";
        
        var flag = (UserStageReward.Flag)PlayerPrefs.GetInt(key);
        if (flag == 0)
        {
            var stageFlag = UserData.GetStageFlag(levelName, stage);
            switch (condition)
            {
                case UserStageReward.Condition.Normal:
                    if((stageFlag & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal)
                        flag |= UserStageReward.Flag.Unlocked;
                    break;
                case UserStageReward.Condition.Once:
                    if ((stageFlag & IUserData.StageFlag.Once) == IUserData.StageFlag.Once)
                        flag |= UserStageReward.Flag.Unlocked;
                    break;
                case UserStageReward.Condition.NoDamage:
                    if ((stageFlag & IUserData.StageFlag.NoDamage) == IUserData.StageFlag.NoDamage)
                        flag |= UserStageReward.Flag.Unlocked;
                    break;
                case UserStageReward.Condition.KillCount:
                    if ((stageFlag & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal && 
                        UserData.GetStageKillCount(levelName, stage) >= conditionValue)
                        flag |= UserStageReward.Flag.Unlocked;
                    break;
            }
        }

        return flag;
    }

    private bool __ApplyStageRewards(
        string levelName, 
        int stage, 
        in StageReward stageReward, 
        List<UserReward> outRewards)
    {
        var flag = __GetStageRewardFlag(
            stageReward.name,
            levelName,
            stage,
            stageReward.conditionValue, 
            stageReward.condition,
            out var key);
        if ((flag & UserStageReward.Flag.Unlocked) != UserStageReward.Flag.Unlocked ||
            (flag & UserStageReward.Flag.Collected) == UserStageReward.Flag.Collected)
            return false;
                    
        flag |= UserStageReward.Flag.Collected;

        PlayerPrefs.SetInt(key, (int)flag);

        __ApplyRewards(stageReward.values, outRewards);

        return true;
    }
    
    private void __SubmitStageFlag()
    {
        var flag = UserDataMain.flag;
        bool isDirty = (flag & Flag.PurchasesUnlockFirst) == Flag.PurchasesUnlockFirst;
        if(isDirty)
            flag &= ~Flag.PurchasesUnlockFirst;
        
        if ((flag & Flag.CardsUnlockFirst) == Flag.CardsUnlockFirst)
        {
            flag &= ~Flag.CardsUnlockFirst;

            isDirty = true;
        }

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
    }

    private int __GetDontCacheStage(Level level, int closestStage)
    {
        int stage;
        for (stage = closestStage; stage > 0; --stage)
        {
            if ((__GetStage(level, stage).flag & Stage.Flag.DontCache) == Stage.Flag.DontCache)
                break;
        }

        return stage;
    }
}

public partial class UserData
{
    public IEnumerator QueryStage(
        uint userID,
        uint stageID,
        Action<IUserData.Stage> onComplete)
    {
        return UserDataMain.instance.QueryStage(userID, stageID, onComplete);
    }

    public IEnumerator ApplyStage(
        uint userID,
        uint levelID,
        int stage, 
        Action<IUserData.StageProperty> onComplete)
    {
        return UserDataMain.instance.ApplyStage(userID, levelID, stage, onComplete);
    }
    
    public IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectStageReward(userID, stageRewardID, onComplete);
    }

    public IEnumerator CollectStageRewards(uint userID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectStageRewards(userID, onComplete);
    }

    [SerializeField]
    internal UserStage.RewardPool[] _rewardPools;

    public IEnumerator ApplyReward(uint userID, string poolName, Action<Memory<UserReward>> onComplete)
    {
        var main = UserDataMain.instance;
        if (null == (object)main)
        {
            yield return null;
            
            int startRewardIndex = Rewards.Count;
            
            ApplyReward(poolName, _rewardPools);

            int numRewards = Rewards.Count;

            if (numRewards > startRewardIndex)
            {
                int index;
                UserRewardData source;
                UserReward destination;
                var rewards = new UserReward[numRewards - startRewardIndex];
                for (int i = startRewardIndex; i < numRewards; ++i)
                {
                    source = Rewards[i];

                    index = i - startRewardIndex;

                    destination = rewards[index];
                    destination.name = source.name;
                    destination.id = 0;
                    destination.type = source.type;
                    destination.count = source.count;

                    rewards[index] = destination;
                }

                onComplete(rewards);
            }
            else
                onComplete(null);
        }
        else
            yield return main.ApplyReward(userID, poolName, onComplete);
    }
}