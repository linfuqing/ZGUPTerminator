using System;
using System.Collections;

public partial interface IUserData
{
    IEnumerator UseTipAd(
        uint userID,
        Action<Memory<UserReward>> onComplete);

    IEnumerator BuyEnergiesAd(uint userID, Action<bool> onComplete);

    IEnumerator BuyProductAd(
        uint userID,
        uint productID,
        Action<Memory<UserReward>> onComplete);
}
