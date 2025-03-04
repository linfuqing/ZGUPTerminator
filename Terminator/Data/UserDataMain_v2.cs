using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        }

        public double maxTime;

        public Reward[] rewards;

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
            result.maxTime = (uint)Math.Round(maxTime * TimeSpan.TicksPerSecond);
            result.tick = tick;
            
            int numRewards = rewards.Length;
            
            result.rewards = new IUserData.Tip.Reward[numRewards];

            int level = UserData.level;
            for (int i = 0; i < numRewards; ++i)
            {
                ref var source = ref rewards[i];
                if(source.minLevel > level || source.minLevel < source.maxLevel && source.maxLevel < level)
                    continue;
                
                ref var destination = ref result.rewards[i];
                
                destination.name = source.name;
                destination.type = source.type;
                destination.min = source.minCount;
                destination.max = source.maxCount;
                destination.unitTime = (uint)Math.Round(source.unitTime * TimeSpan.TicksPerSecond);
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
}