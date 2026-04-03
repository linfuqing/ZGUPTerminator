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

        [Serializable]
        public struct RewardPool
        {
            public string name;

            public int timesPerDay;

            public RewardPool(string text)
            {
                var parameters = text.Split(':');
                
                name = parameters[0];
                timesPerDay = int.Parse(parameters[1]);
            }
        }
        
        public string name;

        public Flag flag;

        public int energy;

        //public Bounds playerBounds;

        public SpawnerAttribute.Scale spawnerAttribute;
        
        public UserRewardData[] directRewards;
        
        public StageReward[] indirectRewards;

        public StageReward[] duplicateRewards;

        public RewardPool[] rewardPools;

        public UserLevelStageData ToLevel(string levelName, int stage, bool isForce)
        {
            UserLevelStageData result;
            /*Vector3 min = playerBounds.min, max = playerBounds.max;
            result.playerOffset = new Vector3(UnityEngine.Random.Range(min.x, max.x),
                UnityEngine.Random.Range(min.y, max.y),
                UnityEngine.Random.Range(min.z, max.z));*/
            result.spawnerAttributeScale = spawnerAttribute;
            int numQuests = indirectRewards == null ? 0 : indirectRewards.Length, i;
            if (!isForce)
            {
                for (i = 0; i < numQuests; ++i)
                {
                    ref var indirectReward = ref indirectRewards[i];

                    if ((__GetStageRewardFlag(indirectRewards[i].name, levelName, stage, indirectReward.conditionValue,
                            indirectReward.condition, out _, out _) & UserStageReward.Flag.Unlocked) ==
                        UserStageReward.Flag.Unlocked)
                    {
                        isForce = true;
                        break;
                    }
                }
            }

            List<LevelQuest> results = null;
            if (isForce)
            {
                for (i = 0; i < numQuests; ++i)
                {
                    ref var indirectReward = ref indirectRewards[i];
                    
                    if ((__GetStageRewardFlag(indirectReward.name, levelName, stage, indirectReward.conditionValue,
                            indirectReward.condition, out _, out _) & UserStageReward.Flag.Unlocked) ==
                        UserStageReward.Flag.Unlocked)
                        continue;
                    
                    if(results == null)
                        results = new  List<LevelQuest>();
                    
                    results.Add(indirectReward.ToLevel());
                }
            }

            numQuests = duplicateRewards == null ? 0 : duplicateRewards.Length;
            for (i = 0; i < numQuests; ++i)
            {
                if(results == null)
                    results = new  List<LevelQuest>();
                    
                results.Add(duplicateRewards[i].ToLevel());
            }
                
            result.quests = results?.ToArray();
            
            return result;
        }
        
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

                for (int i = 0; i < numParameters; ++i)
                    directRewards[i] = UserRewardData.Parse(parameters[i]);
            }
        }
        
        [CSVField]
        public string 小关间接奖励
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    indirectRewards = null;
                    
                    return;
                }
                
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
        
        [CSVField]
        public string 小关重复奖励
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    duplicateRewards = null;
                    
                    return;
                }
                
                var parameters = value.Split('/');
                
                int numParameters = parameters.Length;
                duplicateRewards = new StageReward[numParameters];

                int i, j, numValues;
                string parameter;
                string[] values;
                for (i = 0; i < numParameters; ++i)
                {
                    parameter = parameters[i];
                    values = parameter.Split(':');
                    
                    ref var duplicateReward = ref duplicateRewards[i];
                    duplicateReward.name = values[0];
                    duplicateReward.condition = (UserStageReward.Condition)int.Parse(values[1]);
                    if (values.Length > 3)
                    {
                        duplicateReward.conditionValue = int.Parse(values[2]);
                        
                        values = values[3].Split('+');
                    }
                    else
                    {
                        duplicateReward.conditionValue = 0;
                        
                        values = values[2].Split('+');
                    }

                    numValues = values.Length;

                    duplicateReward.values = new UserRewardData[numValues];
                    for (j = 0; j < numValues; ++j)
                        duplicateReward.values[j] = new UserRewardData(values[j]);
                }
            }
        }

        [CSVField]
        public string 小关奖池
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    rewardPools = null;
                    
                    return;
                }
                
                var parameters = value.Split('/');
                int numParameters = parameters.Length;
                rewardPools = new RewardPool[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    rewardPools[i] = new RewardPool(parameters[i]);
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

        public LevelQuest ToLevel()
        {
            LevelQuest result;
            result.value = conditionValue;
            
            switch (condition)
            {
                case UserStageReward.Condition.Once:
                    result.type = LevelQuestType.Once;
                    break;
                case UserStageReward.Condition.DamagePercentage:
                    result.type = LevelQuestType.DamagePercentage;
                    break;
                case UserStageReward.Condition.HPPercentage:
                    result.type = LevelQuestType.HPPercentage;
                    break;
                case UserStageReward.Condition.KillCount:
                    result.type = LevelQuestType.KillCount;
                    break;
                case UserStageReward.Condition.Gold:
                    result.type = LevelQuestType.Gold;
                    break;
                case UserStageReward.Condition.Time:
                    result.type = LevelQuestType.Time;
                    break;
                default:
                    result.type = LevelQuestType.Unknown;
                    break;
            }

            return result;
        }
    }

    public IEnumerator QueryStage(
        uint userID,
        uint stageID,
        Action<IUserData.Stage> onComplete)
    {
        yield return __CreateEnumerator();
        
        var levelStage = __GetLevelStageInfo(stageID);
        
        IUserData.Stage result;
        var level = _levels[levelStage.levelIndex];

        var stage = __GetStage(level, levelStage.stageIndex);

        result.energy = stage.energy;
        result.levelEnergy = __GetStage(level, __GetDontCacheStage(level, levelStage.stageIndex)).energy;
        result.cache = (stage.flag & Stage.Flag.DontCache) == Stage.Flag.DontCache
            ? IUserData.StageCache.Empty
            : __GetStageCache(level.name, levelStage.stageIndex, level.cacheType);

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
            userStageReward.id = levelStage.rewardID + (uint)i;
            userStageReward.flag = __GetStageRewardFlag(
                stageReward.name,
                level.name,
                levelStage.stageIndex,
                stageReward.conditionValue,
                stageReward.condition,
                out _, out _);
            userStageReward.condition = stageReward.condition;
            userStageReward.conditionValue = stageReward.conditionValue;
            userStageReward.values = stageReward.values;

            result.rewards[i] = userStageReward;
        }

        onComplete(result);
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
            if (stage == 0)
            {
                onComplete(default);
            
                yield break;
            }
            
            var temp = __GetStage(level, stage);
            if((temp.flag & Stage.Flag.DontCache) == Stage.Flag.DontCache)
            {
                onComplete(default);
            
                yield break;
            }

            energy = temp.energy;
            
            stageCache = __GetStageCache(level.name, stage, level.cacheType);
        }
        else
        {
            energy = 0;
            
            stageCache = IUserData.StageCache.Empty;
        }

        if (!__ApplyEnergy(energy))//(!(0 == stage ? __ApplyLevel(level.name, energy) : __ApplyEnergy(energy)))
        {
            onComplete(default);
            
            yield break;
        }

        __SubmitStageFlag();

        if (__GetLevelTicketIndex(level.name, out _, out int levelTicketIndex))
            __AppendQuest(UserQuest.Type.Ticket + levelTicketIndex, 1);
        else
            __AppendQuest(UserQuest.Type.Chapter, 1);
            
        //__AppendQuest(UserQuest.Type.Stage, 1);

        UserData.StartStage(level.name, stage, 0);

        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = __ToID(levelIndex);
        levelCache.seconds = stageCache.seconds == 0 ? DateTimeUtility.GetSeconds() : stageCache.seconds;
        levelCache.stage = stage;
        levelCache.gold = 0;
        levelCache.killCount = 0;
        levelCache.killBossCount = 0;
        UserData.levelCache = levelCache;
        
        IUserData.StageProperty stageProperty;
        stageProperty.stage = stage;
        stageProperty.purchaseFlag = purchaseFlag;
        stageProperty.cache = stageCache;
        stageProperty.value = __ApplyProperty(
            userID, 
            stageProperty.cache.skills);

        stageProperty.levelStages = new UserLevelStageData[numStages];
        for (int i = 0; i < numStages; ++i)
            stageProperty.levelStages[i] = __GetStage(level, i).ToLevel(level.name, i, !IsLevelChapter(level.name));
        
        onComplete(stageProperty);
    }

    private const string NAME_SPACE_USER_STAGE_REWARD_FLAG = "userStageRewardFlag";

    public IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        var rewardInfo = __GetLevelStageRewardInfo(stageRewardID);
        var stageInfo = __GetLevelStageInfo(rewardInfo.stageID);
        var level = _levels[stageInfo.levelIndex];
        
        var rewards = new List<UserReward>();
        if (__ApplyStageRewards(
                level.name, 
                stageInfo.stageIndex, 
                __GetStage(level, stageInfo.stageIndex).indirectRewards[rewardInfo.index], 
                rewards))
        {
            onComplete(rewards.ToArray());

            yield break;
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

    public int GetMaxStage(uint levelID, int stage)
    {
        var level = _levels[__ToIndex(levelID)];
        int numStages = __GetStageCount(level), i;
        for (i = Mathf.Min(stage, numStages); i < numStages; ++i)
        {
            if ((__GetStage(level, i).flag & Stage.Flag.DontCache) == Stage.Flag.DontCache)
                break;
        }

        return i > 0 ? i - 1 : 0;
    }

    public int GetStageFlag(uint levelID, int stage)
    {
        var level = _levels[__ToIndex(levelID)];
        var indirectRewards = __GetStage(level, stage).indirectRewards;
        int numStageRewards = indirectRewards.Length, result = 0;
        for (int i = 0; i < numStageRewards; ++i)
        {
            ref var stageReward = ref indirectRewards[i];
            if ((__GetStageRewardFlag(
                    stageReward.name,
                    level.name,
                    stage,
                    stageReward.conditionValue,
                    stageReward.condition,
                    out _, out _) & UserStageReward.Flag.Unlocked) == UserStageReward.Flag.Unlocked)
                result |= 1 << i;
        }

        return result;
    }

    public int GetStageEnergy(uint levelID,int stage)
    {
        var level = _levels[__ToIndex(levelID)];
        int numStages = __GetStageCount(level);
        return numStages > stage ? __GetStage(level, stage).energy : 0;
    }
    
    public bool ApplyStage(uint levelID, int stage, out int energy)
    {
        return __ApplyEnergy(GetStageEnergy(levelID, stage), out energy);
    }

    public const string NAME_SPACE_USER_STAGE_DUPLICATE_REWARD_RATIO = "userStageDuplicateRewardRatio";

    public UserRewardData[] CollectStageReward(
        uint levelID, 
        int stage, 
        int startStage, 
        int damagePercentage, 
        int hpPercentage, 
        int killCount, 
        int gold, 
        int time)
    {
        List<UserRewardData> rewards = null;
        var level = _levels[__ToIndex(levelID)];
        var duplicateRewards = __GetStage(level, stage).duplicateRewards;
        if (duplicateRewards != null)
        {
            string key = UserData.GetStageNameSpace(NAME_SPACE_USER_STAGE_DUPLICATE_REWARD_RATIO, level.name, stage);
            var stageFlag = startStage == 0 || (__GetStage(level, stage).flag & Stage.Flag.DontCache) == Stage.Flag.DontCache ? 
                IUserData.StageFlag.Once : IUserData.StageFlag.Normal;
            float ratio;
            foreach (var duplicateReward in duplicateRewards)
            {
                ratio = __GetStageRewardRatio(
                    damagePercentage, 
                    hpPercentage, 
                    killCount, 
                    gold, 
                    time, 
                    duplicateReward.conditionValue, 
                    duplicateReward.condition, 
                    stageFlag);
                
                if (ratio > Mathf.Epsilon && duplicateReward.values != null)
                {
                    PlayerPrefs.SetFloat($"{key}{UserData.SEPARATOR}{duplicateReward.name}", ratio);

                    foreach (var value in duplicateReward.values)
                    {
                        if (rewards == null)
                            rewards = new List<UserRewardData>();
                        
                        rewards.Add(value * Mathf.Clamp01(ratio));
                    }
                }
            }
        }

        return rewards?.ToArray();
    }

    private static IUserData.StageCache __GetStageCache(string levelName, int stage, UserLevel.CacheType cacheType)
    {
        var result = UserData.GetStageCache(levelName, stage);
        switch (cacheType)
        {
            case UserLevel.CacheType.Day:
                if (!DateTimeUtility.IsToday(result.seconds, DateTimeUtility.DataTimeType.UTC))
                    return IUserData.StageCache.Empty;
                break;
            case UserLevel.CacheType.Week:
                if (!DateTimeUtility.IsThisWeek(result.seconds, DateTimeUtility.DataTimeType.UTC))
                    return IUserData.StageCache.Empty;
                break;
            case UserLevel.CacheType.Month:
                if (!DateTimeUtility.IsThisMonth(result.seconds, DateTimeUtility.DataTimeType.UTC))
                    return IUserData.StageCache.Empty;
                break;
        }

        return result;
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
            out var key, out _);
        if ((flag & UserStageReward.Flag.Unlocked) != UserStageReward.Flag.Unlocked ||
            (flag & UserStageReward.Flag.Collected) == UserStageReward.Flag.Collected)
            return false;
                    
        flag |= UserStageReward.Flag.Collected;

        PlayerPrefs.SetInt(key, (int)flag);

        __ApplyRewards(stageReward.values, outRewards);

        return true;
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
    
    private static void __SubmitStageFlag()
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

        if ((flag & Flag.TicketsUnlockFirst) == Flag.TicketsUnlockFirst)
        {
            flag &= ~Flag.TicketsUnlockFirst;

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

    private static UserStageReward.Flag __GetStageRewardFlag(
        string stageRewardName,
        string levelName, 
        int stage, 
        int conditionValue, 
        UserStageReward.Condition condition, 
        out string key, 
        out float ratio)
    {
        key = UserData.GetStageNameSpace(NAME_SPACE_USER_STAGE_REWARD_FLAG, levelName, stage);
        key = $"{key}{UserData.SEPARATOR}{stageRewardName}";
        
        var stageFlag = UserData.GetStageFlag(levelName, stage);
        ratio = __GetStageRewardRatio(
            UserData.GetStageDamagePercentage(levelName, stage), 
            UserData.GetStageHPPercentage(levelName, stage), 
            UserData.GetStageKillCount(levelName, stage), 
            UserData.GetStageGold(levelName, stage), 
            UserData.GetStageTime(levelName, stage), 
            conditionValue, 
            condition, 
            stageFlag);
        
        var flag = (UserStageReward.Flag)PlayerPrefs.GetInt(key);
        if(ratio >= 1.0f)
            flag |= UserStageReward.Flag.Unlocked;

        return flag;
    }

    private static float __GetStageRewardRatio(
        int damagePercentage, 
        int hpPercentage, 
        int killCount, 
        int gold, 
        int time, 
        int conditionValue, 
        UserStageReward.Condition condition, 
        IUserData.StageFlag stageFlag)
    {
        float result;
        switch (condition)
        {
            case UserStageReward.Condition.Normal:
                result = (stageFlag & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal ? 1.0f : 0.0f;

                break;
            case UserStageReward.Condition.Once:
                result = (stageFlag & IUserData.StageFlag.Once) == IUserData.StageFlag.Once ? 1.0f : 0.0f;
                break;
            case UserStageReward.Condition.DamagePercentage:
                if ((stageFlag & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal)
                    result = conditionValue > 0 ? damagePercentage * 1.0f / conditionValue : (damagePercentage > 0.5f ? 1.0f : 0.0f);
                else
                    result = 0.0f;
                break;
            case UserStageReward.Condition.HPPercentage:
                if ((stageFlag & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal)
                    result = conditionValue > 0 ? hpPercentage * 1.0f / conditionValue : 1.0f;
                else
                    result = 0.0f;
                break;
            case UserStageReward.Condition.KillCount:
                if ((stageFlag & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal)
                    result = conditionValue > 0 ? killCount * 1.0f / conditionValue : 1.0f;
                else
                    result = 0.0f;
                break;
            case UserStageReward.Condition.Gold:
                if ((stageFlag & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal)
                    result = conditionValue > 0 ? gold * 1.0f / conditionValue : 1.0f;
                else
                    result = 0.0f;
                break;
            case UserStageReward.Condition.Time:
                if ((stageFlag & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal)
                    result = conditionValue > 0 ? 1.0f - Mathf.Min(1.0f, time * 1.0f / conditionValue) : 0.0f;
                else
                    result = 0.0f;
                break;
            default:
                result = 0.0f;
                break;
        }

        return result;
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
        var userDataMain = UserDataMain.instance;
        if (null == (object)userDataMain)
        {
            yield return null;
            
            int chapterIndex = Chapter.IndexOf(_chapters, levelID);
            var chapter = _chapters[chapterIndex];
            var stageCache = GetStageCache(chapter.name, stage);
            
            StartStage(chapter.name, stage, 0);

            LevelCache levelCache;
            levelCache.name = chapter.name;
            levelCache.id = levelID;
            levelCache.seconds = stageCache.seconds == 0 ? DateTimeUtility.GetSeconds() : stageCache.seconds;
            levelCache.stage = stage;
            levelCache.gold = 0;
            levelCache.killCount = 0;
            levelCache.killBossCount = 0;
            UserData.levelCache = levelCache;
        
            IUserData.StageProperty stageProperty;
            stageProperty.stage = stage;
            stageProperty.purchaseFlag = UserDataMain.purchaseFlag;
            stageProperty.cache = stageCache;
            stageProperty.value = chapter.property;

            stageProperty.levelStages = null;
            
            onComplete(stageProperty);
        }
        else
            yield return userDataMain.ApplyStage(userID, levelID, stage, onComplete);
    }
    
    public IEnumerator SubmitStage(
        uint userID,
        int stage,
        int time, 
        int damagePercentage, 
        int hpPercentage,
        int killCount, 
        int killBossCount, 
        int gold, 
        int rage, 
        int exp, 
        int expMax, 
        string[] skills,
        IUserData.Item[] items,
        Action<IUserData.StageResult> onComplete)
    {
        yield return null;

        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            Debug.LogError("WTF?");

            onComplete(default);
            
            yield break;
        }

        var temp = levelCache.Value;
        int oldStage = temp.stage, startStage = GetStartStage(temp.name, out _);;
        
        IUserData.StageResult result;
        var main = UserDataMain.instance;
        bool isNullMain = null == (object)main;
        if (isNullMain || stage <= oldStage)
        {
            result.totalEnergy = 0;
            result.nextStageEnergy = 0;

            if (!isNullMain)
                oldStage = Mathf.Min(oldStage, main.GetMaxStage(temp.id, stage));
        }
        else
        {
            if (startStage < oldStage)
            {
                result.totalEnergy = 0;
                if (main.IsLevelChapter(temp.name))
                    result.nextStageEnergy = 0;
                else
                {
                    if (!main.ApplyStage(temp.id, oldStage, out result.totalEnergy))
                    {
                        Debug.LogError("WTF?");

                        result.flag = 0;

                        onComplete(default);

                        yield break;
                    }

                    result.nextStageEnergy = main.GetStageEnergy(temp.id, stage);
                }
                
                oldStage = Mathf.Min(oldStage, main.GetMaxStage(temp.id, stage));
            }
            else
            {
                result.totalEnergy = main.energy;

                result.nextStageEnergy = main.GetStageEnergy(temp.id, stage);
            }
        }

        __SubmitStageFlag(temp.name, stage, out _, out _);

        __SetStageKillCount(temp.name, oldStage, killCount - temp.killCount);

        __SetStageKillBossCount(temp.name, oldStage, killBossCount - temp.killBossCount);

        __SetStageGold(temp.name, oldStage, gold);

        //result.flag = 0;
        if (temp.stage < stage)
        {
            __SetStageTime(temp.name, temp.stage, time);

            __SetStageHPPercentage(temp.name, temp.stage, hpPercentage);
            __SetStageDamagePercentage(temp.name, temp.stage, damagePercentage);
            
            __SubmitStageFlag(hpPercentage > 0, /*flag, */temp.name, temp.stage, stage);

            IUserData.StageCache stageCache;
            stageCache.seconds = temp.seconds;
            stageCache.rage = rage;
            stageCache.exp = exp;
            stageCache.expMax = expMax;
            stageCache.skills = skills;
            stageCache.items = items;
            PlayerPrefs.SetString(GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE, temp.name, stage),
                stageCache.ToString());
            
            temp.killCount = Mathf.Max(temp.killCount, killCount);
            temp.killBossCount = Mathf.Max(temp.killBossCount, killBossCount);

            result.rewards =
                isNullMain
                    ? null
                    : main.CollectStageReward(temp.id, temp.stage, startStage, damagePercentage, hpPercentage, killCount,
                        gold, time);
        }
        else
            result.rewards = null;
        
        result.flag = (object)main == null ? (int)GetStageFlag(temp.name, oldStage) : main.GetStageFlag(temp.id, oldStage);

        temp.gold = Mathf.Max(temp.gold, gold);
        temp.stage = stage;
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
}