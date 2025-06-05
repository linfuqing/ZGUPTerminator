using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class UserDataMain
{
    [Serializable]
    internal struct PurchaseItem
    {
        public PurchaseType type;

        public int level;

        public UserRewardData[] rewards;
    }

    [Header("Purchase"), SerializeField] 
    internal PurchaseItem[] _purchaseItems;
    
    public IEnumerator Purchase(PurchaseType type, int level, Action<UserReward[]> onComplete)
    {
        yield return null;

        if (PurchaseData.Query(type, level).isValid)
        {
            foreach (var purchaseItem in _purchaseItems)
            {
                if (purchaseItem.type == type && purchaseItem.level == level)
                {
                    onComplete(__ApplyRewards(purchaseItem.rewards).ToArray());
                    
                    yield break;
                }
            }
        }

        onComplete(null);
    }
}
