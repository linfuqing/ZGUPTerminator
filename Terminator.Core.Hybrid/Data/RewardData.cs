using System;
using System.Collections;
using UnityEngine;

public interface IRewardData
{
    public struct Reward
    {
        public string name;
        public int count;
    }

    public struct Rewards
    {
        public string poolName;
        public Reward[] values;
    }
    
    public static IRewardData instance;
    
    IEnumerator ApplyReward(
        string poolName, 
        Action<Rewards> onComplete);
}
