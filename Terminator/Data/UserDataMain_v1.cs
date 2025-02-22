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
        public int times;

        [Tooltip("在第几章开始生效")]
        public int level;

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
        public int 抽卡次数
        {
            set
            {
                times = value;
            }
        }
        
        [CSVField]
        public int 抽卡章节
        {
            set
            {
                level = value;
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
    [SerializeField, CSV("_purchasePoolOptions", guidIndex = -1, nameIndex = -1)] 
    internal string _purchasePoolOptionsPath;
#endif

    private const string NAME_SPACE_USER_PURCHASES_FLAG = "UserPurchasesFlag";
    private const string NAME_SPACE_USER_DIAMOND = "UserDiamond";
    private const string NAME_SPACE_USER_PURCHASE_POOL_KEY = "UserPurchasePoolKey";
    
    public IEnumerator QueryPurchases(
        uint userID,
        Action<IUserData.Purchases> onComplete)
    {
        yield return null;
        
        IUserData.Purchases purchases;
        purchases.flag = (IUserData.Purchases.Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_PURCHASES_FLAG);
        purchases.diamond = PlayerPrefs.GetInt(NAME_SPACE_USER_DIAMOND);

        UserPurchasePool userPurchasePool;
        PurchasePool purchasePool;
        int numPurchasePools = _purchasePools.Length;
        purchases.pools = new UserPurchasePool[numPurchasePools];
        for (int i = 0; i < numPurchasePools; ++i)
        {
            purchasePool = _purchasePools[i];
            userPurchasePool.name = purchasePool.name;
            userPurchasePool.id = __ToID(i);
            userPurchasePool.diamond = purchasePool.diamond;
            
            purchases.pools[i] = userPurchasePool;
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

        purchases.poolKeys = userPurchasePoolKeys.ToArray();
        
        onComplete(purchases);
    }

    [Serializable]
    internal struct Card
    {
        public string name;

        public string styleName;
        
        public string skillName;
    }

    [Header("Cards")]
    [SerializeField] 
    internal string[] _defaultCards;

    [SerializeField] 
    internal Card[] _cards;

    private const string NAME_SPACE_USER_PURCHASE_POOL_TIMES = "UserPurchasePoolTimes";
    private const string NAME_SPACE_USER_CARD_COUNT = "UserCardCount";
    
    public IEnumerator Purchase(
        uint userID,
        uint purchasePoolID,
        int times,
        Action<Memory<UserItem>> onComplete)
    {
        yield return null;

        UserItem result;
        var results = new List<UserItem>();
        string purchasePoolName = _purchasePools[__ToIndex(purchasePoolID)].name, 
            timeKey = $"{NAME_SPACE_USER_PURCHASE_POOL_TIMES}{purchasePoolName}", 
            key;
        float chance, total;
        int purchasePoolTimes = PlayerPrefs.GetInt(timeKey), level = UserData.level, numCards = _cards.Length, count, i, j;
        bool isSelected;
        for (i = 0; i < times; ++i)
        {
            isSelected = false;
            total = 0.0f;
            chance = UnityEngine.Random.value;
            foreach (var purchasePoolOption in _purchasePoolOptions)
            {
                if(purchasePoolOption.poolName != purchasePoolName)
                    continue;
                
                if(purchasePoolOption.times > purchasePoolTimes)
                    continue;
                
                if(purchasePoolOption.level > level)
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
                
                result.name = purchasePoolOption.name;
                result.id = 0;
                for (j = 0; j < numCards; ++j)
                {
                    if (result.name == _cards[j].name)
                    {
                        result.id = __ToID(j);
                        
                        break;
                    }
                }
                
                result.count = UnityEngine.Random.Range(purchasePoolOption.minCount, purchasePoolOption.maxCount);

                key = $"{NAME_SPACE_USER_CARD_COUNT}{purchasePoolOption.name}";
                count = PlayerPrefs.GetInt(key);
                count += result.count;
                PlayerPrefs.SetInt(key, count);
                
                results.Add(result);
            }

            ++purchasePoolTimes;
        }
        
        PlayerPrefs.SetInt(timeKey, purchasePoolTimes);

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

        public float damage;
        
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
        public int 卡牌等级升级伤害
        {
            set
            {
                damage = value;
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

    private UserCardStyle.Level[][] __cardLevels;
    
    private const string NAME_SPACE_USER_CARDS_FLAG = "UserCardsFlag";
    private const string NAME_SPACE_USER_CARDS_CAPACITY = "UserCardsCapacity";
    private const string NAME_SPACE_USER_CARD_LEVEL = "UserCardLevel";
    private const string NAME_SPACE_USER_CARD_GROUP = "UserCardGroup";
    
    public IEnumerator QueryCards(
        uint userID,
        Action<IUserData.Cards> onComplete)
    {
        yield return null;

        IUserData.Cards cards;
        cards.flag = (IUserData.Cards.Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_CARDS_FLAG);
        cards.capacity = PlayerPrefs.GetInt(NAME_SPACE_USER_CARDS_CAPACITY, 3);
        
        CardStyle cardStyle;
        int numCardStyles = _cardStyles == null ? 0 : _cardStyles.Length;
        cards.cardStyles = new UserCardStyle[numCardStyles];

        List<UserCardStyle.Level> userCardStyleLevels = null;
        if (__cardLevels == null)
        {
            __cardLevels = new UserCardStyle.Level[numCardStyles][];
            userCardStyleLevels = new List<UserCardStyle.Level>();
        }

        int i;
        UserCardStyle userCardStyle;
        UserCardStyle.Level userCardStyleLevel;
        for (i = 0; i < numCardStyles; ++i)
        {
            cardStyle = _cardStyles[i];

            if (userCardStyleLevels != null)
            {
                userCardStyleLevels.Clear();
                foreach (var cardLevel in _cardLevels)
                {
                    if (cardLevel.styleName == cardStyle.name)
                    {
                        userCardStyleLevel.name = cardLevel.name;
                        userCardStyleLevel.count = cardLevel.count;
                        userCardStyleLevel.gold = cardLevel.gold;
                        userCardStyleLevel.damage = cardLevel.damage;
                        userCardStyleLevels.Add(userCardStyleLevel);
                    }
                }
                
                __cardLevels[i] = userCardStyleLevels.ToArray();
            }

            userCardStyle.id = __ToID(i);
            userCardStyle.name = cardStyle.name;

            userCardStyle.levels = __cardLevels[i];
            
            cards.cardStyles[i] = userCardStyle;
        }

        string key;
        Card card;
        UserCard userCard;
        UserCard.Group userCardGroup;
        var userCards = new List<UserCard>();
        var userCardGroups = new List<UserCard.Group>();
        int j, numCardGroup = _cardGroups.Length, numCards = _cards.Length;
        for (i = 0; i < numCards; ++i)
        {
            card = _cards[i];
            
            userCard.level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_LEVEL}{card.name}");
            
            key = $"{NAME_SPACE_USER_CARD_COUNT}{card.name}";
            userCard.count = PlayerPrefs.GetInt(key);
            if (userCard.level < 1 && userCard.count < 1)
            {
                if (_defaultCards != null && Array.IndexOf(_defaultCards, card.name) != -1)
                {
                    userCard.count = 1;
                    PlayerPrefs.SetInt(key, 1);
                }
                else
                    continue;
            }

            userCard.name = card.name;
            userCard.skillName = card.skillName;
            userCard.id = __ToID(i);

            userCard.styleID = 0;
            for (j = 0; j < numCardStyles; ++j)
            {
                if (card.styleName == _cardStyles[j].name)
                {
                    userCard.styleID = __ToID(j);
                    
                    break;
                }
            }

            userCardGroups.Clear();
            for (j = 0; j < numCardGroup; ++j)
            {
                userCardGroup.position =
                    PlayerPrefs.GetInt(
                        $"{NAME_SPACE_USER_CARD_GROUP}{userCard.name}{UserData.SEPARATOR}{_cardGroups[j].name}",
                        -1);
                
                if(userCardGroup.position == -1)
                    continue;

                userCardGroup.groupID = __ToID(j);
                
                userCardGroups.Add(userCardGroup);
            }
            
            userCard.groups = userCardGroups.Count > 0 ? userCardGroups.ToArray() : null;
            
            userCards.Add(userCard);
        }
        
        cards.cards = userCards.ToArray();
        
        cards.groups = numCardGroup > 0 ? new UserGroup[numCardGroup] : null;
        UserGroup userGroup;
        for (i = 0; i < numCardGroup; ++i)
        {
            userGroup.id = __ToID(i);
            userGroup.name = _cardGroups[i].name;
            cards.groups[i] = userGroup;
        }
        
        onComplete(cards);
    }

    public IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete)
    {
        yield return null;

        string cardName = _cards[__ToIndex(cardID)].name, cardGroupName = _cardGroups[__ToIndex(groupID)].name;
        PlayerPrefs.SetInt($"{NAME_SPACE_USER_CARD_GROUP}{cardName}{UserData.SEPARATOR}{cardGroupName}", position);
        
        onComplete(true);
    }

    public IEnumerator UpgradeCard(uint userID, uint cardID, Action<bool> onComplete)
    {
        yield return null;

        var card = _cards[__ToIndex(cardID)];
        int numStyles = _cardStyles.Length, i;
        for (i = 0; i < numStyles; ++i)
        {
            if (card.styleName == _cardStyles[i].name)
                break;
        }

        string levelKey = $"{NAME_SPACE_USER_CARD_LEVEL}{card.name}",
            countKey = $"{NAME_SPACE_USER_CARD_COUNT}{card.name}";
        int level = PlayerPrefs.GetInt(levelKey), 
            count = PlayerPrefs.GetInt(countKey), 
            gold = PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);

        var cardLevel = _cardLevels[level];
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
    }

    [Serializable]
    internal struct Accessory
    {
        public string name;

        public string styleName;
    }

    [Serializable]
    internal struct AccessoryDefault
    {
        public string name;

        public int stage;

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
        
        public UserAttributeType attributeType;
    }

    [Serializable]
    internal struct AccessoryLevel
    {
        public string name;
        
        public string styleName;

        public string itemName;

        public int count;

        public float attributeValue;
    }

    [Serializable]
    internal struct AccessoryStage
    {
        public string name;

        public string accessoryName;

        public int count;

        public UserPropertyData property;
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
    [SerializeField, Tooltip("装备槽")] 
    internal AccessorySlot[] _accessorySlots;
    [SerializeField, Tooltip("装备类型")] 
    internal AccessoryStyle[] _accessoryStyles;
    [SerializeField, Tooltip("装备品阶")] 
    internal AccessoryStage[] _accessoryStages;
    [SerializeField, Tooltip("装备槽等级")] 
    internal AccessoryLevel[] _accessoryLevels;

    private const string NAME_SPACE_USER_ROLES_FLAG = "UserRolesFlag";
    private const string NAME_SPACE_USER_ITEM_COUNT = "UserItemCount";
    private const string NAME_SPACE_USER_ROLE_COUNT = "UserRoleCount";
    private const string NAME_SPACE_USER_ROLE_GROUP = "UserRoleGroup";
    private const string NAME_SPACE_USER_ACCESSORY_IDS = "UserAccessoryIDs";
    private const string NAME_SPACE_USER_ACCESSORY_GROUP = "UserAccessoryGroup";
    private const string NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL = "UserAccessorySlotLevel";
    
    public IEnumerator QueryRoles(
        uint userID,
        Action<IUserData.Roles> onComplete)
    {
        yield return null;

        IUserData.Roles result;
        result.flag = (IUserData.Roles.Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_ROLES_FLAG);
        bool isCreated = (result.flag & IUserData.Roles.Flag.Created) != IUserData.Roles.Flag.Created;
        if(isCreated)
            PlayerPrefs.SetInt(NAME_SPACE_USER_ROLES_FLAG, (int)IUserData.Roles.Flag.Created);

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

        bool isNew;
        int j, roleCount, numRoles = _roles.Length;
        UserRole userRole;
        string userRoleGroupName;
        var attributes = new List<UserAttributeData>();
        var userRoles = new List<UserRole>();
        var userRoleGroupIDs = new List<uint>();
        for (i = 0; i < numRoles; ++i)
        {
            userRole.name = _roles[i].name;

            isNew = false;
            key = $"{NAME_SPACE_USER_ROLE_COUNT}{userRole.name}";
            roleCount = PlayerPrefs.GetInt(key);
            if (roleCount < 1)
            {
                if (isCreated && 
                    _roleDefaults != null && 
                    Array.IndexOf(_roleDefaults, userRole.name) != -1)
                {
                    isNew = true;
                    
                    roleCount = 1;
                    
                    PlayerPrefs.SetInt(key, 1);
                }
                else
                    continue;
            }
            
            userRole.id = __ToID(i);
            
            attributes.Clear();
            foreach (var talent in _talents)
            {
                if(talent.roleName != userRole.name)
                    continue;
                
                if (((UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}") &
                     UserTalent.Flag.Collected) != UserTalent.Flag.Collected)
                    continue;

                attributes.Add(talent.attribute);
            }

            userRole.attributes = attributes.ToArray();

            userRoleGroupIDs.Clear();
            for (j = 0; j < numRoleGroups; ++j)
            {
                key = $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[j].name}";
                userRoleGroupName = PlayerPrefs.GetString(key);
                if (userRoleGroupName != userRole.name)
                {
                    if (isNew && string.IsNullOrEmpty(userRoleGroupName))
                        PlayerPrefs.SetString(key, userRole.name);
                    else
                        continue;
                }

                userRoleGroupIDs.Add(__ToID(j));
            }

            userRole.groupIDs = userRoleGroupIDs.ToArray();

            userRoles.Add(userRole);
        }
        
        result.roles = userRoles.ToArray();

        if (isCreated && _accessoryDefaults != null)
        {
            UserRewardData reward;
            reward.type = UserRewardType.Accessory;

            foreach (var accessoryDefault in _accessoryDefaults)
            {
                reward.name = accessoryDefault.name;
                reward.count = accessoryDefault.stage;

                __ApplyReward(reward);
            }
        }

        int k, l, 
            numAccessoryStages, 
            numAccessories = _accessories.Length, 
            numAccessorySlots = _accessorySlots.Length;
        string userAccessoryGroupKey;
        string[] ids;
        Accessory accessory;
        AccessoryStage accessoryStage;
        UserAccessory.Group userAccessoryGroup;
        UserAccessory userAccessory;
        List<int> accessoryStageIndices;
        var userAccessories = new List<UserAccessory>();
        var userAccessoryGroups = new List<UserAccessory.Group>();
        for (i = 0; i < numAccessories; ++i)
        {
            accessory = _accessories[i];

            userAccessory.name = accessory.name;
            userAccessory.styleID = __ToID(__GetAccessoryStyleIndex(accessory.styleName));
            
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
                            $"{NAME_SPACE_USER_ACCESSORY_GROUP}{_roleGroups[k].name}{UserData.SEPARATOR}";
                        for (l = 0; l < numAccessorySlots; ++l)
                        {
                            if (PlayerPrefs.GetString(
                                    $"{userAccessoryGroupKey}{_accessorySlots[l].name}") ==
                                userAccessory.name)
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
        AccessorySlot accessorySlot;
        for (i = 0; i < numAccessorySlots; ++i)
        {
            accessorySlot = _accessorySlots[i];
            userAccessorySlot.name = accessorySlot.name;
            userAccessorySlot.id = __ToID(i);
            userAccessorySlot.level =
                PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}");

            userAccessorySlot.styleID = __ToID(__GetAccessoryIndex(accessorySlot.styleName));
            
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
                userAccessoryStyleLevel.itemID = __ToID(__GetItemIndex(accessoryLevel.itemName));
                userAccessoryStyleLevel.count = accessoryLevel.count;
                userAccessoryStyleLevel.attributeValue = accessoryLevel.attributeValue;
                
                userAccessoryStyle.levels[j] = userAccessoryStyleLevel;
            }
            
            result.accessoryStyles[i] = userAccessoryStyle;
        }

        onComplete(result);
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
        public UserAttributeData attribute;
        public int gold;
    }

    private const string NAME_SPACE_USER_TALENT_FLAG = "UserTalentFlag";

    [Header("Talents")]
    [SerializeField]
    internal Talent[] _talents;

    [SerializeField, CSV("_talents", guidIndex = -1, nameIndex = 0)] 
    internal string _talentsPath;

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
        var userTalents = new UserTalent[numTalents];
        for (int i = 0; i < numTalents; ++i)
        {
            talent = _talents[i];
            if(talent.name != roleName)
                continue;
            
            userTalent.name = talent.name;
            userTalent.id = __ToID(i);
            userTalent.flag = (UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}");
            userTalent.attribute = talent.attribute;
            userTalent.gold = talent.gold;
            userTalents[i] = userTalent;
        }

        onComplete(userTalents);
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

    public IEnumerator SetAccessory(
        uint userID, 
        uint accessoryID, 
        uint groupID, 
        uint slotID, 
        Action<bool> onComplete)
    {
        yield return null;

        string roleGroupName = _roleGroups[__ToIndex(groupID)].name, 
            accessorySlotName = _accessorySlots[__ToIndex(slotID)].name, 
            key =
            $"{NAME_SPACE_USER_ACCESSORY_GROUP}{roleGroupName}{UserData.SEPARATOR}{accessorySlotName}", 
            accessoryName = _accessories[__ToIndex(accessoryID)].name;
        
        if(PlayerPrefs.GetString(key) == accessoryName)
            PlayerPrefs.DeleteKey(key);
        else
            PlayerPrefs.SetString(key, accessoryName);
        
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
            if (itemCount >= accessoryLevel.count)
            {
                PlayerPrefs.SetInt(itemCountKey, itemCount - accessoryLevel.count);

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
                userStageReward.flag =
                    (UserStageReward.Flag)PlayerPrefs.GetInt(
                        $"{NAME_SPACE_USER_STAGE_REWARD_FLAG}-{level.name}-{stage.name}-{stageReward.name}");
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

        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = __ToID(levelIndex);
        levelCache.stage = stage;
        levelCache.gold = 0;
        UserData.levelCache = levelCache;
        
        IUserData.StageProperty stageProperty;
        stageProperty.value = default;
        stageProperty.cache = UserData.GetStageCache(level.name, stage);
        
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

    private UserStageReward.Flag __GetStageRewardFlag(
        string stageRewardName,
        string levelName, 
        int stage, 
        UserStageReward.Condition condition, 
        out string key)
    {
        key = UserData.GetStageNameSpace(NAME_SPACE_USER_STAGE_REWARD_FLAG, levelName, stage);
        key = $"{key}{UserData.SEPARATOR}{stageRewardName}";
        
        var flag = (UserStageReward.Flag)PlayerPrefs.GetInt(key);
        if (flag == 0)
        {
            var stageFlag = UserData.GetStageFlag(levelName, stage);
            switch (condition)
            {
                case UserStageReward.Condition.Once:
                    if ((stageFlag & IUserData.StageFlag.Once) == IUserData.StageFlag.Once)
                        flag |= UserStageReward.Flag.Unlock;
                    break;
                case UserStageReward.Condition.NoDamage:
                    if ((stageFlag & IUserData.StageFlag.NoDamage) == IUserData.StageFlag.NoDamage)
                        flag |= UserStageReward.Flag.Unlock;
                    break;
            }
        }

        return flag;
    }

    private bool __ApplyStageRewards(
        string levelName, 
        int stage, 
        in StageReward stageReward, 
        List<UserReward> outRewards)
    {
        var flag = __GetStageRewardFlag(
            stageReward.name,
            levelName,
            stage,
            stageReward.condition,
            out var key);
        if ((flag & UserStageReward.Flag.Unlock) != UserStageReward.Flag.Unlock ||
            (flag & UserStageReward.Flag.Collected) == UserStageReward.Flag.Collected)
            return false;
                    
        flag |= UserStageReward.Flag.Collected;

        PlayerPrefs.SetInt(key, (int)flag);

        __ApplyRewards(stageReward.values, outRewards);

        return true;
    }

    private uint __ApplyReward(in UserRewardData reward)
    {
        uint id = 0;
        string key;
        switch (reward.type)
        {
            case UserRewardType.PurchasePoolKey:
                id = 1;
                key = $"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{reward.name}";
                break;
            case UserRewardType.CardsCapacity:
                id = 1;
                key = NAME_SPACE_USER_CARDS_CAPACITY;
                break;
            case UserRewardType.Card:
                int numCards = _cards.Length;
                for (int i = 0; i < numCards; ++i)
                {
                    if (_cards[i].name == reward.name)
                    {
                        id = __ToID(i);
                        
                        break;
                    }
                }
                key = $"{NAME_SPACE_USER_CARD_COUNT}{reward.name}";
                break;
            case UserRewardType.Role:
                int numRoles = _roles.Length;
                for (int i = 0; i < numRoles; ++i)
                {
                    if (_roles[i].name == reward.name)
                    {
                        id = __ToID(i);
                        
                        break;
                    }
                }
                key = $"{NAME_SPACE_USER_ROLE_COUNT}{reward.name}";
                break;
            case UserRewardType.Accessory:
                uint accessoryID = (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                __CreateAccessory(accessoryID, __GetAccessoryIndex(reward.name), reward.count);
                return accessoryID;
            case UserRewardType.Item:
                int numItems = _items.Length;
                for (int i = 0; i < numItems; ++i)
                {
                    if (_items[i].name == reward.name)
                    {
                        id = __ToID(i);
                        
                        break;
                    }
                }
                key = $"{NAME_SPACE_USER_ITEM_COUNT}{reward.name}";
                break; 
            case UserRewardType.Diamond:
                id = 1;
                key = $"{NAME_SPACE_USER_DIAMOND}{reward.name}";
                break;
            case UserRewardType.Gold:
                id = 1;
                key = $"{NAME_SPACE_USER_GOLD}{reward.name}";
                break;
            case UserRewardType.Energy:
                id = 1;
                key = $"{NAME_SPACE_USER_ENERGY}{reward.name}";
                break;
            default:
                return 0;
        }
        
        int count = PlayerPrefs.GetInt(key);
        count += reward.count;
        PlayerPrefs.SetInt(key, count);

        return id;
    }

    private void __ApplyRewards(
        UserRewardData[] rewards, 
        List<UserReward> outRewards)
    {
        UserReward outReward;
        foreach (var reward in rewards)
        {
            outReward.id = __ApplyReward(reward);
            if(outReward.id == 0)
                continue;

            outReward.name = reward.name;
            outReward.count = reward.count;
            outReward.type = reward.type;
            
            outRewards.Add(outReward);
        }
    }
    
    private bool __TryGetStage(uint stageID, out int stage, out int levelIndex, out int rewardIndex)
    {
        stage = -1;
        rewardIndex = 0;
        Level level;
        int i, j, 
            stageIndex = 0, 
            targetStageIndex = __ToIndex(stageID), 
            numTargetStages,
            numStages, 
            numLevels = Mathf.Min(_levels.Length, UserData.level + 1);
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            numStages = level.stages.Length;
            numTargetStages = Mathf.Min(stageIndex + numStages, targetStageIndex) - stageIndex;
            for (j = 0; j < numTargetStages; ++j)
                rewardIndex += level.stages[stageIndex + j].indirectRewards.Length;
            
            if (numTargetStages < numStages)
            {
                levelIndex = i;
                
                stage = numTargetStages;

                return true;
            }

            stageIndex += numStages;
        }

        levelIndex = -1;
        rewardIndex = -1;
        
        return false;
    }
}
