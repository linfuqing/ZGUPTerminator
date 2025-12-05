using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum AdvertisementType
{
    //游荡
    Tip, 
    
    //每日商品
    Product, 
    
    //买体力
    Energy, 
    
    //活动预留
    Other
}

public interface IAdvertisementAPI
{
    public static IAdvertisementAPI instance;
    
    void Broadcast(AdvertisementType type, string name, Action<bool> onComplete);
}

public interface IAdvertisementData
{
    public struct Input
    {
        public string name;

        public AdvertisementType type;

        public string ToString(string prefix)
        {
            return $"{prefix}{type}{name}";
        }
    }
    
    public struct Output
    {
        public string name;

        public AdvertisementType type;

        public string ToString(string prefix)
        {
            return $"{prefix}{type}{name}";
        }
    }

    public static IAdvertisementData instance;
    
    /// <summary>
    /// 播放广告，买了免广告也应该调用该接口后再下发奖励，否则下发奖励失败。
    /// 购买了免广告卡后将自动跳过广告。
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="type"></param>
    /// <param name="name">
    /// 当是PurchasedPool时，填宝箱名；Item时填写商品名。其它填空。
    /// </param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator Broadcast(uint userID, AdvertisementType type, string name, Action<bool> onComplete);
}

public class AdvertisementData : MonoBehaviour, IAdvertisementData
{
    public const string NAME_SPACE_TIMES = "AdvertisementTimes";

    public static string GetNameSpace(AdvertisementType type, string name)
    {
        return $"{NAME_SPACE_TIMES}{type}{name}";
    }
    
    public static int QueryTimes(AdvertisementType type, string name)
    {
        return PlayerPrefs.GetInt(GetNameSpace(type, name));
    }
    
    public static bool Exchange(AdvertisementType type, string name, string key)
    {
        key = $"{key}{type}{name}";

        int times = PlayerPrefs.GetInt(key) + 1;

        if(QueryTimes(type, name) < times)
            return false;
        
        PlayerPrefs.SetInt(key, times);

        return true;
    }
    
    public IEnumerator Broadcast(uint userID, AdvertisementType type, string name, Action<bool> onComplete)
    {
        yield return null;

        var output = PurchaseData.Query(PurchaseType.AdvertisingFreeCard, 0);
        var api = output.IsValid(1) || output.GetDeadline(DateTime.UtcNow.Ticks) > 0 ? 
            null : IAdvertisementAPI.instance;
        if (api == null)
        {
            var key = GetNameSpace(type, name);
            int times = PlayerPrefs.GetInt(key);
            PlayerPrefs.SetInt(key, times + 1);

            onComplete(true);
        }
        else
        {
            bool? result = null;
            
            //非主綫程回調
            api.Broadcast(type, name, x =>
            {
                result = x;
            });
            
            while(result == null)
                yield return null;
            
            if (result.Value)
            {
                var key = GetNameSpace(type, name);
                PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key) + 1);
            }

            onComplete(result.Value);
        }
    }

    void Awake()
    {
        IAdvertisementData.instance = this;
    }
}
