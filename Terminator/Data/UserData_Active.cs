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
        Login, 
        
        /// <summary>
        /// 天赋升级次数
        /// </summary>
        Talents, 
        
        /// <summary>
        /// 升级卡片
        /// </summary>
        CardToUpgrade, 
        
        /// <summary>
        /// 获得技能卡数
        /// </summary>
        Cards, 
        
        /// <summary>
        /// 获得稀有技能卡数
        /// </summary>
        Cards1, 
        /// <summary>
        /// 获得史诗技能卡数
        /// </summary>
        Cards2,
        /// <summary>
        /// 获得传说技能卡数
        /// </summary>
        Cards3,

        /// <summary>
        /// 获得装备数量
        /// </summary>
        Accessories,
        
        /// <summary>
        /// 获得稀有装备数量
        /// </summary>
        Accessories1, 
        
        /// <summary>
        /// 获得史诗装备数量
        /// </summary>
        Accessories2, 
        
        /// <summary>
        /// 获得传说装备数量
        /// </summary>
        Accessories3, 

        /// <summary>
        /// 升级装备槽
        /// </summary>
        AccessorySlotToUpgrade,
        
        /// <summary>
        /// 合成装备
        /// </summary>
        AccessoryToUprank, 
        
        /// <summary>
        /// 开宝箱次数
        /// </summary>
        Purchase, 
        
        /// <summary>
        /// 触发保底次数
        /// </summary>
        Purchases, 

        /// <summary>
        /// 游荡
        /// </summary>
        Tip, 
        
        /// <summary>
        /// 挑战主线关卡次数
        /// </summary>
        Stage, 
        
        /// <summary>
        /// 击杀怪物数
        /// </summary>
        KillCount, 
        /// <summary>
        /// 击杀BOSS数
        /// </summary>
        KillBoss, 
        
        /// <summary>
        /// 获得金币
        /// </summary>
        GoldsToGet, 

        /// <summary>
        /// 消耗金币
        /// </summary>
        GoldsToUse, 

        /// <summary>
        /// 获得钻石
        /// </summary>
        DiamondsToGet, 

        /// <summary>
        /// 消耗钻石
        /// </summary>
        DiamondsToUse, 
        
        /// <summary>
        /// 消耗体力
        /// </summary>
        EnergiesToUse, 
        /// <summary>
        /// 购买体力
        /// </summary>
        EnergiesToBuy, 
        
        /// <summary>
        /// 充值次数
        /// </summary>
        Buy, 
        
        /// <summary>
        /// 达到主线章节
        /// </summary>
        AchievementLevels, 
        
        /// <summary>
        /// 技能卡达到最高级别
        /// </summary>
        AchievementCard,
        
        /// <summary>
        /// 技能卡种类
        /// </summary>
        AchievementCardStyles,

        /// <summary>
        /// 获得装备种类
        /// </summary>
        AchievementAccessoryStyles,
        
        /// <summary>
        /// 装备槽位最高级
        /// </summary>
        AchievementAccessorySlot,
        
        /// <summary>
        /// 全身装备槽位最低级
        /// </summary>
        AchievementAccessorySlots,
        
        /// <summary>
        /// 获得角色数量
        /// </summary>
        AchievementRoles, 

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
}
