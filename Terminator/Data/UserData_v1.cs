using System;
using System.Collections;

public struct UserStageReward
{
    [Flags]
    public enum Flag
    {
        Unlock = 0x01, 
        Collected = 0x02
    }

    public enum Condition
    {
        None, 
        Once, 
        NoDamage
    }
    
    [Serializable]
    public struct PoolKey
    {
        public string name;
        public int count;
    }

    public string name;
    public uint id;
    public Flag flag;
    public Condition condition;
    public int gold;
    public PoolKey[] poolKeys;
}

public struct UserGroup
{
    public string name;

    public uint id;
}

public struct UserItem
{
    public string name;

    public uint id;

    public int count;
}

public struct UserPurchasePool
{
    public string name;

    public uint id;
}

public struct UserCardStyle
{
    [Serializable]
    public struct Level
    {
        public string name;

        /// <summary>
        /// 升级需要的卡片数量
        /// </summary>
        public int count;
        /// <summary>
        /// 升级需要的金币数量
        /// </summary>
        public int gold;
    }

    public string name;

    public uint id;

    public Level[] levels;
}

public struct UserCard
{
    public struct Group
    {
        public uint groupID;

        /// <summary>
        /// 装备位置，-1代表没装备
        /// </summary>
        public int position;
    }

    public string name;

    public uint id;

    /// <summary>
    /// 属于什么品质<see cref="UserCardStyle"/>：普通、稀有、史诗、传说
    /// </summary>
    public uint styleID;

    /// <summary>
    /// 等级
    /// </summary>
    public int level;

    /// <summary>
    /// 卡片数量
    /// </summary>
    public int count;

    /// <summary>
    /// 装备卡组
    /// </summary>
    public Group[] groups;
}

[Serializable]
public struct UserRole
{
    public string name;

    public uint id;

    public int hp;
    public int attack;
    public int defence;

    /// <summary>
    /// 被装备到的套装ID
    /// </summary>
    public uint[] groupIDs;
}

public struct UserAccessoryStyle
{
    public struct Level
    {
        public string name;

        /// <summary>
        /// 升级需要的卷轴ID
        /// </summary>
        public uint itemID;

        /// <summary>
        /// 升级需要的卷轴数量
        /// </summary>
        public int count;
    }

    [Serializable]
    public struct Stage
    {
        public string name;

        /// <summary>
        /// 升阶需要的相同装备数量
        /// </summary>
        public int count;
    }

    public string name;

    public uint id;

    /// <summary>
    /// 当前等级
    /// </summary>
    public int level;

    /// <summary>
    /// 当前等级描述
    /// </summary>
    public Level levelDesc;

    /// <summary>
    /// 阶
    /// </summary>
    public Stage[] stages;
}

public struct UserAccessory
{
    public string name;

    public uint id;

    /// <summary>
    /// 属于什么类型<see cref="UserAccessoryStyle"/>：头、手、脚、背包、超能武器
    /// </summary>
    public uint styleID;

    /// <summary>
    /// 阶
    /// </summary>
    public int stage;

    /// <summary>
    /// 被装备到的套装ID
    /// </summary>
    public int[] groupIDs;
}

public struct UserStage
{
    /*[Flags]
    public enum Flag
    {
        Unlock = 0x01,
        Collected = 0x02
    }*/
    
    public string name;
    public uint id;
    public UserStageReward.Flag[] rewardFlags;
}

public partial struct UserLevel
{
    public UserStage[] stages;
}

public partial interface IUserData
{
    public struct Purchases
    {
        [Flags]
        public enum Flag
        {
            FirstUnlock = 0x01
        }

        public struct PoolKey
        {
            public uint poolID;
            public int count;
        }

        /// <summary>
        /// 用来判定首次解锁并播放动画
        /// </summary>
        public Flag flag;
        /// <summary>
        /// 钻石数量
        /// </summary>
        public int diamond;
        /// <summary>
        /// 卡池
        /// </summary>
        public UserPurchasePool[] pools;
        /// <summary>
        /// 钥匙
        /// </summary>
        public PoolKey[] poolKeys;
    }

    public struct Cards
    {
        [Flags]
        public enum Flag
        {
            FirstUnlock = 0x01
        }

        /// <summary>
        /// 用来判定首次解锁完整卡槽并播放动画
        /// </summary>
        public Flag flag;

        /// <summary>
        /// 卡牌容量
        /// </summary>
        public int capacity;

        /// <summary>
        /// 卡组
        /// </summary>
        public UserGroup[] groups;
        
        /// <summary>
        /// 卡牌
        /// </summary>
        public UserCard[] cards;

        /// <summary>
        /// 卡牌品质
        /// </summary>
        public UserCardStyle[] cardStyles;
    }

    public struct Roles
    {
        [Flags]
        public enum Flag
        {
            FirstUnlock = 0x01
        }

        public Flag flag;

        /// <summary>
        /// 卷轴
        /// </summary>
        public UserItem[] items;

        /// <summary>
        /// 套装
        /// </summary>
        public UserGroup[] groups;
        
        /// <summary>
        /// 角色
        /// </summary>
        public UserRole[] roles;

        /// <summary>
        /// 装备
        /// </summary>
        public UserAccessory[] accessories;
        
        /// <summary>
        /// 装备类型
        /// </summary>
        public UserAccessoryStyle[] accessoryStyles;
    }

    public struct StageCache
    {
        public int exp;
        public int expMax;
        public string[] skills;

        public StageCache(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                exp = 0;
                expMax = 0;

                skills = null;

                return;
            }
            
            skills = value.Split(UserData.SEPARATOR);
            
            int length = skills.Length;
            exp = int.Parse(skills[--length]);
            expMax = int.Parse(skills[--length]);
            
            Array.Resize(ref skills, length);
        }

        public override string ToString()
        {
            string result = $"{expMax}{UserData.SEPARATOR}{exp}";
            return skills == null || skills.Length < 1
                ? result
                : $"{string.Join(UserData.SEPARATOR, skills)}{UserData.SEPARATOR}{result}";
        }
    }

    public struct Stage
    {
        public int energy;
        public StageCache cache;
        public UserStageReward[] rewards;
    }

    public struct StageRewards
    {
        public int gold;
        public UserStageReward.PoolKey[] poolKeys;
    }

    /// <summary>
    /// 商店
    /// </summary>
    IEnumerator QueryPurchases(
        uint userID,
        Action<Purchases> onComplete);

    /// <summary>
    /// 抽卡
    /// </summary>
    IEnumerator Purchase(
        uint userID,
        uint purchasePoolID, 
        int times, 
        Action<Memory<UserItem>> onComplete);

    /// <summary>
    /// 卡牌
    /// </summary>
    IEnumerator QueryCards(
        uint userID,
        Action<Cards> onComplete);

    /// <summary>
    /// 装备卡组或卸下卡组(position为-1）
    /// </summary>
    IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete);

    /// <summary>
    /// 升级卡牌
    /// </summary>
    IEnumerator UpgradeCard(uint userID, uint cardID, Action<bool> onComplete);

    /// <summary>
    /// 角色
    /// </summary>
    IEnumerator QueryRoles(
        uint userID,
        Action<Roles> onComplete);

    /// <summary>
    /// 装备角色
    /// </summary>
    IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete);

    /// <summary>
    /// 角色养成
    /// </summary>
    IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID, 
        Action<Memory<UserTalent>> onComplete);

    /// <summary>
    /// 角色养成升级
    /// </summary>
    IEnumerator UpgradeRoleTalent(
        uint userID,
        uint roleID,
        uint talentID,
        Action<bool> onComplete);

    /// <summary>
    /// 装备或卸下装备
    /// </summary>
    IEnumerator SetAccessory(uint userID, uint accessoryID, int groupID, Action<bool> onComplete);

    /// <summary>
    /// 升级装备，返回下一级描述
    /// </summary>
    IEnumerator UpgradeAccessory(uint userID, uint accessoryStyleID, Action<UserAccessoryStyle.Level?> onComplete);

    /// <summary>
    /// 升阶装备
    /// </summary>
    IEnumerator UprankAccessory(uint userID, uint accessoryID, Action<bool> onComplete);
    
    /// <summary>
    /// 查询关卡
    /// </summary>
    IEnumerator QueryStage(
        uint userID,
        uint stageID,
        Action<Stage> onComplete);

    /// <summary>
    /// 继续游戏
    /// </summary>
    IEnumerator ApplyStage(
        uint userID,
        uint stageID,
        Action<bool> onComplete);

    IEnumerator SubmitStage(
        uint userID,
        StageFlag flag,
        int stage,
        int gold,
        int exp,
        int expMax,
        string[] skills,
        Action<bool> onComplete);
    
    /// <summary>
    /// 收集关卡奖励
    /// </summary>
    IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<bool> onComplete);

    /// <summary>
    /// 一键收集全部关卡奖励
    /// </summary>
    IEnumerator CollectStageRewards(uint userID, Action<StageRewards> onComplete);
}

public partial class UserData
{
    public IEnumerator QueryPurchases(
        uint userID,
        Action<IUserData.Purchases> onComplete)
    {
        return UserDataMain.instance.QueryPurchases(userID, onComplete);
    }

    public IEnumerator Purchase(
        uint userID,
        uint purchasePoolID,
        int times,
        Action<Memory<UserItem>> onComplete)
    {
        return UserDataMain.instance.Purchase(userID, purchasePoolID, times, onComplete);
    }

    public IEnumerator QueryCards(
        uint userID,
        Action<IUserData.Cards> onComplete)
    {
        return UserDataMain.instance.QueryCards(userID, onComplete);
    }

    public IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete)
    {
        return UserDataMain.instance.SetCard(userID, cardID, groupID, position, onComplete);
    }

    public IEnumerator UpgradeCard(uint userID, uint cardID, Action<bool> onComplete)
    {
        return UserDataMain.instance.UpgradeCard(userID, cardID, onComplete);
    }

    public IEnumerator QueryRoles(
        uint userID,
        Action<IUserData.Roles> onComplete)
    {
        return null;
    }

    public IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID,
        Action<Memory<UserTalent>> onComplete)
    {
        return null;
    }

    public IEnumerator UpgradeRoleTalent(
        uint userID,
        uint roleID,
        uint talentID,
        Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator SetAccessory(uint userID, uint accessoryID, int groupID, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator UpgradeAccessory(uint userID, uint accessoryStyleID, Action<UserAccessoryStyle.Level?> onComplete)
    {
        return null;
    }

    public IEnumerator UprankAccessory(uint userID, uint accessoryID, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator QueryStage(
        uint userID,
        uint stageID,
        Action<IUserData.Stage> onComplete)
    {
        return UserDataMain.instance.QueryStage(userID, stageID, onComplete);
    }

    public IEnumerator ApplyStage(
        uint userID,
        uint stageID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.ApplyStage(userID, stageID, onComplete);
    }
    
    public IEnumerator SubmitStage(
        uint userID,
        IUserData.StageFlag flag,
        int stage,
        int gold, 
        int exp, 
        int expMax, 
        string[] skills,
        Action<bool> onComplete)
    {
        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            onComplete(false);
            
            yield break;
        }

        var temp = levelCache.Value;

        __SubmitStageFlag(flag, temp.name, temp.stage, stage);

        IUserData.StageCache stageCache;
        stageCache.exp = exp;
        stageCache.expMax = expMax;
        stageCache.skills = skills;
        PlayerPrefs.SetString(GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE, temp.name, stage), stageCache.ToString());
        
        temp.stage = stage;
        temp.gold = gold;
        UserData.levelCache = temp;
        
        onComplete(true);
    }
    
    public IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<bool> onComplete)
    {
        return UserDataMain.instance.CollectStageReward(userID, stageRewardID, onComplete);
    }

    public IEnumerator CollectStageRewards(uint userID, Action<IUserData.StageRewards> onComplete)
    {
        return UserDataMain.instance.CollectStageRewards(userID, onComplete);
    }
}