using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    internal struct Skill
    {
        public string name;

        public string group;
        
#if UNITY_EDITOR
        [CSVField]
        public string 技能名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 技能组名称
        {
            set
            {
                group = value;
            }
        }
#endif
    }

    [Header("Common")]
    [SerializeField]
    internal Skill[] _skills;

    [SerializeField, CSV("_skills", guidIndex = -1, nameIndex = 0)]
    internal string _skillsPath;
    
    [Serializable]
    internal struct PurchasePool
    {
        public string name;

        [Tooltip("抽卡需要多少钻石")]
        public int diamond;

        [Tooltip("抽一次可获得多少金币")]
        public int gold;
    }
    
    [Serializable]
    internal struct PurchasePoolOption
    {
        [Tooltip("抽卡的名字")]
        public string name;
        
        [Tooltip("卡池名字")]
        public string poolName;
        
        [Tooltip("概率")]
        public float chance;
        
        [Tooltip("抽多少次后有效")]
        public int minTimes;

        [Tooltip("抽多少次后无效")]
        public int maxTimes;
        
        [Tooltip("在第几章开始生效")]
        public int minLevel;

        [Tooltip("在第几章开始生效")]
        public int maxLevel;
        
        [Tooltip("最小获得数量")]
        public int minCount;
        [Tooltip("最大获得数量")]
        public int maxCount;
        
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
        public float 抽卡概率
        {
            set
            {
                chance = value;
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
                minLevel = value;
            }
        }

        [CSVField]
        public int 抽卡最大章节
        {
            set
            {
                maxLevel = value;
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
#endif
    }

    [Header("Purchases")]
    [SerializeField] 
    internal PurchasePool[] _purchasePools;
    
    [SerializeField]
    internal PurchasePoolOption[] _purchasePoolOptions;

#if UNITY_EDITOR
    [SerializeField, CSV("_purchasePoolOptions", guidIndex = -1, nameIndex = 0)] 
    internal string _purchasePoolOptionsPath;
#endif

    private const string NAME_SPACE_USER_DIAMOND = "UserDiamond";
    private const string NAME_SPACE_USER_PURCHASE_POOL_KEY = "UserPurchasePoolKey";

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

    [Serializable]
    internal struct Card
    {
        public string name;

        public string styleName;
        
        public string skillName;

        public float skillGroupDamage;
        
#if UNITY_EDITOR
        [CSVField]
        public string 卡牌名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 卡牌品质
        {
            set
            {
                styleName = value;
            }
        }
        
        [CSVField]
        public string 卡牌技能名
        {
            set
            {
                skillName = value;
            }
        }
        
        [CSVField]
        public float 卡牌技能组伤害
        {
            set
            {
                skillGroupDamage = value;
            }
        }
#endif
    }

    [Serializable]
    internal struct CardDefault
    {
        public string name;

        public int count;
    }

    [Header("Cards")]
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_cardsDefaults")] 
    internal CardDefault[] _cardDefaults;

    [SerializeField] 
    internal Card[] _cards;

#if UNITY_EDITOR
    [SerializeField, CSV("_cards", guidIndex = -1, nameIndex = 0)] 
    internal string _cardsPath;
#endif

    private const string NAME_SPACE_USER_PURCHASE_POOL_TIMES = "UserPurchasePoolTimes";
    private const string NAME_SPACE_USER_CARD_COUNT = "UserCardCount";
    
    public IEnumerator Purchase(
        uint userID,
        uint purchasePoolID,
        int times,
        Action<Memory<UserItem>> onComplete)
    {
        yield return __CreateEnumerator();

        var purchasePool = _purchasePools[__ToIndex(purchasePoolID)];
        string poolKey = $"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{purchasePool.name}";
        int keyCount = PlayerPrefs.GetInt(poolKey);
        if (keyCount < times)
        {
            int destination = (times - keyCount) * purchasePool.diamond, source = diamond;
            if (destination > source)
            {
                onComplete(null);

                yield break;
            }
            
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
        reward.type = UserRewardType.Card;
        
        UserItem userItem;
        var results = new List<UserItem>();
        string timeKey = $"{NAME_SPACE_USER_PURCHASE_POOL_TIMES}{purchasePool.name}";
        float chance, total;
        int purchasePoolTimes = PlayerPrefs.GetInt(timeKey), level = UserData.level, i;
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
                
                if(purchasePoolOption.minLevel > level ||
                   purchasePoolOption.minLevel < purchasePoolOption.maxLevel && purchasePoolOption.maxLevel <= level)
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

                userItem.id = __ApplyReward(reward);
                userItem.name = reward.name;
                userItem.count = reward.count;

                results.Add(userItem);
            }

            ++purchasePoolTimes;
        }
        
        PlayerPrefs.SetInt(timeKey, purchasePoolTimes);

        flag &= ~Flag.PurchasesUnlockFirst;
        
        __AppendQuest(UserQuest.Type.Purchase, times);

        onComplete(results.ToArray());
    }
    
    [Serializable]
    internal struct Group
    {
        public string name;
    }
    
    [Serializable]
    internal struct CardStyle
    {
        public string name;
    }

    [Serializable]
    internal struct CardLevel
    {
        public string name;
        
        public string styleName;

        public int count;
        
        public int gold;

        [Tooltip("下一等级技能组伤害")]
        public float skillGroupDamage;
        
#if UNITY_EDITOR
        [CSVField]
        public string 卡牌等级名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 卡牌等级品质
        {
            set
            {
                styleName = value;
            }
        }
        
        [CSVField]
        public int 卡牌等级升级卡数
        {
            set
            {
                count = value;
            }
        }
        
        [CSVField]
        public int 卡牌等级升级金币
        {
            set
            {
                gold = value;
            }
        }
        
        [CSVField]
        public float 卡牌等级升级伤害
        {
            set
            {
                skillGroupDamage = value;
            }
        }
#endif
    }

    [SerializeField] 
    internal Group[] _cardGroups;
    
    [SerializeField] 
    internal CardStyle[] _cardStyles;
    
    [SerializeField]
    internal CardLevel[] _cardLevels;

#if UNITY_EDITOR
    [SerializeField, CSV("_cardLevels", guidIndex = -1, nameIndex = 0)] 
    internal string _cardLevelsPath;
#endif

    private const string NAME_SPACE_USER_CARDS_CAPACITY = "UserCardsCapacity";
    private const string NAME_SPACE_USER_CARD_LEVEL = "UserCardLevel";
    private const string NAME_SPACE_USER_CARD_GROUP = "UserCardGroup";
    
    public IEnumerator QueryCards(
        uint userID,
        Action<IUserData.Cards> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.Cards result;
        result.flag = 0;

        var flag = UserDataMain.flag;
        if ((flag & Flag.CardsUnlockFirst) == Flag.CardsUnlockFirst)
            result.flag |= IUserData.Cards.Flag.UnlockFirst;
        else if ((flag & Flag.CardsUnlock) != 0)
            result.flag |= IUserData.Cards.Flag.Unlock;
        
        if ((flag & Flag.CardUnlockFirst) == Flag.CardUnlockFirst)
            result.flag |= IUserData.Cards.Flag.CardFirst;
        
        if ((flag & Flag.CardUpgradeFirst) == Flag.CardUpgradeFirst)
            result.flag |= IUserData.Cards.Flag.CardUpgrade;

        if ((flag & Flag.CardReplaceFirst) == Flag.CardReplaceFirst)
            result.flag |= IUserData.Cards.Flag.CardReplace;

        bool isCreated = (flag & Flag.CardsCreated) != Flag.CardsCreated;

        result.capacity = PlayerPrefs.GetInt(NAME_SPACE_USER_CARDS_CAPACITY, 3);

        string groupName = PlayerPrefs.GetString(NAME_SPACE_USER_CARD_GROUP);
        result.selectedGroupID = __ToID(string.IsNullOrEmpty(groupName) ? 0 : __GetCardGroupIndex(groupName));

        int i, numCardGroups = _cardGroups.Length;
        result.groups = numCardGroups > 0 ? new UserGroup[numCardGroups] : null;
        
        UserGroup userGroup;
        for (i = 0; i < numCardGroups; ++i)
        {
            userGroup.id = __ToID(i);
            userGroup.name = _cardGroups[i].name;
            result.groups[i] = userGroup;
        }

        CardStyle cardStyle;
        int numCardStyles = _cardStyles == null ? 0 : _cardStyles.Length;
        result.cardStyles = new UserCardStyle[numCardStyles];

        int j, numCardLevelIndices;
        CardLevel cardLevel;
        UserCardStyle userCardStyle;
        UserCardStyle.Level userCardStyleLevel;
        List<int> cardLevelIndices;
        for (i = 0; i < numCardStyles; ++i)
        {
            cardStyle = _cardStyles[i];

            cardLevelIndices = __GetCardLevelIndices(i);
            numCardLevelIndices = cardLevelIndices == null ? 0 : cardLevelIndices.Count;
            if(numCardLevelIndices < 1)
                continue;
            
            userCardStyle.levels = new UserCardStyle.Level[numCardLevelIndices];
            for (j = 0; j < numCardLevelIndices; ++j)
            {
                cardLevel = _cardLevels[cardLevelIndices[j]];
                
                userCardStyleLevel.name = cardLevel.name;
                userCardStyleLevel.count = cardLevel.count;
                userCardStyleLevel.gold = cardLevel.gold;
                userCardStyleLevel.skillGroupDamage = cardLevel.skillGroupDamage;

                userCardStyle.levels[j] = userCardStyleLevel;
            }

            userCardStyle.id = __ToID(i);
            userCardStyle.name = cardStyle.name;

            result.cardStyles[i] = userCardStyle;
        }

        if (isCreated && _cardDefaults != null)
        {
            UserRewardData reward;
            reward.type = UserRewardType.Card;

            CardDefault cardDefault;
            int numCardDefaults = _cardDefaults.Length;
            for(i = 0; i < numCardDefaults; ++i)
            {
                cardDefault = _cardDefaults[i];
                reward.name = cardDefault.name;
                reward.count = cardDefault.count;
                        
                __ApplyReward(reward);
                
                for (j = 0; j < numCardGroups; ++j)
                    PlayerPrefs.SetInt(
                        $"{NAME_SPACE_USER_CARD_GROUP}{_cardGroups[j].name}{UserData.SEPARATOR}{cardDefault.name}", i);
            }
        }

        Card card;
        UserCard userCard;
        UserCard.Group userCardGroup;
        var userCards = new List<UserCard>();
        var userCardGroups = new List<UserCard.Group>();
        int numCards = _cards.Length;
        for (i = 0; i < numCards; ++i)
        {
            card = _cards[i];

            userCard.level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_LEVEL}{card.name}", -1);
            if (userCard.level == -1)
                continue;
            
            userCard.name = card.name;
            userCard.skillNames = __GetSkillGroupSkillNames(__GetSkillGroupName(card.skillName)).ToArray();
            
            userCard.id = __ToID(i);
            userCard.styleID = __ToID(__GetCardStyleIndex(card.styleName));
            
            userCard.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_COUNT}{card.name}");

            userCard.skillGroupDamage = card.skillGroupDamage;
            
            userCardGroups.Clear();
            for (j = 0; j < numCardGroups; ++j)
            {
                userCardGroup.position =
                    PlayerPrefs.GetInt(
                        $"{NAME_SPACE_USER_CARD_GROUP}{_cardGroups[j].name}{UserData.SEPARATOR}{userCard.name}",
                        -1);
                
                if(userCardGroup.position == -1)
                    continue;

                userCardGroup.groupID = __ToID(j);
                
                userCardGroups.Add(userCardGroup);
            }
            
            userCard.groups = userCardGroups.Count > 0 ? userCardGroups.ToArray() : null;
            
            userCards.Add(userCard);
        }
        
        result.cards = userCards.ToArray();
        
        if (isCreated)
        {
            flag |= Flag.CardsCreated;
            
            UserDataMain.flag = flag;
        }
        
        onComplete(result);
    }

    public IEnumerator QueryCard(
        uint userID,
        uint cardID,
        Action<UserCard> onComplete)
    {
        yield return __CreateEnumerator();

        var card = _cards[__ToIndex(cardID)];
        
        UserCard result;
        result.level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_LEVEL}{card.name}", -1);
        if (result.level == -1)
            yield break;
            
        result.name = card.name;
        result.skillNames = __GetSkillGroupSkillNames(__GetSkillGroupName(card.skillName)).ToArray();
            
        result.id = cardID;
        result.styleID = __ToID(__GetCardStyleIndex(card.styleName));
            
        result.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_COUNT}{card.name}");

        result.skillGroupDamage = card.skillGroupDamage;
        
        int numCardGroups = _cardGroups.Length;
        UserCard.Group userCardGroup;
        var userCardGroups = new List<UserCard.Group>();
        for (int i = 0; i < numCardGroups; ++i)
        {
            userCardGroup.position =
                PlayerPrefs.GetInt(
                    $"{NAME_SPACE_USER_CARD_GROUP}{_cardGroups[i].name}{UserData.SEPARATOR}{card.name}",
                    -1);
                
            if(userCardGroup.position == -1)
                continue;

            userCardGroup.groupID = __ToID(i);
                
            userCardGroups.Add(userCardGroup);
        }
            
        result.groups = userCardGroups.Count > 0 ? userCardGroups.ToArray() : null;

        onComplete(result);
    }
    
    public IEnumerator SetCardGroup(uint userID, uint groupID, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        PlayerPrefs.SetString(NAME_SPACE_USER_CARD_GROUP, _cardGroups[__ToIndex(groupID)].name);

        onComplete(true);
    }

    public IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        string cardName = _cards[__ToIndex(cardID)].name, cardGroupName = _cardGroups[__ToIndex(groupID)].name;
        PlayerPrefs.SetInt($"{NAME_SPACE_USER_CARD_GROUP}{cardGroupName}{UserData.SEPARATOR}{cardName}", position);

        var flag = UserDataMain.flag;
        flag &= ~Flag.CardsUnlockFirst;
        flag &= ~Flag.CardUnlockFirst;
        flag &= ~Flag.CardReplaceFirst;

        UserDataMain.flag = flag;
        
        onComplete(true);
    }

    public IEnumerator UpgradeCard(uint userID, uint cardID, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        var card = _cards[__ToIndex(cardID)];
        var levelIndices = __GetCardLevelIndices(__GetCardStyleIndex(card.styleName));
        string levelKey = $"{NAME_SPACE_USER_CARD_LEVEL}{card.name}";
        int level = PlayerPrefs.GetInt(levelKey);
        if (level >= levelIndices.Count)
        {
            onComplete(false);
            
            yield break;
        }
        
        string countKey = $"{NAME_SPACE_USER_CARD_COUNT}{card.name}";
        int count = PlayerPrefs.GetInt(countKey), 
            gold = UserDataMain.gold;

        var cardLevel = _cardLevels[levelIndices[level]];
        if (cardLevel.count > count || cardLevel.gold > gold)
        {
            onComplete(false);
            
            yield break;
        }

        PlayerPrefs.SetInt(levelKey, ++level);
        PlayerPrefs.SetInt(countKey, count - cardLevel.count);

        UserDataMain.gold = gold - cardLevel.gold;
        
        var flag = UserDataMain.flag;
        flag &= ~Flag.CardUpgradeFirst;

        UserDataMain.flag = flag;

        __AppendQuest(UserQuest.Type.CardToUpgrade, 1);
        
        onComplete(true);
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

    public IEnumerator QueryCards(
        uint userID,
        Action<IUserData.Cards> onComplete)
    {
        return UserDataMain.instance.QueryCards(userID, onComplete);
    }

    public IEnumerator QueryCard(
        uint userID,
        uint cardID,
        Action<UserCard> onComplete)
    {
        return UserDataMain.instance.QueryCard(userID, cardID, onComplete);
    }
    
    public IEnumerator SetCardGroup(uint userID, uint groupID, Action<bool> onComplete)
    {
        return UserDataMain.instance.SetCardGroup(userID, groupID, onComplete);
    }

    public IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete)
    {
        return UserDataMain.instance.SetCard(userID, cardID, groupID, position, onComplete);
    }

    public IEnumerator UpgradeCard(uint userID, uint cardID, Action<bool> onComplete)
    {
        return UserDataMain.instance.UpgradeCard(userID, cardID, onComplete);
    }
}