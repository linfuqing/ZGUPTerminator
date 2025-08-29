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

    [SerializeField] 
    internal string[] _levelNames;

#if UNITY_EDITOR
    [SerializeField, CSV("_levelNames", guidIndex = -1, nameIndex = 0)] 
    internal string _levelNamesPath;
#endif

    [Serializable]
    internal struct LevelTicket
    {
        public string name;
        
        public int level;

        public int capacity;
        
        [CSVField]
        public string 章节门票名字
        {
            set => name = value;
        }
        
        [CSVField]
        public int 章节门票解锁需要章节数
        {
            set => level = value;
        }
        
        [CSVField]
        public int 章节门票每日次数
        {
            set => capacity = value;
        }
    }

    [SerializeField] 
    internal LevelTicket[] _levelTickets;

#if UNITY_EDITOR
    [SerializeField, CSV("_levelTickets", guidIndex = -1, nameIndex = 0)] 
    internal string _levelTicketsPath;
#endif

    public IEnumerator QueryLevels(
        uint userID,
        Action<IUserData.Levels> onComplete)
    {
        yield return __CreateEnumerator();

        bool isUnlock = true;
        int stageIndex = 0, 
            levelIndex = UserData.level, 
            numLevels = Mathf.Clamp(levelIndex + 1, 1, _levels.Length);
        var userLevels = new UserLevel[numLevels];
        for (int i = 0; i < numLevels; ++i)
            userLevels[i] = __ToUserLevel(i, ref stageIndex, ref isUnlock);

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
        
        var rewards = new List<UserReward>();
        int stageCount, 
            levelIndex = __ToIndex(levelCache.id), 
            ticketIndex = __GetLevelTicketIndex(levelCache.name);
        var level = _levels[levelIndex];
        if (ticketIndex == -1)
        {
            int userLevel = UserData.level;

            if (userLevel < levelIndex)
            {
                onComplete(null);

                yield break;
            }

            stageCount = __GetStageCount(level);
            
            if (stageCount == levelCache.stage && userLevel == levelIndex)
                UserData.level = ++userLevel;
        }
        else
            stageCount = __GetStageCount(level);

        UserData.EndStage(level.name, levelCache.stage);

        gold += levelCache.gold;
        
        __AppendQuest(UserQuest.Type.KillCount, levelCache.killCount);
        __AppendQuest(UserQuest.Type.KillBoss, levelCache.killBossCount);

        stageCount = Mathf.Min(stageCount, levelCache.stage);

        string key;
        Stage stage;
        for(int i = 0; i < stageCount; ++i)
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
    
    private static readonly string NAME_SPACE_USER_LEVEL_TICKET = "UserLevelTicket";

    public IEnumerator QueryLevelTickets(
        uint userID,
        Action<IUserData.LevelTickets> onComplete)
    {
        yield return __CreateEnumerator();
        
        List<IUserData.LevelTicket> tickets = null;
        List<UserLevel> levels = null;

        if (_levelTickets != null)
        {
            UserLevel userLevel;
            IUserData.LevelTicket destinationTicket;
            Active<int> active;
            int level = UserData.level;
            foreach (var sourceTicket in _levelTickets)
            {
                if(sourceTicket.level > level)
                    continue;
                
                destinationTicket.name = sourceTicket.name;
                active = new Active<int>(PlayerPrefs.GetString($"{NAME_SPACE_USER_LEVEL_TICKET}{sourceTicket.name}"), __Parse);
                destinationTicket.count =
                    DateTimeUtility.IsToday(active.seconds) ? active.value : sourceTicket.capacity;

                if (tickets == null)
                    tickets = new List<IUserData.LevelTicket>();
                
                tickets.Add(destinationTicket);
                
                userLevel = __ToUserLevel(__GetLevelIndex(sourceTicket.name));

                if (levels == null)
                    levels = new List<UserLevel>();

                levels.Add(userLevel);
            }
        }

        IUserData.LevelTickets result;
        result.tickets = tickets == null ? null : tickets.ToArray();
        result.levels = levels == null ? null : levels.ToArray();
        
        onComplete(result);
    }

    private Dictionary<string, int> __levelNameToIndices;
    
    private int __GetLevelIndex(string name)
    {
        if (__levelNameToIndices == null)
        {
            __levelNameToIndices = new Dictionary<string, int>();

            int numLevels = _levels.Length;
            for (int i = 0; i < numLevels; ++i)
                __levelNameToIndices.Add(_levels[i].name, i);
        }

        return __levelNameToIndices[name];
    }

    private Dictionary<string, int> __levelTicketNameToIndices;

    private int __GetLevelTicketIndex(string name)
    {
        if (__levelTicketNameToIndices == null)
        {
            __levelTicketNameToIndices = new Dictionary<string, int>();

            int numLevelTickets = _levelTickets == null ? 0 : _levelTickets.Length;
            for(int i = 0; i < numLevelTickets; ++i)
                __levelTicketNameToIndices.Add(_levelTickets[i].name, i);
        }
        
        return __levelTicketNameToIndices.TryGetValue(name, out int index) ? index : -1;
    }
    
    private UserLevel __ToUserLevel(int levelIndex, ref int stageIndex, ref bool isUnlock)
    {
        ref var level = ref _levels[levelIndex];
        
        UserLevel userLevel;
        userLevel.name = level.name;
        userLevel.id = __ToID(levelIndex);
        userLevel.energy = level.energy;
            
        int i, j, numStageRewards, numStages = __GetStageCount(level);
        StageReward stageReward;
        Stage stage;
        UserStage userStage;
        userLevel.stages = new UserStage[numStages];
        for (i = 0; i < numStages; ++i)
        {
            stage = __GetStage(level, i);
            userStage.name = stage.name;
            userStage.id = __ToID(stageIndex++);
            userStage.energy = stage.energy;
                
            if (isUnlock)
            {
                userStage.rewards = null;
                numStageRewards = stage.indirectRewards.Length;
                userStage.rewardFlags = new UserStageReward.Flag[numStageRewards];
                for (j = 0; j < numStageRewards; ++j)
                {
                    stageReward = stage.indirectRewards[j];
                    userStage.rewardFlags[j] = __GetStageRewardFlag(
                        stageReward.name,
                        level.name,
                        i,
                        stageReward.conditionValue, 
                        stageReward.condition,
                        out _);
                }
                    
                isUnlock = (UserData.GetStageFlag(level.name, i) & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal;
            }
            else
            {
                userStage.rewards = stage.directRewards;
                userStage.rewardFlags = null;
            }

            //userStage.rewardPools = stage.rewardPools;

            userLevel.stages[i] = userStage;
        }

        return userLevel;
    }

    private UserLevel __ToUserLevel(int levelIndex)
    {
        bool isUnlock = true;
        int stageIndex = 0, numStages, i, j;
        for (i = 0; i < levelIndex; ++i)
        {
            ref var level = ref _levels[i];

            numStages = __GetStageCount(level);

            stageIndex += numStages;

            if (isUnlock)
            {
                for (j = 0; j < numStages; ++j)
                {
                    isUnlock = isUnlock && (UserData.GetStageFlag(level.name, j) & IUserData.StageFlag.Normal) ==
                        IUserData.StageFlag.Normal;

                    if (!isUnlock)
                        break;
                }
            }
        }
        
        return __ToUserLevel(levelIndex, ref stageIndex, ref isUnlock);
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

    public IEnumerator QueryLevelTickets(
        uint userID,
        Action<IUserData.LevelTickets> onComplete)
    {
        return UserDataMain.instance.QueryLevelTickets(userID, onComplete);
    }
}