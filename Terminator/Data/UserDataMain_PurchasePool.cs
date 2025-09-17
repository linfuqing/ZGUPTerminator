using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    internal struct PurchasePool
    {
        [Flags]
        internal enum Flag
        {
            Hide = 0x01
        }
        
        public string name;

        public Flag flag;
        
        [Tooltip("卡池类型")]
        public UserRewardType type;

        [Tooltip("抽卡每日免费次数")]
        public int freeTimes;
        
        [Tooltip("抽卡需要多少钻石")]
        public int diamond;

        [Tooltip("抽一次可获得多少金币")]
        public int gold;
        
#if UNITY_EDITOR
        [CSVField]
        public string 卡池名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public int 卡池标签
        {
            set
            {
                flag = (Flag)value;
            }
        }
        
        [CSVField]
        public int 卡池类型
        {
            set
            {
                type = (UserRewardType)value;
            }
        }
        
        [CSVField]
        public int 卡池每日免费次数
        {
            set
            {
                freeTimes = value;
            }
        }

        [CSVField]
        public int 卡池需要钻石
        {
            set
            {
                diamond = value;
            }
        }
        
        [CSVField]
        public int 卡池获得金币
        {
            set
            {
                gold = value;
            }
        }
#endif
    }
    
    [Serializable]
    internal struct PurchasePoolOption
    {
        [Tooltip("抽卡的名字")]
        public string name;
        
        [Tooltip("卡池名字")]
        public string poolName;
        
        [Tooltip("抽多少次后有效")]
        public int minTimes;

        [Tooltip("抽多少次后无效")]
        public int maxTimes;
        
        [Tooltip("在第几章开始生效")]
        [UnityEngine.Serialization.FormerlySerializedAs("minLevel")]
        public int minChapter;

        [Tooltip("在第几章开始生效")]
        [UnityEngine.Serialization.FormerlySerializedAs("maxLevel")]
        public int maxChapter;
        
        [Tooltip("最小获得数量")]
        public int minCount;
        [Tooltip("最大获得数量")]
        public int maxCount;
        
        [Tooltip("概率")]
        public float chance;

#if UNITY_EDITOR
        [CSVField]
        public string 抽卡名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 抽卡卡池名字
        {
            set
            {
                poolName = value;
            }
        }
        
        [CSVField]
        public int 抽卡最小次数
        {
            set
            {
                minTimes = value;
            }
        }
        
        [CSVField]
        public int 抽卡最大次数
        {
            set
            {
                maxTimes = value;
            }
        }

        [CSVField]
        public int 抽卡最小章节
        {
            set
            {
                minChapter = value;
            }
        }

        [CSVField]
        public int 抽卡最大章节
        {
            set
            {
                maxChapter = value;
            }
        }
        
        [CSVField]
        public int 抽卡最小张数
        {
            set
            {
                minCount = value;
            }
        }
        
        [CSVField]
        public int 抽卡最大张数
        {
            set
            {
                maxCount = value;
            }
        }
        
        [CSVField]
        public float 抽卡概率
        {
            set
            {
                chance = value;
            }
        }

#endif
    }

    [Header("Purchases")]
    [SerializeField] 
    internal PurchasePool[] _purchasePools;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_purchasePools", guidIndex = -1, nameIndex = 0)] 
    internal string _purchasePoolsPath;
#endif

    [SerializeField]
    internal PurchasePoolOption[] _purchasePoolOptions;

#if UNITY_EDITOR
    [SerializeField, CSV("_purchasePoolOptions", guidIndex = -1, nameIndex = 0)] 
    internal string _purchasePoolOptionsPath;
#endif

    private const string NAME_SPACE_USER_PURCHASE_POOL_KEY = "UserPurchasePoolKey";
    
    public IEnumerator QueryPurchases(
        uint userID,
        Action<IUserData.Purchases> onComplete)
    {
        yield return null;
        
        IUserData.Purchases result;
        result.flag = 0;

        var flag = UserDataMain.flag;
        if ((flag & Flag.PurchasesUnlockFirst) == Flag.PurchasesUnlockFirst)
            result.flag |= IUserData.Purchases.Flag.UnlockFirst;
        else if ((flag & Flag.PurchasesUnlock) != 0)
            result.flag |= IUserData.Purchases.Flag.Unlock;
        
        result.diamond = diamond;

        UserPurchasePool userPurchasePool;
        PurchasePool purchasePool;
        int numPurchasePools = _purchasePools.Length;
        result.pools = new UserPurchasePool[numPurchasePools];
        for (int i = 0; i < numPurchasePools; ++i)
        {
            purchasePool = _purchasePools[i];
            userPurchasePool.name = purchasePool.name;
            userPurchasePool.id = __ToID(i);
            userPurchasePool.diamond = purchasePool.diamond;
            userPurchasePool.gold = purchasePool.gold;
            
            result.pools[i] = userPurchasePool;
        }
        
        var userPurchasePoolKeys = new List<IUserData.Purchases.PoolKey>(numPurchasePools);
        IUserData.Purchases.PoolKey userPurchasePoolKey;
        for (int i = 0; i < numPurchasePools; ++i)
        {
            userPurchasePoolKey.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{_purchasePools[i].name}");
            
            if(userPurchasePoolKey.count < 1)
                continue;
            
            userPurchasePoolKey.poolID = __ToID(i);
            userPurchasePoolKeys.Add(userPurchasePoolKey);
        }

        result.poolKeys = userPurchasePoolKeys.ToArray();
        
        onComplete(result);
    }

    public IEnumerator Purchase(
        uint userID,
        uint purchasePoolID,
        int times,
        Action<Memory<UserItem>> onComplete)
    {
        yield return __CreateEnumerator();
        
        List<UserItem> results = null;
        var rewards = new List<UserReward>();
        if (__PurchasePool(__ToIndex(purchasePoolID), times, rewards))
        {
            UserItem userItem;
            foreach (var reward in rewards)
            {
                userItem.name = reward.name;
                userItem.id = reward.id;
                userItem.count = reward.count;

                if (results == null)
                    results = new List<UserItem>();
                
                results.Add(userItem);
            }
            
            flag &= ~Flag.PurchasesUnlockFirst;

            __AppendQuest(UserQuest.Type.Purchase, times);
        }

        onComplete(results == null ? null : results.ToArray());
    }
    
    private const string NAME_SPACE_USER_PURCHASE_POOL_TIMES = "UserPurchasePoolTimes";

    private bool __PurchasePool(int purchasePoolIndex, int times, List<UserReward> outRewards)
    {
        var purchasePool = _purchasePools[purchasePoolIndex];
        string poolKey = $"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{purchasePool.name}";
        int keyCount = PlayerPrefs.GetInt(poolKey);
        if (keyCount < times)
        {
            int destination = (times - keyCount) * purchasePool.diamond, source = diamond;
            if (destination > source)
                return false;
            
            diamond = source - destination;
            
            PlayerPrefs.DeleteKey(poolKey);
        }
        else
        {
            keyCount -= times;
            
            PlayerPrefs.SetInt(poolKey, keyCount);
        }

        gold += purchasePool.gold * times;
        
        UserRewardData reward;
        reward.type = purchasePool.type;
        
        //var results = new List<UserItem>();
        //UserItem userItem;
        string timeKey = $"{NAME_SPACE_USER_PURCHASE_POOL_TIMES}{purchasePool.name}";
        float chance, total;
        int purchasePoolTimes = PlayerPrefs.GetInt(timeKey), chapter = UserData.chapter, i;
        bool isSelected;
        for (i = 0; i < times; ++i)
        {
            isSelected = false;
            total = 0.0f;
            chance = UnityEngine.Random.value;
            foreach (var purchasePoolOption in _purchasePoolOptions)
            {
                if(purchasePoolOption.poolName != purchasePool.name)
                    continue;
                
                if(purchasePoolOption.minTimes > purchasePoolTimes || 
                   purchasePoolOption.minTimes < purchasePoolOption.maxTimes && purchasePoolOption.maxTimes <= purchasePoolTimes)
                    continue;
                
                if(purchasePoolOption.minChapter > chapter ||
                   purchasePoolOption.minChapter < purchasePoolOption.maxChapter && purchasePoolOption.maxChapter <= chapter)
                    continue;

                total += purchasePoolOption.chance;
                if (total > 1.0f)
                {
                    total -= 1.0f;
                    
                    chance = UnityEngine.Random.value;

                    isSelected = false;
                }

                if (isSelected || total < chance)
                    continue;

                isSelected = true;
                
                reward.name = purchasePoolOption.name;
                reward.count = UnityEngine.Random.Range(purchasePoolOption.minCount, purchasePoolOption.maxCount);

                __ApplyReward(reward, outRewards);
            }

            ++purchasePoolTimes;
        }
        
        PlayerPrefs.SetInt(timeKey, purchasePoolTimes);

        return true;
    }
    
    private Dictionary<string, int> __purchasePoolNameToIndices;
    
    private int __GetPurchasePoolIndex(string name)
    {
        if (__purchasePoolNameToIndices == null)
        {
            __purchasePoolNameToIndices = new Dictionary<string, int>();

            int numPurchasePools = _purchasePools.Length;
            for(int i = 0; i < numPurchasePools; ++i)
                __purchasePoolNameToIndices.Add(_purchasePools[i].name, i);
        }

        return __purchasePoolNameToIndices.TryGetValue(name, out int index) ? index : -1;
    }
}

public partial class UserData
{
    public IEnumerator QueryPurchases(
        uint userID,
        Action<IUserData.Purchases> onComplete)
    {
        return UserDataMain.instance.QueryPurchases(userID, onComplete);
    }

    public IEnumerator Purchase(
        uint userID,
        uint purchasePoolID,
        int times,
        Action<Memory<UserItem>> onComplete)
    {
        return UserDataMain.instance.Purchase(userID, purchasePoolID, times, onComplete);
    }
}