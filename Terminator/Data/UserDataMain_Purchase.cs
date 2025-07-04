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

        [Tooltip("（每日）购买上限")]
        public int capacity;

        [Tooltip("存钱罐需要的金币或者钻石&章节礼包需要达到的章节数")]
        public int exp;

        public UserRewardData[] rewards;
    }

    [Header("Purchase"), SerializeField] 
    internal PurchaseItem[] _purchaseItems;
    
    public const string NAME_SPACE_USER_PURCHASE_ITEM = "UserPurchaseItem";
    
    public IEnumerator QueryPurchaseItems(PurchaseType type, int level, Action<IUserData.PurchaseItems> onComplete)
    {
        yield return __CreateEnumerator();
        
        IUserData.PurchaseItems result;
        result.status = PurchaseData.IsValid(
            type,
            level,
            NAME_SPACE_USER_PURCHASE_ITEM,
            out int time,
            out var output)
            ? (time < output.times ? IUserData.PurchaseItems.Status.Purchased : IUserData.PurchaseItems.Status.Valid) : 
            IUserData.PurchaseItems.Status.Invalid;
        switch (type)
        {
            case PurchaseType.Level:
                result.exp = UserData.level;
                break;
            case PurchaseType.GoldBank:
                result.exp = goldBank;
                break;
            default:
                result.exp = 0;
                break;
        }

        result.expMax = 0;
        result.capacity = 0;
        
        result.times = output.times;
        result.deadline = output.deadline;
        result.ticks = output.ticks;
        result.rewards = null;
        
        foreach (var purchaseItem in _purchaseItems)
        {
            if (purchaseItem.type == type && 
                purchaseItem.level == level)
            {
                result.expMax = purchaseItem.exp;
                result.capacity = purchaseItem.capacity;
                result.rewards = purchaseItem.rewards;

                break;
            }
        }

        onComplete(result);
    }

    public IEnumerator CollectPurchaseItem(PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        if (PurchaseData.Exchange(type, level, NAME_SPACE_USER_PURCHASE_ITEM))
        {
            List<UserReward> rewards = null;
            foreach (var purchaseItem in _purchaseItems)
            {
                if (purchaseItem.type == type && purchaseItem.level == level)
                {
                    switch (type)
                    {
                        case PurchaseType.Level:
                            if (UserData.level < purchaseItem.exp)
                                break;
                            
                            rewards = __ApplyRewards(purchaseItem.rewards);
                            
                            break;
                        case PurchaseType.GoldBank:
                            if (goldBank < purchaseItem.exp)
                                break;
                            
                            rewards = __ApplyRewards(purchaseItem.rewards);

                            //goldBank = 0;
                            break;
                        default:
                            rewards = __ApplyRewards(purchaseItem.rewards);
                            break;
                    }
                    
                    break;
                }
            }

            __AppendQuest(UserQuest.Type.Buy, 1);
            
            onComplete(rewards == null ? Array.Empty<UserReward>() : rewards.ToArray());

            yield break;
        }

        onComplete(null);
    }
    
    [Serializable]
    internal struct PurchaseToken
    {
        public string name;
        
        public PurchaseType type;

        public int level;

        [Tooltip("首充填写天数，补给卡，月卡，游荡卡填0，基金代表章节，通行证代表活跃度")]
        public int exp;

        public UserRewardData[] rewards;
    }
    
    [SerializeField]
    internal PurchaseToken[] _purchaseTokens;
    
    public const string NAME_SPACE_USER_PURCHASE_TOKEN = "UserPurchaseToken";
    public const string NAME_SPACE_USER_PURCHASE_TOKEN_SECONDS = "UserPurchaseTokenSeconds";
    public const string NAME_SPACE_USER_PURCHASE_TOKEN_TIMES = "UserPurchaseTokenTimes";

    public IEnumerator QueryPurchaseTokens(PurchaseType type, int level, Action<IUserData.PurchaseTokens> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.PurchaseTokens result;
        switch (type)
        {
            case PurchaseType.FirstCharge:
                result.exp = PlayerPrefs.GetInt($"{NAME_SPACE_USER_PURCHASE_TOKEN_TIMES}{type}{level}");
                break;
            case PurchaseType.DiamondCard:
            case PurchaseType.MonthlyCard:
            case PurchaseType.SweepCard:
                result.exp = 0;
                break;
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
        List<UserPurchaseToken> values = null;
        UserPurchaseToken value;
        PurchaseToken token;
        string key;
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

                key = $"{NAME_SPACE_USER_PURCHASE_TOKEN_SECONDS}{type}{level}";

                if (!(PlayerPrefs.HasKey(key) && ZG.DateTimeUtility.IsToday((uint)PlayerPrefs.GetInt(key))) &&
                    PlayerPrefs.GetInt($"{NAME_SPACE_USER_PURCHASE_TOKEN}{token.name}") < output.times)
                    value.flag = 0;
                else
                    value.flag = UserPurchaseToken.Flag.Collected;
                
                value.rewards = token.rewards;

                if (values == null)
                    values = new List<UserPurchaseToken>();
                
                values.Add(value);
            }
        }

        result.values = values == null ? null : values.ToArray();
        
        onComplete(result);
    }
    
    public IEnumerator CollectPurchaseToken(PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        bool isWriteSeconds = false, isWriteTimes = false;
        int exp;
        string expKey = null;
        switch (type)
        {
            case PurchaseType.FirstCharge:
                isWriteSeconds = true;
                isWriteTimes = true;

                expKey = $"{NAME_SPACE_USER_PURCHASE_TOKEN_TIMES}{type}{level}";
                
                exp = PlayerPrefs.GetInt(expKey);
                break;
            case PurchaseType.DiamondCard:
            case PurchaseType.MonthlyCard:
            case PurchaseType.SweepCard:
                isWriteSeconds = true;
                
                exp = 0;
                break;
            case PurchaseType.Fund:
                isWriteTimes = true;
                
                exp = UserData.level;
                break;
            case PurchaseType.Pass:
                isWriteTimes = true;
                
                exp = am;
                break;
            default:
                yield break;
        }

        List<UserReward> rewards = null;
        
        string secondsKey, key;
        foreach (var purchaseToken in _purchaseTokens)
        {
            if (purchaseToken.type == type && 
                purchaseToken.level == level && 
                purchaseToken.exp <= exp)
            {
                if (PurchaseData.IsValid(
                        type, 
                        purchaseToken.level, 
                        NAME_SPACE_USER_PURCHASE_ITEM, 
                        out int times,
                        out _))
                {
                    secondsKey = $"{NAME_SPACE_USER_PURCHASE_TOKEN_SECONDS}{type}{level}";
                    if (!(PlayerPrefs.HasKey(secondsKey) && ZG.DateTimeUtility.IsToday((uint)PlayerPrefs.GetInt(secondsKey))))
                    {
                        key = $"{NAME_SPACE_USER_PURCHASE_TOKEN}{purchaseToken.name}";
                        if (PlayerPrefs.GetInt(key) < times)
                        {
                            if (isWriteTimes)
                                PlayerPrefs.SetInt(key, times);

                            if (isWriteSeconds)
                                PlayerPrefs.SetInt(secondsKey, (int)ZG.DateTimeUtility.GetSeconds());
                            
                            if(expKey != null)
                                PlayerPrefs.SetInt(expKey, exp + 1);

                            if (rewards == null)
                                rewards = new List<UserReward>();

                            __ApplyRewards(purchaseToken.rewards, rewards);
                        }
                    }
                }
            }
        }
        
        onComplete(rewards == null ? null : rewards.ToArray());
    }
    
    public IEnumerator CollectPurchaseToken(PurchaseType type, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

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
        
        string key;
        foreach (var purchaseToken in _purchaseTokens)
        {
            if (purchaseToken.type == type && 
                purchaseToken.exp <= exp)
            {
                if (PurchaseData.IsValid(
                        type, 
                        purchaseToken.level, 
                        NAME_SPACE_USER_PURCHASE_ITEM, 
                        out int times,
                        out _))
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

public partial class UserData
{
    public IEnumerator QueryPurchaseItems(PurchaseType type, int level, Action<IUserData.PurchaseItems> onComplete)
    {
        return UserDataMain.instance.QueryPurchaseItems(type, level, onComplete);
    }
    
    public IEnumerator CollectPurchaseItem(PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectPurchaseItem(type, level, onComplete);
    }

    public IEnumerator QueryPurchaseTokens(PurchaseType type, int level, Action<IUserData.PurchaseTokens> onComplete)
    {
        return UserDataMain.instance.QueryPurchaseTokens(type, level, onComplete);
    }

    public IEnumerator CollectPurchaseToken(PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectPurchaseToken(type, level, onComplete);
    }
    
    public IEnumerator CollectPurchaseToken(PurchaseType type, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectPurchaseToken(type, onComplete);
    }
}