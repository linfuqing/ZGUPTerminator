using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
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
    
    [SerializeField]
    internal PurchasePoolOption[] _purchasePoolOptions;

#if UNITY_EDITOR
    [CSV("_purchasePoolOptions", guidIndex = -1, nameIndex = -1)] 
    internal string _purchasePoolOptionsPath;
#endif

    private const string NAME_SPACE_USER_PURCHASES_FLAG = "UserPurchasesFlag";
    private const string NAME_SPACE_USER_DIAMOND = "UserDiamond";
    private const string NAME_SPACE_USER_PURCHASE_POOL_KEY = "UserPurchasePoolKey";
    
    private string[] __purchasePoolNames;

    private string[] __GetPurchasePoolNames()
    {
        if (__purchasePoolNames == null)
        {
            var purchasePoolNames = new HashSet<string>();
            foreach (var purchasePoolOption in _purchasePoolOptions)
                purchasePoolNames.Add(purchasePoolOption.poolName);

            __purchasePoolNames = new string[purchasePoolNames.Count];
            
            purchasePoolNames.CopyTo(__purchasePoolNames, 0);
        }

        return __purchasePoolNames;
    }
    
    public IEnumerator QueryPurchases(
        uint userID,
        Action<IUserData.Purchases> onComplete)
    {
        yield return null;
        
        IUserData.Purchases purchases;
        purchases.flag = (IUserData.Purchases.Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_PURCHASES_FLAG);
        purchases.diamond = PlayerPrefs.GetInt(NAME_SPACE_USER_DIAMOND);

        var purchasePoolNames = __GetPurchasePoolNames();

        UserPurchasePool userPurchasePool;
        int numPurchasePoolNames = purchasePoolNames.Length;
        purchases.pools = new UserPurchasePool[numPurchasePoolNames];
        for (int i = 0; i < numPurchasePoolNames; ++i)
        {
            userPurchasePool.name = purchasePoolNames[i];
            userPurchasePool.id = __ToID(i);
            
            purchases.pools[i] = userPurchasePool;
        }
        
        purchases.poolKeys = new IUserData.Purchases.PoolKey[numPurchasePoolNames];
        IUserData.Purchases.PoolKey userPurchasePoolKey;
        for (int i = 0; i < numPurchasePoolNames; ++i)
        {
            userPurchasePoolKey.poolID = __ToID(i);
            
            userPurchasePoolKey.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{purchasePoolNames[i]}");
        }

        onComplete(purchases);
    }

    [Serializable]
    internal struct Card
    {
        public string name;

        public string styleName;
    }

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
        string purchasePoolName = __GetPurchasePoolNames()[__ToIndex(purchasePoolID)], 
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

        public UserCardStyle.Level[] levels;
    }

    [SerializeField] 
    internal Group[] _cardGroups;
    
    [SerializeField] 
    internal CardStyle[] _cardStyles;
    
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
        cards.flag = (IUserData.Cards.Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_CARDS_FLAG);;
        cards.capacity = PlayerPrefs.GetInt(NAME_SPACE_USER_CARDS_CAPACITY, 3);
        
        CardStyle cardStyle;
        int numCardStyles = _cardStyles == null ? 0 : _cardStyles.Length;
        cards.cardStyles = new UserCardStyle[numCardStyles];

        UserCardStyle userCardStyle;
        int i;
        for (i = 0; i < numCardStyles; ++i)
        {
            cardStyle = _cardStyles[i];
            userCardStyle.id = __ToID(i);
            userCardStyle.name = cardStyle.name;
            userCardStyle.levels = cardStyle.levels;
            
            cards.cardStyles[i] = userCardStyle;
        }
        
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
            userCard.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_COUNT}{card.name}");
            if(userCard.level < 1 && userCard.count < 1)
                continue;

            userCard.name = card.name;
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

        var levels = _cardStyles[i].levels;
        var levelData = levels[level];
        if (levelData.count > count || levelData.gold > gold)
        {
            onComplete(false);
            
            yield break;
        }

        PlayerPrefs.SetInt(levelKey, ++level);
        PlayerPrefs.SetInt(countKey, count - levelData.count);
        PlayerPrefs.SetInt(NAME_SPACE_USER_GOLD, gold - levelData.gold);
        
        onComplete(true);
    }

    [Serializable]
    internal struct Item
    {
        public string name;
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
    internal struct AccessorySlot
    {
        public string name;

        public string styleName;
    }

    [Serializable]
    internal struct AccessorySlotLevel
    {
        public string name;

        public string itemName;

        public int count;
    }

    [Serializable]
    internal struct AccessoryStyle
    {
        public string name;

        public string[] levelNames;

        public UserAccessoryStyle.Stage[] stages;
    }
    
    [SerializeField, Tooltip("卷轴")] 
    internal Item[] _items;
    [SerializeField, Tooltip("套装")] 
    internal Group[] _roleGroups;
    [SerializeField, Tooltip("角色")] 
    internal Role[] _roles;
    [SerializeField, Tooltip("装备")] 
    internal Accessory[] _accessories;
    [SerializeField, Tooltip("装备槽")] 
    internal AccessorySlot[] _accessorySlots;
    [SerializeField, Tooltip("装备槽等级")]
    internal AccessorySlotLevel[] _accessorySlotLevels;
    [SerializeField, Tooltip("装备类型")] 
    internal AccessoryStyle[] _accessoryStyles;

    private const string NAME_SPACE_USER_ROLES_FLAG = "UserRolesFlag";
    private const string NAME_SPACE_USER_ITEM_COUNT = "UserItemCount";
    private const string NAME_SPACE_USER_ROLE_COUNT = "UserRoleCount";
    private const string NAME_SPACE_USER_ROLE_GROUP = "UserRoleGroup";
    private const string NAME_SPACE_USER_ACCESSORY_COUNT = "UserAccessoryCount";
    private const string NAME_SPACE_USER_ACCESSORY_STAGE = "UserAccessoryStage";
    private const string NAME_SPACE_USER_ACCESSORY_GROUP = "UserAccessoryGroup";
    private const string NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL = "UserAccessorySlotLevel";
    
    public IEnumerator QueryRoles(
        uint userID,
        Action<IUserData.Roles> onComplete)
    {
        yield return null;

        IUserData.Roles result;
        result.flag = (IUserData.Roles.Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_ROLES_FLAG);
        
        List<UserItem> items = new List<UserItem>();
        UserItem userItem;
        int i, numItems = _items.Length;
        for (i = 0; i < numItems; ++i)
        {
            userItem.name = _items[i].name;
            userItem.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ITEM_COUNT}{userItem.name}");
            if(userItem.count < 1)
                continue;
            
            userItem.id = __ToID(i);

            items.Add(userItem);
        }
        
        result.items = items.ToArray();

        int numRoleGroups = _roleGroups.Length;
        result.groups = new UserGroup[numRoleGroups];
        UserGroup userGroup;
        for (i = 0; i < numRoleGroups; ++i)
        {
            userGroup.id = __ToID(i);
            userGroup.name = _roleGroups[i].name;

            result.groups[i] = userGroup;
        }

        int j, roleCount, numRoles = _roles.Length;
        UserRole userRole;
        var userRoles = new List<UserRole>();
        var userRoleGroupIDs = new List<uint>();
        for (i = 0; i < numRoles; ++i)
        {
            userRole.name = _roles[i].name;
            roleCount = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_COUNT}{userRole.name}");
            if(roleCount < 1)
                continue;
            
            userRole.id = __ToID(i);
            userRole.hp = 0;
            userRole.attack = 0;
            userRole.defence = 0;

            foreach (var talent in _talents)
            {
                if(talent.roleName != userRole.name)
                    continue;
                
                if (((UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}") &
                     UserTalent.Flag.Collected) != UserTalent.Flag.Collected)
                    continue;

                switch (talent.rewardType)
                {
                    case UserTalent.RewardType.Hp:
                        userRole.hp += talent.rewardCount;
                        break;
                    case UserTalent.RewardType.Attack:
                        userRole.attack += talent.rewardCount;
                        break;
                    case UserTalent.RewardType.Defence:
                        userRole.defence += talent.rewardCount;
                        break;
                }
            }

            userRoleGroupIDs.Clear();
            for (j = 0; j < numRoleGroups; ++j)
            {
                if (PlayerPrefs.GetString(
                        $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[j].name}") != userRole.name)
                    continue;

                userRoleGroupIDs.Add(__ToID(j));
            }

            userRole.groupIDs = userRoleGroupIDs.ToArray();

            userRoles.Add(userRole);
        }
        
        result.roles = userRoles.ToArray();

        int k,
            numAccessories = _accessories.Length,
            numAccessoryStyles = _accessoryStyles.Length,
            numAccessorySlots = _accessorySlots.Length;
        string userAccessoryGroupKey;
        Accessory accessory;
        UserAccessory.Group userAccessoryGroup;
        UserAccessory userAccessory;
        var userAccessories = new List<UserAccessory>();
        var userAccessoryGroups = new List<UserAccessory.Group>();
        for (i = 0; i < numAccessories; ++i)
        {
            accessory = _accessories[i];
            
            userAccessory.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_COUNT}{accessory.name}");
            if(userAccessory.count < 1)
                continue;
            
            userAccessory.id = __ToID(i);
            userAccessory.name = accessory.name;

            userAccessory.styleID = 0;
            for (j = 0; j < numAccessoryStyles; ++j)
            {
                if (_accessoryStyles[j].name == accessory.styleName)
                {
                    userAccessory.styleID = __ToID(j);
                    
                    break;
                }
            }
            
            userAccessory.stage = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_STAGE}{accessory.name}");

            userAccessoryGroups.Clear();
            for (j = 0; j < numRoleGroups; ++j)
            {
                userAccessoryGroupKey = $"{NAME_SPACE_USER_ACCESSORY_GROUP}{_roleGroups[j].name}{UserData.SEPARATOR}";
                for (k = 0; k < numAccessorySlots; ++k)
                {
                    if (PlayerPrefs.GetString(
                            $"{userAccessoryGroupKey}{_accessorySlots[k].name}") ==
                        userAccessory.name)
                        break;
                }
                
                if(k == numAccessoryStyles)
                    continue;
                
                userAccessoryGroup.slotID = __ToID(k);
                userAccessoryGroup.groupID = __ToID(j);
                userAccessoryGroups.Add(userAccessoryGroup);
            }

            userAccessory.groups = userAccessoryGroups.ToArray();

            userAccessories.Add(userAccessory);
        }
        
        result.accessories = userAccessories.ToArray();
        
        result.accessorySlots = new UserAccessorySlot[numAccessorySlots];
        UserAccessorySlot userAccessorySlot;
        AccessorySlot accessorySlot;
        AccessorySlotLevel accessorySlotLevel;
        for (i = 0; i < numAccessoryStyles; ++i)
        {
            accessorySlot = _accessorySlots[i];
            userAccessorySlot.name = accessorySlot.name;
            userAccessorySlot.id = __ToID(i);
            userAccessorySlot.level =
                PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}");
            accessorySlotLevel = _accessorySlotLevels[userAccessorySlot.level];
            
            userAccessorySlot.levelDesc.name = accessorySlotLevel.name;
            userAccessorySlot.levelDesc.itemID = 0;
            for (j = 0; j < numItems; ++j)
            {
                if (accessorySlotLevel.itemName == _items[j].name)
                {
                    userAccessorySlot.levelDesc.itemID = __ToID(j);
                    
                    break;
                }
            }
            
            userAccessorySlot.levelDesc.count = accessorySlotLevel.count;
            
            result.accessorySlots[i] = userAccessorySlot;
        }
        
        result.accessoryStyles = new UserAccessoryStyle[numAccessoryStyles];
        
        UserAccessoryStyle userAccessoryStyle;
        AccessoryStyle accessoryStyle;
        for (i = 0; i < numAccessoryStyles; ++i)
        {
            accessoryStyle = _accessoryStyles[i];
            userAccessoryStyle.name = accessoryStyle.name;
            userAccessoryStyle.id = __ToID(i);
            
            userAccessoryStyle.stages = accessoryStyle.stages;
            
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
            userTalent.rewardType = talent.rewardType;
            userTalent.rewardCount = talent.rewardCount;
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
        Action<UserAccessorySlot.Level?> onComplete)
    {
        yield return null;

        var accessorySlot = _accessorySlots[__ToIndex(accessorySlotID)];
        string levelKey = $"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}";
        int levelIndex = PlayerPrefs.GetInt(levelKey);
        if (levelIndex < _accessorySlotLevels.Length)
        {
            var accessorySlotLevel = _accessorySlotLevels[levelIndex];
            string itemCountKey = $"{NAME_SPACE_USER_ITEM_COUNT}{accessorySlotLevel.itemName}";
            int itemCount = PlayerPrefs.GetInt(itemCountKey);
            if (itemCount >= accessorySlotLevel.count)
            {
                PlayerPrefs.SetInt(itemCountKey, itemCount - accessorySlotLevel.count);
                
                PlayerPrefs.SetInt(levelKey, ++levelIndex);

                UserAccessorySlot.Level level;
                if (levelIndex < _accessorySlotLevels.Length)
                {
                    accessorySlotLevel = _accessorySlotLevels[levelIndex];

                    level.name = accessorySlotLevel.name;

                    level.itemID = 0;
                    int numItems = _items.Length;
                    for (int i = 0; i < numItems; ++i)
                    {
                        if (accessorySlotLevel.itemName == _items[i].name)
                        {
                            level.itemID = __ToID(i);

                            break;
                        }
                    }

                    level.count = accessorySlotLevel.count;
                }
                else
                    level = default;

                onComplete(level);
            }
        }

        onComplete(null);
    }

    public IEnumerator UprankAccessory(uint userID, uint accessoryID, Action<bool> onComplete)
    {
        yield return null;

        UserAccessoryStyle.Stage accessoryStyleStage;
        var accessory = _accessories[__ToIndex(accessoryID)];
        string stageKey = $"{NAME_SPACE_USER_ACCESSORY_STAGE}{accessory.name}",
            countKey = $"{NAME_SPACE_USER_ACCESSORY_COUNT}{accessory.name}";
        int count = PlayerPrefs.GetInt(countKey), stageIndex = PlayerPrefs.GetInt(stageKey), numStages;
        foreach (var accessoryStyle in _accessoryStyles)
        {
            if(accessoryStyle.name != accessory.styleName)
                continue;

            numStages = accessoryStyle.stages == null ? 0 : accessoryStyle.stages.Length;
            if (stageIndex < numStages)
            {
                accessoryStyleStage = accessoryStyle.stages[stageIndex];
                if (accessoryStyleStage.count <= count)
                {
                    PlayerPrefs.SetInt(countKey, count - accessoryStyleStage.count);
                    
                    PlayerPrefs.SetInt(stageKey, ++stageIndex);

                    onComplete(true);
                    
                    yield break;
                }
            }
            
            break;
        }
        
        onComplete(false);
    }

    [Serializable]
    internal struct StageReward
    {
        public string name;
        public UserStageReward.Condition condition;
        public int gold;
        public UserStageReward.PoolKey[] poolKeys;
    }

    internal partial struct Stage
    {
        public int energy;
        
        public StageReward[] rewards;
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

            int numStageRewards = stage.rewards.Length;
            result.rewards = new UserStageReward[numStageRewards];

            int i;
            StageReward stageReward;
            UserStageReward userStageReward;
            for (i = 0; i < numStageRewards; ++i)
            {
                stageReward = stage.rewards[i];
                userStageReward.name = stageReward.name;
                userStageReward.id = __ToID(rewardIndex + i);
                userStageReward.flag =
                    (UserStageReward.Flag)PlayerPrefs.GetInt(
                        $"{NAME_SPACE_USER_STAGE_REWARD_FLAG}-{level.name}-{stage.name}-{stageReward.name}");
                userStageReward.condition = stageReward.condition;
                userStageReward.gold = stageReward.gold;

                userStageReward.poolKeys = stageReward.poolKeys;

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
        Action<bool> onComplete)
    {
        yield return null;

        if (!__TryGetStage(stageID, out int stage, out int levelIndex, out _))
        {
            onComplete(false);
            
            yield break;
        }

        int userLevel = UserData.level;
        if (userLevel < levelIndex)
        {
            onComplete(false);
            
            yield break;
        }
        
        var level = _levels[levelIndex];
        if (!__ApplyEnergy(level.stages[stage].energy))
        {
            onComplete(false);

            yield break;
        }
        
        UserData.ApplyStageFlag(level.name, stage);

        UserData.LevelCache levelCache;
        levelCache.name = level.name;
        levelCache.id = __ToID(levelIndex);
        levelCache.stage = stage;
        levelCache.gold = 0;
        UserData.levelCache = levelCache;
        
        onComplete(true);
    }

    private const string NAME_SPACE_USER_STAGE_REWARD_FLAG = "userStageRewardFlag";

    public IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<bool> onComplete)
    {
        yield return null;

        int i, j, 
            gold, 
            poolKeyCount, 
            stageRewardIndex = __ToIndex(stageRewardID), 
            numStages, 
            numStageRewards, 
            numLevels = Mathf.Min(_levels.Length, UserData.level + 1);
        string key;
        Level level;
        Stage stage;
        Dictionary<string, int> poolKeyCounts = null;
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            numStages = level.stages.Length;
            for (j = 0; j < numStages; ++j)
            {
                stage = level.stages[j];

                numStageRewards = stage.rewards.Length;
                if (stageRewardIndex < numStageRewards)
                {
                    gold = PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);
                    poolKeyCounts = new Dictionary<string, int>();
                    if (__ApplyStageRewards(level.name, 
                            j, 
                            stage.rewards[stageRewardIndex], 
                            poolKeyCounts, 
                            ref gold))
                    {
                        PlayerPrefs.SetInt(NAME_SPACE_USER_GOLD, gold);
                        foreach (var pair in poolKeyCounts)
                        {
                            key = $"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{pair.Key}";
                            poolKeyCount = PlayerPrefs.GetInt(key);
                            poolKeyCount += pair.Value;
                            PlayerPrefs.SetInt(key, poolKeyCount);
                        }
                        
                        onComplete(true);

                        yield break;
                    }
                }

                stageRewardIndex -= numStageRewards;
            }
        }
        
        onComplete(false);
    }

    public IEnumerator CollectStageRewards(uint userID, Action<IUserData.StageRewards> onComplete)
    {
        yield return null;

        bool result = false;
        int i, j, k, 
            numStages, 
            numStageRewards, 
            numLevels = Mathf.Min(_levels.Length, UserData.level + 1), 
            gold = 0;
        Level level;
        Stage stage;
        var poolKeyCounts = new Dictionary<string, int>();
        for (i = 0; i < numLevels; ++i)
        {
            level = _levels[i];
            numStages = level.stages.Length;
            for (j = 0; j < numStages; ++j)
            {
                stage = level.stages[j];

                numStageRewards = stage.rewards.Length;
                for(k = 0; k < numStageRewards; ++k)
                    result |= __ApplyStageRewards(level.name, j, stage.rewards[k], poolKeyCounts, ref gold);
            }
        }

        if (result)
        {
            PlayerPrefs.SetInt(NAME_SPACE_USER_GOLD, gold + PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD));

            int index = 0, poolKeyCount;
            string key;
            UserStageReward.PoolKey poolKey;
            var poolKeys = new UserStageReward.PoolKey[poolKeyCounts.Count];
            foreach (var pair in poolKeyCounts)
            {
                poolKey.name = pair.Key;
                poolKey.count = pair.Value;

                key = $"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{poolKey.name}";
                poolKeyCount = PlayerPrefs.GetInt(key);
                poolKeyCount += poolKey.count;
                PlayerPrefs.SetInt(key, poolKeyCount);

                poolKeys[index++] = poolKey;
            }

            IUserData.StageRewards stageRewards;
            stageRewards.gold = gold;
            stageRewards.poolKeys = poolKeys;
            onComplete(stageRewards);
        }
        else
            onComplete(default);
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
        Dictionary<string, int> poolKeyCounts, 
        ref int gold)
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

        //gold = PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);
        gold += stageReward.gold;
        //PlayerPrefs.SetInt(NAME_SPACE_USER_GOLD, gold);
        int numPoolKeys = stageReward.poolKeys.Length, poolKeyCount;
        for (int i = 0; i < numPoolKeys; ++i)
        {
            ref var poolKey = ref stageReward.poolKeys[i];

            //key = $"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{poolKey.name}";
            //poolKeyCount = PlayerPrefs.GetInt(key);
            //poolKeyCount += poolKey.count;
            //PlayerPrefs.SetInt(key, poolKeyCount);

            if (poolKeyCounts != null)
            {
                if (poolKeyCounts.TryGetValue(poolKey.name, out poolKeyCount))
                    poolKeyCount += poolKey.count;
                else
                    poolKeyCount = poolKey.count;

                poolKeyCounts[poolKey.name] = poolKeyCount;
            }
        }

        return true;
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
                rewardIndex += level.stages[stageIndex + j].rewards.Length;
            
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
