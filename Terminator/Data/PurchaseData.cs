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
        
        public bool isValid => times >= 0 && (ticks == 0 || ticks > DateTime.UtcNow.Ticks);
    }
    
    /// <summary>
    ///  查询有效期
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator Query(uint userID, Input[] inputs, System.Action<Output[]> onComplete);
    
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
    public const string NAME_SPACE_TICKS = "PurchaseDataTicks";
    
    public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IPurchaseData.Output Query(in IPurchaseData.Input input)
    {
        IPurchaseData.Output result;
        
        result.times = PlayerPrefs.GetInt(input.ToString(NAME_SPACE_TIMES));
        result.ticks = PlayerPrefs.GetInt(input.ToString(NAME_SPACE_TICKS)) * TimeSpan.TicksPerSecond;
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

        long ticks;
        switch (type)
        {
            case PurchaseType.MonthlyCard:
            case PurchaseType.SweepCard:
                string ticksKey = input.ToString(NAME_SPACE_TICKS);
                ticks = PlayerPrefs.GetInt(ticksKey);
                if(ticks == 0)
                    ticks = DateTime.UtcNow.Ticks;

                ticks += TimeSpan.TicksPerDay * 30;
                
                PlayerPrefs.SetInt(ticksKey, (int)((ticks - UnixEpoch.Ticks) / TimeSpan.TicksPerSecond));
                break;
            default:
                ticks = 0;
                break;
        }

        onComplete(ticks);
    }

    void Awake()
    {
        IPurchaseData.instance = this;
    }
}