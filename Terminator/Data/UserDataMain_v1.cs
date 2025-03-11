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
        public string 技能名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 技能组名字
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
        
        result.diamond = PlayerPrefs.GetInt(NAME_SPACE_USER_DIAMOND);

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
        yield return null;

        var purchasePool = _purchasePools[__ToIndex(purchasePoolID)];
        string poolKey = $"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{purchasePool.name}";
        int keyCount = PlayerPrefs.GetInt(poolKey);
        if (keyCount < times)
        {
            int destination = (times - keyCount) * purchasePool.diamond, source = PlayerPrefs.GetInt(NAME_SPACE_USER_DIAMOND);
            if (destination > source)
            {
                onComplete(null);

                yield break;
            }
            
            PlayerPrefs.SetInt(NAME_SPACE_USER_DIAMOND, source - destination);
            
            PlayerPrefs.DeleteKey(poolKey);
        }
        else
        {
            keyCount -= times;
            
            PlayerPrefs.SetInt(poolKey, keyCount);
        }

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
        yield return null;

        IUserData.Cards result;
        result.flag = 0;

        var flag = UserDataMain.flag;
        if ((flag & Flag.CardsUnlockFirst) == Flag.CardsUnlockFirst)
            result.flag |= IUserData.Cards.Flag.UnlockFirst;
        else if ((flag & Flag.CardsUnlock) != 0)
            result.flag |= IUserData.Cards.Flag.Unlock;
        
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
        yield return null;

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
        yield return null;

        PlayerPrefs.SetString(NAME_SPACE_USER_CARD_GROUP, _cardGroups[__ToIndex(groupID)].name);

        onComplete(true);
    }

    public IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete)
    {
        yield return null;

        string cardName = _cards[__ToIndex(cardID)].name, cardGroupName = _cardGroups[__ToIndex(groupID)].name;
        PlayerPrefs.SetInt($"{NAME_SPACE_USER_CARD_GROUP}{cardGroupName}{UserData.SEPARATOR}{cardName}", position);
        
        flag &= ~Flag.CardsUnlockFirst;
        
        onComplete(true);
    }

    public IEnumerator UpgradeCard(uint userID, uint cardID, Action<bool> onComplete)
    {
        yield return null;

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
            gold = PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);

        var cardLevel = _cardLevels[levelIndices[level]];
        if (cardLevel.count > count || cardLevel.gold > gold)
        {
            onComplete(false);
            
            yield break;
        }

        PlayerPrefs.SetInt(levelKey, ++level);
        PlayerPrefs.SetInt(countKey, count - cardLevel.count);
        PlayerPrefs.SetInt(NAME_SPACE_USER_GOLD, gold - cardLevel.gold);
        
        onComplete(true);
    }

    [Serializable]
    internal struct Item
    {
        public string name;
    }

    [Serializable]
    internal struct ItemDefault
    {
        public string name;

        public int count;
    }

    [Serializable]
    internal struct Role
    {
        public string name;

        public string instanceName;
        
        [Tooltip("技能")]
        public string[] skillNames;
    }

    [Serializable]
    internal struct Accessory
    {
        public string name;

        public string styleName;
        
        [Tooltip("技能，可填空")]
        public string skillName;
        
        [Tooltip("基础属性值")]
        public float attributeValue;
        
        public UserAccessory.Property property;

#if UNITY_EDITOR
        [CSVField]
        public string 装备名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 装备类型
        {
            set
            {
                styleName = value;
            }
        }
        
        [CSVField]
        public string 装备技能名
        {
            set
            {
                skillName = value;
            }
        }
        
        [CSVField]
        public float 装备基础属性值
        {
            set
            {
                attributeValue = value;
            }
        }
        
        [CSVField]
        public string 装备属性
        {
            set
            {
                //skillGroupName = value;
                if (string.IsNullOrEmpty(value))
                {
                    property.attributes = null;
                    
                    return;
                }

                var parameters = value.Split('/');

                int numParameters = parameters.Length;
                string[] attributeParameters;
                UserAccessory.Attribute attribute;
                property.attributes = new UserAccessory.Attribute[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    attributeParameters = parameters[i].Split(':');
                    attribute.type = (UserAttributeType)int.Parse(attributeParameters[0]);
                    attribute.opcode = (UserAccessory.Opcode)int.Parse(attributeParameters[1]);
                    attribute.value = float.Parse(attributeParameters[2]);

                    property.attributes[i] = attribute;
                }
            }
        }
        
        [CSVField]
        public string 装备技能
        {
            set
            {
                //skillGroupName = value;
                if (string.IsNullOrEmpty(value))
                {
                    property.skills = null;
                    
                    return;
                }

                var parameters = value.Split('/');

                int numParameters = parameters.Length;
                string[] skillParameters;
                UserAccessory.Skill skill;
                property.skills = new UserAccessory.Skill[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    skillParameters = parameters[i].Split(':');
                    skill.name = skillParameters[0];
                    skill.type = (UserSkillType)int.Parse(skillParameters[1]);
                    skill.opcode = (UserAccessory.Opcode)int.Parse(skillParameters[2]);
                    skill.damage = float.Parse(skillParameters[3]);

                    property.skills[i] = skill;
                }
            }
        }
#endif
    }

    [Serializable]
    internal struct AccessoryDefault
    {
        public string name;

        public string stageName;

        public int count;
    }

    [Serializable]
    internal struct AccessorySlot
    {
        public string name;

        public string styleName;
    }

    [Serializable]
    internal struct AccessoryStyle
    {
        public string name;
        
        [Tooltip("该种类加什么属性")]
        public UserAttributeType attributeType;
    }

    [Serializable]
    internal struct AccessoryLevel
    {
        public string name;
        
        public string styleName;

        public string itemName;

        public int itemCount;

        [Tooltip("下一级属性加成")]
        public float attributeValue;

        [Tooltip("下一级技能伤害")]
        public float skillDamage;
        
#if UNITY_EDITOR
        [CSVField]
        public string 装备槽等级名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 装备槽等级类型
        {
            set
            {
                styleName = value;
            }
        }
        
        [CSVField]
        public string 装备槽等级升级卷轴名
        {
            set
            {
                itemName = value;
            }
        }
        
        [CSVField]
        public int 装备槽等级升级卷轴数
        {
            set
            {
                itemCount = value;
            }
        }

        [CSVField]
        public float 装备槽等级下一级属性
        {
            set
            {
                attributeValue = value;
            }
        }
        
        [CSVField]
        public float 装备槽等级下一级技能伤害
        {
            set
            {
                skillDamage = value;
            }
        }
#endif
    }

    [Serializable]
    internal struct AccessoryStage
    {
        public string name;

        public string accessoryName;

        public int count;

        public UserAccessory.Property property;
        
#if UNITY_EDITOR
        [CSVField]
        public string 装备品阶名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 装备品阶装备
        {
            set
            {
                accessoryName = value;
            }
        }
        
        [CSVField]
        public int 装备品阶需要个数
        {
            set
            {
                count = value;
            }
        }
        
        [CSVField]
        public string 装备品阶属性
        {
            set
            {
                //skillGroupName = value;
                if (string.IsNullOrEmpty(value))
                {
                    property.attributes = null;
                    
                    return;
                }

                var parameters = value.Split('/');

                int numParameters = parameters.Length;
                string[] attributeParameters;
                UserAccessory.Attribute attribute;
                property.attributes = new UserAccessory.Attribute[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    attributeParameters = parameters[i].Split(':');
                    attribute.type = (UserAttributeType)int.Parse(attributeParameters[0]);
                    attribute.opcode = (UserAccessory.Opcode)int.Parse(attributeParameters[1]);
                    attribute.value = float.Parse(attributeParameters[2]);

                    property.attributes[i] = attribute;
                }
            }
        }
        
        [CSVField]
        public string 装备品阶技能
        {
            set
            {
                //skillGroupName = value;
                if (string.IsNullOrEmpty(value))
                {
                    property.skills = null;
                    
                    return;
                }

                var parameters = value.Split('/');

                int numParameters = parameters.Length;
                string[] skillParameters;
                UserAccessory.Skill skill;
                property.skills = new UserAccessory.Skill[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    skillParameters = parameters[i].Split(':');
                    skill.name = skillParameters[0];
                    skill.type = (UserSkillType)int.Parse(skillParameters[1]);
                    skill.opcode = (UserAccessory.Opcode)int.Parse(skillParameters[2]);
                    skill.damage = float.Parse(skillParameters[3]);

                    property.skills[i] = skill;
                }
            }
        }
#endif
    }

    [Header("Items")]
    [SerializeField] 
    internal ItemDefault[] _itemDefaults;
    [SerializeField, Tooltip("卷轴")] 
    internal Item[] _items;
    
    [Header("Roles")]
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_defaultRoles")] 
    internal string[] _roleDefaults;
    [SerializeField, Tooltip("套装")] 
    internal Group[] _roleGroups;
    [SerializeField, Tooltip("角色")] 
    internal Role[] _roles;
    
    [Header("Accessories")]
    [SerializeField] 
    internal AccessoryDefault[] _accessoryDefaults;
    [SerializeField, Tooltip("装备")] 
    internal Accessory[] _accessories;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_accessories", guidIndex = -1, nameIndex = 0)] 
    internal string _accessoriesPath;
#endif

    [SerializeField, Tooltip("装备槽")] 
    internal AccessorySlot[] _accessorySlots;
    [SerializeField, Tooltip("装备类型")] 
    internal AccessoryStyle[] _accessoryStyles;
    [SerializeField, Tooltip("装备槽等级")] 
    internal AccessoryLevel[] _accessoryLevels;

#if UNITY_EDITOR
    [SerializeField, CSV("_accessoryLevels", guidIndex = -1, nameIndex = 0)] 
    internal string _accessoryLevelsPath;
#endif

    [SerializeField, Tooltip("装备品阶")] 
    internal AccessoryStage[] _accessoryStages;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_accessoryStages", guidIndex = -1, nameIndex = 0)] 
    internal string _accessoryStagesPath;
#endif
    
    private const string NAME_SPACE_USER_ITEM_COUNT = "UserItemCount";
    private const string NAME_SPACE_USER_ROLE_COUNT = "UserRoleCount";
    private const string NAME_SPACE_USER_ROLE_GROUP = "UserRoleGroup";
    private const string NAME_SPACE_USER_ACCESSORY_IDS = "UserAccessoryIDs";
    private const string NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL = "UserAccessorySlotLevel";
    
    public IEnumerator QueryRoles(
        uint userID,
        Action<IUserData.Roles> onComplete)
    {
        yield return null;

        IUserData.Roles result;
        result.flag = 0;

        var flag = UserDataMain.flag;
        if ((flag & Flag.RolesUnlockFirst) == Flag.RolesUnlockFirst)
            result.flag |= IUserData.Roles.Flag.UnlockFirst;
        else if ((flag & Flag.RolesUnlock) != 0)
        {
            result.flag |= IUserData.Roles.Flag.Unlock;
            
            if ((flag & Flag.RoleUnlock) == 0)
            {
                flag |= Flag.RoleUnlock;

                UserDataMain.flag = flag;
            }
        }
        
        if((flag & Flag.RoleUnlockFirst) == Flag.RoleUnlockFirst)
            result.flag |= IUserData.Roles.Flag.RoleUnlockFirst;
        else if ((flag & Flag.RoleUnlock) != 0)
            result.flag |= IUserData.Roles.Flag.RoleUnlock;
        
        bool isCreated = (flag & Flag.RolesCreated) != Flag.RolesCreated;

        string groupName = PlayerPrefs.GetString(NAME_SPACE_USER_ROLE_GROUP);
        result.selectedGroupID = __ToID(string.IsNullOrEmpty(groupName) ? 0 : __GetRoleGroupIndex(groupName));

        int i, numRoleGroups = _roleGroups.Length;
        result.groups = new UserGroup[numRoleGroups];
        UserGroup userGroup;
        for (i = 0; i < numRoleGroups; ++i)
        {
            userGroup.id = __ToID(i);
            userGroup.name = _roleGroups[i].name;
            result.groups[i] = userGroup;
        }

        if (isCreated && _itemDefaults != null)
        {
            UserRewardData reward;
            reward.type = UserRewardType.Item;

            foreach (var itemDefault in _itemDefaults)
            {
                reward.name = itemDefault.name;
                reward.count = itemDefault.count;

                __ApplyReward(reward);
            }
        }

        List<UserItem> items = new List<UserItem>();
        string key;
        UserItem userItem;
        int numItems = _items.Length;
        for (i = 0; i < numItems; ++i)
        {
            userItem.name = _items[i].name;

            key = $"{NAME_SPACE_USER_ITEM_COUNT}{userItem.name}";
            userItem.count = PlayerPrefs.GetInt(key);
            if (userItem.count < 1)
                continue;

            userItem.id = __ToID(i);

            items.Add(userItem);
        }
        
        result.items = items.ToArray();

        int j;
        if (isCreated && _itemDefaults != null)
        {
            UserRewardData reward;
            reward.type = UserRewardType.Role;

            foreach (var roleDefault in _roleDefaults)
            {
                reward.name = roleDefault;
                reward.count = 1;

                __ApplyReward(reward);
                
                for (j = 0; j < numRoleGroups; ++j)
                    PlayerPrefs.SetString(
                        $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[j].name}", roleDefault);
            }
        }

        int roleCount, numRoles = _roles.Length;
        string skillGroupName;
        Role role;
        UserRole userRole;
        var skillNames = new List<string>();
        var userRoles = new List<UserRole>();
        var userRoleGroupIDs = new List<uint>();
        for (i = 0; i < numRoles; ++i)
        {
            role = _roles[i];
            key = $"{NAME_SPACE_USER_ROLE_COUNT}{role.name}";
            roleCount = PlayerPrefs.GetInt(key);
            if (roleCount < 1)
                continue;
            
            userRole.id = __ToID(i);

            userRole.attributes = __CollectRoleAttributes(role.name, null, out userRole.skillGroupDamage)?.ToArray();

            userRoleGroupIDs.Clear();
            for (j = 0; j < numRoleGroups; ++j)
            {
                key = $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[j].name}";
                if (PlayerPrefs.GetString(key) != role.name)
                    continue;

                userRoleGroupIDs.Add(__ToID(j));
            }

            userRole.groupIDs = userRoleGroupIDs.ToArray();

            userRole.name = role.name;
            
            skillNames.Clear();

            foreach (var skillName in role.skillNames)
            {
                skillGroupName = __GetSkillGroupName(skillName);
                if (string.IsNullOrEmpty(skillGroupName))
                    skillNames.Add(skillName);
                else
                    skillNames.AddRange(__GetSkillGroupSkillNames(skillGroupName));
            }
            
            userRole.skillNames = skillNames.ToArray();

            userRoles.Add(userRole);
        }
        
        result.roles = userRoles.ToArray();

        int numAccessorySlots = _accessorySlots.Length, k;
        Accessory accessory;
        AccessorySlot accessorySlot;
        List<int> accessoryStageIndices;
        if (isCreated && _accessoryDefaults != null)
        {
            uint id;
            int accessoryIndex, numAccessoryStageIndices;
            UserRewardData reward;
            reward.type = UserRewardType.Accessory;
            foreach (var accessoryDefault in _accessoryDefaults)
            {
                reward.name = accessoryDefault.name;
                reward.count = -1;

                accessoryIndex = __GetAccessoryIndex(accessoryDefault.name);
                accessoryStageIndices = __GetAccessoryStageIndices(accessoryIndex);
                numAccessoryStageIndices = accessoryStageIndices.Count;
                for (i = 0; i < numAccessoryStageIndices; ++i)
                {
                    if (_accessoryStages[accessoryStageIndices[i]].name == accessoryDefault.stageName)
                    {
                        reward.count = i;
                        
                        break;
                    }
                }

                id = __ApplyReward(reward);

                accessory = _accessories[accessoryIndex];
                for (j = 0; j < numAccessorySlots; ++j)
                {
                    accessorySlot = _accessorySlots[j];
                    if(accessorySlot.styleName != accessory.styleName)
                        continue;
                    
                    for (k = 0; k < numRoleGroups; ++k)
                        PlayerPrefs.SetInt(
                            $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[k].name}{UserData.SEPARATOR}{accessorySlot.name}",
                            (int)id);
                }
            }
        }

        int l, 
            numAccessoryStages, 
            numAccessories = _accessories.Length;
        string userAccessoryGroupKey;
        string[] ids;
        AccessoryStage accessoryStage;
        UserAccessory.Group userAccessoryGroup;
        UserAccessory userAccessory;
        var userAccessories = new List<UserAccessory>();
        var userAccessoryGroups = new List<UserAccessory.Group>();
        for (i = 0; i < numAccessories; ++i)
        {
            accessory = _accessories[i];

            userAccessory.name = accessory.name;

            if (string.IsNullOrEmpty(accessory.skillName))
                userAccessory.skillNames = null;
            else
            {
                skillGroupName = __GetSkillGroupName(accessory.skillName);
                if (string.IsNullOrEmpty(skillGroupName))
                {
                    userAccessory.skillNames = new string[1];
                    userAccessory.skillNames[0] = accessory.skillName;
                }
                else
                    userAccessory.skillNames = __GetSkillGroupSkillNames(skillGroupName).ToArray();
            }

            userAccessory.styleID = __ToID(__GetAccessoryStyleIndex(accessory.styleName));

            userAccessory.attributeValue = accessory.attributeValue;

            userAccessory.property = accessory.property;
            
            accessoryStageIndices = __GetAccessoryStageIndices(i);

            numAccessoryStages = accessoryStageIndices.Count;
            for (j = 0; j < numAccessoryStages; ++j)
            {
                key =
                    $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessory.name}{UserData.SEPARATOR}{_accessoryStages[accessoryStageIndices[j]].name}";
                key = PlayerPrefs.GetString(key);
                ids = string.IsNullOrEmpty(key) ? null : key.Split(UserData.SEPARATOR);
                if (ids == null || ids.Length < 1)
                    continue;

                userAccessory.stage = j;

                accessoryStage = _accessoryStages[accessoryStageIndices[j]];

                userAccessory.stageDesc.name = accessoryStage.name;
                userAccessory.stageDesc.count = accessoryStage.count;
                userAccessory.stageDesc.property = accessoryStage.property;
                
                foreach (var id in ids)
                {
                    userAccessory.id = uint.Parse(id);
                    
                    userAccessoryGroups.Clear();
                    for (k = 0; k < numRoleGroups; ++k)
                    {
                        userAccessoryGroupKey =
                            $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[k].name}{UserData.SEPARATOR}";
                        for (l = 0; l < numAccessorySlots; ++l)
                        {
                            if ((uint)PlayerPrefs.GetInt(
                                    $"{userAccessoryGroupKey}{_accessorySlots[l].name}") ==
                                userAccessory.id)
                                break;
                        }

                        if (l == numAccessorySlots)
                            continue;

                        userAccessoryGroup.slotID = __ToID(l);
                        userAccessoryGroup.groupID = __ToID(k);
                        userAccessoryGroups.Add(userAccessoryGroup);
                    }

                    userAccessory.groups = userAccessoryGroups.ToArray();
                    userAccessories.Add(userAccessory);
                }
            }
        }
        
        result.accessories = userAccessories.ToArray();
        
        result.accessorySlots = new UserAccessorySlot[numAccessorySlots];

        UserAccessorySlot userAccessorySlot;
        for (i = 0; i < numAccessorySlots; ++i)
        {
            accessorySlot = _accessorySlots[i];
            userAccessorySlot.name = accessorySlot.name;
            userAccessorySlot.id = __ToID(i);
            userAccessorySlot.level =
                PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}");

            userAccessorySlot.styleID = __ToID(__GetAccessoryStyleIndex(accessorySlot.styleName));
            
            result.accessorySlots[i] = userAccessorySlot;
        }
        
        int numAccessoryStyles = _accessoryStyles.Length;
        result.accessoryStyles = new UserAccessoryStyle[numAccessoryStyles];
        
        int numAccessoryLevelIndices;
        AccessoryLevel accessoryLevel;
        AccessoryStyle accessoryStyle;
        UserAccessoryStyle userAccessoryStyle;
        UserAccessoryStyle.Level userAccessoryStyleLevel;
        List<int> accessoryLevelIndices;
        for (i = 0; i < numAccessoryStyles; ++i)
        {
            accessoryStyle = _accessoryStyles[i];
            userAccessoryStyle.name = accessoryStyle.name;
            userAccessoryStyle.id = __ToID(i);
            userAccessoryStyle.attributeType = accessoryStyle.attributeType;

            accessoryLevelIndices = __GetAccessoryStyleLevelIndices(i);

            numAccessoryLevelIndices = accessoryLevelIndices.Count;
            userAccessoryStyle.levels = new UserAccessoryStyle.Level[numAccessoryLevelIndices];
            for (j = 0; j < numAccessoryLevelIndices; ++j)
            {
                accessoryLevel = _accessoryLevels[accessoryLevelIndices[j]];
                
                userAccessoryStyleLevel.name = accessoryLevel.name;
                userAccessoryStyleLevel.itemName = accessoryLevel.itemName;
                userAccessoryStyleLevel.itemCount = accessoryLevel.itemCount;
                userAccessoryStyleLevel.attributeValue = accessoryLevel.attributeValue;
                
                userAccessoryStyle.levels[j] = userAccessoryStyleLevel;
            }
            
            result.accessoryStyles[i] = userAccessoryStyle;
        }

        if (isCreated)
        {
            flag |= Flag.RolesCreated;
            
            UserDataMain.flag = flag;
        }
        
        onComplete(result);
    }

    public IEnumerator QueryRole(
        uint userID,
        uint roleID,
        Action<UserRole> onComplete)
    {
        yield return null;
        
        UserRole result;
        
        var role = _roles[__ToIndex(roleID)];
        var key = $"{NAME_SPACE_USER_ROLE_COUNT}{role.name}";
        if (PlayerPrefs.GetInt(key) < 1)
        {
            onComplete(default);
            
            yield break;
        }
            
        result.name = role.name;

        string skillGroupName;
        var skillNames = new List<string>();
        foreach (var skillName in role.skillNames)
        {
            skillGroupName = __GetSkillGroupName(skillName);
            if (string.IsNullOrEmpty(skillGroupName))
                skillNames.Add(skillName);
            else
                skillNames.AddRange(__GetSkillGroupSkillNames(skillGroupName));
        }
            
        result.skillNames = skillNames.ToArray();

        result.id = roleID;

        result.attributes = __CollectRoleAttributes(role.name, null, out result.skillGroupDamage)?.ToArray();

        int numRoleGroups = _roleGroups.Length;
        var userRoleGroupIDs = new List<uint>();
        for (int i = 0; i < numRoleGroups; ++i)
        {
            key = $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[i].name}";
            if (PlayerPrefs.GetString(key) != role.name)
                continue;

            userRoleGroupIDs.Add(__ToID(i));
        }

        result.groupIDs = userRoleGroupIDs.ToArray();
        
        onComplete(result);
    }
    
    public IEnumerator SetRoleGroup(uint userID, uint groupID, Action<bool> onComplete)
    {
        yield return null;

        PlayerPrefs.SetString(NAME_SPACE_USER_ROLE_GROUP, _roleGroups[__ToIndex(groupID)].name);

        onComplete(true);
    }

    public IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete)
    {
        yield return null;
        
        string key =
                $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[__ToIndex(groupID)].name}",
            roleName = _roles[__ToIndex(roleID)].name;
        if (PlayerPrefs.GetString(key) == roleName)
            PlayerPrefs.DeleteKey(key);
        else
            PlayerPrefs.SetString(key, roleName);

        onComplete(true);
    }

    [Serializable]
    internal struct Talent
    {
        public string name;
        public string roleName;
        public int gold;
        public float skillGroupDamage;
        public UserAttributeData attribute;
        
#if UNITY_EDITOR
        [CSVField]
        public string 能力名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 能力角色名
        {
            set
            {
                roleName = value;
            }
        }
        
        [CSVField]
        public int 能力解锁消耗
        {
            set
            {
                gold = value;
            }
        }
        
        [CSVField]
        public float 能力技能组伤害加成
        {
            set
            {
                skillGroupDamage = value;
            }
        }
        
        [CSVField]
        public int 能力属性类型
        {
            set
            {
                attribute.type = (UserAttributeType)value;
            }
        }
        
        [CSVField]
        public float 能力属性值
        {
            set
            {
                attribute.value = value;
            }
        }
#endif
    }

    private const string NAME_SPACE_USER_TALENT_FLAG = "UserTalentFlag";

    [Header("Talents")]
    [SerializeField]
    internal Talent[] _talents;

#if UNITY_EDITOR
    [SerializeField, CSV("_talents", guidIndex = -1, nameIndex = 0)] 
    internal string _talentsPath;
#endif

    public IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID,
        Action<Memory<UserTalent>> onComplete)
    {
        yield return null;

        int numTalents = _talents.Length;
        string roleName = _roles[__ToIndex(roleID)].name;
        Talent talent;
        UserTalent userTalent;
        var userTalents = new List<UserTalent>();
        for (int i = 0; i < numTalents; ++i)
        {
            talent = _talents[i];
            if(talent.roleName != roleName)
                continue;
            
            userTalent.name = talent.name;
            userTalent.id = __ToID(i);
            userTalent.flag = (UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}");
            userTalent.gold = talent.gold;
            userTalent.skillGroupDamage = talent.skillGroupDamage;
            userTalent.attribute = talent.attribute;
            userTalents.Add(userTalent);
        }

        flag &= ~Flag.RoleUnlockFirst;

        onComplete(userTalents.ToArray());
    }

    public IEnumerator UpgradeRoleTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        yield return null;

        var talent = _talents[__ToIndex(talentID)];
        string key = $"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}";
        var flag = (UserTalent.Flag)PlayerPrefs.GetInt(key);
        if ((flag & UserTalent.Flag.Collected) == UserTalent.Flag.Collected)
        {
            onComplete(false);
            
            yield break;
        }

        int gold = UserDataMain.gold;
        
        if (talent.gold > gold)
        {
            onComplete(false);
            
            yield break;
        }

        UserDataMain.gold = gold - talent.gold;

        flag |= UserTalent.Flag.Collected;
        PlayerPrefs.SetInt(key, (int)flag);

        onComplete(true);
    }

    public IEnumerator QueryAccessory(
        uint userID,
        uint accessoryID,
        Action<UserAccessory> onComplete)
    {
        yield return null;

        if (!__TryGetAccessory(accessoryID, out var info))
        {
            onComplete(default);
            
            yield break;
        }
        
        UserAccessory result;
        var accessory = _accessories[info.index];
        result.name = accessory.name;
        result.id = accessoryID;
        
        if (string.IsNullOrEmpty(accessory.skillName))
            result.skillNames = null;
        else
        {
            string skillGroupName = __GetSkillGroupName(accessory.skillName);
            if (string.IsNullOrEmpty(skillGroupName))
            {
                result.skillNames = new string[1];
                result.skillNames[0] = accessory.skillName;
            }
            else
                result.skillNames = __GetSkillGroupSkillNames(skillGroupName).ToArray();
        }

        result.styleID = __ToID(__GetAccessoryStyleIndex(accessory.styleName));

        result.stage = info.stage;

        result.attributeValue = accessory.attributeValue;
        result.property = accessory.property;

        var accessoryStageIndices = __GetAccessoryStageIndices(info.index);
        var accessoryStage = _accessoryStages[accessoryStageIndices[info.stage]];

        result.stageDesc.name = accessoryStage.name;
        result.stageDesc.count = accessoryStage.count;
        result.stageDesc.property = accessoryStage.property;

        int i, j, numRoleGroups = _roleGroups.Length, numAccessorySlots = _accessorySlots.Length;
        string userAccessoryGroupKey;
        UserAccessory.Group userAccessoryGroup;
        var userAccessoryGroups = new List<UserAccessory.Group>();
        for (i = 0; i < numRoleGroups; ++i)
        {
            userAccessoryGroupKey =
                $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[i].name}{UserData.SEPARATOR}";
            for (j = 0; j < numAccessorySlots; ++j)
            {
                if ((uint)PlayerPrefs.GetInt(
                        $"{userAccessoryGroupKey}{_accessorySlots[j].name}") ==
                    accessoryID)
                    break;
            }

            if (j == numAccessorySlots)
                continue;

            userAccessoryGroup.slotID = __ToID(j);
            userAccessoryGroup.groupID = __ToID(i);
            userAccessoryGroups.Add(userAccessoryGroup);
        }

        result.groups = userAccessoryGroups.ToArray();
        
        onComplete(result);
    }

    public IEnumerator QueryAccessoryStages(
        uint userID,
        uint accessoryID,
        Action<Memory<UserAccessory.Stage>> onComplete)
    {
        yield return null;

        if (!__TryGetAccessory(accessoryID, out var info))
        {
            onComplete(null);

            yield break;
        }

        var stageIndices = __GetAccessoryStageIndices(info.index);
        int numStageIndices = stageIndices.Count;
        var userAccessoryStages = new UserAccessory.Stage[numStageIndices];
        UserAccessory.Stage userAccessoryStage;
        AccessoryStage accessoryStage;
        for (int i = 0; i < numStageIndices; ++i)
        {
            accessoryStage = _accessoryStages[stageIndices[i]];

            userAccessoryStage.name = accessoryStage.name;
            userAccessoryStage.count = accessoryStage.count;
            userAccessoryStage.property = accessoryStage.property;
            
            userAccessoryStages[i] = userAccessoryStage;
        }
        
        onComplete(userAccessoryStages);
    }
    
    public IEnumerator SetAccessory(
        uint userID, 
        uint accessoryID, 
        uint groupID, 
        uint slotID, 
        Action<bool> onComplete)
    {
        yield return null;

        var accessorySlot = _accessorySlots[__ToIndex(slotID)];
        string roleGroupName = _roleGroups[__ToIndex(groupID)].name, 
            key =
            $"{NAME_SPACE_USER_ROLE_GROUP}{roleGroupName}{UserData.SEPARATOR}{accessorySlot.name}";
        
        if((uint)PlayerPrefs.GetInt(key) == accessoryID)
            PlayerPrefs.DeleteKey(key);
        else if(__TryGetAccessory(accessoryID, out var accessoryInfo) && 
                _accessories[accessoryInfo.index].styleName == accessorySlot.styleName)
            PlayerPrefs.SetInt(key, (int)accessoryID);
        else
        {
            onComplete(false);
            
            yield break;
        }

        flag &= ~Flag.RolesUnlockFirst;
        
        onComplete(true);
    }

    public IEnumerator UpgradeAccessory(uint userID, uint accessorySlotID,
        Action<bool> onComplete)
    {
        yield return null;

        var accessorySlot = _accessorySlots[__ToIndex(accessorySlotID)];
        var levelIndices = __GetAccessoryStyleLevelIndices(__GetAccessoryStyleIndex(accessorySlot.styleName));

        string accessoryLevelKey = $"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}";
        int level = PlayerPrefs.GetInt(accessoryLevelKey);

        if (level < levelIndices.Count)
        {
            var accessoryLevel = _accessoryLevels[levelIndices[level]];
            string itemName = _items[__GetItemIndex(accessoryLevel.itemName)].name, 
                itemCountKey = $"{NAME_SPACE_USER_ITEM_COUNT}{itemName}";
            int itemCount = PlayerPrefs.GetInt(itemCountKey);
            if (itemCount >= accessoryLevel.itemCount)
            {
                PlayerPrefs.SetInt(itemCountKey, itemCount - accessoryLevel.itemCount);

                PlayerPrefs.SetInt(accessoryLevelKey, ++level);

                onComplete(true);
                
                yield break;
            }
        }

        onComplete(false);
    }

    public IEnumerator UprankAccessory(
        uint userID, 
        uint destinationAccessoryID, 
        uint[] sourceAccessoryIDs, 
        Action<UserAccessory.Stage?> onComplete)
    {
        yield return null;

        if (!__TryGetAccessory(destinationAccessoryID, out var info))
        {
            onComplete(null);

            yield break;
        }

        int index = info.index, stage = info.stage;
        string styleName = _accessories[index].styleName;
        foreach (var accessoryID in sourceAccessoryIDs)
        {
            if (!__TryGetAccessory(accessoryID, out info) || 
                stage != -1 && stage != info.stage || 
                styleName != null && styleName != _accessories[info.index].styleName)
            {
                onComplete(null);
                
                yield break;
            }
        }
        
        foreach (var accessoryID in sourceAccessoryIDs)
            __DeleteAccessory(accessoryID);
        
        __DeleteAccessory(destinationAccessoryID);
        
        __CreateAccessory(destinationAccessoryID, index, ++stage);

        UserAccessory.Stage userAccessoryStage;
        var stageIndices = __GetAccessoryStageIndices(index);
        if (stage < stageIndices.Count)
        {
            var accessoryStage = _accessoryStages[stageIndices[stage]];

            userAccessoryStage.name = accessoryStage.name;
            userAccessoryStage.count = accessoryStage.count;
            userAccessoryStage.property = accessoryStage.property;
        }
        else
            userAccessoryStage = default;
        
        onComplete(userAccessoryStage);
    }

    [Serializable]
    internal struct StageReward
    {
        public string name;
        public UserStageReward.Condition condition;
        public UserRewardData[] values;
    }

    [Serializable]
    internal struct Stage
    {
        public string name;

        public int energy;
        
        public UserRewardData[] directRewards;
        
        public StageReward[] indirectRewards;
        
        public UserStage.RewardPool[] rewardPools;
    }

    public IEnumerator QueryStage(
        uint userID,
        uint stageID, 
        Action<IUserData.Stage> onComplete)
    {
        yield return null;
        
        if (__TryGetStage(stageID, out int targetStage, out int levelIndex, out int rewardIndex))
        {
            IUserData.Stage result;
            var level = _levels[levelIndex];
            
            var stage = level.stages[targetStage];

            result.energy = stage.energy;
            result.levelEnergy = level.energy;
            result.cache = UserData.GetStageCache(level.name, targetStage);

            int numStageRewards = stage.indirectRewards.Length;
            result.rewards = new UserStageReward[numStageRewards];

            int i;
            StageReward stageReward;
            UserStageReward userStageReward;
            for (i = 0; i < numStageRewards; ++i)
            {
                stageReward = stage.indirectRewards[i];
                userStageReward.name = stageReward.name;
                userStageReward.id = __ToID(rewardIndex + i);
                userStageReward.flag = __GetStageRewardFlag(stageReward.name, level.name, targetStage,
                    stageReward.condition, out _);
                userStageReward.condition = stageReward.condition;
                userStageReward.values = stageReward.values;

                result.rewards[i] = userStageReward;
            }
            
            onComplete(result);

            yield break;
        }

        onComplete(default);
    }

    public IEnumerator ApplyStage(
        uint userID,
        uint stageID,
        Action<IUserData.StageProperty> onComplete)
    {
        yield return null;

        if (!__TryGetStage(stageID, out int stage, out int levelIndex, out _))
        {
            onComplete(default);
            
            yield break;
        }

        int userLevel = UserData.level;
        if (userLevel < levelIndex)
        {
            onComplete(default);
            
            yield break;
        }
        
        var level = _levels[levelIndex];
        if (!__ApplyEnergy(level.stages[stage].energy))
        {
            onComplete(default);

            yield break;
        }
        
        UserData.ApplyStageFlag(level.name, stage);

        //flag &= ~Flag.UnlockFirst;

        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = __ToID(levelIndex);
        levelCache.stage = stage;
        levelCache.gold = 0;
        UserData.levelCache = levelCache;
        
        IUserData.StageProperty stageProperty;
        stageProperty.cache = UserData.GetStageCache(level.name, stage);
        stageProperty.value = __ApplyProperty(
            userID, 
            stageProperty.cache.skills);
        
        onComplete(stageProperty);
    }

    private const string NAME_SPACE_USER_STAGE_REWARD_FLAG = "userStageRewardFlag";

    public IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<Memory<UserReward>> onComplete)
    {
        yield return null;

        int i, j, 
            stageRewardIndex = __ToIndex(stageRewardID), 
            numStages, 
            numStageRewards, 
            numLevels = Mathf.Min(_levels.Length, UserData.level + 1);
        Level level;
        Stage stage;
        List<UserReward> rewards = null;
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            numStages = level.stages.Length;
            for (j = 0; j < numStages; ++j)
            {
                stage = level.stages[j];

                numStageRewards = stage.indirectRewards.Length;
                if (stageRewardIndex < numStageRewards)
                {
                    if (rewards == null)
                        rewards = new List<UserReward>();
                    
                    if (__ApplyStageRewards(level.name, 
                            j, 
                            stage.indirectRewards[stageRewardIndex], 
                            rewards))
                    {
                        onComplete(rewards.ToArray());

                        yield break;
                    }

                    break;
                }

                stageRewardIndex -= numStageRewards;
            }
        }
        
        onComplete(null);
    }

    public IEnumerator CollectStageRewards(uint userID, Action<Memory<UserReward>> onComplete)
    {
        yield return null;

        bool result = false;
        int i,
            j,
            k,
            numStages,
            numStageRewards,
            numLevels = Mathf.Min(_levels.Length, UserData.level + 1);
        Level level;
        Stage stage;
        StageReward stageReward;
        List<UserReward> rewards = null;
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            numStages = level.stages.Length;
            for (j = 0; j < numStages; ++j)
            {
                stage = level.stages[j];

                numStageRewards = stage.indirectRewards.Length;
                for (k = 0; k < numStageRewards; ++k)
                {
                    stageReward = stage.indirectRewards[k];

                    if (rewards == null)
                        rewards = new List<UserReward>();
                    
                    result |= __ApplyStageRewards(level.name, j, stageReward, rewards);
                }
            }
        }

        onComplete(result ? rewards.ToArray() : null);
    }

    public IEnumerator ApplyReward(uint userID, string poolName, Action<Memory<UserReward>> onComplete)
    {
        yield return null;

        var levelCache = UserData.levelCache;
        if (levelCache == null)
            yield break;

        var temp = levelCache.Value;
        var level = _levels[__ToIndex(temp.id)];

        bool isSelected;
        float chance, total;
        var stage = level.stages[Mathf.Min(temp.stage, level.stages.Length - 1)];
        var results = new List<UserRewardData>();
        foreach (var rewardPool in stage.rewardPools)
        {
            if (rewardPool.name == poolName)
            {
                isSelected = false;
                chance = UnityEngine.Random.value;
                total = 0.0f;
                foreach (var option in rewardPool.options)
                {
                    total += option.chance;
                    if (total > 1.0f)
                    {
                        total -= 1.0f;
                        
                        chance = UnityEngine.Random.value;

                        isSelected = false;
                    }
                    
                    if(isSelected || total < chance)
                        continue;

                    isSelected = true;

                    results.Add(option.value);
                }
                
                break;
            }
        }

        if (results.Count > 0)
        {
            var rewards = new List<UserReward>();
            __ApplyRewards(results.ToArray(), rewards);

            onComplete(rewards.ToArray());
        }
        else
            onComplete(null);
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

    public IEnumerator QueryRoles(
        uint userID,
        Action<IUserData.Roles> onComplete)
    {
        return UserDataMain.instance.QueryRoles(userID, onComplete);
    }

    public IEnumerator SetRoleGroup(uint userID, uint groupID, Action<bool> onComplete)
    {
        return UserDataMain.instance.SetRoleGroup(userID, groupID, onComplete);
    }
    
    public IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete)
    {
        return UserDataMain.instance.SetRole(userID, roleID, groupID, onComplete);
    }

    public IEnumerator QueryRole(
        uint userID,
        uint roleID,
        Action<UserRole> onComplete)
    {
        return UserDataMain.instance.QueryRole(userID, roleID, onComplete);
    }

    public IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID,
        Action<Memory<UserTalent>> onComplete)
    {
        return UserDataMain.instance.QueryRoleTalents(userID, roleID, onComplete);
    }

    public IEnumerator UpgradeRoleTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.UpgradeRoleTalent(userID, talentID, onComplete);
    }

    public IEnumerator QueryAccessory(
        uint userID,
        uint accessoryID,
        Action<UserAccessory> onComplete)
    {
        return UserDataMain.instance.QueryAccessory(userID, accessoryID, onComplete);
    }
    
    public IEnumerator QueryAccessoryStages(
        uint userID,
        uint accessoryID,
        Action<Memory<UserAccessory.Stage>> onComplete)
    {
        return UserDataMain.instance.QueryAccessoryStages(userID, accessoryID, onComplete);
    }

    public IEnumerator SetAccessory(uint userID, uint accessoryID, uint groupID, uint slotID, Action<bool> onComplete)
    {
        return UserDataMain.instance.SetAccessory(userID, accessoryID, groupID, slotID, onComplete);
    }

    public IEnumerator UpgradeAccessory(uint userID, uint accessoryslotID, Action<bool> onComplete)
    {
        return UserDataMain.instance.UpgradeAccessory(userID, accessoryslotID, onComplete);
    }

    public IEnumerator UprankAccessory(
        uint userID, 
        uint destinationAccessoryID, 
        uint[] sourceAccessoryIDs, 
        Action<UserAccessory.Stage?> onComplete)
    {
        return UserDataMain.instance.UprankAccessory(userID, destinationAccessoryID, sourceAccessoryIDs, onComplete);
    }

    public IEnumerator QueryStage(
        uint userID,
        uint stageID,
        Action<IUserData.Stage> onComplete)
    {
        return UserDataMain.instance.QueryStage(userID, stageID, onComplete);
    }

    public IEnumerator ApplyStage(
        uint userID,
        uint stageID,
        Action<IUserData.StageProperty> onComplete)
    {
        return UserDataMain.instance.ApplyStage(userID, stageID, onComplete);
    }
    
    public IEnumerator SubmitStage(
        uint userID,
        IUserData.StageFlag flag,
        int stage,
        int gold, 
        int rage, 
        int exp, 
        int expMax, 
        string[] skills,
        Action<bool> onComplete)
    {
        var levelCache = UserData.levelCache;
        if (levelCache == null)
        {
            onComplete(false);
            
            yield break;
        }

        var temp = levelCache.Value;

        __SubmitStageFlag(flag, temp.name, temp.stage, stage);

        IUserData.StageCache stageCache;
        stageCache.rage = rage;
        stageCache.exp = exp;
        stageCache.expMax = expMax;
        stageCache.skills = skills;
        PlayerPrefs.SetString(GetStageNameSpace(NAME_SPACE_USER_STAGE_CACHE, temp.name, stage), stageCache.ToString());
        
        temp.stage = stage;
        temp.gold = gold;
        UserData.levelCache = temp;
        
        onComplete(true);
    }
    
    public IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectStageReward(userID, stageRewardID, onComplete);
    }

    public IEnumerator CollectStageRewards(uint userID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectStageRewards(userID, onComplete);
    }

    public IEnumerator ApplyReward(uint userID, string poolName, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.ApplyReward(userID, poolName, onComplete);
    }
}