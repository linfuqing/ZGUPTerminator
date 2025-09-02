using System;
using System.Collections;

public struct UserGroup
{
    public string name;

    public uint id;
}

public struct UserPurchasePool
{
    public string name;

    public uint id;

    //花费的钻石数量
    public int diamond;
    
    //抽一次获得多少金币
    public int gold;
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

        /// <summary>
        /// 技能组伤害
        /// </summary>
        public float skillGroupDamage;
    }

    public string name;

    public uint id;

    public Level[] levels;
}

public struct UserCard
{
    public struct Group
    {
        /// <summary>
        /// 被装备到的套装ID
        /// </summary>
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

    public float skillGroupDamage;

    /// <summary>
    /// 技能
    /// </summary>
    public string[] skillNames;
    
    /// <summary>
    /// 装备卡组
    /// </summary>
    public Group[] groups;
}

public partial interface IUserData
{
    public struct Purchases
    {
        [Flags]
        public enum Flag
        {
            Unlock = 0x01, 
            UnlockFirst = 0x02 | Unlock, 
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
            Unlock = 0x01, 
            UnlockFirst = 0x02 | Unlock, 
            
            CardFirst = 0x04, 
            CardUpgrade = 0x08, 
            CardReplace = 0x10
        }

        /// <summary>
        /// 用来判定首次解锁完整卡槽并播放动画
        /// </summary>
        public Flag flag;

        /// <summary>
        /// 卡牌容量
        /// </summary>
        public int capacity;

        public uint selectedGroupID;

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
    /// 卡牌
    /// </summary>
    IEnumerator QueryCard(
        uint userID,
        uint cardID, 
        Action<UserCard> onComplete);

    /// <summary>
    /// 设置卡组
    /// </summary>
    IEnumerator SetCardGroup(uint userID, uint groupID, Action<bool> onComplete);

    /// <summary>
    /// 装备卡组或卸下卡组(position为-1）
    /// </summary>
    IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete);

    /// <summary>
    /// 升级卡牌
    /// </summary>
    IEnumerator UpgradeCard(uint userID, uint cardID, Action<bool> onComplete);
}