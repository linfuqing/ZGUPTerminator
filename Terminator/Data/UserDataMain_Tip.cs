using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    internal struct Tip
    {
        public struct Used
        {
            public int timesFromAd;
            public int timesFromEnergy;

            public static Used Parse(Memory<string> parameters)
            {
                Used used;
                used.timesFromAd = int.Parse(parameters.Span[0]);
                used.timesFromEnergy = int.Parse(parameters.Span[1]);

                return used;
            }
            
            public override string ToString()
            {
                return $"{timesFromAd}{UserData.SEPARATOR}{timesFromEnergy}";
            }
        }

        [Serializable]
        public struct Reward
        {
            public string name;

            public UserRewardType type;

            public int minCount;
            public int maxCount;

            public int minChapter;
            public int maxChapter;

            public int maxUnits;

            public float unitTime;

            public float chance;
            
#if UNITY_EDITOR
            [CSVField]
            public string 游荡奖励名字
            {
                set
                {
                    name = value;
                }
            }
            
            [CSVField]
            public int 游荡奖励类型
            {
                set
                {
                    type = (UserRewardType)value;
                }
            }
            
            [CSVField]
            public int 游荡奖励最小数量
            {
                set
                {
                    minCount = value;
                }
            }
            
            [CSVField]
            public int 游荡奖励最大数量
            {
                set
                {
                    maxCount = value;
                }
            }
            
            [CSVField]
            public int 游荡奖励最小章节
            {
                set
                {
                    minChapter = value;
                }
            }
            
            [CSVField]
            public int 游荡奖励最大章节
            {
                set
                {
                    maxChapter = value;
                }
            }
            
            [CSVField]
            public int 游荡奖励最大刷新次数
            {
                set
                {
                    maxUnits = value;
                }
            }

            [CSVField]
            public float 游荡奖励刷新时间
            {
                set
                {
                    unitTime = value;
                }
            }
            
            [CSVField]
            public float 游荡奖励刷新概率
            {
                set
                {
                    chance = value;
                }
            }
#endif
        }

        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("timesPerDayFromAd")]
        internal int _timesPerDayFromAd;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("timesPerDayFromEnergy")]
        internal int _timesPerDayFromEnergy;

        [Tooltip("填1.2")]
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("sweepCardMultiplier")]
        internal float _sweepCardMultiplier;

        [Tooltip("每次快速游蕩消耗的縂時間")]
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("intervalPerTime")]
        internal double _intervalPerTime;

        [Tooltip("最大挂機游蕩時間")]
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("maxTime")]
        internal double _maxTime;

        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("rewards")]
        internal Reward[] _rewards;
        
#if UNITY_EDITOR
        [SerializeField, CSV("_rewards", guidIndex = -1, nameIndex = 0)]
        internal string _rewardsPath;
#endif

        private Dictionary<string, int> __rewardIndices;

        public static Used used
        {
            get => new Active<Used>(PlayerPrefs.GetString(NAME_SPACE_USER_TIP_USED), Used.Parse).ToDay();

            set => PlayerPrefs.SetString(NAME_SPACE_USER_TIP_USED, new Active<Used>(value).ToString());
        }

        public Reward GetReward(string name)
        {
            return _rewards[__GetRewardIndex(name)];
        }

        public IUserData.Tip Create(in Used used, string[] rewardNames)
        {
            int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
            if (time == 0)
            {
                time = (int)DateTimeUtility.GetSeconds();
                PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time);
            }

            return Create(__HasSweepCard(), DateTimeUtility.GetTicks((uint)time), used, rewardNames);
        }

        public IUserData.Tip Create(bool hasSweepCard, long ticks, in Used used, string[] rewardNames)
        {
            IUserData.Tip result;
            result.timesFromAd = _timesPerDayFromAd - used.timesFromAd;
            result.timesFromEnergy = _timesPerDayFromEnergy - used.timesFromEnergy;
            result.sweepCardMultiplier = hasSweepCard ? _sweepCardMultiplier : 1.0f;
            result.ticksPerTime =  (long)Math.Round(_intervalPerTime * TimeSpan.TicksPerSecond);
            result.maxTime = (long)Math.Round(_maxTime * TimeSpan.TicksPerSecond);
            result.ticks = ticks;
            
            int chapter = UserData.chapter;
            UserTipReward destination;
            List<UserTipReward> rewards = null;
            if (rewardNames == null || rewardNames.Length < 1)
            {
                foreach (var source in _rewards)
                {
                    if (source.minChapter > chapter ||
                        source.minChapter < source.maxChapter && source.maxChapter <= chapter)
                        continue;

                    destination.name = source.name;
                    destination.type = source.type;
                    destination.min = source.minCount;
                    destination.max = source.maxCount;
                    destination.maxUnits = source.maxUnits;
                    destination.unitTime = (long)Math.Round(source.unitTime * TimeSpan.TicksPerSecond);
                    destination.chance = source.chance;

                    if (rewards == null)
                        rewards = new List<UserTipReward>();
                    
                    rewards.Add(destination);
                }
            }
            else
            {
                int rewardIndex;
                foreach (var rewardName in rewardNames)
                {
                    rewardIndex = __GetRewardIndex(rewardName);
                    ref var source = ref _rewards[rewardIndex];
                    if (source.minChapter > chapter ||
                        source.minChapter < source.maxChapter && source.maxChapter <= chapter)
                        continue;

                    destination.name = source.name;
                    destination.type = source.type;
                    destination.min = source.minCount;
                    destination.max = source.maxCount;
                    destination.maxUnits = source.maxUnits;
                    destination.unitTime = (long)Math.Round(source.unitTime * TimeSpan.TicksPerSecond);
                    destination.chance = source.chance;
                    
                    if (rewards == null)
                        rewards = new List<UserTipReward>();
                    
                    rewards.Add(destination);
                }
            }

            result.rewards = rewards == null ? null : rewards.ToArray();
            
            return result;
        }

        private int __GetRewardIndex(string name)
        {
            if (__rewardIndices == null)
            {
                __rewardIndices = new Dictionary<string, int>();
                    
                int numRewards = _rewards.Length;
                for (int i = 0; i < numRewards; ++i)
                    __rewardIndices.Add(_rewards[i].name, i);
            }

            return __rewardIndices[name];
        }
    }

    private const string NAME_SPACE_USER_TIP_TIME = "UserTipTime";
    private const string NAME_SPACE_USER_TIP_USED = "UserTipUsed";
    private const string NAME_SPACE_USER_TIP_LEVEL = "UserTipLevel";
    
    //private static readonly DateTime Utc1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Header("Tip")]
    [SerializeField]
    internal int _energiesPerTime = 5;

    [SerializeField]
    internal Tip _tip;
    
    [Serializable]
    internal struct TipLevel
    {
        public string name;
        public string nextLevel;

        public int chapter;
        
        public string[] rewardNames;
    }

    [SerializeField]
    internal TipLevel[] _tipLevels;

    [SerializeField] 
    internal string[] _tipLevelNames;
    
    public IEnumerator QueryTip(
        uint userID,
        Action<IUserData.TipData> onComplete)
    {
        yield return __CreateEnumerator();

        int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
        if (time == 0)
        {
            time = (int)DateTimeUtility.GetSeconds();
            PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time - (int)_tip._maxTime);
        }

        IUserData.TipData result;
        result.energiesPerTime = _energiesPerTime;
        result.value = __CreateTip(out var levelNames);
        
        int numLevelNames = levelNames.Length, numRewards, tipLevelIndex, i, j;
        TipLevel tipLevel;
        UserTipLevel userTipLevel;
        UserTipReward userTipReward;
        Tip.Reward tipReward;
        result.levels = new UserTipLevel[numLevelNames];
        for (i = 0; i < numLevelNames; ++i)
        {
            tipLevelIndex = __GetTipLevelIndex(levelNames[i]);
            tipLevel = _tipLevels[tipLevelIndex];
            userTipLevel.name = tipLevel.name;
            userTipLevel.id = __ToID(tipLevelIndex);
            userTipLevel.rewardNames = tipLevel.rewardNames;

            if (string.IsNullOrEmpty(tipLevel.nextLevel))
                userTipLevel.next = default;
            else
            {
                tipLevelIndex = __GetTipLevelIndex(tipLevel.nextLevel);
                tipLevel = _tipLevels[tipLevelIndex];
                userTipLevel.next.name = tipLevel.name;
                userTipLevel.next.id = __ToID(tipLevelIndex);
                userTipLevel.next.chapter = tipLevel.chapter;

                numRewards = tipLevel.rewardNames == null ? 0 : tipLevel.rewardNames.Length;
                userTipLevel.next.rewards = numRewards > 0 ? new UserTipReward[numRewards] : null;
                for (j = 0; j < numRewards; ++j)
                {
                    tipReward = _tip.GetReward(tipLevel.rewardNames[j]);
                    
                    userTipReward.name = tipReward.name;
                    userTipReward.type = tipReward.type;
                    userTipReward.min = tipReward.minCount;
                    userTipReward.max = tipReward.maxCount;
                    userTipReward.maxUnits = tipReward.maxUnits;
                    userTipReward.unitTime = (long)Math.Round(tipReward.unitTime * TimeSpan.TicksPerSecond);
                    userTipReward.chance = tipReward.chance;

                    userTipLevel.next.rewards[j] = userTipReward;
                }
            }

            result.levels[i] = userTipLevel;
        }

        onComplete(result);
    }

    public IEnumerator CollectTip(
        uint userID,
        Action<long, Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        var results = __CreateTip(out _).Generate();

        uint seconds = DateTimeUtility.GetSeconds();
        PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, (int)seconds);

        var rewards = new List<UserReward>();
        __ApplyRewards(results, rewards);
        
        __AppendQuest(UserQuest.Type.Tip, 1);

        onComplete(DateTimeUtility.GetTicks(seconds), rewards.ToArray());
    }

    public IEnumerator UseTip(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        var used = Tip.used;
        var instance = __CreateTip(used, out _);
        if (++used.timesFromEnergy > _tip._timesPerDayFromEnergy && instance.sweepCardMultiplier < 1.0f + Mathf.Epsilon || 
            !__ApplyEnergy(_energiesPerTime))
        {
            onComplete(null);
            
            yield break;
        }

        Tip.used = used;

        var rewards = instance.Generate((long)(_tip._intervalPerTime * TimeSpan.TicksPerSecond));

        __AppendQuest(UserQuest.Type.Tip, 1);

        var results = __ApplyRewards(rewards);

        onComplete(results == null ? null : results.ToArray());
    }

    public IEnumerator UpgradeTip(uint userID, uint tipLevelID, Action<UserTipLevel.Next?> onComplete)
    {
        yield return __CreateEnumerator();

        var level = _tipLevels[__ToIndex(tipLevelID)];

        var levelNamesString = PlayerPrefs.GetString(NAME_SPACE_USER_TIP_LEVEL);
        var levelNames = string.IsNullOrEmpty(levelNamesString)
            ? _tipLevelNames
            : levelNamesString.Split(UserData.SEPARATOR);
        
        int index = Array.IndexOf(levelNames, level.name);
        if (index == -1)
        {
            onComplete(null);
            
            yield break;
        }

        int numLevelNames = levelNames.Length - 1;
        Array.Copy(levelNames, index + 1, levelNames, index, numLevelNames - index);

        UserTipLevel.Next next;
        if (string.IsNullOrEmpty(level.nextLevel))
        {
            Array.Resize(ref levelNames, numLevelNames);

            next = default;
        }
        else
        {
            levelNames[numLevelNames] = level.nextLevel;

            index = __GetTipLevelIndex(level.nextLevel);
            level = _tipLevels[index];
            
            next.name = level.name;
            next.id = __ToID(index);
            next.chapter = level.chapter;

            int numRewards = level.rewardNames == null ? 0 : level.rewardNames.Length;
            Tip.Reward source;
            UserTipReward destination;
            next.rewards = numRewards > 0 ? new UserTipReward[numRewards] : null;
            for (int i = 0; i < numRewards; ++i)
            {
                source = _tip.GetReward(level.rewardNames[i]);
                    
                destination.name = source.name;
                destination.type = source.type;
                destination.min = source.minCount;
                destination.max = source.maxCount;
                destination.maxUnits = source.maxUnits;
                destination.unitTime = (long)Math.Round(source.unitTime * TimeSpan.TicksPerSecond);
                destination.chance = source.chance;

                next.rewards[i] = destination;
            }
        }

        PlayerPrefs.SetString(NAME_SPACE_USER_TIP_LEVEL, string.Join(UserData.SEPARATOR, levelNames));

        onComplete(next);
    }

    private Dictionary<string, int> __tipLevelIndices;

    private int __GetTipLevelIndex(string name)
    {
        if (__tipLevelIndices == null)
        {
            __tipLevelIndices = new Dictionary<string, int>();

            int numTipLevels = _tipLevels == null ? 0 : _tipLevels.Length;
            for(int i = 0; i < numTipLevels; ++i)
                __tipLevelIndices.Add(_tipLevels[i].name, i);
        }
        
        return __tipLevelIndices[name];
    }

    private IUserData.Tip __CreateTip(in Tip.Used used, out string[] levelNames)
    {
        int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
        if (time == 0)
        {
            time = (int)DateTimeUtility.GetSeconds();
            PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time);
        }
        
        var levelNamesString = PlayerPrefs.GetString(NAME_SPACE_USER_TIP_LEVEL);
        levelNames = string.IsNullOrEmpty(levelNamesString)
            ? _tipLevelNames
            : levelNamesString.Split(UserData.SEPARATOR);
        
        List<string> levelRewardNames = null;
        foreach (var levelName in levelNames)
        {
            ref var level = ref _tipLevels[__GetTipLevelIndex(levelName)];
            if(level.rewardNames == null || level.rewardNames.Length < 1)
                continue;
                
            if(levelRewardNames == null)
                levelRewardNames = new List<string>();
                
            levelRewardNames.AddRange(level.rewardNames);
        }
        
        return _tip.Create(__HasSweepCard(), DateTimeUtility.GetTicks((uint)time), used, 
            //服务器要判断空，别崩溃了
            levelRewardNames.ToArray());
    }

    private IUserData.Tip __CreateTip(out string[] levelNames)
    {
        return __CreateTip(Tip.used, out levelNames);
    }
}


public partial class UserData
{
    public IEnumerator QueryTip(
        uint userID,
        Action<IUserData.TipData> onComplete)
    {
        return UserDataMain.instance.QueryTip(userID, onComplete);
    }
    
    public IEnumerator CollectTip(
        uint userID,
        Action<long, Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectTip(userID, onComplete);
    }
    
    public IEnumerator UseTip(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.UseTip(userID, onComplete);
    }

    public IEnumerator UpgradeTip(uint userID, uint tipLevelID, Action<UserTipLevel.Next?> onComplete)
    {
        return UserDataMain.instance.UpgradeTip(userID, tipLevelID, onComplete);
    }
}