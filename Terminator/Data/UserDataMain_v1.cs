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

    private const string NAME_SPACE_USER_PURCHASE_POOL_TIMES = "UserPurchasePoolTimes";
    private const string NAME_SPACE_USER_CARDS = "UserCards";
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
        var cardNames = PlayerPrefs.GetString(NAME_SPACE_USER_CARDS)?.Split(UserData.SEPARATOR);
        string purchasePoolName = __GetPurchasePoolNames()[__ToIndex(purchasePoolID)], 
            timeKey = $"{NAME_SPACE_USER_PURCHASE_POOL_TIMES}{purchasePoolName}", 
            key;
        float chance, total;
        int purchasePoolTimes = PlayerPrefs.GetInt(timeKey), level = UserData.level, cardIndex, count;
        for (int i = 0; i < times; ++i)
        {
            cardIndex = -1;
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

                    cardIndex = -1;
                }

                if (cardIndex != -1 || total < chance)
                    continue;

                cardIndex = cardNames == null ? -1 : Array.IndexOf(cardNames, purchasePoolOption.name);
                if (cardIndex == -1)
                {
                    cardIndex = cardNames == null ? 0 : cardNames.Length;
                    Array.Resize(ref cardNames, cardIndex + 1);
                    cardNames[cardIndex] = purchasePoolOption.name;
                }
                
                result.name = purchasePoolOption.name;
                result.id = __ToID(cardIndex);
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
        PlayerPrefs.SetString(NAME_SPACE_USER_CARDS, string.Join(UserData.SEPARATOR, cardNames));

        onComplete(results.ToArray());
    }
    
    [Serializable]
    internal struct Card
    {
        public string name;

        public string styleName;
    }
    
    [Serializable]
    internal struct CardStyle
    {
        public string name;

        public UserCardStyle.Level[] levels;
    }

    [Serializable]
    internal struct CardGroup
    {
        public string name;
    }

    [SerializeField] 
    internal Card[] _cards;
    
    [SerializeField] 
    internal CardStyle[] _cardStyles;
    
    [SerializeField] 
    internal CardGroup[] _cardGroups;
    
    private const string NAME_SPACE_USER_CARDS_FLAG = "UserCardsFlag";
    private const string NAME_SPACE_USER_CARDS_CAPACITY = "UserCardsCapacity";
    private const string NAME_SPACE_USER_CARD_LEVEL = "UserCardLevel";
    private const string NAME_SPACE_USER_CARD_GROUP_POSITION = "UserCardGroupPosition";
    
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
        
        var cardNames = PlayerPrefs.GetString(NAME_SPACE_USER_CARDS)?.Split(UserData.SEPARATOR);
        int numCardNames = cardNames == null ? 0 : cardNames.Length;
        cards.cards = numCardNames > 0 ? new UserCard[numCardNames] : null;

        var userCardGroups = new List<UserCard.Group>();
        string userCardStyleName;
        UserCard userCard;
        UserCard.Group userCardGroup;
        int j, numCardGroup = _cardGroups.Length, numCards = _cards.Length;
        for (i = 0; i < numCardNames; ++i)
        {
            userCard.name = cardNames[i];

            for (j = 0; j < numCards; ++j)
            {
                if (userCard.name == _cards[j].name)
                    break;
            }
            
            userCard.id = __ToID(i);

            userCardStyleName = _cards[j].styleName;
            
            for (j = 0; j < numCardStyles; ++j)
            {
                if (userCardStyleName == _cardStyles[j].name)
                    break;
            }

            userCard.styleID = __ToID(j);
            userCard.level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_LEVEL}{userCard.name}");
            userCard.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_COUNT}{userCard.name}");

            //userCardGroupNames = PlayerPrefs.GetString($"{NAME_SPACE_USER_CARD_GROUP_NAMES}{userCard.name}")
            //    ?.Split(UserData.SEPARATOR);
            
            //numUserCardGroupNames = userCardGroupNames == null ? 0 : userCardGroupNames.Length;
            //userCard.groups = numUserCardGroupNames > 0 ? new UserCard.Group[numUserCardGroupNames] : null;

            userCardGroups.Clear();
            for (j = 0; j < numCardGroup; ++j)
            {
                userCardGroup.position =
                    PlayerPrefs.GetInt(
                        $"{NAME_SPACE_USER_CARD_GROUP_POSITION}{userCard.name}{UserData.SEPARATOR}{_cardGroups[j].name}",
                        -1);
                
                if(userCardGroup.position == -1)
                    continue;

                userCardGroup.groupID = __ToID(j);
                
                userCardGroups.Add(userCardGroup);
            }
            
            userCard.groups = userCardGroups.Count > 0 ? userCardGroups.ToArray() : null;
            
            cards.cards[i] = userCard;
        }
        
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
        PlayerPrefs.SetInt($"{NAME_SPACE_USER_CARD_GROUP_POSITION}{cardName}{UserData.SEPARATOR}{cardGroupName}", position);
        
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
    internal struct RoleGroup
    {
        public string name;
    }

    [SerializeField] 
    internal RoleGroup[] _roleGroups;

    private const string NAME_SPACE_USER_ROLES_FLAG = "UserRolesFlag";
    private const string NAME_SPACE_USER_ROLES = "UserRoles";
    private const string NAME_SPACE_USER_ROLE_GROUP = "UserRoleGroup";
    
    /*public IEnumerator QueryRoles(
        uint userID,
        Action<IUserData.Roles> onComplete)
    {
        yield return null;

        IUserData.Roles roles;
        roles.flag = (IUserData.Roles.Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_ROLES_FLAG);
        
        var roleNames = PlayerPrefs.GetString(NAME_SPACE_USER_ROLES)?.Split(UserData.SEPARATOR);
        int numRoleNames = roleNames == null ? 0 : roleNames.Length;
        roles.roles = numRoleNames > 0 ? new UserRole[numRoleNames] : null;

        var groupIDs = new List<uint>();
        int numGroups = 
        string[] groupNames;
        UserRole userRole;
        for (int i = 0; i < numRoleNames; ++i)
        {
            userRole.name = roleNames[i];
            userRole.id = __ToID(i);
            userRole.hp = 0;
            userRole.attack = 0;
            userRole.defence = 0;

            groupNames = PlayerPrefs.GetString($"{NAME_SPACE_USER_ROLE_GROUP}{userRole.name}")
                ?.Split(UserData.SEPARATOR);
            
            groupIDs.Clear();
            
            
            userRole.groupIDs = 
        }
    }*/

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
