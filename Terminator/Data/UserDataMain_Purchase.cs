using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class UserDataMain
{
    [Serializable]
    internal struct PurchaseItem
    {
        //public string name;
        
        public PurchaseType type;

        public int level;

        public UserRewardData[] rewards;
    }

    [Header("Purchase"), SerializeField] 
    internal PurchaseItem[] _purchaseItems;
    
    public const string NAME_SPACE_USER_PURCHASE_ITEM = "UserPurchaseItem";
    
    public IEnumerator QueryPurchaseItems(PurchaseType type, int level, Action<IUserData.PurchaseItems> onComplete)
    {
        yield return null;
        
        IUserData.PurchaseItems result;
        result.status = PurchaseData.IsValid(
            type,
            level,
            NAME_SPACE_USER_PURCHASE_ITEM,
            out int time,
            out var output)
            ? IUserData.PurchaseItems.Status.Vaild : 
            (time + 1 == output.times ? IUserData.PurchaseItems.Status.Purchased : IUserData.PurchaseItems.Status.Invaild);
        result.times = output.times;
        result.ticks = output.ticks;
        result.rewards = null;
        
        foreach (var purchaseItem in _purchaseItems)
        {
            if (purchaseItem.type == type && 
                purchaseItem.level == level)
            {
                result.rewards = purchaseItem.rewards;

                break;
            }
        }

        onComplete(result);
    }

    public IEnumerator CollectPurchaseItem(PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        yield return null;

        if (PurchaseData.Exchange(type, level, NAME_SPACE_USER_PURCHASE_ITEM))
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
    
    [Serializable]
    internal struct PurchaseToken
    {
        public string name;
        
        public PurchaseType type;

        public int level;

        public int exp;

        public UserRewardData[] rewards;
    }
    
    [SerializeField]
    internal PurchaseToken[] _purchaseTokens;
    
    public const string NAME_SPACE_USER_PURCHASE_TOKEN = "UserPurchaseToken";

    public IEnumerator QueryPurchaseTokens(PurchaseType type, int level, Action<IUserData.PurchaseTokens> onComplete)
    {
        yield return null;

        IUserData.PurchaseTokens result;
        switch (type)
        {
            case PurchaseType.Fund:
                result.exp = UserData.level;
                break;
            case PurchaseType.Pass:
                result.exp = am;
                break;
            default:
                yield break;
        }
        
        var output = PurchaseData.Query(type, level);
        UserPurchaseToken value;
        List<UserPurchaseToken> values = null;
        PurchaseToken token;
        int numPurchasesTokens = _purchaseTokens.Length;
        for(int i = 0; i < numPurchasesTokens; ++i)
        {
            token = _purchaseTokens[i];
            if (token.type == type && 
                token.level == level)
            {
                value.name = token.name;
                value.id = __ToID(i);
                value.exp = token.exp;

                if (PlayerPrefs.GetInt($"{NAME_SPACE_USER_PURCHASE_TOKEN}{token.name}") < output.times)
                    value.flag = 0;
                else
                    value.flag = UserPurchaseToken.Flag.Collected;
                
                value.rewards = token.rewards;
                
                values.Add(value);
            }
        }

        result.values = values == null ? values.ToArray() : null;
        
        onComplete(result);
    }
    
    public IEnumerator CollectPurchaseToken(PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        yield return null;

        int exp;
        switch (type)
        {
            case PurchaseType.Fund:
                exp = UserData.level;
                break;
            case PurchaseType.Pass:
                exp = am;
                break;
            default:
                yield break;
        }

        List<UserReward> rewards = null;
        if (PurchaseData.IsValid(type, level, NAME_SPACE_USER_PURCHASE_ITEM, out int times, out _))
        {
            string key;
            foreach (var purchaseToken in _purchaseTokens)
            {
                if (purchaseToken.type == type && 
                    purchaseToken.level == level && 
                    purchaseToken.exp <= exp)
                {
                    key = $"{NAME_SPACE_USER_PURCHASE_TOKEN}{purchaseToken.name}";
                    if (PlayerPrefs.GetInt(key) < times)
                    {
                        PlayerPrefs.SetInt(key, times);
                        
                        if (rewards == null)
                            rewards = new List<UserReward>();

                        __ApplyRewards(purchaseToken.rewards, rewards);
                    }
                }
            }
        }
        
        onComplete(rewards == null ? null : rewards.ToArray());
    }
}
