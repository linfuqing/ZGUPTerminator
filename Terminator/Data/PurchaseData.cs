using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public enum PurchaseType
{
    //首充，分0，1，2三档
    FirstCharge, 
    
    //免广告卡
    AdvertisingFreeCard,
    //补给卡
    DiamondCard, 
    //月卡
    MonthlyCard,
    //扫荡卡
    SweepCard, 
    
    //基金
    Fund, 
    //通行证，分0，1两档
    Pass,
    
    //章节礼包，章节为对应数字挡位，只能购买已打通的章节
    Level,
    
    //存钱罐
    GoldBank, 
    
    //钻石，分为0，1，2，3，4，5这6个首充挡位以及6，7，8，9，10，11这6个普通挡位
    Diamond, 
    
    //买体力
    Energy, 
    
    //活动预留
    Other
}

public interface IPurchaseAPI
{
    /*public struct Product
    {
        public PurchaseType type;

        public int level;

        public Product(PurchaseType type, int level)
        {
            this.type = type;
            this.level = level;
        }

        public string ToString(string prefix)
        {
            return $"{prefix}{type}{level}";
        }
    }*/

    public struct Metadata
    {
        public string localizedPriceString;
        public string localizedTitle;
        public string localizedDescription;
        public string isoCurrencyCode;

        public Metadata(
            string localizedPriceString, 
            string localizedTitle, 
            string localizedDescription, 
            string isoCurrencyCode)
        {
            this.localizedPriceString = localizedPriceString;
            this.localizedTitle = localizedTitle;
            this.localizedDescription = localizedDescription;
            this.isoCurrencyCode = isoCurrencyCode;
        }
    }

    public static IPurchaseAPI instance;

    /*public static readonly Product[] AllProducts = new []
    {
        new Product(PurchaseType.FirstCharge, 0), 
        new Product(PurchaseType.FirstCharge, 1), 
        new Product(PurchaseType.FirstCharge, 2), 
        
        new Product(PurchaseType.AdvertisingFreeCard, 0), 
        new Product(PurchaseType.DiamondCard, 0), 
        new Product(PurchaseType.MonthlyCard, 0), 
        new Product(PurchaseType.SweepCard, 0), 
        
        new Product(PurchaseType.Fund, 0), 
        new Product(PurchaseType.Fund, 1), 
        
        new Product(PurchaseType.Pass, 0), 
        new Product(PurchaseType.Pass, 1), 
        
        new Product(PurchaseType.Level, 0), 
        new Product(PurchaseType.Level, 1), 
        new Product(PurchaseType.Level, 2), 
        new Product(PurchaseType.Level, 3), 
        new Product(PurchaseType.Level, 4), 
        new Product(PurchaseType.Level, 5), 
        new Product(PurchaseType.Level, 6), 
        new Product(PurchaseType.Level, 7), 
        new Product(PurchaseType.Level, 8),  
        new Product(PurchaseType.Level, 9), 
        
        new Product(PurchaseType.GoldBank, 0), 
        
        new Product(PurchaseType.Diamond, 0), 
        new Product(PurchaseType.Diamond, 1), 
        new Product(PurchaseType.Diamond, 2), 
        new Product(PurchaseType.Diamond, 3), 
        new Product(PurchaseType.Diamond, 4), 
        new Product(PurchaseType.Diamond, 5), 
        
        new Product(PurchaseType.Diamond, 6), 
        new Product(PurchaseType.Diamond, 7), 
        new Product(PurchaseType.Diamond, 8), 
        new Product(PurchaseType.Diamond, 9), 
        new Product(PurchaseType.Diamond, 11), 
        
        new Product(PurchaseType.Energy, 0)
    };*/
    
    bool isPending { get; }

    void Query(uint userID, PurchaseType purchaseType, int level, Action<Metadata?> onComplete);
    
    void Buy(uint userID, PurchaseType type, int level, Action<bool> onComplete);
}

public interface IPurchaseData
{
    public static IPurchaseData instance;
    
    public struct Input
    {
        public PurchaseType type;

        public int level;

        public string ToString(string prefix)
        {
            return $"{prefix}{type}{level}";
        }
    }

    public struct Output
    {
        /// <summary>
        /// 购买次数
        /// </summary>
        public int times;

        /// <summary>
        /// 有效期
        /// </summary>
        public int deadline;
        
        /// <summary>
        /// 购买时间
        /// </summary>
        public long ticks;
        
        public bool IsValid(int times)
        {
            return times > 0 && this.times >= times && (deadline == 0 || ticks + deadline * TimeSpan.TicksPerSecond > DateTime.UtcNow.Ticks);
        }
        
        public int GetDeadline(long ticks)
        {
            int seconds = this.ticks > 0 ? (int)((ticks - this.ticks) / TimeSpan.TicksPerSecond) : 0;

            return seconds < deadline ? deadline - seconds : 0;
        }
    }
    
    /// <summary>
    ///  查询付费状态，不需要查询奖励的时候使用，需要查询奖励时用<see cref="IUserData.QueryPurchaseItems"/>替代。
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator Query(uint userID, Input[] inputs, Action<Output[]> onComplete);
    
    /// <summary>
    /// 购买商品
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator Buy(uint userID, PurchaseType type, int level, Action<long?> onComplete);
}

public class PurchaseData : MonoBehaviour, IPurchaseData
{
    public const string NAME_SPACE_TIMES = "PurchaseTimes";
    public const string NAME_SPACE_DEADLINE = "PurchaseDeadline";
    public const string NAME_SPACE_PAY_TIME = "PurchasePayTime";
    
    //public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IPurchaseData.Output Query(in IPurchaseData.Input input)
    {
        IPurchaseData.Output result;
        //在服务器，该数据结构要有订单号及状态，如果订单未完成，需要查询平台端有没有完成，完成了执行Buy逻辑
        result.times = PlayerPrefs.GetInt(input.ToString(NAME_SPACE_TIMES));
        result.deadline = PlayerPrefs.GetInt(input.ToString(NAME_SPACE_DEADLINE));
        result.ticks = DateTimeUtility.GetTicks((uint)PlayerPrefs.GetInt(input.ToString(NAME_SPACE_PAY_TIME)));

        return result;
    }

    public static IPurchaseData.Output Query(PurchaseType type, int level)
    {
        IPurchaseData.Input input;
        input.type = type;
        input.level = level;

        return Query(input);
    }

    public static bool IsValid(PurchaseType type, int level, string key, out int times, out IPurchaseData.Output output)
    {
        IPurchaseData.Input input;
        input.type = type;
        input.level = level;

        key = input.ToString(key);

        times = PlayerPrefs.GetInt(key);

        output = Query(input);

        return output.IsValid(times);
    }

    public static bool Exchange(PurchaseType type, int level, string key)
    {
        IPurchaseData.Input input;
        input.type = type;
        input.level = level;

        key = input.ToString(key);

        int times = PlayerPrefs.GetInt(key) + 1;

        var output = Query(input);
        if(output.times < times)
            return false;
        
        PlayerPrefs.SetInt(key, times);

        return true;
    }

    public static long Buy(PurchaseType type, int level)
    {
        IPurchaseData.Input input;
        input.type = type;
        input.level = level;
        
        var output = Query(input);
        
        PlayerPrefs.SetInt(input.ToString(NAME_SPACE_TIMES), ++output.times);

        int seconds;
        long ticks = DateTime.UtcNow.Ticks;
        switch (type)
        {
            case PurchaseType.FirstCharge:
                input.type = PurchaseType.AdvertisingFreeCard;
                input.level = level;
                output = Query(input);
                //if (output.times < 1)
                {
                    seconds = output.GetDeadline(ticks);
                    seconds += (int)(TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond);//(int)(DateTime.Today.AddDays(1).ToUniversalTime().Ticks - ticks);
                
                    PlayerPrefs.SetInt(input.ToString(NAME_SPACE_DEADLINE), seconds);
                }
                
                seconds = 0;
                break;
            case PurchaseType.MonthlyCard:
            case PurchaseType.SweepCard:
                seconds = output.GetDeadline(ticks);
                seconds += (int)(TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond) * 30;
                
                PlayerPrefs.SetInt(input.ToString(NAME_SPACE_DEADLINE), seconds);
                break;
            case PurchaseType.Pass:
                seconds = output.GetDeadline(ticks);
                seconds = (int)((seconds == 0
                        ? DateTime.Today
                        : new DateTime(seconds * TimeSpan.TicksPerSecond + ticks).ToLocalTime()).AddMonths(1)
                    .ToUniversalTime().Ticks - ticks);
                
                PlayerPrefs.SetInt(input.ToString(NAME_SPACE_DEADLINE), seconds);
                break;
            case PurchaseType.GoldBank:
                seconds = output.GetDeadline(ticks);
                seconds = (int)((seconds == 0
                        ? DateTime.Today
                        : new DateTime(seconds * TimeSpan.TicksPerSecond + ticks).ToLocalTime()).AddDays(1)
                    .ToUniversalTime().Ticks - ticks);

                PlayerPrefs.SetInt(input.ToString(NAME_SPACE_DEADLINE), seconds);
                break;
            default:
                seconds = 0;
                
                PlayerPrefs.DeleteKey(input.ToString(NAME_SPACE_DEADLINE));
                break;
        }

        PlayerPrefs.SetInt(input.ToString(NAME_SPACE_PAY_TIME), (int)DateTimeUtility.GetSeconds(ticks));

        return seconds == 0 ? 0 : (int)DateTimeUtility.GetTicks((uint)seconds);
    }
    
    public IEnumerator Query(
        uint userID, 
        IPurchaseData.Input[] inputs, 
        Action<IPurchaseData.Output[]> onComplete)
    {
        yield return null;

        int length = inputs.Length;
        var outputs = new IPurchaseData.Output[length];
        for (int i = 0; i < length; ++i)
            outputs[i] = Query(inputs[i]);
        
        onComplete(outputs);
    }

    public IEnumerator Buy(uint userID, PurchaseType type, int level, Action<long?> onComplete)
    {
        yield return null;

        var api = IPurchaseAPI.instance;
        if (api == null)
        {
            long result = Buy(type, level);
            onComplete(result);
        }
        else
        {
            bool result = false;
            api.Buy(userID, type, level, x =>
            {
                result = true;
                
                //这里实现服务器的时候要注意，平台回调或者确认订单（在服务端，该数据结构要有订单号）完成后，Buy才会被执行（而不是在这里执行）。
                onComplete(x ? Buy(type, level) : null);
            });
            
            while(!result)
                yield return null;
        }
    }

    void Awake()
    {
        IPurchaseData.instance = this;
    }
}