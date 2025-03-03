using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial interface IUserData
{
    public struct Tip
    {
        public struct Reward
        {
            public string name;

            public UserRewardType type;

            public int min;
            public int max;

            public uint unitTime;

            public float chance;
        }
        
        public uint maxTime;
        public long tick;
        
        public Reward[] rewards;

        public UserRewardData[] Generate()
        {
            uint hash = (uint)this.tick ^ (uint)(this.tick >> 32);
            var random = new Unity.Mathematics.Random(hash);

            bool isContains;
            int numRewards = rewards.Length;
            long tick = Math.Min(DateTime.UtcNow.Ticks - this.tick, maxTime);
            UserRewardData result;
            var results = new Dictionary<int, UserRewardData>();
            var rewardTimes = new int[numRewards];
            do
            {
                isContains = false;
                for(int i = 0; i < numRewards; ++i)
                {
                    ref var reward = ref rewards[i];
                    if (++rewardTimes[i] * reward.unitTime > tick ||
                        reward.chance < random.NextFloat())
                        continue;
                    
                    isContains = true;

                    if (!results.TryGetValue(i, out result))
                    {
                        result.type = reward.type;
                        result.name = reward.name;
                        result.count = 0;
                    }

                    result.count += random.NextInt(reward.min, reward.max);
                    
                    results[i] = result;
                }
            } while (isContains);

            var values = new UserRewardData[results.Count];
            results.Values.CopyTo(values, 0);
            return values;
        }
    }
    
    IEnumerator QueryTip(
        uint userID, 
        Action<Tip> onComplete);

    IEnumerator CollectTip(
        uint userID,
        Action<Memory<UserReward>> onComplete);
}
