using System;
using System.Collections;

public struct UserPurchasePool
{
    public string name;

    public uint id;

    //剩余每日免费次数
    public int freeTimes;
    
    //花费的钻石数量
    public int diamond;
    
    //抽一次获得多少金币
    public int gold;
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
}
