using System;
using System.Collections;

public partial interface IUserData
{
    IEnumerator UseTipAd(
        uint userID,
        Action<Memory<UserReward>> onComplete);
}
