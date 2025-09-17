using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    internal struct PurchaseItem
    {
        public string name;
        
        public PurchaseType type;

        public int level;

        [Tooltip("（每日）购买上限")]
        public int capacity;

        [Tooltip("存钱罐需要的金币或者钻石&章节礼包需要达到的章节数")]
        public int exp;

        public UserRewardOptionData[] options;
        
#if UNITY_EDITOR
        [CSVField]
        public string 内购项目名称
        {
            set => name = value;
        }
        
        [CSVField]
        public int 内购项目类型
        {
            set => type = (PurchaseType)value;
        }
        
        [CSVField]
        public int 内购项目档位
        {
            set => level = value;
        }
        
        [CSVField]
        public int 内购项目购买上限每日
        {
            set => capacity = value;
        }
        
        [CSVField]
        public int 内购项目条件
        {
            set => exp = value;
        }
        
        [CSVField]
        public string 内购项目奖励
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    options = null;
                    
                    return;
                }

                var parameters = value.Split('/');
                int numParameters = parameters.Length;
                options = new UserRewardOptionData[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    options[i] = new UserRewardOptionData(parameters[i]);
            }
        }
#endif
    }

    [Header("Purchase"), SerializeField] 
    internal PurchaseItem[] _purchaseItems;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_purchaseItems", guidIndex = -1, nameIndex = 0)] 
    internal string _purchaseItemsPath;
#endif
    
    private const string NAME_SPACE_USER_PURCHASE_ITEM = "UserPurchaseItem";
    
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
                result.exp = UserData.chapter;
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
        result.options = null;
        
        foreach (var purchaseItem in _purchaseItems)
        {
            if (purchaseItem.type == type && 
                purchaseItem.level == level)
            {
                result.expMax = purchaseItem.exp;
                result.capacity = purchaseItem.capacity;
                result.options = purchaseItem.options;

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
                            if (UserData.chapter < purchaseItem.exp)
                                break;
                            
                            rewards = __ApplyRewards(purchaseItem.options);
                            break;
                        case PurchaseType.GoldBank:
                            if (goldBank < purchaseItem.exp)
                                break;
                            
                            rewards = __ApplyRewards(purchaseItem.options);

                            //goldBank = 0;
                            break;
                        default:
                            rewards = __ApplyRewards(purchaseItem.options);
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

        [Tooltip("首充填写天数，补给卡，月卡，游荡卡填0，基金代表星星数，通行证代表活跃度")]
        public int exp;

        public UserRewardOptionData[] options;
        
#if UNITY_EDITOR
        [CSVField]
        public string 内购票据名称
        {
            set => name = value;
        }
        
        [CSVField]
        public int 内购票据类型
        {
            set => type = (PurchaseType)value;
        }
        
        [CSVField]
        public int 内购票据档位
        {
            set => level = value;
        }
        
        [CSVField]
        public int 内购票据条件
        {
            set => exp = value;
        }
        
        [CSVField]
        public string 内购票据奖励
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    options = null;
                    
                    return;
                }

                var parameters = value.Split('/');
                int numParameters = parameters.Length;
                options = new UserRewardOptionData[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    options[i] = new UserRewardOptionData(parameters[i]);
            }
        }
#endif
    }
    
    [SerializeField]
    internal PurchaseToken[] _purchaseTokens;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_purchaseTokens", guidIndex = -1, nameIndex = 0)] 
    internal string _purchaseTokensPath;
#endif

    public const string NAME_SPACE_USER_PURCHASE_TOKEN = "UserPurchaseToken";
    public const string NAME_SPACE_USER_PURCHASE_TOKEN_SECONDS = "UserPurchaseTokenSeconds";
    public const string NAME_SPACE_USER_PURCHASE_TOKEN_TIMES = "UserPurchaseTokenTimes";

    public IEnumerator QueryPurchaseTokens(PurchaseType type, int level, Action<IUserData.PurchaseTokens> onComplete)
    {
        yield return __CreateEnumerator();

        var output = PurchaseData.Query(type, level);
        
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
                result.exp = UserData.chapter;
                
                if (level == -1 && output.ticks == 0)
                {
                    PurchaseData.Buy(type, level);
            
                    output = PurchaseData.Query(type, level);
                }

                break;
            case PurchaseType.Pass:
                result.exp = am;
                
                if (level == -1 && (output.ticks == 0 || output.ticks + output.deadline * TimeSpan.TicksPerSecond < DateTime.UtcNow.Ticks))
                {
                    PurchaseData.Buy(type, level);
            
                    output = PurchaseData.Query(type, level);
                }

                break;
            default:
                yield break;
        }
        
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

                if ( token.exp <= result.exp && output.times > 0)
                {
                    if (PlayerPrefs.GetInt($"{NAME_SPACE_USER_PURCHASE_TOKEN}{token.name}") < output.times)
                    {
                        key = $"{NAME_SPACE_USER_PURCHASE_TOKEN_SECONDS}{type}{level}";

                        value.flag =
                            PlayerPrefs.HasKey(key) && ZG.DateTimeUtility.IsToday((uint)PlayerPrefs.GetInt(key))
                                ? UserPurchaseToken.Flag.Locked
                                : 0;
                    }
                    else
                        value.flag = UserPurchaseToken.Flag.Collected;
                }
                else
                    value.flag = UserPurchaseToken.Flag.Locked;

                value.options = token.options;

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

                exp = __GetChapterStageRewardCount();//UserData.chapter;
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

                            __ApplyRewards(purchaseToken.options, rewards);
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
                exp = __GetChapterStageRewardCount();//UserData.chapter;
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
                if (purchaseToken.level < 0 && PurchaseData.Exchange(type, purchaseToken.level, NAME_SPACE_USER_PURCHASE_ITEM))
                {
                    foreach (var purchaseItem in _purchaseItems)
                    {
                        if (purchaseItem.type == type && purchaseItem.level == purchaseToken.level)
                        {
                            if (rewards == null)
                                rewards = new List<UserReward>();
                            
                            __ApplyRewards(purchaseItem.options, rewards);
                            
                            break;
                        }
                    }
                }

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

                        __ApplyRewards(purchaseToken.options, rewards);
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