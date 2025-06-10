using System;
using System.Collections;

/// <summary>
/// 代表令牌可领取的单项
/// </summary>
public struct UserPurchaseToken
{
    [Flags]
    public enum Flag
    {
        Collected = 0x01
    }
    
    public string name;

    public uint id;

    /// <summary>
    /// 领取状态
    /// </summary>
    public Flag flag;

    /// <summary>
    /// 基金代表等级数，通行证代表日活经验
    /// </summary>
    public int exp;
    
    /// <summary>
    /// 奖励
    /// </summary>
    public UserRewardData[] rewards;
}

public partial interface IUserData
{
    public struct PurchaseItems
    {
        public enum Status
        {
            /// <summary>
            /// 未购买&过期
            /// </summary>
            Invaild,
            /// <summary>
            /// 已购买未领取（闪退等情况）
            /// </summary>
            Purchased, 
            /// <summary>
            /// 当前购买已领取，有的付费（如充值）等价于<see cref="Invaild"/>
            /// </summary>
            Vaild
        }
        
        /// <summary>
        /// 付费状态
        /// </summary>
        public Status status;
        /// <summary>
        /// 付费次数
        /// </summary>
        public int times;
        /// <summary>
        /// 有效期
        /// </summary>
        public long ticks;
        /// <summary>
        /// 奖励
        /// </summary>
        public UserRewardData[] rewards;
    }

    public struct PurchaseTokens
    {
        public int exp;
        
        public UserPurchaseToken[] values;
    }
    
    /// <summary>
    /// 针对常规付费的查询
    /// </summary>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryPurchaseItems(PurchaseType type, int level, Action<PurchaseItems> onComplete);

    /// <summary>
    /// 针对对应令牌的查询
    /// </summary>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryPurchaseTokens(PurchaseType type, int level, Action<PurchaseTokens> onComplete);
    
    /// <summary>
    /// 付费后
    /// </summary>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator CollectPurchaseItem(PurchaseType type, int level, Action<Memory<UserReward>> onComplete);

    /// <summary>
    /// 领取赛季开始至今日的所有令牌奖励
    /// </summary>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator CollectPurchaseToken(PurchaseType type, int level, Action<Memory<UserReward>> onComplete);
}

public partial class UserData
{
    public IEnumerator QueryPurchaseItems(PurchaseType type, int level, Action<IUserData.PurchaseItems> onComplete)
    {
        return UserDataMain.instance.QueryPurchaseItems(type, level, onComplete);
    }
    
    public IEnumerator CollectPurchaseItem(PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectPurchaseItem(type, level, onComplete);
    }

    public IEnumerator QueryPurchaseTokens(PurchaseType type, int level, Action<IUserData.PurchaseTokens> onComplete)
    {
        return UserDataMain.instance.QueryPurchaseTokens(type, level, onComplete);
    }
    
    public IEnumerator CollectPurchaseToken(PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectPurchaseToken(type, level, onComplete);
    }
}
