using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    
    //章节礼包
    Level,
    
    //存钱罐
    PiggyBank, 
    
    //钻石，分为0，1，2，3，4，5，6个挡位
    Diamond, 
    
    //买体力
    Energy, 
    
    //活动预留
    Other
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
        public long ticks;
        
        public bool IsValid(int times)
        {
            return this.times == times && (ticks == 0 || ticks > DateTime.UtcNow.Ticks);
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
    public const string NAME_SPACE_TIMES = "PurchaseDataTimes";
    public const string NAME_SPACE_SECONDS = "PurchaseDataSeconds";
    
    public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IPurchaseData.Output Query(in IPurchaseData.Input input)
    {
        IPurchaseData.Output result;
        
        result.times = PlayerPrefs.GetInt(input.ToString(NAME_SPACE_TIMES));
        result.ticks = PlayerPrefs.GetInt(input.ToString(NAME_SPACE_SECONDS)) * TimeSpan.TicksPerSecond;
        if(result.ticks != 0)
            result.ticks += UnixEpoch.Ticks;

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

        if(!Query(input).IsValid(times))
            return false;
        
        PlayerPrefs.SetInt(key, times);

        return true;
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

        IPurchaseData.Input input;
        input.type = type;
        input.level = level;

        string timesKey = input.ToString(NAME_SPACE_TIMES);
        int times = PlayerPrefs.GetInt(timesKey);
        PlayerPrefs.SetInt(timesKey, ++times);

        int seconds;
        string key;
        switch (type)
        {
            case PurchaseType.MonthlyCard:
            case PurchaseType.SweepCard:
                key = input.ToString(NAME_SPACE_SECONDS);
                seconds = PlayerPrefs.GetInt(key);
                if(seconds == 0)
                    seconds = (int)((DateTime.UtcNow.Ticks - UnixEpoch.Ticks) / TimeSpan.TicksPerSecond);

                seconds += (int)(TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond) * 30;
                
                PlayerPrefs.SetInt(key, seconds);
                break;
            case PurchaseType.Pass:
                key = input.ToString(NAME_SPACE_SECONDS);
                seconds = PlayerPrefs.GetInt(key);
                if(seconds == 0)
                    seconds = (int)((DateTime.UtcNow.Ticks - UnixEpoch.Ticks) / TimeSpan.TicksPerSecond);

                seconds = (int)((new DateTime(seconds * TimeSpan.TicksPerSecond + UnixEpoch.Ticks).ToLocalTime()
                                     .AddMonths(1).ToUniversalTime().Ticks -
                                 UnixEpoch.Ticks) / TimeSpan.TicksPerSecond);
                
                PlayerPrefs.SetInt(key, seconds);
                break;
            default:
                seconds = 0;
                break;
        }

        onComplete(seconds * TimeSpan.TicksPerSecond + UnixEpoch.Ticks);
    }

    void Awake()
    {
        IPurchaseData.instance = this;
    }
}