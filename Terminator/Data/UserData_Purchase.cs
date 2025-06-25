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
            Invalid,
            /// <summary>
            /// 已购买未领取（闪退等情况）
            /// </summary>
            Purchased, 
            /// <summary>
            /// 当前购买已领取，有的付费（如充值）等价于<see cref="Invalid"/>
            /// </summary>
            Valid
        }
        
        /// <summary>
        /// 付费状态
        /// </summary>
        public Status status;
        
        /// <summary>
        /// 存钱罐当前的金币或者钻石&章节礼包当前的章节数
        /// </summary>
        public int exp;

        /// <summary>
        /// 存钱罐需要的金币或者钻石&章节礼包需要达到的章节数
        /// </summary>
        public int expMax;

        /// <summary>
        /// （每日）购买上限
        /// </summary>
        public int capacity;
        /// <summary>
        /// 购买次数
        /// </summary>
        public int times;
        /// <summary>
        /// 有效期，为0则没有有效期
        /// </summary>
        public int deadline;
        /// <summary>
        /// 购买日期
        /// </summary>
        public long ticks;
        /// <summary>
        /// 奖励
        /// </summary>
        public UserRewardData[] rewards;
    }

    public struct PurchaseTokens
    {
        /// <summary>
        /// 首充天数，补给卡，月卡，游荡卡填0，基金代表章节，通行证代表活跃度
        /// </summary>
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
    /// 领取首充、补给卡、月卡、游荡卡日常奖励
    /// </summary>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator CollectPurchaseToken(PurchaseType type, int level, Action<Memory<UserReward>> onComplete);
    
    /// <summary>
    /// 领取赛季开始至今日的所有令牌奖励
    /// </summary>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator CollectPurchaseToken(PurchaseType type, Action<Memory<UserReward>> onComplete);
}
