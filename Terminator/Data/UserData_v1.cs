using System;
using System.Collections;

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

public struct UserPurchases
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

[Serializable]
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

public struct UserCards
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
    /// 卡牌品质
    /// </summary>
    public UserCardStyle[] cardStyles;

    /// <summary>
    /// 卡牌
    /// </summary>
    public UserCard[] cards;

    /// <summary>
    /// 卡组
    /// </summary>
    public UserGroup[] groups;
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

[Serializable]
public struct UserAccessoryStyle
{
    [Serializable]
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

public struct UserRoles
{
    [Flags]
    public enum Flag
    {
        FirstUnlock = 0x01
    }

    public Flag flag;

    /// <summary>
    /// 角色
    /// </summary>
    public UserRole[] roles;

    /// <summary>
    /// 装备类型
    /// </summary>
    public UserAccessoryStyle[] accessoryStyles;

    /// <summary>
    /// 装备
    /// </summary>
    public UserAccessory[] accessories;

    /// <summary>
    /// 卷轴
    /// </summary>
    public UserItem[] items;

    /// <summary>
    /// 套装
    /// </summary>
    public UserGroup[] groups;
}

public partial interface IUserData
{
    /// <summary>
    /// 商店
    /// </summary>
    IEnumerator QueryPurchases(
        uint userID,
        Action<UserPurchases> onComplete);

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
        Action<UserCards> onComplete);

    /// <summary>
    /// 装备卡组或卸下卡组(position为-1）
    /// </summary>
    IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete);

    /// <summary>
    /// 升级卡牌
    /// </summary>
    IEnumerator UpgradeCard(uint userID, Action<bool> onComplete);

    /// <summary>
    /// 角色
    /// </summary>
    IEnumerator QueryRoles(
        uint userID,
        Action<UserRoles> onComplete);

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
}

public partial class UserData
{
    public IEnumerator QueryPurchases(
        uint userID,
        Action<UserPurchases> onComplete)
    {
        return null;
    }

    public IEnumerator Purchase(
        uint userID,
        uint purchasePoolID,
        int times,
        Action<Memory<UserItem>> onComplete)
    {
        return null;
    }

    public IEnumerator QueryCards(
        uint userID,
        Action<UserCards> onComplete)
    {
        return null;
    }

    public IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator UpgradeCard(uint userID, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator QueryRoles(
        uint userID,
        Action<UserRoles> onComplete)
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
}