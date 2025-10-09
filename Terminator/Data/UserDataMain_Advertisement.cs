using System;
using System.Collections;
using UnityEngine;

public partial class UserDataMain
{
    public const string NAME_SPACE_USER_TIP_AD = "UserTipAd";
    
    public IEnumerator UseTipAd(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        var used = Tip.used;
        if (++used.timesFromAd > _tip.timesPerDayFromAd)
        {
            onComplete(null);
            
            yield break;
        }

        if (!AdvertisementData.Exchange(AdvertisementType.Tip, string.Empty, NAME_SPACE_USER_TIP_AD))
        {
            onComplete(null);

            yield break;
        }

        var instance = _tip.instance;
        Tip.used = used;
        
        bool hasSweepCard = PurchaseData.IsValid(PurchaseType.SweepCard,
            0,
            NAME_SPACE_USER_PURCHASE_ITEM,
            out _,
            out _);

        float multiplier = hasSweepCard ? _tip.sweepCardMultiplier : 1.0f;
        var rewards = instance.Generate((long)(_tip.intervalPerTime * multiplier * TimeSpan.TicksPerSecond));
        
        __AppendQuest(UserQuest.Type.Tip, 1);

        var results = __ApplyRewards(rewards);

        onComplete(results == null ? null : results.ToArray());
    }

    public const string NAME_SPACE_USER_ENERGY_AD = "UserEnergyAd";

    public IEnumerator BuyEnergiesAd(uint userID, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();
        
        int buyTimesByAd = new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD), __Parse).ToDay();
        if (buyTimesByAd < _energies.buyTimesByAd && 
            AdvertisementData.Exchange(AdvertisementType.Energy, string.Empty, NAME_SPACE_USER_ENERGY_AD))
        {
            __ApplyEnergy(-_energies.energyPerTime);
            
            PlayerPrefs.SetString(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD, new Active<int>(++buyTimesByAd).ToString());
            
            onComplete(true);
            
            yield break;
        }

        onComplete(false);
    }
}

public partial class UserData
{
    public IEnumerator UseTipAd(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.UseTipAd(userID, onComplete);
    }
    
    public IEnumerator BuyEnergiesAd(
        uint userID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.BuyEnergiesAd(userID, onComplete);
    }
    
    
    /*public IEnumerator BuyProductAd(
        uint userID,
        uint productID, 
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.BuyProductAd(userID, productID, onComplete);
    }*/
}