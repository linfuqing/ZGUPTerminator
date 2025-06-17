using System;
using System.Collections;

public partial class UserDataMain
{
    public const string NAME_SPACE_USER_TIP_AD = "UserTipAd";
    
    public IEnumerator UseTipAd(
        uint userID,
        Action<Memory<UserReward>> onComplete)
    {
        yield return null;

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

    public IEnumerator BuyEnergiesAd(uint userID, Action<bool> onComplete)
    {
        yield return null;
    }
    
    public IEnumerator BuyProductAd(uint userID, uint productID, Action<Memory<UserReward>> onComplete)
    {
        yield return null;
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
    
    
    public IEnumerator BuyProductAd(
        uint userID,
        uint productID, 
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.BuyProductAd(userID, productID, onComplete);
    }
}