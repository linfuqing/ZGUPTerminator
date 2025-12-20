using System;
using System.Collections;

public struct UserGroup
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

public struct UserCardBond
{
    public struct Card
    {
        public string name;

        /// <summary>
        /// 卡牌当前等级
        /// </summary>
        public int level;
    }

    [Serializable]
    public struct Level
    {
        /// <summary>
        /// 需要达到卡牌总等级才能升级
        /// </summary>
        public int cardLevels;

        public UserPropertyData property;
    }

    public string name;

    public uint id;

    /// <summary>
    /// 羁绊等级，0为未激活，2代表<see cref="levels"/>索引0和1的羁绊等级属性已经被激活，以此类推
    /// </summary>
    public int level;
    
    public Level[] levels;

    public Card[] cards;
}

public partial interface IUserData
{
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
        uint[] cardIDs, 
        Action<Memory<UserCard>> onComplete);

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
    
    /// <summary>
    /// 查询卡牌羁绊
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryCardBonds(uint userID, Action<Memory<UserCardBond>> onComplete);
    
    /// <summary>
    /// 升级卡牌羁绊，并返回当前卡牌羁绊等级
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="cardBondID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator UpgradeCardBonds(uint userID, uint cardBondID, Action<int?> onComplete);
}