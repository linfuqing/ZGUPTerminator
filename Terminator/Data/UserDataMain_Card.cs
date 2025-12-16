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
    private const string NAME_SPACE_USER_CARD_COUNT = "UserCardCount";
    
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
                    __SetCard(cardDefault.name, _cardGroups[j].name, i);
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

            userCard.level = __GetCardLevel(card.name, out _);
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
        result.level = __GetCardLevel(card.name, out _);
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

        string cardName = _cards[__ToIndex(cardID)].name,
            cardGroupName = _cardGroups[__ToIndex(groupID)].name;
        __SetCard(cardName, cardGroupName, position);

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
        int level = __GetCardLevel(card.name, out string levelKey);
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
        flag &= ~Flag.CardsUnlockFirst;
        flag &= ~Flag.CardUpgradeFirst;

        UserDataMain.flag = flag;

        __AppendQuest(UserQuest.Type.CardToUpgrade, 1);
        
        onComplete(true);
    }

    [Serializable]
    internal struct CardBoundLevel
    {
        public string name;

        public string cardBondName;
        
        public UserCardBond.Level value;
        
#if UNITY_EDITOR
        [CSVField]
        public string 卡牌连携等级名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 卡牌连携等级对应连携名
        {
            set
            {
                cardBondName = value;
            }
        }
        
        [CSVField]
        public int 卡牌连携等级卡牌总等级
        {
            set
            {
                this.value.cardLevels = value;
            }
        }
        
        [CSVField]
        public string 卡牌连携等级技能
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    this.value.property.skills = null;
                    
                    return;
                }

                var parameters = value.Split('/');

                int numParameters = parameters.Length;
                string[] skillParameters;
                UserPropertyData.Skill skill;
                this.value.property.skills = new UserPropertyData.Skill[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    skillParameters = parameters[i].Split(':');
                    skill.name = skillParameters[0];
                    skill.type = (UserSkillType)int.Parse(skillParameters[1]);
                    skill.opcode = (UserPropertyData.Opcode)int.Parse(skillParameters[2]);
                    skill.damage = float.Parse(skillParameters[3]);

                    this.value.property.skills[i] = skill;
                }
            }
        }
        
        [CSVField]
        public string 卡牌连携等级属性
        {
            set
            {
                //skillGroupName = value;
                if (string.IsNullOrEmpty(value))
                {
                    this.value.property.attributes = null;
                    
                    return;
                }

                var parameters = value.Split('/');

                int numParameters = parameters.Length;
                string[] attributeParameters;
                UserPropertyData.Attribute attribute;
                this.value.property.attributes = new UserPropertyData.Attribute[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    attributeParameters = parameters[i].Split(':');
                    attribute.type = (UserAttributeType)int.Parse(attributeParameters[0]);
                    attribute.opcode = (UserPropertyData.Opcode)int.Parse(attributeParameters[1]);
                    attribute.value = float.Parse(attributeParameters[2]);

                    this.value.property.attributes[i] = attribute;
                }
            }
        }
#endif
    }

    [Serializable]
    internal struct CardBond
    {
        public string name;

        public string[] cardNames;
        
#if UNITY_EDITOR
        [CSVField]
        public string 卡牌连携名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 卡牌连携卡牌名字
        {
            set
            {
                cardNames = value.Split('/');
            }
        }
#endif
    }

    [Header("CardBonds")] 
    [SerializeField] 
    internal CardBoundLevel[] _cardBondLevels;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_cardBondLevels", guidIndex = -1, nameIndex = 0)] 
    internal string _cardBondLevelsPath;
#endif

    [SerializeField]
    internal CardBond[] _cardBonds;

#if UNITY_EDITOR
    [SerializeField, CSV("_cardBonds", guidIndex = -1, nameIndex = 0)] 
    internal string _cardBondsPath;
#endif
    
    private const string NAME_SPACE_USER_CARDS_BONDS_LEVEL = "UserCardsBondsLevel";

    public IEnumerator QueryCardBonds(uint userID, Action<Memory<UserCardBond>> onComplete)
    {
        yield return __CreateEnumerator();

        int i, j, numCardBondLevels, numCardBondCards, numCardBonds = _cardBonds.Length;
        CardBond cardBond;
        UserCardBond userCardBond;
        UserCardBond.Card userCardBondCard;
        List<int> cardBondLevelIndices;
        var results = new UserCardBond[numCardBonds];
        for (i = 0; i < numCardBonds; ++i)
        {
            cardBond = _cardBonds[i];
            userCardBond.name = cardBond.name;
            userCardBond.id = __ToID(i);
            userCardBond.level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARDS_BONDS_LEVEL}{cardBond.name}");
            
            cardBondLevelIndices = __GetCardBondLevelIndices(userCardBond.name);
            numCardBondLevels = cardBondLevelIndices.Count;
            userCardBond.levels = new UserCardBond.Level[numCardBondLevels];
            for (j = 0; j < numCardBondLevels; ++j)
                userCardBond.levels[j] = _cardBondLevels[cardBondLevelIndices[j]].value;

            numCardBondCards = cardBond.cardNames.Length;
            userCardBond.cards = new UserCardBond.Card[numCardBondCards];
            for (j = 0; j < numCardBondCards; ++j)
            {
                userCardBondCard.name = cardBond.cardNames[j];
                
                userCardBondCard.level = __GetCardLevel(userCardBondCard.name, out _);
                
                userCardBond.cards[j] = userCardBondCard;
            }
            
            results[i] = userCardBond;
        }

        onComplete(results);
    }

    public IEnumerator UpgradeCardBonds(uint userID, uint cardBondID, Action<int?> onComplete)
    {
        yield return __CreateEnumerator();

        var cardBond = _cardBonds[__ToIndex(cardBondID)];
        string key = $"{NAME_SPACE_USER_CARDS_BONDS_LEVEL}{cardBond.name}";
        int level = PlayerPrefs.GetInt(key), 
            cardLevels = _cardBondLevels[__GetCardBondLevelIndices(cardBond.name)[level]].value.cardLevels, totalCardLevels = 0;

        bool result = false;
        foreach (var cardName in cardBond.cardNames)
        {
            totalCardLevels += __GetCardLevel(cardName, out _);
            if (totalCardLevels >= cardLevels)
            {
                result = true;

                break;
            }
        }

        if (result)
        {
            PlayerPrefs.SetInt(key, ++level);

            onComplete(level);
        }
        else
            onComplete(null);
    }
    
    private Dictionary<string, int> __cardGroupNameToIndices;
    
    private int __GetCardGroupIndex(string name)
    {
        if (__cardGroupNameToIndices == null)
        {
            int numCardGroups = _cardGroups.Length;
            __cardGroupNameToIndices = new Dictionary<string, int>(numCardGroups);
            for (int i = 0; i < numCardGroups; ++i)
                __cardGroupNameToIndices.Add(_cardGroups[i].name, i);
        }

        return __cardGroupNameToIndices[name];
    }

    private Dictionary<string, int> __cardNameToIndices;
    
    private int __GetCardIndex(string name)
    {
        if (__cardNameToIndices == null)
        {
            int numCards = _cards.Length;
            __cardNameToIndices = new Dictionary<string, int>(numCards);
            for (int i = 0; i < numCards; ++i)
                __cardNameToIndices.Add(_cards[i].name, i);
        }

        return __cardNameToIndices[name];
    }

    private Dictionary<string, int> __cardStyleNameToIndices;
    
    private int __GetCardStyleIndex(string name)
    {
        if (__cardStyleNameToIndices == null)
        {
            int numCardStyles = _cardStyles.Length;
            __cardStyleNameToIndices = new Dictionary<string, int>(numCardStyles);
            for (int i = 0; i < numCardStyles; ++i)
                __cardStyleNameToIndices.Add(_cardStyles[i].name, i);
        }

        return __cardStyleNameToIndices[name];
    }
    
    private List<int>[] __cardLevelIndices;

    private List<int> __GetCardLevelIndices(int index)
    {
        if (__cardLevelIndices == null)
        {
            int numCardStyles = _cardStyles.Length;
            
            __cardLevelIndices = new List<int>[numCardStyles];

            List<int> cardLevelIndices;
            int cardStyleIndex, numCardLevels = _cardLevels.Length;
            for (int i = 0; i < numCardLevels; ++i)
            {
                cardStyleIndex = __GetCardStyleIndex(_cardLevels[i].styleName);
                cardLevelIndices = __cardLevelIndices[cardStyleIndex];
                if (cardLevelIndices == null)
                {
                    cardLevelIndices = new List<int>();

                    __cardLevelIndices[cardStyleIndex] = cardLevelIndices;
                }
                
                cardLevelIndices.Add(i);
            }
        }
        
        return __cardLevelIndices[index];
    }

    private Dictionary<string, List<int>> __cardBondLevelIndices;

    private List<int> __GetCardBondLevelIndices(string name)
    {
        if (__cardBondLevelIndices == null)
        {
            __cardBondLevelIndices = new Dictionary<string, List<int>>();
            List<int> cardBondLevelIndices;
            string cardBondName;
            int numCardBondLevels = _cardBondLevels.Length;
            for(int i = 0; i < numCardBondLevels; ++i)
            {
                cardBondName = _cardBondLevels[i].cardBondName;
                if (!__cardBondLevelIndices.TryGetValue(cardBondName, out cardBondLevelIndices))
                {
                    cardBondLevelIndices = new List<int>();
                    
                    __cardBondLevelIndices[cardBondName] = cardBondLevelIndices;
                }

                cardBondLevelIndices.Add(i);
            }
        }
        
        return __cardBondLevelIndices[name];
    }

    private static int __GetCardLevel(string name, out string key)
    {
        key = $"{NAME_SPACE_USER_CARD_LEVEL}{name}";
        return PlayerPrefs.GetInt(key, -1);
    }

    private static void __SetCard(string cardName, string cardGroupName, int position)
    {
        string cardKey = $"{NAME_SPACE_USER_CARD_GROUP}{cardGroupName}{UserData.SEPARATOR}{cardName}";
        int oldPosition = PlayerPrefs.GetInt(cardKey, -1);
        if (oldPosition != -1)
            PlayerPrefs.DeleteKey($"{NAME_SPACE_USER_CARD_GROUP}{cardGroupName}{UserData.SEPARATOR}{oldPosition}");
        
        if (position == -1)
            PlayerPrefs.DeleteKey(cardKey);
        else
        {
            string positionKey = $"{NAME_SPACE_USER_CARD_GROUP}{cardGroupName}{UserData.SEPARATOR}{position}",
                oldCardName = PlayerPrefs.GetString(positionKey);
            if (!string.IsNullOrEmpty(oldCardName))
                PlayerPrefs.DeleteKey($"{NAME_SPACE_USER_CARD_GROUP}{cardGroupName}{UserData.SEPARATOR}{oldCardName}");

            PlayerPrefs.SetInt(cardKey, position);
            PlayerPrefs.SetString(positionKey, cardName);
        }
    }

    private void __ApplyCardBonds(ref List<UserPropertyData.Attribute> attributeResults, ref List<UserPropertyData.Skill> skillResults)
    {
        int level;
        foreach (var cardBond in _cardBonds)
        {
            level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARDS_BONDS_LEVEL}{cardBond.name}");
            if(level < 1)
                continue;

            ref var property = ref _cardBondLevels[__GetCardBondLevelIndices(cardBond.name)[level - 1]].value.property;
            if (property.attributes != null && property.attributes.Length > 0)
            {
                if(attributeResults == null)
                    attributeResults = new List<UserPropertyData.Attribute>();
                
                attributeResults.AddRange(property.attributes);
            }

            if (property.skills != null && property.skills.Length > 0)
            {
                if(skillResults == null)
                    skillResults = new List<UserPropertyData.Skill>();

                skillResults.AddRange(property.skills);
            }
        }
    }
}

public partial class UserData
{
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

    public IEnumerator QueryCardBonds(uint userID, Action<Memory<UserCardBond>> onComplete)
    {
        return UserDataMain.instance.QueryCardBonds(userID, onComplete);
    }
    
    public IEnumerator UpgradeCardBonds(uint userID, uint cardBondID, Action<int?> onComplete)
    {
        return UserDataMain.instance.UpgradeCardBonds(userID, cardBondID, onComplete);
    }
}