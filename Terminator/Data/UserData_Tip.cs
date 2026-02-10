using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct UserTipReward
{
    public string name;

    public UserRewardType type;

    public int min;
    public int max;

    public int maxUnits;

    public long unitTime;

    public float chance;
}

public struct UserTipLevel
{
    public struct Next
    {
        public string name;

        public uint id;

        /// <summary>
        /// 下一级需要章节
        /// </summary>
        public int chapter;
        
        public UserTipReward[] rewards;
    }
    
    public string name;
    
    public uint id;

    public string[] rewardNames;

    public Next next;
}

public partial interface IUserData
{
    public struct Tip
    {
        /// <summary>
        /// 快速游荡剩余看广告次数
        /// </summary>
        public int timesFromAd;
        
        /// <summary>
        /// 快速游荡剩余消耗体力次数
        /// </summary>
        public int timesFromEnergy;

        /// <summary>
        /// 游荡卡倍率
        /// </summary>
        public float sweepCardMultiplier;

        /// <summary>
        /// 每次快速游荡可消耗的时间长度， 可传入<see cref="Generate"/>获得结果。
        /// </summary>
        public long ticksPerTime;
        
        public long maxTime;
        public long ticks;
        
        public UserTipReward[] rewards;

        public UserRewardData[] Generate(long deltaTicks = 0)
        {
            uint hash = (uint)this.ticks ^ (uint)(this.ticks >> 32);
            uint times = (uint)(timesFromAd + timesFromEnergy);
            if (times > 0)
                hash *= times;
            
            var random = new Unity.Mathematics.Random(hash);

            bool isContains;
            int uints, numRewards = rewards.Length, accessoryIndex = numRewards;
            long ticks = (long)Math.Round(Math.Min(deltaTicks == 0 ? DateTime.UtcNow.Ticks - this.ticks : deltaTicks, maxTime) * sweepCardMultiplier);
            UserRewardData result;
            var results = new Dictionary<int, UserRewardData>();
            var rewardTimes = new int[numRewards];
            do
            {
                isContains = false;
                for(int i = 0; i < numRewards; ++i)
                {
                    uints = ++rewardTimes[i];
                    
                    ref var reward = ref rewards[i];
                    if (reward.maxUnits > 0 && reward.maxUnits < uints || 
                        uints * reward.unitTime > ticks ||
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
                    
                    if (reward.type == UserRewardType.Accessory)
                        results[accessoryIndex++] = result;
                    else
                        results[i] = result;
                }

            } while (isContains);

            var values = new UserRewardData[results.Count];
            results.Values.CopyTo(values, 0);
            return values;
        }

        public void Upgrade(in UserTipLevel level)
        {
            int numRewards = rewards == null ? 0 : rewards.Length, source = numRewards;
            for (int i = 0; i < numRewards; ++i)
            {
                if(Array.IndexOf(level.rewardNames, rewards[i].name) == -1)
                    continue;

                --numRewards;
                Array.Copy(rewards, i + 1, rewards, i, numRewards - i);
                --i;
            }
            
            int destination = numRewards + (level.next.rewards == null ? 0 : level.next.rewards.Length);
            Array.Resize(ref rewards, destination);
            for (int i = numRewards; i < destination; ++i)
                rewards[i] = level.next.rewards[i - numRewards];
        }
    }

    public struct TipData
    {
        /// <summary>
        /// 快速游荡每次消耗多少体力
        /// </summary>
        public int energiesPerTime;

        public Tip value;

        public UserTipLevel[] levels;
    }

    IEnumerator QueryTip(
        uint userID, 
        Action<TipData> onComplete);

    IEnumerator CollectTip(
        uint userID,
        Action<long, Memory<UserReward>> onComplete);

    IEnumerator UseTip(
        uint userID,
        Action<Memory<UserReward>> onComplete);

    IEnumerator UpgradeTip(uint userID, uint tipLevelID, Action<UserTipLevel.Next?> onComplete);
}
