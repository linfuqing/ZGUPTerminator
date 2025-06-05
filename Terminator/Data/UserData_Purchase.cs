using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial interface IUserData
{
    IEnumerator Purchase(PurchaseType type, int level, Action<UserReward[]> onComplete);
}

public partial class UserData
{
    public IEnumerator Purchase(PurchaseType type, int level, Action<UserReward[]> onComplete)
    {
        return UserDataMain.instance.Purchase(type, level, onComplete);
    }
}
