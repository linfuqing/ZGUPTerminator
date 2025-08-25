using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    internal struct Level
    {
        public string name;
        public int energy;

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
    
    [Header("Level")]

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
        yield return __CreateEnumerator();

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
        int closestStage, 
        Action<IUserData.LevelProperty> onComplete)
    {
        yield return __CreateEnumerator();

        int levelIndex = __ToIndex(levelID);
        
        int userLevel = UserData.level;
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

        __SubmitStageFlag();

        __AppendQuest(UserQuest.Type.Stage, 1);

        IUserData.LevelProperty result;

        for (result.stage = closestStage; result.stage > 0; --result.stage)
        {
            if ((__GetStage(level, result.stage).flag & Stage.Flag.DontCache) == Stage.Flag.DontCache)
                break;
        }

        UserData.StartStage(level.name, result.stage);

        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = levelID;
        levelCache.stage = result.stage;
        levelCache.gold = 0;
        levelCache.killCount = 0;
        levelCache.killBossCount = 0;
        UserData.levelCache = levelCache;

        result.value = __ApplyProperty(userID);

        int numStages = __GetStageCount(level);
        result.spawnerAttributes = new SpawnerAttribute.Scale[numStages];
        for (int i = 0; i < numStages; ++i)
            result.spawnerAttributes[i] = __GetStage(level, i).spawnerAttribute;

        onComplete(result);
    }

    private const string NAME_SPACE_USER_LEVEL_STAGE_FLAG = "UserLevelStageFlag";

    public IEnumerator CollectLevel(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

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

        var level = _levels[levelIndex];
        UserData.EndStage(level.name, levelCache.stage);

        //bool isNextLevel = false;
        if (__GetStageCount(level) == levelCache.stage)
        {
            if (userLevel == levelIndex)
            {
                UserData.level = ++userLevel;

                //isNextLevel = true;
            }
        }

        gold += levelCache.gold;
        
        __AppendQuest(UserQuest.Type.KillCount, levelCache.killCount);
        __AppendQuest(UserQuest.Type.KillBoss, levelCache.killBossCount);

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
        reward.count = levelCache.gold;
        rewards.Add(reward);

        //__CollectLevelLegacy(isNextLevel, levelIndex, levelCache.stage);
        
        onComplete(rewards.ToArray());
    }
}


public partial class UserData
{
    public IEnumerator QueryLevels(
        uint userID,
        Action<IUserData.Levels> onComplete)
    {
        return UserDataMain.instance.QueryLevels(userID, onComplete);
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID,
        int closestStage, 
        Action<IUserData.LevelProperty> onComplete)
    {
        var userDataMain = UserDataMain.instance;
        if (userDataMain == null)
        {
            yield return null;
            
            LevelCache levelCache;
            levelCache.name = _defaultSceneName;
            levelCache.id = levelID;
            levelCache.stage = closestStage;
            levelCache.gold = 0;
            levelCache.killCount = 0;
            levelCache.killBossCount = 0;

            UserData.levelCache = levelCache;
        }
        else
            yield return userDataMain.ApplyLevel(userID, levelID, closestStage, onComplete);
    }

    public IEnumerator CollectLevel(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectLevel(userID, onComplete);
    }
}