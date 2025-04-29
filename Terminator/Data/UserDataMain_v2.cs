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
        [Serializable]
        public struct Reward
        {
            public string name;

            public UserRewardType type;

            [UnityEngine.Serialization.FormerlySerializedAs("min")]
            public int minCount;
            [UnityEngine.Serialization.FormerlySerializedAs("max")]
            public int maxCount;

            public int minLevel;
            public int maxLevel;

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
                    minLevel = value;
                }
            }
            
            [CSVField]
            public int 游荡奖励最大章节
            {
                set
                {
                    maxLevel = value;
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

        public double maxTime;

        public Reward[] rewards;

#if UNITY_EDITOR
        [SerializeField, CSV("rewards", guidIndex = -1, nameIndex = 0)]
        internal string _rewardsPath;
#endif
        public IUserData.Tip instance
        {
            get
            {
                int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
                if (time == 0)
                {
                    var timeUnix = DateTime.UtcNow - Utc1970;
                    time = (int)timeUnix.TotalSeconds;
            
                    PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time);
                }

                return Create((uint)time * TimeSpan.TicksPerSecond + Utc1970.Ticks);
            }
        }

        public IUserData.Tip Create(long tick)
        {
            IUserData.Tip result;
            result.maxTime = (long)Math.Round(maxTime * TimeSpan.TicksPerSecond);
            result.tick = tick;
            
            int numRewards = rewards.Length;
            
            result.rewards = new IUserData.Tip.Reward[numRewards];

            int level = UserData.level;
            for (int i = 0; i < numRewards; ++i)
            {
                ref var source = ref rewards[i];
                if(source.minLevel > level || source.minLevel < source.maxLevel && source.maxLevel <= level)
                    continue;
                
                ref var destination = ref result.rewards[i];
                
                destination.name = source.name;
                destination.type = source.type;
                destination.min = source.minCount;
                destination.max = source.maxCount;
                destination.unitTime = (long)Math.Round(source.unitTime * TimeSpan.TicksPerSecond);
                destination.chance = source.chance;
            }

            return result;
        }
    }

    private const string NAME_SPACE_USER_TIP_TIME = "UserTipTime";
    private static readonly DateTime Utc1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SerializeField]
    internal Tip _tip;
    
    public IEnumerator QueryTip(
        uint userID,
        Action<IUserData.Tip> onComplete)
    {
        yield return null;

        int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
        if (time == 0)
        {
            var timeUnix = DateTime.UtcNow - Utc1970;
            time = (int)timeUnix.TotalSeconds;
            
            PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time);
        }

        onComplete(_tip.instance);
    }

    public IEnumerator CollectTip(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        yield return null;

        var results = _tip.instance.Generate();

        var timeUnix = DateTime.UtcNow - Utc1970;
        PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, (int)timeUnix.TotalSeconds);

        var rewards = new List<UserReward>();
        __ApplyRewards(results, rewards);
        
        onComplete(rewards.ToArray());
    }
    
    
    public IEnumerator QueryTalents(
        uint userID,
        Action<IUserData.Talents> onComplete)
    {
        yield return null;

        IUserData.Talents result;
        result.flag = 0;
        if ((flag & Flag.TalentsUnlock) != 0)
        {
            if ((flag & Flag.UnlockFirst) == Flag.TalentsUnlockFirst)
                result.flag |= IUserData.Talents.Flag.UnlockFirst;
            else
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
                userTalent.flag = (UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{userTalent.id}");
                userTalent.gold = talent.gold;
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
        yield return null;

        string key = $"{NAME_SPACE_USER_TALENT_FLAG}{talentID}";
        var flag = (UserTalent.Flag)PlayerPrefs.GetInt(key);
        if ((flag & UserTalent.Flag.Collected) == UserTalent.Flag.Collected)
        {
            onComplete(false);
            
            yield break;
        }

        int gold = UserDataMain.gold;
        
        var talent = _talents[__ToIndex(talentID)];
        if (talent.gold > gold)
        {
            onComplete(false);
            
            yield break;
        }

        UserDataMain.gold = gold - talent.gold;

        flag |= UserTalent.Flag.Collected;
        PlayerPrefs.SetInt(key, (int)flag);
        
        UserDataMain.flag &= ~Flag.TalentsUnlockFirst;

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
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectTip(userID, onComplete);
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