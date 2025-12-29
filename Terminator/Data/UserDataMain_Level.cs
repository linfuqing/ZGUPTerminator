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
        //public int energy;
        
        public UserLevel.CacheType cacheType;

        public string[] stageNames;
        
        public UserRewardData[] sweepRewards;

#if UNITY_EDITOR
        [CSVField]
        public string 关卡名字
        {
            set => name = value;
        }
        
        [CSVField]
        public int 关卡存档类型
        {
            set => cacheType = (UserLevel.CacheType)value;
        }

        /*[CSVField]
        public int 关卡体力
        {
            set => energy = value;
        }*/
        
        [CSVField]
        public string 关卡小关
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
        
        [CSVField]
        public string 关卡扫荡奖励
        {
            set
            {
                
                if(string.IsNullOrEmpty(value))
                {
                    sweepRewards = null;
                    
                    return;
                }

                var parameters = value.Split('/');
                
                int numParameters = parameters.Length;
                sweepRewards = new UserRewardData[numParameters];

                for (int i = 0; i < numParameters; ++i)
                    sweepRewards[i] = UserRewardData.Parse(parameters[i]);
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

    [Serializable]
    internal struct LevelChapter
    {
        public string name;

        public int stageRewardCount;
        
#if UNITY_EDITOR
        [CSVField]
        public string 章节名字
        {
            set => name = value;
        }
        
        [CSVField]
        public int 章节解锁需要星星数
        {
            set => stageRewardCount = value;
        }
#endif
    }

    [SerializeField] 
    internal LevelChapter[] _levelChapters;

#if UNITY_EDITOR
    [SerializeField, CSV("_levelChapters", guidIndex = -1, nameIndex = 0)] 
    internal string _levelChaptersPath;
#endif
    
    [Serializable]
    internal struct LevelTicket
    {
        [Serializable]
        internal struct Level
        {
            public string name;

            public int chapter;
        }

        public string name;
        
        public int capacity;
        
        public Level[] levels;

#if UNITY_EDITOR
        [CSVField]
        public string 关卡门票名字
        {
            set => name = value;
        }
        
        [CSVField]
        public string 关卡门票对应关卡
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                    levels = null;
                else
                {
                    var parameters = value.Split('/');
                    
                    int numParameters = parameters.Length, index;
                    string parameter;
                    levels = new Level[numParameters];
                    for (int i = 0; i < numParameters; ++i)
                    {
                        parameter = parameters[i];
                        
                        ref var level = ref levels[i];
                        
                        index = parameter.IndexOf(':');
                        if (index == -1)
                        {
                            level.name = parameter;
                            level.chapter = 0;
                        }
                        else
                        {
                            level.name = parameter.Remove(index);
                            level.chapter = int.Parse(parameter.Substring(index + 1));
                        }
                    }
                }
            }
        }

        [CSVField]
        public int 关卡门票每日次数
        {
            set => capacity = value;
        }
#endif
        
        private static readonly string NAME_SPACE_USER_LEVEL_TICKET = "UserLevelTicket";
        

        public int count
        {
            get
            {
                var active = new Active<int>(PlayerPrefs.GetString($"{NAME_SPACE_USER_LEVEL_TICKET}{name}"), __Parse);
                return DateTimeUtility.IsToday(active.seconds, DateTimeUtility.DataTimeType.UTC) ? active.value : Mathf.Max(active.value, capacity);
            }

            set
            {
                PlayerPrefs.SetString($"{NAME_SPACE_USER_LEVEL_TICKET}{name}", new Active<int>(value).ToString());
            }
        }
    }

    [SerializeField] 
    internal LevelTicket[] _levelTickets;

#if UNITY_EDITOR
    [SerializeField, CSV("_levelTickets", guidIndex = -1, nameIndex = 0)] 
    internal string _levelTicketsPath;
#endif

    public bool IsLevelChapter(string name)
    {
        return __GetLevelChapterIndex(name) != -1;
    }

    public IEnumerator ApplyLevel(
        uint userID,
        uint levelID, 
        int closestStage, 
        Action<IUserData.LevelProperty> onComplete)
    {
        yield return __CreateEnumerator();

        int levelIndex = __ToIndex(levelID);
        var level = _levels[levelIndex];
        int stage = __GetDontCacheStage(level, closestStage), stageCount = __GetStageCount(level);
        if (!__ApplyLevel(level.name, stage < stageCount ? __GetStage(level, stage).energy : 0))
        {
            Debug.LogError("Apply level failed!");
            
            onComplete(default);

            yield break;
        }
        
        __SubmitStageFlag();

        __AppendQuest(UserQuest.Type.Stage, 1);

        UserData.StartStage(level.name, stage);

        IUserData.LevelProperty result;
        result.stage = stage;
        
        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = levelID;
        levelCache.seconds = DateTimeUtility.GetSeconds();
        levelCache.stage = stage;
        levelCache.gold = 0;
        levelCache.killCount = 0;
        levelCache.killBossCount = 0;
        UserData.levelCache = levelCache;

        result.value = __ApplyProperty(userID);

        int numStages = __GetStageCount(level);
        result.levelStages = new UserLevelStageData[numStages];
        for (int i = 0; i < numStages; ++i)
            result.levelStages[i] = __GetStage(level, i).ToLevel(level.name, i, !IsLevelChapter(level.name));

        onComplete(result);
    }

    private const string NAME_SPACE_USER_LEVEL_FLAG = "UserLevelFlag";
    private const string NAME_SPACE_USER_LEVEL_STAGE_FLAG = "UserLevelStageFlag";

    public IEnumerator CollectLevel(
        uint userID,
        Action<IUserData.LevelStage> onComplete)
    {
        yield return __CreateEnumerator();

        var temp = UserData.levelCache;
        if (temp == null)
        {
            onComplete(default);
            
            yield break;
        }

        UserData.levelCache = null;

        var levelCache = temp.Value;
        var level = _levels[__ToIndex(levelCache.id)];
        /*bool isLevelTicket = __GetLevelTicketIndex(level.name, out _, out _);
        if (isLevelTicket && 
            UserData.GetStartStage(level.name, out _) < levelCache.stage && 
            !ApplyStage(levelCache.id, levelCache.stage - 1, out _))
            Debug.LogError("WTF??????");*/

        uint selectedLevelID = levelCache.id;
        int selectedStage = levelCache.stage, stageCount = __GetStageCount(level);
        if (stageCount <= levelCache.stage)
        {
            if (__GetLevelTicketIndex(level.name, out _, out _))
            {
                PlayerPrefs.SetInt($"{NAME_SPACE_USER_LEVEL_FLAG}{level.name}", 1);

                for (int i = 1; i <= levelCache.stage; ++i)
                    UserData.DeleteStageCache(level.name, i);
            }
            else
            {
                selectedStage = stageCount - 1;
                
                int chapter = UserData.chapter;
                if (chapter == __GetLevelChapterIndex(level.name))
                {
                    UserData.chapter = ++chapter;

                    if (chapter < _levelChapters.Length)
                    {
                        selectedLevelID = __ToID(__GetLevelIndex(_levelChapters[chapter].name));

                        selectedStage = 0;
                    }
                    
                    var flag = UserDataMain.flag;
                    if ((flag & Flag.TicketsUnlock) == 0 && 
                        _levelTickets != null && 
                        _levelTickets.Length > 0 && 
                        _levelTickets[0].levels[0].chapter <= chapter)
                    {
                        flag |= Flag.TicketsUnlock;

                        UserDataMain.flag = flag;
                    }
                }
            }
        }

        UserData.EndStage(level.name, levelCache.stage);

        gold += levelCache.gold;
        
        __AppendQuest(UserQuest.Type.KillCount, levelCache.killCount);
        __AppendQuest(UserQuest.Type.KillBoss, levelCache.killBossCount);

        var rewards = new List<UserReward>();

        string key;
        Stage stage;
        stageCount = Mathf.Min(stageCount, levelCache.stage);
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

        IUserData.LevelStage result;
        result.levelID = selectedLevelID;
        result.stage = selectedStage;
        result.rewards = rewards.ToArray();

        //__CollectLevelLegacy(isNextLevel, levelIndex, levelCache.stage);
        
        onComplete(result);
    }

    public IEnumerator SweepLevel(
        uint userID, 
        uint levelID, 
        Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        var level = _levels[__ToIndex(levelID)];
        if (!__GetLevelTicketIndex(level.name, out _, out int levelTicketIndex))
        {
            onComplete(null);
            
            yield break;
        }
        
        var levelTicket = _levelTickets[levelTicketIndex];
        int count = levelTicket.count;
        if (count < 1)
            yield break;

        levelTicket.count = count - 1;
        
        var rewards = __ApplyRewards(level.sweepRewards);
        
        onComplete(rewards.ToArray());
    }
    
    public IEnumerator QueryLevelChapters(
        uint userID,
        Action<IUserData.LevelChapters> onComplete)
    {
        yield return __CreateEnumerator();

        bool isUnlock = true;
        int stageRewardCount = 0, 
            chapter = UserData.chapter, 
            numChapters = Mathf.Clamp(chapter + 1, 1, _levelChapters.Length);
        LevelChapter levelChapter;
        var userLevels = new UserLevel[numChapters];
        for (int i = 0; i < numChapters; ++i)
        {
            levelChapter = _levelChapters[i];

            stageRewardCount = levelChapter.stageRewardCount;
            
            userLevels[i] = __ToUserLevel(__GetLevelIndex(levelChapter.name), ref isUnlock);
        }

        IUserData.LevelChapters result;
        result.flag = (flag & Flag.UnlockFirst) == 0 ? 0 : IUserData.LevelChapters.Flag.UnlockFirst;
        result.stageRewardCount = stageRewardCount;
        result.levels = userLevels;
        
        onComplete(result);
    }

    public IEnumerator QueryLevelTickets(
        uint userID,
        Action<IUserData.LevelTickets> onComplete)
    {
        yield return __CreateEnumerator();
        
        List<IUserData.LevelTicket> tickets = null;
        List<UserLevel> levels = null;

        if (_levelTickets != null)
        {
            int chapter = UserData.chapter;
            UserLevel userLevel;
            IUserData.LevelTicket destinationTicket;
            List<string> levelNames = null;
            foreach (var sourceTicket in _levelTickets)
            {
                destinationTicket.name = sourceTicket.name;
                destinationTicket.count = sourceTicket.count;
                destinationTicket.chapter = 0;
                
                if(levelNames != null)
                    levelNames.Clear();

                foreach (var level in sourceTicket.levels)
                {
                    if (level.chapter > chapter)
                    {
                        destinationTicket.chapter = level.chapter;
                        
                        break;
                    }

                    if (levelNames == null)
                        levelNames = new List<string>();
                    
                    levelNames.Add(level.name);

                    userLevel = __ToUserLevel(__GetLevelIndex(level.name));

                    if (levels == null)
                        levels = new List<UserLevel>();

                    levels.Add(userLevel);
                    
                    if (PlayerPrefs.GetInt($"{NAME_SPACE_USER_LEVEL_FLAG}{level.name}") == 0)
                        break;
                }
                
                destinationTicket.levelNames = levelNames == null ? null : levelNames.ToArray();

                if (tickets == null)
                    tickets = new List<IUserData.LevelTicket>();
                
                tickets.Add(destinationTicket);
            }
        }

        IUserData.LevelTickets result;
        result.tickets = tickets == null ? null : tickets.ToArray();
        result.levels = levels == null ? null : levels.ToArray();

        var flag = UserDataMain.flag;
        if ((flag & Flag.TicketsUnlockFirst) == Flag.TicketsUnlockFirst)
            result.flag = IUserData.LevelTickets.Flag.UnlockFirst;
        else if ((flag & Flag.TicketsUnlock) != 0)
            result.flag = IUserData.LevelTickets.Flag.Unlock;
        else
            result.flag = 0;

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

    private Dictionary<string, int> __levelChapterToIndices;

    private int __GetLevelChapterIndex(string name)
    {
        if (__levelChapterToIndices == null)
        {
            __levelChapterToIndices = new Dictionary<string, int>();

            int numLevelChapters = _levelChapters.Length;
            for (int i = 0; i < numLevelChapters; ++i)
                __levelChapterToIndices.Add(_levelChapters[i].name, i);
        }

        return __levelChapterToIndices.GetValueOrDefault(name, -1);
    }

    private Dictionary<string, int> __levelTicketIndices;

    private int __GetLevelTicketIndex(string name)
    {
        if (__levelTicketIndices == null)
        {
            __levelTicketIndices = new Dictionary<string, int>();

            int numLevelTickets = _levelTickets == null ? 0 : _levelTickets.Length;
            for (int i = 0; i < numLevelTickets; ++i)
                __levelTicketIndices.Add(_levelTickets[i].name, i);
        }
        
        return __levelTicketIndices.TryGetValue(name, out int index) ? index : -1;
    }
    
    private Dictionary<string, (int, int)> __levelNameToTicketIndices;

    private bool __GetLevelTicketIndex(string levelName, out int levelIndexOfTicket, out int levelTicketIndex)
    {
        if (__levelNameToTicketIndices == null)
        {
            __levelNameToTicketIndices = new Dictionary<string, (int, int)>();

            LevelTicket.Level[] levels;
            int numLevelTickets = _levelTickets == null ? 0 : _levelTickets.Length, numLevels, i, j;
            for (i = 0; i < numLevelTickets; ++i)
            {
                levels = _levelTickets[i].levels;
                numLevels = levels.Length;
                for(j = 0; j < numLevels; ++j)
                    __levelNameToTicketIndices.Add(levels[j].name, (i, j));
            }
        }

        if (__levelNameToTicketIndices.TryGetValue(levelName, out (int ticketIndex, int levelIndex) ticketIndex))
        {
            levelTicketIndex =  ticketIndex.ticketIndex;
            levelIndexOfTicket =  ticketIndex.levelIndex;

            return true;
        }
        
        levelIndexOfTicket = -1;
        levelTicketIndex = -1;

        return false;
    }

    private Dictionary<(int, int), uint> __levelStageIDs;

    private struct LevelStageInfo
    {
        public int stageIndex;
        public int levelIndex;
        public uint rewardID;

        public uint GetRewardID(int index)
        {
            return rewardID + (uint)index;
        }
    }

    private struct LevelStageRewardInfo
    {
        public uint stageID;
        public int index;
    }
    
    private Dictionary<uint, LevelStageInfo> __levelStageInfos;
    private Dictionary<uint, LevelStageRewardInfo> __levelStageRewardInfos;

    private void __BuildLevelStages()
    {
        __levelStageIDs = new Dictionary<(int, int), uint>();
        __levelStageInfos = new Dictionary<uint, LevelStageInfo>();
        __levelStageRewardInfos = new Dictionary<uint, LevelStageRewardInfo>();
            
        int rewardIndex = 0, stageIndex = 0, numLevels = _levels.Length, numStages, numStageRewards, i, j, k;
        uint id;
        LevelStageInfo stageInfo;
        LevelStageRewardInfo rewardInfo;
        for (i = 0; i < numLevels; ++i)
        {
            ref var level = ref _levels[i];
            numStages = __GetStageCount(level);

            stageInfo.levelIndex = i;

            for (j = 0; j < numStages; ++j)
            {
                id = __ToID(stageIndex++);
                __levelStageIDs[(i, j)] = id;

                stageInfo.stageIndex = j;
                stageInfo.rewardID = __ToID(rewardIndex);

                rewardInfo.stageID = id;
                numStageRewards = __GetStage(level, j).indirectRewards.Length;
                for (k = 0; k < numStageRewards; ++k)
                {
                    rewardInfo.index = k;
                    
                    __levelStageRewardInfos[stageInfo.GetRewardID(k)] = rewardInfo;
                }

                __levelStageInfos[id] = stageInfo;

                rewardIndex += numStageRewards;
            }
        }
    }

    private uint __GetLevelStageID(int levelIndex, int stage)
    {
        if (__levelStageIDs == null)
            __BuildLevelStages();

        return __levelStageIDs[(levelIndex, stage)];
    }

    private LevelStageInfo __GetLevelStageInfo(uint stageID)
    {
        if (__levelStageInfos == null)
            __BuildLevelStages();
        
        return __levelStageInfos[stageID];
    }

    private LevelStageRewardInfo __GetLevelStageRewardInfo(uint rewardID)
    {
        if (__levelStageRewardInfos == null)
            __BuildLevelStages();
        
        return __levelStageRewardInfos[rewardID];
    }

    private int __GetChapterStageRewardCount()
    {
        Stage stage;
        int i,
            j,
            k,
            numStageRewards,
            numStages,
            numChapters = Mathf.Clamp(UserData.chapter + 1, 1, _levelChapters.Length),
            result = 0;
        for (i = 0; i < numChapters; ++i)
        {
            ref var level = ref _levels[__GetLevelIndex(_levelChapters[i].name)];
            numStages = __GetStageCount(level);
            for (j = 0; j < numStages; ++j)
            {
                stage = __GetStage(level, j);

                numStageRewards = stage.indirectRewards.Length;
                for (k = 0; k < numStageRewards; ++k)
                {
                    ref var stageReward = ref stage.indirectRewards[k];
                    if ((__GetStageRewardFlag(
                            stageReward.name,
                            level.name,
                            j,
                            stageReward.conditionValue,
                            stageReward.condition,
                            out _) & UserStageReward.Flag.Unlocked) == UserStageReward.Flag.Unlocked)
                        ++result;
                }

                if ((UserData.GetStageFlag(level.name, j) & IUserData.StageFlag.Normal) !=
                    IUserData.StageFlag.Normal)
                    break;
            }
        }

        return result;
    }
    
    private UserLevel __ToUserLevel(int levelIndex, ref bool isUnlock)
    {
        ref var level = ref _levels[levelIndex];
        
        UserLevel userLevel;
        userLevel.name = level.name;
        userLevel.id = __ToID(levelIndex);
        userLevel.cacheType = level.cacheType;
        //userLevel.energy = level.energy;
            
        int i, j, numStageRewards, numStages = __GetStageCount(level);
        StageReward stageReward;
        Stage stage;
        UserStage userStage;
        userLevel.stages = new UserStage[numStages];
        for (i = 0; i < numStages; ++i)
        {
            stage = __GetStage(level, i);
            userStage.name = stage.name;
            userStage.id = __GetLevelStageID(levelIndex, i);
            userStage.energy = stage.energy;
            userStage.flag = 0;
                
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
                
                if((stage.flag & Stage.Flag.DontCache) != Stage.Flag.DontCache && 
                   !__GetStageCache(level.name, i, level.cacheType).isEmpty)
                    userStage.flag |= UserStage.Flag.Cached;
                
                isUnlock = (UserData.GetStageFlag(level.name, i) & IUserData.StageFlag.Normal) == IUserData.StageFlag.Normal;
                if (isUnlock)
                    userStage.flag |= UserStage.Flag.Unlocked;
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
        return __ToUserLevel(levelIndex, ref isUnlock);
    }

    private bool __ApplyLevel(string levelName, int energy)
    {
        if (__GetLevelTicketIndex(levelName, out int levelIndexOfTicket, out int levelTicketIndex))
        {
            var levelTicket = _levelTickets[levelTicketIndex];
            if (UserData.chapter < levelTicket.levels[levelIndexOfTicket].chapter)
                return false;

            int count = levelTicket.count;
            if (count < 1 || energy > 0 && !__ApplyEnergy(energy))
                return false;

            int stageCount = __GetStageCount(_levels[__GetLevelIndex(levelName)]);
            for (int i = 1; i <= stageCount; ++i)
                UserData.DeleteStageCache(levelName, i);
            
            levelTicket.count = count - 1;
        }
        else
        {
            var chapterIndex = __GetLevelChapterIndex(levelName);
            if (chapterIndex > UserData.chapter || 
                _levelChapters[chapterIndex].stageRewardCount > __GetChapterStageRewardCount() || 
                energy > 0 && !__ApplyEnergy(energy))
                return false;
        }

        return true;
    }
}

public partial class UserData
{
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
            
            StartStage(_defaultSceneName, closestStage);
            
            LevelCache levelCache;
            levelCache.name = _defaultSceneName;
            levelCache.id = levelID;
            levelCache.seconds = DateTimeUtility.GetSeconds();
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
        Action<IUserData.LevelStage> onComplete)
    {
        return UserDataMain.instance.CollectLevel(userID, onComplete);
    }

    public IEnumerator SweepLevel(
        uint userID,
        uint levelID,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.SweepLevel(userID,  levelID, onComplete);
    }

    public IEnumerator QueryLevelChapters(
        uint userID,
        Action<IUserData.LevelChapters> onComplete)
    {
        return UserDataMain.instance.QueryLevelChapters(userID, onComplete);
    }

    public IEnumerator QueryLevelTickets(
        uint userID,
        Action<IUserData.LevelTickets> onComplete)
    {
        return UserDataMain.instance.QueryLevelTickets(userID, onComplete);
    }
}