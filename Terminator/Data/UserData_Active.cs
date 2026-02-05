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
        Locked = 0x01, 
        Collected = 0x02
    }
    
    public string name;

    public uint id;

    public Flag flag;

    /// <summary>
    /// 天数或活跃值
    /// </summary>
    public int exp;

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
        /// <summary>
        /// 登录
        /// </summary>
        Login = 0, 
        
        /// <summary>
        /// 天赋升级次数
        /// </summary>
        Talents = 1, 
        
        /// <summary>
        /// 升级卡片
        /// </summary>
        CardToUpgrade = 2, 
        
        /// <summary>
        /// 获得技能卡数
        /// </summary>
        Cards = 3, 
        
        /// <summary>
        /// 获得稀有技能卡数
        /// </summary>
        Cards1 = 4, 
        /// <summary>
        /// 获得史诗技能卡数
        /// </summary>
        Cards2 = 5,
        /// <summary>
        /// 获得传说技能卡数
        /// </summary>
        Cards3 = 6,

        /// <summary>
        /// 获得装备数量
        /// </summary>
        Accessories = 7,
        
        /// <summary>
        /// 升级装备槽
        /// </summary>
        AccessorySlotToUpgrade = 23,
        
        /// <summary>
        /// 合成装备
        /// </summary>
        AccessoryToUprank = 24, 
        
        /// <summary>
        /// 开宝箱次数
        /// </summary>
        Purchase = 25, 
        
        /// <summary>
        /// 触发保底次数
        /// </summary>
        Purchases = 26, 

        /// <summary>
        /// 游荡
        /// </summary>
        Tip = 27, 
        
        /// <summary>
        /// 挑战主线关卡次数
        /// </summary>
        Stage = 28, 
        
        /// <summary>
        /// 击杀怪物数
        /// </summary>
        KillCount = 29, 
        /// <summary>
        /// 击杀BOSS数
        /// </summary>
        KillBoss = 30, 
        
        /// <summary>
        /// 获得金币
        /// </summary>
        GoldsToGet = 31, 

        /// <summary>
        /// 消耗金币
        /// </summary>
        GoldsToUse = 32, 

        /// <summary>
        /// 获得钻石
        /// </summary>
        DiamondsToGet = 33, 

        /// <summary>
        /// 消耗钻石
        /// </summary>
        DiamondsToUse = 34, 
        
        /// <summary>
        /// 消耗体力
        /// </summary>
        EnergiesToUse = 35, 
        /// <summary>
        /// 购买体力
        /// </summary>
        EnergiesToBuy = 36, 
        
        /// <summary>
        /// 充值次数
        /// </summary>
        Buy = 37, 
        
        /// <summary>
        /// 达到主线章节
        /// </summary>
        AchievementChapters = 38, 
        
        /// <summary>
        /// 技能卡升级最高次数
        /// </summary>
        AchievementCard = 39,
        
        /// <summary>
        /// 技能卡种类
        /// </summary>
        AchievementCardStyles = 40,

        /// <summary>
        /// 获得装备种类
        /// </summary>
        AchievementAccessoryStyles = 41,
        
        /// <summary>
        /// 装备槽位最高级
        /// </summary>
        AchievementAccessorySlot = 42,
        
        /// <summary>
        /// 全身装备槽位最低级
        /// </summary>
        AchievementAccessorySlots = 43,
        
        /// <summary>
        /// 获得角色数量
        /// </summary>
        AchievementRoles = 44, 

        Unknown
    }

    public string name;
    public uint id;

    public Type type;

    public Flag flag;

    public int count;
    public int capacity;

    public UserRewardData[] rewards;
}

public struct UserActiveEvent
{
    public uint id;
    public string name;
        
    public int startDay;
    public int days;

    public int exp;

    public UserActive[] actives;
    public UserQuest[] quests;
}

public partial interface IUserData
{
    public struct SignIn
    {
        public int day;
        
        public UserActive[] actives;
    }
    
    IEnumerator QuerySignIn(uint userID, Action<SignIn> onComplete);

    IEnumerator CollectSignIn(uint userID, Action<Memory<UserReward>> onComplete);

    public struct Actives
    {
        public int exp;
        
        public UserActive[] actives;

        public UserQuest[] quests;
    }
    
    IEnumerator QueryActives(uint userID, UserActiveType type, Action<Actives> onComplete);
    
    IEnumerator CollectActive(
        uint userID, 
        uint activeID, 
        UserActiveType type, 
        Action<Memory<UserReward>> onComplete);
    
    IEnumerator CollectActiveQuest(
        uint userID, 
        uint questID, 
        UserActiveType type, 
        Action<Memory<UserReward>> onComplete);
    
    IEnumerator CollectAchievementQuest(
        uint userID, 
        uint questID, 
        Action<Memory<UserReward>> onComplete);
    
    IEnumerator QueryAchievements(
        uint userID, 
        Action<Memory<UserQuest>> onComplete);

    public struct ActiveEvents
    {
        public int days;
        
        public UserActiveEvent[] values;
    }

    IEnumerator QueryActiveEvents(
        uint userID,
        Action<IUserData.ActiveEvents> onComplete);

    IEnumerator CollectActiveEventActive(uint userID, uint activeEventID, uint activeID,
        Action<Memory<UserReward>> onComplete);

    IEnumerator CollectActiveEventQuest(uint userID, uint activeEventID, uint questID,
        Action<Memory<UserReward>> onComplete);
}
