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
            return null;
        }
    }
    
    IEnumerator QueryTip(
        uint userID, 
        Action<Tip> onComplete);

    IEnumerator CollectTip(
        uint userID,
        Action<Memory<UserRewardData>> onComplete);
}
