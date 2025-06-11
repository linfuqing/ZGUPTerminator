using System;
using System.Collections;

public enum UserActiveType
{
    Day, 
    Week
}

public struct UserActive
{
    [Flags]
    public enum Flag
    {
        Collected = 0x01
    }
    
    public string name;

    public uint id;

    /// <summary>
    /// 天数或活跃值
    /// </summary>
    public int exp;
    
    public Flag flag;

    public UserRewardData[] rewards;
}

public struct UserQuest
{
    [Flags]
    public enum Flag
    {
        Collected = 0x01
    }

    public enum Type
    {
        Login, 
        
        UpgradeCard, 
        UpgradeAccessory,
        
        Level
    }

    public string name;
    public uint id;

    public Type type;

    public Flag flag;

    public int count;
    public int capacity;

    public UserRewardData[] rewards;
}

public partial interface IUserData
{
    public struct SignIn
    {
        public int day;
        
        public UserActive[] actives;
    }
    
    IEnumerator QuerySignIn(uint userID, Action<SignIn> onComplete);

    IEnumerator CollectSignIn(uint userID, uint activeID, Action<Memory<UserReward>> onComplete);

    public struct Active
    {
        public int exp;
        
        public UserActive[] actives;

        public UserQuest[] quests;
    }
    
    IEnumerator QueryActive(uint userID, UserActiveType type, Action<Active> onComplete);
    
    IEnumerator CollectActive(
        uint userID, 
        UserActiveType type, 
        int activeID, 
        Action<Memory<UserReward>> onComplete);
    
    IEnumerator CollectActiveQuest(
        uint userID, 
        UserActiveType type, 
        int questID, 
        Action<Memory<UserReward>> onComplete);
    
    IEnumerator CollectAchievementQuest(
        uint userID, 
        int questID, 
        Action<Memory<UserReward>> onComplete);
    
    IEnumerator QueryAchievements(
        uint userID, 
        Action<Memory<UserQuest>> onComplete);
}
