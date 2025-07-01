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

        Tip.used = used;

        var rewards = _tip.instance.Generate((long)(_tip.intervalPerTime * TimeSpan.TicksPerSecond));

        var results = __ApplyRewards(rewards);

        onComplete(results == null ? null : results.ToArray());
    }

    public const string NAME_SPACE_USER_ENERGY_AD = "UserEnergyAd";

    public IEnumerator BuyEnergiesAd(uint userID, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();
        
        int buyTimesByAd = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD);
        if (buyTimesByAd < _energies.buyTimesByAd && 
            AdvertisementData.Exchange(AdvertisementType.Energy, string.Empty, NAME_SPACE_USER_ENERGY_AD))
        {
            __ApplyEnergy(-_energies.energyPerTime);
            
            PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD, ++buyTimesByAd);
            
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