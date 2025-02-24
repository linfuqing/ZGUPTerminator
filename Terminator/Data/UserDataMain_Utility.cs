using System.Collections.Generic;
using UnityEngine;

public partial class UserDataMain
{
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

    
    private Dictionary<string, int> __itemNameToIndices;

    private int __GetItemIndex(string name)
    {
        if (__itemNameToIndices == null)
        {
            int numItems = _items.Length;
            __itemNameToIndices = new Dictionary<string, int>(numItems);
            for (int i = 0; i < numItems; ++i)
                __itemNameToIndices.Add(_items[i].name, i);
        }

        return __itemNameToIndices[name];
    }

    
    private Dictionary<string, int> __roleToIndices;
    
    private int __GetRoleIndex(string name)
    {
        if (__roleToIndices == null)
        {
            int numRoles = _roles.Length;
            __roleToIndices = new Dictionary<string, int>(numRoles);
            for (int i = 0; i < numRoles; ++i)
                __roleToIndices.Add(_roles[i].name, i);
        }

        return __roleToIndices[name];
    }

    private Dictionary<string, int> __accessoryNameToIndices;

    private int __GetAccessoryIndex(string name)
    {
        if (__accessoryNameToIndices == null)
        {
            int numAccessories = _accessories.Length;
            __accessoryNameToIndices = new Dictionary<string, int>(numAccessories);
            for (int i = 0; i < numAccessories; ++i)
                __accessoryNameToIndices.Add(_accessories[i].name, i);
        }

        return __accessoryNameToIndices[name];
    }
    
    private Dictionary<string, int> __accessoryStyleNameToIndices;
    
    private int __GetAccessoryStyleIndex(string name)
    {
        if (__accessoryStyleNameToIndices == null)
        {
            int numAccessoryStyles = _accessoryStyles.Length;
            __accessoryStyleNameToIndices = new Dictionary<string, int>(numAccessoryStyles);
            for (int i = 0; i < numAccessoryStyles; ++i)
                __accessoryStyleNameToIndices.Add(_accessoryStyles[i].name, i);
        }

        return __accessoryStyleNameToIndices[name];
    }
    
    private List<int>[] __accessoryStageIndices;

    private List<int> __GetAccessoryStageIndices(int index)
    {
        if (__accessoryStageIndices == null)
        {
            int numAccessories = _accessories.Length;
            
            __accessoryStageIndices = new List<int>[numAccessories];

            List<int> accessoryStageIndices;
            int accessoryIndex, numAccessoryStages = _accessoryStages.Length;
            for (int i = 0; i < numAccessoryStages; ++i)
            {
                accessoryIndex = __GetAccessoryIndex(_accessoryStages[i].accessoryName);
                accessoryStageIndices = __accessoryStageIndices[accessoryIndex];
                if (accessoryStageIndices == null)
                {
                    accessoryStageIndices = new List<int>();

                    __accessoryStageIndices[accessoryIndex] = accessoryStageIndices;
                }
                
                accessoryStageIndices.Add(i);
            }
        }
        
        return __accessoryStageIndices[index];
    }

    
    private List<int>[] __accessoryStyleLevelIndices;
    
    private List<int> __GetAccessoryStyleLevelIndices(int styleIndex)
    {
        if (__accessoryStyleLevelIndices == null)
        {
            int numAccessoryStyles = _accessoryStyles.Length;
            __accessoryStyleLevelIndices = new List<int>[numAccessoryStyles];
            
            int numAccessoryLevels = _accessoryLevels.Length, accessoryStyleIndex;
            List<int> accessoryLevelIndices;
            for (int i = 0; i < numAccessoryLevels; ++i)
            {
                accessoryStyleIndex = __GetAccessoryStyleIndex(_accessoryLevels[i].styleName);
                
                accessoryLevelIndices = __accessoryStyleLevelIndices[accessoryStyleIndex];
                if (accessoryLevelIndices == null)
                {
                    accessoryLevelIndices = new List<int>();
                    
                    __accessoryStyleLevelIndices[accessoryStyleIndex] = accessoryLevelIndices;
                }
                
                accessoryLevelIndices.Add(i);
            }
        }

        return __accessoryStyleLevelIndices[styleIndex];
    }
    
    private struct AccessoryInfo
    {
        public int index;
        public int stage;
    }
    
    private Dictionary<uint, AccessoryInfo> __accessoryIDToInfos;

    private bool __TryGetAccessory(uint id, out AccessoryInfo info)
    {
        if (__accessoryIDToInfos == null)
        {
            __accessoryIDToInfos = new Dictionary<uint, AccessoryInfo>();

            int i,
                j,
                numAccessoryStages,
                numAccessories = _accessories.Length;
            AccessoryInfo accessoryInfo;
            string name, key;
            string[] ids;
            List<int> accessoryStageIndices;
            for (i = 0; i < numAccessories; ++i)
            {
                name = _accessories[i].name;
                
                accessoryInfo.index = i;

                accessoryStageIndices = __GetAccessoryStageIndices(i);
                numAccessoryStages = accessoryStageIndices.Count;
                for (j = 0; j < numAccessoryStages; ++j)
                {
                    accessoryInfo.stage = j;
                    
                    key =
                        $"{NAME_SPACE_USER_ACCESSORY_IDS}{name}{UserData.SEPARATOR}{_accessoryStages[accessoryStageIndices[j]].name}";
                    key = PlayerPrefs.GetString(key);
                    ids = string.IsNullOrEmpty(key) ? null : key.Split(UserData.SEPARATOR);
                    if (ids == null || ids.Length < 1)
                        continue;

                    foreach (var idString in ids)
                        __accessoryIDToInfos.Add(uint.Parse(idString), accessoryInfo);
                }
            }
        }

        return __accessoryIDToInfos.TryGetValue(id, out info);
    }

    private bool __DeleteAccessory(uint id)
    {
        if(!__TryGetAccessory(id, out AccessoryInfo info))
            return false;

        int accessoryStageIndex = __GetAccessoryStageIndices(info.index)[info.stage];
        string accessoryStageName = _accessoryStages[accessoryStageIndex].name, 
            accessoryName = _accessories[info.index].name, 
            key = $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessoryName}{UserData.SEPARATOR}{accessoryStageName}", 
            idsString = PlayerPrefs.GetString(key);

        if (string.IsNullOrEmpty(idsString))
            return false;
        
        var ids = new HashSet<string>(idsString.Split(UserData.SEPARATOR));
        if (!ids.Remove(id.ToString()))
            return false;

        int numIDs = ids.Count;
        if (numIDs > 0)
        {
            idsString = string.Join(UserData.SEPARATOR, ids);
            PlayerPrefs.SetString(key, idsString);
        }
        else
            PlayerPrefs.DeleteKey(key);

        return true;
    }

    private void __CreateAccessory(uint id, int index, int stage)
    {
        int accessoryStageIndex = __GetAccessoryStageIndices(index)[stage];
        string accessoryStageName = _accessoryStages[accessoryStageIndex].name, 
            accessoryName = _accessories[index].name, 
            key = $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessoryName}{UserData.SEPARATOR}{accessoryStageName}", 
            idsString = PlayerPrefs.GetString(key);
        
        idsString = string.IsNullOrEmpty(idsString) ? id.ToString() : $"{idsString}{UserData.SEPARATOR}{id}";
        PlayerPrefs.SetString(key, idsString);
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
                case UserStageReward.Condition.Normal:
                    if((stageFlag | IUserData.StageFlag.Normal) != IUserData.StageFlag.Normal)
                        flag |= UserStageReward.Flag.Unlock;
                    break;
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
                id = __ToID(__GetCardIndex(reward.name));
                key = $"{NAME_SPACE_USER_CARD_COUNT}{reward.name}";
                
                string levelKey = $"{NAME_SPACE_USER_CARD_LEVEL}{reward.name}";
                int level = PlayerPrefs.GetInt(levelKey, -1);
                if (level == -1)
                {
                    int cardCount = PlayerPrefs.GetInt(key) + reward.count;
                    
                    PlayerPrefs.SetInt(key, cardCount - 1);
                    PlayerPrefs.SetInt(levelKey, 0);

                    return id;
                }

                break;
            case UserRewardType.Role:
                id = __ToID(__GetRoleIndex(reward.name));
                key = $"{NAME_SPACE_USER_ROLE_COUNT}{reward.name}";
                break;
            case UserRewardType.Accessory:
                uint accessoryID = (uint)Random.Range(int.MinValue, int.MaxValue);
                __CreateAccessory(accessoryID, __GetAccessoryIndex(reward.name), reward.count);
                return accessoryID;
            case UserRewardType.Item:
                id = __ToID(__GetItemIndex(reward.name));
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

    private List<UserAttributeData> __CollectRoleAttributes(
        string roleName, 
        out float skillGroupDamage)
    {
        skillGroupDamage = 0.0f;
        List<UserAttributeData> attributes = null;
        
        foreach (var talent in _talents)
        {
            if(talent.roleName != roleName)
                continue;
                
            if (((UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}") &
                 UserTalent.Flag.Collected) != UserTalent.Flag.Collected)
                continue;

            skillGroupDamage += talent.skillGroupDamage;

            if (attributes == null)
                attributes = new List<UserAttributeData>();
            
            attributes.Add(talent.attribute);
        }

        return attributes;
    }

    private IUserData.Property __ApplyProperty(uint userID)
    {
        var skills = new List<IUserData.Skill>();
        IUserData.Skill skill;
        skill.type = UserSkillType.Group;
        
        int i, level, styleIndex, numCards = _cards.Length;
        List<int> indices;
        string groupName = _cardGroups[PlayerPrefs.GetInt(NAME_SPACE_USER_CARD_GROUP)].name, 
         keyPrefix = $"{NAME_SPACE_USER_CARD_GROUP}{groupName}{UserData.SEPARATOR}";
        for (i = 0; i < numCards; ++i)
        {
            ref var card = ref _cards[i];
            if(PlayerPrefs.GetInt($"{keyPrefix}{card.name}") == -1)
                continue;

            skill.name = card.skillGroupName;

            level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_LEVEL}{card.name}");
            if (level > 0)
            {
                styleIndex = __GetCardStyleIndex(card.styleName);
                indices = __GetCardLevelIndices(styleIndex);
                skill.damage = _cardLevels[indices[level - 1]].skillGroupDamage;
            }
            else
                skill.damage = 1.0f;

            skills.Add(skill);
        }

        groupName = _roleGroups[PlayerPrefs.GetInt(NAME_SPACE_USER_ROLE_GROUP)].name;
        keyPrefix = $"{NAME_SPACE_USER_ROLE_GROUP}{groupName}";

        string roleName = PlayerPrefs.GetString(keyPrefix);
        int roleIndex = __GetRoleIndex(roleName);
        ref var role = ref _roles[roleIndex];
        
        var attributes = __CollectRoleAttributes(role.name, out skill.damage);
        if (attributes == null)
            attributes = new List<UserAttributeData>();
        
        skill.damage = 1.0f;
        skill.name = role.skillGroupName;
        skills.Add(skill);

        skill.name = role.skillName;
        skill.type = UserSkillType.Individual;
        skills.Add(skill);
        
        keyPrefix = $"{keyPrefix}{UserData.SEPARATOR}";
        
        int j, 
            numSkills = skills.Count, 
            numAttributes = attributes.Count, 
            numAccessorySlots = _accessorySlots.Length;
        uint accessoryID;
        AccessoryInfo accessoryInfo;
        UserAttributeData attribute;
        string accessoryIDString;
        for (i = 0; i < numAccessorySlots; ++i)
        {
            ref var accessorySlot = ref _accessorySlots[i];
            accessoryIDString = PlayerPrefs.GetString(
                $"{keyPrefix}{accessorySlot.name}");
            
            if(string.IsNullOrEmpty(accessoryIDString) || 
               !uint.TryParse(accessoryIDString, out accessoryID) || 
               !__TryGetAccessory(accessoryID, out accessoryInfo))
                continue;

            styleIndex = __GetAccessoryStyleIndex(accessorySlot.styleName);
            ref var accessoryStyle = ref _accessoryStyles[styleIndex];
            for(j = 0; j < numAttributes ; ++j)
            {
                if (attributes[j].type == accessoryStyle.attributeType)
                    break;
            }

            if (j < numAttributes)
                attribute = attributes[j];
            else
            {
                ++numAttributes;
                
                attribute.type = accessoryStyle.attributeType;
                attribute.value = 0.0f;
            }
            
            level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}");
            if (level > 0)
            {
                indices = __GetAccessoryStyleLevelIndices(styleIndex);
                attribute.value += _accessoryLevels[indices[level - 1]].attributeValue;
            }
            
            ref var accessory = ref _accessories[accessoryInfo.index];
            attribute.value += accessory.attributeValue;
            
            attributes[j] = attribute;

            if (string.IsNullOrEmpty(accessory.skillName))
            {
                ++numSkills;
                
                skill.name = accessory.skillName;
                skill.type = UserSkillType.Individual;
                skill.damage = 1.0f;
                skills.Add(skill);
            }
            
            if (string.IsNullOrEmpty(accessory.skillGroupName))
            {
                ++numSkills;
                
                skill.name = accessory.skillGroupName;
                skill.type = UserSkillType.Group;
                skill.damage = 1.0f;
                skills.Add(skill);
            }

            if (accessoryInfo.stage > 0)
            {
                indices = __GetAccessoryStageIndices(accessoryInfo.index);
                
                ref var accessoryStage = ref _accessoryStages[indices[accessoryInfo.stage - 1]];
            }
        }

        IUserData.Property result;
        result.skills = skills.ToArray();
        result.attributes = attributes.ToArray();

        return result;
    }
}
