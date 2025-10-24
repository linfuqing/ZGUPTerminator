using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    public struct Tip
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

            [UnityEngine.Serialization.FormerlySerializedAs("minLevel")]
            public int minChapter;
            [UnityEngine.Serialization.FormerlySerializedAs("maxLevel")]
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

        public int timesPerDayFromAd;
        public int timesPerDayFromEnergy;

        public int energiesPerTime;

        [Tooltip("填1.2")]
        public float sweepCardMultiplier;

        [Tooltip("每次快速游蕩消耗的縂時間")]
        public double intervalPerTime;

        [Tooltip("最大挂機游蕩時間")]
        public double maxTime;

        public Reward[] rewards;

#if UNITY_EDITOR
        [SerializeField, CSV("rewards", guidIndex = -1, nameIndex = 0)]
        internal string _rewardsPath;
#endif

        public static Used used
        {
            get => new Active<Used>(PlayerPrefs.GetString(NAME_SPACE_USER_TIP_USED), Used.Parse).ToDay();

            set => PlayerPrefs.SetString(NAME_SPACE_USER_TIP_USED, new Active<Used>(value).ToString());
        }

        public IUserData.Tip instance => Create(used);

        public IUserData.Tip Create(in Used used)
        {
            int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
            if (time == 0)
            {
                time = (int)DateTimeUtility.GetSeconds();
                PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time);
            }

            return Create(__HasSweepCard(), DateTimeUtility.GetTicks((uint)time), used);
        }

        public IUserData.Tip Create(bool hasSweepCard, long ticks, in Used used)
        {
            IUserData.Tip result;
            result.timesFromAd = timesPerDayFromAd - used.timesFromAd;
            result.timesFromEnergy = timesPerDayFromEnergy - used.timesFromEnergy;
            result.energiesPerTime = energiesPerTime;
            result.sweepCardMultiplier = hasSweepCard ? sweepCardMultiplier : 1.0f;
            result.ticksPerTime =  (long)Math.Round(intervalPerTime * TimeSpan.TicksPerSecond);
            result.maxTime = (long)Math.Round(maxTime * TimeSpan.TicksPerSecond);
            result.ticks = ticks;
            
            int numRewards = rewards.Length;
            
            result.rewards = new IUserData.Tip.Reward[numRewards];

            int chapter = UserData.chapter;
            for (int i = 0; i < numRewards; ++i)
            {
                ref var source = ref rewards[i];
                if(source.minChapter > chapter || source.minChapter < source.maxChapter && source.maxChapter <= chapter)
                    continue;
                
                ref var destination = ref result.rewards[i];
                
                destination.name = source.name;
                destination.type = source.type;
                destination.min = source.minCount;
                destination.max = source.maxCount;
                destination.maxUnits = source.maxUnits;
                destination.unitTime = (long)Math.Round(source.unitTime * TimeSpan.TicksPerSecond);
                destination.chance = source.chance;
            }

            return result;
        }
    }

    private const string NAME_SPACE_USER_TIP_TIME = "UserTipTime";
    private const string NAME_SPACE_USER_TIP_USED = "UserTipUsed";
    
    //private static readonly DateTime Utc1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SerializeField]
    internal Tip _tip;
    
    public IEnumerator QueryTip(
        uint userID,
        Action<IUserData.Tip> onComplete)
    {
        yield return __CreateEnumerator();

        int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
        if (time == 0)
        {
            time = (int)DateTimeUtility.GetSeconds();
            PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time);
        }

        onComplete(_tip.instance);
    }

    public IEnumerator CollectTip(
        uint userID,
        Action<long, Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        var results = _tip.instance.Generate();

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
        var instance = _tip.Create(used);
        if (++used.timesFromEnergy > _tip.timesPerDayFromEnergy && instance.sweepCardMultiplier < 1.0f + Mathf.Epsilon || 
            !__ApplyEnergy(_tip.energiesPerTime))
        {
            onComplete(null);
            
            yield break;
        }

        Tip.used = used;

        var rewards = instance.Generate((long)(_tip.intervalPerTime * TimeSpan.TicksPerSecond));

        __AppendQuest(UserQuest.Type.Tip, 1);

        var results = __ApplyRewards(rewards);

        onComplete(results == null ? null : results.ToArray());
    }

    private static bool __HasSweepCard()
    {
        return PurchaseData.IsValid(PurchaseType.SweepCard,
            0,
            NAME_SPACE_USER_PURCHASE_ITEM,
            out _,
            out _);
    }
    
    [Serializable]
    internal struct Talent
    {
        public string name;
        public string roleName;
        public int gold;
        public int exp;
        public float skillGroupDamage;
        public UserAttributeData attribute;
        
#if UNITY_EDITOR
        [CSVField]
        public string 能力名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 能力角色名
        {
            set
            {
                roleName = value;
            }
        }
        
        [CSVField]
        public int 能力解锁消耗
        {
            set
            {
                gold = value;
            }
        }
        
        [CSVField]
        public int 能力解锁消耗经验
        {
            set
            {
                exp = value;
            }
        }

        [CSVField]
        public float 能力技能组伤害加成
        {
            set
            {
                skillGroupDamage = value;
            }
        }
        
        [CSVField]
        public int 能力属性类型
        {
            set
            {
                attribute.type = (UserAttributeType)value;
            }
        }
        
        [CSVField]
        public float 能力属性值
        {
            set
            {
                attribute.value = value;
            }
        }
#endif
    }

    private const string NAME_SPACE_USER_TALENT_FLAG = "UserTalentFlag";

    [Header("Talents")]
    [SerializeField]
    internal Talent[] _talents;

#if UNITY_EDITOR
    [SerializeField, CSV("_talents", guidIndex = -1, nameIndex = 0)] 
    internal string _talentsPath;
#endif

    public IEnumerator QueryTalents(
        uint userID,
        Action<IUserData.Talents> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.Talents result;
        result.flag = 0;
        if ((flag & Flag.TalentsUnlock) != 0)
        {
            if ((flag & Flag.UnlockFirst) == Flag.TalentsUnlockFirst)
                result.flag |= IUserData.Talents.Flag.UnlockFirst;
            else if((flag & Flag.TalentsUnlockFirst) == 0)
                result.flag |= IUserData.Talents.Flag.Unlock;
        }

        if (result.flag == 0)
            result.talents = null;
        else
        {
            int numTalents = _talents.Length;
            Talent talent;
            UserTalent userTalent;
            var userTalents = new UserTalent[numTalents];
            for (int i = 0; i < numTalents; ++i)
            {
                talent = _talents[i];
                if (!string.IsNullOrEmpty(talent.roleName))
                    continue;

                userTalent.name = talent.name;
                userTalent.id = __ToID(i);
                userTalent.flag = (UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}");
                userTalent.gold = talent.gold;
                userTalent.exp = talent.exp;
                userTalent.skillGroupDamage = talent.skillGroupDamage;
                userTalent.attribute = talent.attribute;
                userTalents[i] = userTalent;
            }

            result.talents = userTalents;
        }

        onComplete(result);
    }

    public IEnumerator CollectTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        var talent = _talents[__ToIndex(talentID)];
        string key = $"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}";
        var flag = (UserTalent.Flag)PlayerPrefs.GetInt(key);
        if ((flag & UserTalent.Flag.Collected) == UserTalent.Flag.Collected)
        {
            onComplete(false);
            
            yield break;
        }

        int gold = UserDataMain.gold, exp = UserDataMain.exp;
        
        if (talent.gold > gold || talent.exp > exp)
        {
            onComplete(false);
            
            yield break;
        }

        UserDataMain.gold = gold - talent.gold;
        UserDataMain.exp = exp - talent.exp;

        flag |= UserTalent.Flag.Collected;
        PlayerPrefs.SetInt(key, (int)flag);
        
        UserDataMain.flag &= ~Flag.TalentsUnlockFirst;
        
        __AppendQuest(UserQuest.Type.Talents, 1);

        onComplete(true);
    }
}

public partial class UserData
{
    public IEnumerator QueryTip(
        uint userID,
        Action<IUserData.Tip> onComplete)
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

    public IEnumerator QueryTalents(
        uint userID,
        Action<IUserData.Talents> onComplete)
    {
        return UserDataMain.instance.QueryTalents(userID, onComplete);
    }

    public IEnumerator CollectTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.CollectTalent(userID, talentID, onComplete);
    }
}