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
        
        if (__TryGetStage(stageID, out int targetStage, out int levelIndex, out int rewardIndex))
        {
            IUserData.Stage result;
            var level = _levels[levelIndex];

            var stage = __GetStage(level, targetStage);

            result.energy = stage.energy;
            result.levelEnergy = level.energy;
            result.cache = (stage.flag & Stage.Flag.DontCache) == Stage.Flag.DontCache ? IUserData.StageCache.Empty : UserData.GetStageCache(level.name, targetStage);

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
                userStageReward.id = __ToID(rewardIndex + i);
                userStageReward.flag = __GetStageRewardFlag(
                    stageReward.name, 
                    level.name, 
                    targetStage,
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
        uint stageID,
        Action<IUserData.StageProperty> onComplete)
    {
        yield return __CreateEnumerator();

        if (!__TryGetStage(stageID, out int stageIndex, out int levelIndex, out _))
        {
            onComplete(default);
            
            yield break;
        }

        int userLevel = UserData.level;
        if (userLevel < levelIndex)
        {
            onComplete(default);
            
            yield break;
        }
        
        var level = _levels[levelIndex];
        var stage = __GetStage(level, stageIndex);
        if (!__ApplyEnergy(stage.energy))
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
        levelCache.stage = stageIndex;
        levelCache.gold = 0;
        levelCache.killCount = 0;
        levelCache.killBossCount = 0;
        UserData.levelCache = levelCache;
        
        IUserData.StageProperty stageProperty;
        stageProperty.stage = stageIndex;
        stageProperty.cache = (stage.flag & Stage.Flag.DontCache) == Stage.Flag.DontCache ? IUserData.StageCache.Empty : UserData.GetStageCache(level.name, stageIndex);
        stageProperty.value = __ApplyProperty(
            userID, 
            stageProperty.cache.skills);

        int numStages = __GetStageCount(level);
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
            numLevels = Mathf.Min(_levels.Length, UserData.level + 1);
        Level level;
        Stage stage;
        List<UserReward> rewards = null;
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
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
            numLevels = Mathf.Min(_levels.Length, UserData.level + 1);
        Level level;
        Stage stage;
        StageReward stageReward;
        List<UserReward> rewards = null;
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
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

        /*bool isSelected;
        float chance, total;
        var results = new List<UserRewardData>();
        foreach (var rewardPool in _rewardPools)
        {
            if (rewardPool.name == poolName)
            {
                isSelected = false;
                chance = UnityEngine.Random.value;
                total = 0.0f;
                foreach (var option in rewardPool.options)
                {
                    total += option.chance;
                    if (total > 1.0f)
                    {
                        total -= 1.0f;
                        
                        chance = UnityEngine.Random.value;

                        isSelected = false;
                    }
                    
                    if(isSelected || total < chance)
                        continue;

                    isSelected = true;

                    results.Add(option.value);
                }
                
                break;
            }
        }*/
        
        UserData.ApplyReward(poolName, _rewardPools);

        var rewards = new List<UserReward>();
        
        __ApplyRewards(rewards);
        
        onComplete(rewards.Count > 0 ? rewards.ToArray() : null);
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
        uint stageID,
        Action<IUserData.StageProperty> onComplete)
    {
        return UserDataMain.instance.ApplyStage(userID, stageID, onComplete);
    }
    
    public IEnumerator SubmitStage(
        uint userID,
        IUserData.StageFlag flag,
        int stage,
        int killCount, 
        int killBossCount, 
        int gold, 
        int rage, 
        int exp, 
        int expMax, 
        string[] skills,
        Action<int> onComplete)
    {
        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            UnityEngine.Debug.LogError("WTF?");

            onComplete(0);
            
            yield break;
        }

        var temp = levelCache.Value;
        if (temp.stage >= stage)
        {
            UnityEngine.Debug.LogError("WTF?");
            
            onComplete(0);
            
            yield break;
        }

        yield return null;

        __SubmitStageFlag(flag, temp.name, temp.stage, stage);
        
        __SetStageKillCount(temp.name, temp.stage, killCount);

        __SetStageKillBossCount(temp.name, temp.stage, killBossCount);
        
        int result = (int)GetStageFlag(temp.name, temp.stage);

        IUserData.StageCache stageCache;
        stageCache.rage = rage;
        stageCache.exp = exp;
        stageCache.expMax = expMax;
        stageCache.skills = skills;
        PlayerPrefs.SetString(GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE, temp.name, stage), stageCache.ToString());
        
        temp.stage = stage;
        temp.gold = gold;
        UserData.levelCache = temp;
        
        onComplete(result);
        
        //return null;
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