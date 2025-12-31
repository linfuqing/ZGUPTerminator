using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    private const string NAME_SPACE_USER_DIAMOND = "UserDiamond";

    /// <summary>
    /// 注意，实现钻石服务器逻辑的时候，要根据不同的平台调用不同的虚拟货币接口，我们游戏服务器是不直接存储钻石这个数据的。
    /// 参见：
    /// 查询剩余钻石：https://developers.weixin.qq.com/minigame/dev/api-backend/midas-payment-v2/pay_v2.getBalance.html
    /// 消耗钻石：https://developers.weixin.qq.com/minigame/dev/api-backend/midas-payment-v2/pay_v2.pay.html
    /// 奖励钻石：https://developers.weixin.qq.com/minigame/dev/api-backend/midas-payment-v2/pay_v2.present.html
    /// </summary>
    public static int diamond
    {
        get => PlayerPrefs.GetInt(NAME_SPACE_USER_DIAMOND);

        private set
        {
            int result = value - diamond;
            if (result > 0)
                __AppendQuest(UserQuest.Type.DiamondsToGet, result);
            else if(result < 0)
                __AppendQuest(UserQuest.Type.DiamondsToUse, -result);
            
            PlayerPrefs.SetInt(NAME_SPACE_USER_DIAMOND, value);
        }
    }

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
    
    public IEnumerator QueryPurchaseItems(uint userID, IPurchaseData.Input[] inputs, Action<Memory<IUserData.PurchaseItems>> onComplete)
    {
        //客户端
        while (IPurchaseAPI.instance != null && IPurchaseAPI.instance.isPending)
            yield return null;
        
        yield return __CreateEnumerator();

        bool isValid;
        int numResults = inputs.Length, time;
        IPurchaseData.Input input;
        IPurchaseData.Output output;
        IUserData.PurchaseItems result;
        var results = new IUserData.PurchaseItems[numResults];
        for (int i = 0; i < numResults; ++i)
        {
            input = inputs[i];
            isValid = PurchaseData.IsValid(
                input.type,
                input.level,
                NAME_SPACE_USER_PURCHASE_ITEM,
                out time,
                out output);
            result.status = time < output.times
                ? IUserData.PurchaseItems.Status.Purchased :
                (isValid ? IUserData.PurchaseItems.Status.Valid : IUserData.PurchaseItems.Status.Invalid);
            switch (input.type)
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
                if (purchaseItem.type == input.type &&
                    purchaseItem.level == input.level)
                {
                    result.expMax = purchaseItem.exp;
                    result.capacity = purchaseItem.capacity;
                    result.options = purchaseItem.options;

                    break;
                }
            }

            result.metadata = null;

            //客户端（服务器返回时）
            if (IUserData.PurchaseItems.Status.Invalid == result.status && IPurchaseAPI.instance != null)
            {
                bool isWaiting = true;
                IPurchaseAPI.instance.Query(userID, input.type, input.level, x =>
                {
                    result.metadata = x;

                    isWaiting = false;
                });

                while (isWaiting)
                    yield return null;
            }

            results[i] = result;
        }

        onComplete(results);
    }

    public IEnumerator CollectPurchaseItem(uint userID, PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
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

    public IEnumerator QueryPurchaseTokens(uint userID, IPurchaseData.Input[] inputs, Action<Memory<IUserData.PurchaseTokens>> onComplete)
    {
        yield return __CreateEnumerator();

        int i, j, k, maxDays, numOptions, numPurchasesTokens = _purchaseTokens.Length, numResults = inputs.Length;
        string key;
        IPurchaseData.Input input;
        IPurchaseData.Output output;
        IUserData.PurchaseTokens result;
        PurchaseToken token;
        UserPurchaseToken value;
        Active<string> name;
        List<UserPurchaseToken> values = null;
        var results = new IUserData.PurchaseTokens[numResults];
        for (i = 0; i < numResults; ++i)
        {
            input = inputs[i];
            output = PurchaseData.Query(input.type, input.level);

            key = $"{NAME_SPACE_USER_PURCHASE_TOKEN_SECONDS}{input.type}{input.level}";

            name = new Active<string>(PlayerPrefs.GetString(key), __PurchaseParse);

            result.days = name.seconds == 0 ? 1 : Mathf.Abs(DateTimeUtility.GetTotalDays(name.seconds, out _, out _,
                    DateTimeUtility.DataTimeType.UTC));

            maxDays = int.MaxValue;
            switch (input.type)
            {
                case PurchaseType.FirstCharge:
                    maxDays = 1;
                    
                    result.exp = PlayerPrefs.GetInt($"{NAME_SPACE_USER_PURCHASE_TOKEN_TIMES}{input.type}{input.level}");
                    if(result.days > 1)
                        result.exp += result.days - 1;
                    break;
                case PurchaseType.DiamondCard:
                case PurchaseType.MonthlyCard:
                case PurchaseType.SweepCard:
                    result.exp = 0;
                    break;
                case PurchaseType.Fund:
                    result.exp = __GetChapterStageRewardCount();

                    if (input.level == -1 && output.ticks == 0)
                    {
                        PurchaseData.Buy(input.type, input.level);

                        output = PurchaseData.Query(input.type, input.level);
                    }

                    break;
                case PurchaseType.Pass:
                    result.exp = am;

                    if (input.level == -1 && (output.ticks == 0 ||
                                              output.ticks + output.deadline * TimeSpan.TicksPerSecond <
                                              DateTime.UtcNow.Ticks))
                    {
                        PurchaseData.Buy(input.type, input.level);

                        output = PurchaseData.Query(input.type, input.level);
                    }

                    break;
                default:
                    continue;
            }

            if(values != null)
                values.Clear();

            for (j = 0; j < numPurchasesTokens; ++j)
            {
                token = _purchaseTokens[j];
                if (token.type == input.type &&
                    token.level == input.level)
                {
                    value.name = token.name;
                    value.id = __ToID(j);
                    value.exp = token.exp;

                    value.options = token.options;

                    if (output.times > 0 && token.exp <= result.exp)
                    {
                        if (PlayerPrefs.GetInt($"{NAME_SPACE_USER_PURCHASE_TOKEN}{token.name}") < output.times)
                        {
                            value.flag = name.ToDay() == null ? 0 : UserPurchaseToken.Flag.Locked;
                            if (value.flag == 0 && name.seconds != 0)
                            {
                                numOptions = token.options == null ? 0 : token.options.Length;
                                if (numOptions > 0)
                                {
                                    if (result.days > 0)
                                    {
                                        result.days = Mathf.Min(result.days, maxDays);

                                        Array.Resize(ref value.options, result.days * numOptions);

                                        for (k = 0; k < result.days; ++k)
                                            Array.Copy(token.options, 0, value.options, k * numOptions, numOptions);
                                    }
                                    else
                                        value.options = null;
                                }
                            }
                        }
                        else
                            value.flag = UserPurchaseToken.Flag.Collected;
                    }
                    else
                        value.flag = UserPurchaseToken.Flag.Locked;

                    if (values == null)
                        values = new List<UserPurchaseToken>();

                    values.Add(value);
                }
            }

            result.values = values == null ? null : values.ToArray();

            results[i] = result;
        }

        onComplete(results);
    }
    
    public IEnumerator CollectPurchaseToken(uint userID, PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
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
        
        int i, days;
        string secondsKey, key;
        Active<string> name;
        DateTime now;
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
                    name = new Active<string>(PlayerPrefs.GetString(secondsKey), __PurchaseParse);
                    if (name.ToDay() == null)
                    {
                        key = $"{NAME_SPACE_USER_PURCHASE_TOKEN}{purchaseToken.name}";
                        if (PlayerPrefs.GetInt(key) < times)
                        {
                            if (isWriteTimes)
                                PlayerPrefs.SetInt(key, times);

                            if (name.seconds == 0)
                            {
                                days = 1;

                                name.seconds = isWriteSeconds ? DateTimeUtility.GetSeconds() : 0;
                            }
                            else
                            {
                                days = Mathf.Abs(DateTimeUtility.GetTotalDays(name.seconds, out _, out now, DateTimeUtility.DataTimeType.UTC));
                                
                                name.seconds = DateTimeUtility.GetSeconds(now/*.ToUniversalTime()*/.Ticks);
                            }

                            if (expKey != null)
                            {
                                PlayerPrefs.SetInt(expKey, exp + days);

                                if (days > 1)
                                {
                                    exp += --days;

                                    name.seconds -= 60u * 60u * 24u * (uint)days;
                                    
                                    days = 1;
                                }
                            }

                            if (isWriteSeconds)
                            {
                                name.value = purchaseToken.name;
                                PlayerPrefs.SetString(secondsKey, name.ToString());
                            }

                            if (rewards == null)
                                rewards = new List<UserReward>();

                            for(i = 0; i < days; ++i)
                                __ApplyRewards(purchaseToken.options, rewards);
                        }
                    }
                }
            }
        }
        
        onComplete(rewards == null ? null : rewards.ToArray());
    }
    
    public IEnumerator CollectPurchaseToken(uint userID, PurchaseType type, Action<Memory<UserReward>> onComplete)
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
    
    private static string __PurchaseParse(Memory<string> parameters)
    {
        return parameters.Span[0];
    }

}

public partial class UserData
{
    public IEnumerator QueryPurchaseItems(uint userID, IPurchaseData.Input[] inputs, Action<Memory<IUserData.PurchaseItems>> onComplete)
    {
        return UserDataMain.instance.QueryPurchaseItems(userID, inputs, onComplete);
    }
    
    public IEnumerator CollectPurchaseItem(uint userID, PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectPurchaseItem(userID, type, level, onComplete);
    }

    public IEnumerator QueryPurchaseTokens(uint userID, IPurchaseData.Input[] inputs, Action<Memory<IUserData.PurchaseTokens>> onComplete)
    {
        return UserDataMain.instance.QueryPurchaseTokens(userID, inputs, onComplete);
    }

    public IEnumerator CollectPurchaseToken(uint userID, PurchaseType type, int level, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectPurchaseToken(userID, type, level, onComplete);
    }
    
    public IEnumerator CollectPurchaseToken(uint userID, PurchaseType type, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectPurchaseToken(userID, type, onComplete);
    }
}