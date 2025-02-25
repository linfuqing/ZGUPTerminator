using System;
using System.Collections;

public struct UserTip
{
    public int value;
    public int max;
    public uint unitTime;
    public long tick;
}

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

public struct UserWeapon
{
    [Flags]
    public enum Flag
    {
        Selected = 0x01
    }
    
    public string name;
    public uint id;
    public Flag flag;
}

public struct UserSkill
{
    public string name;
}

public partial interface IUserData
{
    IEnumerator QueryTip(
        uint userID, 
        Action<UserTip> onComplete);

    IEnumerator QuerySkills(
        uint userID, 
        Action<Memory<UserSkill>> onComplete);

    IEnumerator QueryWeapons(
        uint userID, 
        Action<Memory<UserWeapon>> onComplete);
    
    IEnumerator QueryTalents(
        uint userID, 
        Action<Memory<UserTalent>> onComplete);

    IEnumerator QueryStages(
        uint userID,
        Action<Memory<UserStage_v0>> onComplete);
    
    IEnumerator CollectStage(
        uint userID,
        uint stageID, 
        Action<bool> onComplete);
    
    IEnumerator CollectTalent(
        uint userID,
        uint talentID, 
        Action<bool> onComplete);
    
    IEnumerator SelectWeapon(
        uint userID,
        uint weaponID, 
        Action<bool> onComplete);
    
    IEnumerator CollectTip(
        uint userID,
        Action<int> onComplete);
}
