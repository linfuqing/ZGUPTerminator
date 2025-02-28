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

            public int min;
            public int max;

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
            result.maxTime = (uint)Math.Round(maxTime * TimeSpan.TicksPerMillisecond);
            result.tick = tick;
            
            int numRewards = rewards.Length;
            
            result.rewards = new IUserData.Tip.Reward[numRewards];

            for (int i = 0; i < numRewards; ++i)
            {
                ref var source = ref rewards[i];
                ref var destination = ref result.rewards[i];
                
                destination.name = source.name;
                destination.type = source.type;
                destination.min = source.min;
                destination.max = source.max;
                destination.unitTime = (uint)Math.Round(source.unitTime * TimeSpan.TicksPerMillisecond);
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
        foreach (var result in results)
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