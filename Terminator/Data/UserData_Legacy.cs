using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct UserStage_v0
{
    [Flags]
    public enum Flag
    {
        Unlock = 0x01, 
        Collected = 0x02
    }

    public enum RewardType
    {
        Gold, 
        Weapon
    }
        
    public string name;
    public uint id;
    //public int levelID;
    public Flag flag;
    public RewardType rewardType;
    public int rewardCount;
}

public partial interface IUserData
{
    
}

public partial class UserData
{
}
