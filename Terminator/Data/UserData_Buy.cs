using System;
using System.Collections;

public enum UserCurrencyType
{
    Gold,
    Diamond,
    Ad, 
    Free
}

public struct UserProduct
{
    public enum Type
    {
        Normal, 
        Day
    }

    [Flags]
    public enum Flag
    {
        Collected = 0x01
    }
    
    public string name;
    
    public uint id;
    
    public Flag flag;

    public Type productType;

    /// <summary>
    /// 货币类型
    /// </summary>
    public UserCurrencyType currencyType;

    /// <summary>
    /// 价钱
    /// </summary>
    public int price;

    /// <summary>
    /// 购买奖励
    /// </summary>
    public UserRewardData[] rewards;
}

public partial interface IUserData
{
    public struct Energies
    {
        /// <summary>
        /// 每次购买获得多少体力
        /// </summary>
        public int energyPerTime;
        
        /// <summary>
        /// 每次扣多少钻石
        /// </summary>
        public int diamondPerTime;
        /// <summary>
        /// 用钻石购买次数
        /// </summary>
        public int buyTimesByDiamond;
        /// <summary>
        /// 看广告购买次数
        /// </summary>
        public int buyTimesByAd;
    }
    
    /// <summary>
    /// 查询购买体力
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryEnergies(uint userID, Action<Energies> onComplete);
    
    /// <summary>
    /// 购买体力
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator BuyEnergies(uint userID, Action<bool> onComplete);
    
    /// <summary>
    /// 查询每日商品
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryProducts(uint userID, Action<Memory<UserProduct>> onComplete);
    
    /// <summary>
    /// 购买每日商品
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="productID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator BuyProduct(uint userID, uint productID, Action<Memory<UserReward>> onComplete);
}
