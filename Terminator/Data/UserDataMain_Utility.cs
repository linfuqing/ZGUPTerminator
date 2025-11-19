using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public partial class UserDataMain
{
    private Dictionary<string, string> __skillToGroupNames;

    private string __GetSkillGroupName(string skillName)
    {
        if (__skillToGroupNames == null)
        {
            __skillToGroupNames = new Dictionary<string, string>();

            foreach (var skill in _skills)
                __skillToGroupNames.Add(skill.name, skill.group);
        }

        return __skillToGroupNames.TryGetValue(skillName, out string skillGroupName) ? skillGroupName : null;
    }

    private bool __ApplyReward(in UserRewardData reward, List<UserReward> outRewards = null)
    {
        var flag = UserDataMain.flag;
        int count = 0;
        uint id = 0;
        string key;
        switch (reward.type)
        {
            case UserRewardType.PurchasePoolKey:
                int purchasePoolIndex = __GetPurchasePoolIndex(reward.name);
                id = __ToID(purchasePoolIndex);
                key = $"{NAME_SPACE_USER_PURCHASE_POOL_KEY}{reward.name}";

                if ((_purchasePools[purchasePoolIndex].flag & PurchasePool.Flag.Hide) == PurchasePool.Flag.Hide)
                {
                    PlayerPrefs.SetInt(key, reward.count);
                    
                    return __PurchasePool(purchasePoolIndex, reward.count, out _, outRewards);
                }

                if ((flag & Flag.PurchasesUnlock) == 0 && UserData.chapter > 0)
                    UserDataMain.flag |= Flag.PurchasesUnlock;

                break;
            case UserRewardType.CardsCapacity:
                if ((flag & Flag.CardUnlock) == 0)
                {
                    flag &= ~Flag.CardUpgradeFirst;
                    
                    UserDataMain.flag = flag | Flag.CardUnlock;
                }

                id = 1;
                count = 3;
                key = NAME_SPACE_USER_CARDS_CAPACITY;
                break;
            case UserRewardType.Card:
                __AppendQuest(UserQuest.Type.Cards, reward.count);

                int cardIndex = __GetCardIndex(reward.name), 
                    cardStyleIndex = __GetCardStyleIndex(_cards[cardIndex].styleName);
                if(cardStyleIndex > 0)
                    __AppendQuest(UserQuest.Type.Cards + cardStyleIndex, reward.count);

                bool isDirty = false;
                if ((flag & Flag.CardsUnlock) == 0 /* && UserData.level > 0*/) //(flag & Flag.CardsCreated) == 0)
                {
                    flag |= Flag.CardsUnlock;

                    isDirty = true;
                }

                /*if ((flag & (Flag.CardUnlock | Flag.CardUpgrade)) == 0)
                {
                    flag |= Flag.CardUpgrade;

                    isDirty = true;
                }*/

                id = __ToID(cardIndex);
                key = $"{NAME_SPACE_USER_CARD_COUNT}{reward.name}";
                
                int cardCount = PlayerPrefs.GetInt(key) + reward.count;

                int level = __GetCardLevel(reward.name, out string levelKey);
                if (level == -1)
                {
                    if ((flag & Flag.CardReplace) == 0 && UserData.chapter > 0)
                    {
                        int capacity = PlayerPrefs.GetInt(NAME_SPACE_USER_CARDS_CAPACITY), length = 0;
                        foreach (var card in _cards)
                        {
                            if(__GetCardLevel(card.name, out _) == -1)
                                continue;

                            if (++length >= capacity)
                            {
                                flag |= Flag.CardReplace;

                                isDirty = true;
                                
                                break;
                            }
                        }
                    }
                    
                    if (isDirty)
                        UserDataMain.flag = flag;
                    
                    PlayerPrefs.SetInt(key, cardCount - 1);
                    PlayerPrefs.SetInt(levelKey, 0);

                    key = null;
                    break;
                    //return id;
                }
                
                var levelIndices = __GetCardLevelIndices(__GetCardStyleIndex(_cards[cardIndex].styleName));
                if (levelIndices.Count > level)
                {
                    var cardLevel = _cardLevels[levelIndices[level]];
                    
                    if (cardLevel.count <= cardCount && 
                        //cardLevel.gold <= gold && 
                        (flag & (Flag.CardUnlock | Flag.CardUpgrade)) == 0)
                    {
                        flag |= Flag.CardUpgrade;

                        isDirty = true;
                    }
                }
                
                if (isDirty)
                    UserDataMain.flag = flag;

                break;
            case UserRewardType.Role:
                id = __ToID(__GetRoleIndex(reward.name));
                if (reward.count == 0)
                {
                    key = $"{NAME_SPACE_USER_ROLE_FLAG}{reward.name}";
                    int roleFlag = PlayerPrefs.GetInt(key);
                    if ((roleFlag & (int)UserRole.Flag.Unlocked) == (int)UserRole.Flag.Unlocked)
                        return false;
                    
                    PlayerPrefs.SetInt(key, roleFlag | (int)UserRole.Flag.Unlocked);

                    key = null;
                    break;
                    //return id;
                }
                
                key = $"{NAME_SPACE_USER_ROLE_COUNT}{reward.name}";
                break;
            case UserRewardType.Accessory:
                __AppendQuest(UserQuest.Type.Accessories, 1);
                if(reward.count > 1)
                    __AppendQuest(UserQuest.Type.Accessories + reward.count - 1, 1);
                
                if ((flag & Flag.RolesUnlock) == 0 && UserData.chapter > 0)//(flag & Flag.RolesCreated) == 0)
                    UserDataMain.flag |= Flag.RolesUnlock;
                
                id = (uint)Random.Range(int.MinValue, int.MaxValue);
                __CreateAccessory(id, __GetAccessoryIndex(reward.name), reward.count);

                key = null;
                break;
                //return accessoryID;
            case UserRewardType.Item:
                id = __ToID(__GetItemIndex(reward.name));
                key = $"{NAME_SPACE_USER_ITEM_COUNT}{reward.name}";
                break; 
            case UserRewardType.Diamond:
                diamond += reward.count;
                key = null;
                break;
            case UserRewardType.Gold:
                gold += reward.count;
                key = null;
                break;
            case UserRewardType.Energy:
                __ApplyEnergy(-reward.count);
                key = null;
                break;
            case UserRewardType.EnergyMax:
                id = 1;
                key = NAME_SPACE_USER_ENERGY_MAX;
                break;
            case UserRewardType.ActiveDay:
                __AppendActive(reward.count, ActiveType.Day);
                
                key = null;
                break;
            case UserRewardType.ActiveWeek:
                __AppendActive(reward.count, ActiveType.Week);
                
                key = null;
                break;
            case UserRewardType.Ticket:
                var levelTicket = _levelTickets[__GetLevelTicketIndex(reward.name)];
                
                levelTicket.count += reward.count;
                
                key = null;
                break;
            case UserRewardType.Exp:
                exp += reward.count;
                key = null;
                break;
            case UserRewardType.RoleExp:
                roleExp += reward.count;
                key = null;
                break;
            default:
                return false;
        }

        if (!string.IsNullOrEmpty(key))
        {
            count = PlayerPrefs.GetInt(key, count);
            count += reward.count;
            PlayerPrefs.SetInt(key, count);
        }

        if (outRewards != null)
        {
            UserReward result;
            result.name = reward.name;
            result.type = reward.type;
            result.count = reward.count;
            result.id = id;
            
            outRewards.Add(result);
        }

        return true;
    }

    private List<UserReward> __ApplyRewards(List<UserReward> outRewards)
    {
        foreach (var reward in UserData.Rewards)
            __ApplyReward(reward, outRewards);
        
        UserData.Rewards.Clear();

        return outRewards;
    }

    private List<UserReward> __ApplyRewards(UserRewardData[] rewards, List<UserReward> outRewards = null)
    {
        UserData.Rewards.AddRange(rewards);

        if (outRewards == null)
            outRewards = new List<UserReward>();
        
        return __ApplyRewards(outRewards);
    }

    private List<UserReward> __ApplyRewards(UserRewardOptionData[] options, List<UserReward> outRewards = null)
    {
        UserData.ApplyRewards(options);
        
        if (outRewards == null)
            outRewards = new List<UserReward>();

        return __ApplyRewards(outRewards);
    }

    private List<UserAttributeData> __CollectRoleAttributes(
        string roleName, 
        string groupName, 
        List<UserAttributeData> attributes, 
        out float skillGroupDamage)
    {
        skillGroupDamage = 0.0f;
        foreach (var talent in _talents)
        {
            if(!string.IsNullOrEmpty(talent.roleName) && talent.roleName != roleName)
                continue;
                
            if (((UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}") &
                 UserTalent.Flag.Collected) != UserTalent.Flag.Collected)
                continue;

            skillGroupDamage += talent.skillGroupDamage;

            if (attributes == null)
                attributes = new List<UserAttributeData>();
            
            attributes.Add(talent.attribute);
        }

        if (string.IsNullOrEmpty(groupName))
        {
            groupName = PlayerPrefs.GetString(NAME_SPACE_USER_ROLE_GROUP);
            if(string.IsNullOrEmpty(groupName))
                groupName = _roleGroups[0].name;
        }
        
        int styleIndex, level, 
            numAccessorySlots = _accessorySlots.Length;
        uint accessoryID;
        AccessoryInfo accessoryInfo;
        string keyPrefix = $"{NAME_SPACE_USER_ROLE_GROUP}{groupName}{UserData.SEPARATOR}";
        List<int> indices;
        for (int i = 0; i < numAccessorySlots; ++i)
        {
            ref var accessorySlot = ref _accessorySlots[i];
            accessoryID = (uint)PlayerPrefs.GetInt(
                $"{keyPrefix}{accessorySlot.name}");

            if (!__TryGetAccessory(accessoryID, out accessoryInfo))
                continue;

            ref var accessory = ref _accessories[accessoryInfo.index];

            level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}");
            if (level > 0)
            {
                styleIndex = __GetAccessoryStyleIndex(accessorySlot.styleName);
                indices = __GetAccessoryStyleLevelIndices(styleIndex);

                ref var accessoryLevel = ref _accessoryLevels[indices[level - 1]];

                skillGroupDamage += accessoryLevel.roleSkillGroupDamage;
            }
            else
                skillGroupDamage += accessory.roleSkillGroupDamage;
        }

        return attributes;
    }

    private void __ApplyAttributes(
        List<UserAttributeData> attributes, 
        List<UserPropertyData.Attribute> accessoryStageAttributes)
    {
        accessoryStageAttributes.Sort();

        UserAttributeData attribute;
        int i, numAttributes = attributes.Count;
        foreach (var accessoryStageAttribute in accessoryStageAttributes)
        {
            for (i = 0; i < numAttributes; ++i)
            {
                attribute = attributes[i];
                if (attribute.type == accessoryStageAttribute.type)
                {
                    switch (accessoryStageAttribute.opcode)
                    {
                        case UserPropertyData.Opcode.Add:
                            attribute.value += accessoryStageAttribute.value;
                            break;
                        case UserPropertyData.Opcode.Mul:
                            attribute.value *= accessoryStageAttribute.value;
                            break;
                    }

                    attributes[i] = attribute;

                    break;
                }
            }

            if (i == numAttributes)
            {
                attribute.type = accessoryStageAttribute.type;
                attribute.value = accessoryStageAttribute.value;
                            
                attributes.Add(attribute);
                            
                ++numAttributes;
            }
        }
    }

    private void __ApplySkills(
        string[] roleSkillNames,
        List<IUserData.Skill> skills, 
        List<UserPropertyData.Skill> accessoryStageSkills)
    {
        accessoryStageSkills.Sort();

        string skillGroupName;
        IUserData.Skill skill;
        int i, numSkills = skills.Count;
        bool result;
        foreach (var accessoryStageSkill in accessoryStageSkills)
        {
            for (i = 0; i < numSkills; ++i)
            {
                skill = skills[i];
                switch (accessoryStageSkill.type)
                {
                    case UserSkillType.Individual:
                        result = UserSkillType.Individual == skill.type;
                        if (result)
                            result = string.IsNullOrEmpty(accessoryStageSkill.name)
                                ? Array.IndexOf(roleSkillNames, skill.name) != -1
                                : skill.name == accessoryStageSkill.name;
                        break;
                    case UserSkillType.Group:
                        switch (skill.type)
                        {
                            case UserSkillType.Individual:
                                skillGroupName = __GetSkillGroupName(skill.name);
                                break;
                            case UserSkillType.Group:
                                skillGroupName = skill.name;
                                break;
                            default:
                                skillGroupName = null;
                                break;
                        }

                        result = !string.IsNullOrEmpty(skillGroupName);
                        if (result)
                        {
                            if (string.IsNullOrEmpty(accessoryStageSkill.name))
                            {
                                result = false;
                                foreach (var roleSkillName in roleSkillNames)
                                {
                                    if (__GetSkillGroupName(roleSkillName) == skillGroupName)
                                    {
                                        result = true;

                                        break;
                                    }
                                }
                            }
                            else
                                result = accessoryStageSkill.name == skillGroupName;
                        }
                        
                        break;
                    default:
                        result = false;
                        break;
                }
                
                if (result)
                {
                    switch (accessoryStageSkill.opcode)
                    {
                        case UserPropertyData.Opcode.Add:
                            skill.damage += accessoryStageSkill.damage;
                            break;
                        case UserPropertyData.Opcode.Mul:
                            skill.damage *= accessoryStageSkill.damage;
                            break;
                    }

                    skills[i] = skill;

                    break;
                }
            }

            if (i == numSkills)
            {
                skill.type = accessoryStageSkill.type;
                skill.damage = accessoryStageSkill.damage;
                if (string.IsNullOrEmpty(accessoryStageSkill.name))
                {
                    switch (accessoryStageSkill.type)
                    {
                        case UserSkillType.Individual:
                            foreach (var roleSkillName in roleSkillNames)
                            {
                                skill.name = roleSkillName;
                                skills.Add(skill);
                            }
                            break;
                        case UserSkillType.Group:
                            foreach (var roleSkillName in roleSkillNames)
                            {
                                skill.name = __GetSkillGroupName(roleSkillName);
                                if (string.IsNullOrEmpty(skill.name))
                                    continue;
                                
                                skill.type = UserSkillType.Group;
                                skills.Add(skill);
                            }

                            break;
                    }
                }
                else
                {
                    skill.name = accessoryStageSkill.name;

                    skills.Add(skill);
                }

                ++numSkills;
            }
        }
    }

    private IUserData.Property __ApplyProperty(uint userID)
    {
        IUserData.Property result;
        IUserData.Skill skill;

        string groupName = PlayerPrefs.GetString(NAME_SPACE_USER_ROLE_GROUP);
        if(string.IsNullOrEmpty(groupName))
            groupName = _roleGroups[0].name;
        
        string keyPrefix = $"{NAME_SPACE_USER_ROLE_GROUP}{groupName}";

        string roleName = PlayerPrefs.GetString(keyPrefix);
        int roleIndex = __GetRoleIndex(roleName);
        ref var role = ref _roles[roleIndex];

        var attributes = __CollectRoleAttributes(
            role.name, 
            groupName, 
            new List<UserAttributeData>(), 
            out skill.damage);

        var skills = new List<IUserData.Skill>();
        foreach (var skillName in role.skillNames)
        {
            skill.name = skillName;
            skill.type = UserSkillType.Individual;
            skills.Add(skill);

            skill.name = __GetSkillGroupName(skillName);
            if (!string.IsNullOrEmpty(skill.name))
            {
                skill.type = UserSkillType.Group;
                skills.Add(skill);
            }
        }
        
        List<UserPropertyData.Skill> skillResults = null;
        List<UserPropertyData.Attribute> attributeResults = null;
        UserPropertyData property;
        var roleRankIndices = __GetRoleRankIndices(roleIndex);
        int rank = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_RANK}{role.name}");
        if (rank > 0)
        {
            property = _roleRanks[roleRankIndices[rank - 1]].property;
            
            if (property.attributes != null && property.attributes.Length > 0)
            {
                attributeResults = new List<UserPropertyData.Attribute>();

                attributeResults.AddRange(property.attributes);
            }

            if (property.skills != null && property.skills.Length > 0)
            {
                skillResults = new List<UserPropertyData.Skill>();

                skillResults.AddRange(property.skills);
            }
        }
        
        keyPrefix = $"{keyPrefix}{UserData.SEPARATOR}";

        result.spawnerLayerMaskAndTags = default;
        
        int i, j, styleIndex, level, 
            numAttributes = attributes.Count, 
            numAccessorySlots = _accessorySlots.Length;
        uint accessoryID;
        AccessoryInfo accessoryInfo;
        UserAttributeData attribute;
        List<int> indices;
        for (i = 0; i < numAccessorySlots; ++i)
        {
            ref var accessorySlot = ref _accessorySlots[i];
            accessoryID = (uint)PlayerPrefs.GetInt(
                $"{keyPrefix}{accessorySlot.name}");
            
            if(!__TryGetAccessory(accessoryID, out accessoryInfo))
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
                attributes.Add(attribute);
            }
            
            ref var accessory = ref _accessories[accessoryInfo.index];

            result.spawnerLayerMaskAndTags |= accessory.spawnerLayerMaskAndTags;
            
            level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}");
            if (level > 0)
            {
                indices = __GetAccessoryStyleLevelIndices(styleIndex);

                ref var accessoryLevel = ref _accessoryLevels[indices[level - 1]];
                attribute.value += accessoryLevel.attributeValue;

                skill.damage = accessoryLevel.skillDamage;
            }
            else
            {
                skill.damage = accessory.skillDamage;

                attribute.value += accessory.attributeValue;
            }

            attributes[j] = attribute;

            if (!string.IsNullOrEmpty(accessory.skillName))
            {
                skill.name = accessory.skillName;
                skill.type = UserSkillType.Individual;
                skills.Add(skill);
                
                //++numSkills;
            }
            
            skill.name = __GetSkillGroupName(accessory.skillName);
            if (!string.IsNullOrEmpty(skill.name))
            {
                skill.type = UserSkillType.Group;
                skills.Add(skill);
                
                //++numSkills;
            }

            if (accessoryInfo.stage > 0)
            {
                indices = __GetAccessoryStageIndices(accessoryInfo.index);

                property = _accessoryStages[indices[accessoryInfo.stage - 1]].property;
            }
            else
                property = accessory.property;
            
            if (property.attributes != null && property.attributes.Length > 0)
            {
                if (attributeResults == null)
                    attributeResults = new List<UserPropertyData.Attribute>();

                attributeResults.AddRange(property.attributes);
            }

            if (property.skills != null && property.skills.Length > 0)
            {
                if (skillResults == null)
                    skillResults = new List<UserPropertyData.Skill>();

                skillResults.AddRange(property.skills);
            }
        }
        
        groupName = PlayerPrefs.GetString(NAME_SPACE_USER_CARD_GROUP);
        if(string.IsNullOrEmpty(groupName))
            groupName = _cardGroups[0].name;
        
        keyPrefix = $"{NAME_SPACE_USER_CARD_GROUP}{groupName}{UserData.SEPARATOR}";
        
        int numCards = _cards.Length;
        for (i = 0; i < numCards; ++i)
        {
            ref var card = ref _cards[i];
            if (PlayerPrefs.GetInt($"{keyPrefix}{card.name}", -1) == -1)
                continue;

            level = __GetCardLevel(card.name, out _);
            if (level > 0)
            {
                styleIndex = __GetCardStyleIndex(card.styleName);
                indices = __GetCardLevelIndices(styleIndex);
                skill.damage = _cardLevels[indices[level - 1]].skillGroupDamage;
            }
            else
                skill.damage = card.skillGroupDamage;

            skill.type = UserSkillType.Individual;
            skill.name = card.skillName;
            skills.Add(skill);

            skill.type = UserSkillType.Group;
            skill.name = __GetSkillGroupName(card.skillName);
            skills.Add(skill);
        }

        __ApplyCardBonds(ref attributeResults, ref skillResults);

        if(attributeResults != null)
            __ApplyAttributes(attributes, attributeResults);

        if(skillResults != null)
            __ApplySkills(role.skillNames, skills, skillResults);

        result.name = role.instanceName;
        result.hpMax = role.hpMax;
        result.attributes = attributes.ToArray();
        result.skills = skills.ToArray();

        return result;
    }

    private Dictionary<string, List<string>> __skillGroupSkillNames;
    
    private List<string> __GetSkillGroupSkillNames(string skillGroupName)
    {
        if (__skillGroupSkillNames == null)
        {
            __skillGroupSkillNames = new Dictionary<string, List<string>>();

            List<string> skillNames;
            foreach (var skill in _skills)
            {
                if (!__skillGroupSkillNames.TryGetValue(skill.group, out skillNames))
                {
                    skillNames = new List<string>();

                    __skillGroupSkillNames[skill.group] = skillNames;
                }
                
                skillNames.Add(skill.name);
            }
        }
        
        return __skillGroupSkillNames[skillGroupName];
    }

    private struct SkillInfo : IEquatable<SkillInfo>
    {
        public enum BelongTo
        {
            Card, 
            Role, 
            Accessory
        }

        public BelongTo belongTo;
        public int index;

        public bool Equals(SkillInfo other)
        {
            return belongTo == other.belongTo && index == other.index;
        }
    }

    private Dictionary<string, SkillInfo> __skillNameToInfos;

    private bool __TryGetSkill(string name, out SkillInfo info)
    {
        List<string> skillNames;
        if (__skillNameToInfos == null)
        {
            __skillNameToInfos = new Dictionary<string, SkillInfo>();

            info.belongTo = SkillInfo.BelongTo.Card;
            int i, numCards = _cards.Length;
            for (i = 0; i < numCards; ++i)
            {
                ref var card = ref _cards[i];
                
                info.index = i;

                skillNames = __GetSkillGroupSkillNames(__GetSkillGroupName(card.skillName));

                foreach (var skillName in skillNames)
                    __skillNameToInfos.Add(skillName, info);
            }
            
            info.belongTo = SkillInfo.BelongTo.Role;

            string skillGroupName;
            int numRoles = _roles.Length;
            for (i = 0; i < numRoles; ++i)
            {
                ref var role = ref _roles[i];

                info.index = i;

                foreach (var roleSkillName in role.skillNames)
                {
                    skillGroupName = __GetSkillGroupName(roleSkillName);
                    if (!string.IsNullOrEmpty(skillGroupName))
                        /*__skillNameToInfos.Add(roleSkillName, info);
                    else*/
                    {
                        skillNames = __GetSkillGroupSkillNames(skillGroupName);
                        
                        foreach (var skillName in skillNames)
                            __skillNameToInfos.Add(skillName, info);
                    }
                }
            }
            
            info.belongTo = SkillInfo.BelongTo.Accessory;

            int numAccessories = _accessories.Length;
            for (i = 0; i < numAccessories; ++i)
            {
                ref var accessory = ref _accessories[i];

                info.index = i;

                if (!string.IsNullOrEmpty(accessory.skillName))
                {
                    skillGroupName = __GetSkillGroupName(accessory.skillName);
                    if (string.IsNullOrEmpty(skillGroupName))
                        __skillNameToInfos.Add(accessory.skillName, info);
                    else
                    {
                        skillNames = __GetSkillGroupSkillNames(skillGroupName);
                        foreach (var skillName in skillNames)
                            __skillNameToInfos.Add(skillName, info);
                    }
                }
            }
        }

        return __skillNameToInfos.TryGetValue(name, out info);
    }
    
    private IUserData.Property __ApplyProperty(uint userID, string[] cacheSkills)
    {
        int numCacheSkills = cacheSkills == null ?  0 : cacheSkills.Length;
        if (numCacheSkills < 1)
            return __ApplyProperty(userID);
        
        string cardGroupName = PlayerPrefs.GetString(NAME_SPACE_USER_CARD_GROUP);
        if(string.IsNullOrEmpty(cardGroupName))
            cardGroupName = _cardGroups[0].name;
        
        string cardKeyPrefix = $"{NAME_SPACE_USER_CARD_GROUP}{cardGroupName}{UserData.SEPARATOR}";
        
        string roleGroupName = PlayerPrefs.GetString(NAME_SPACE_USER_ROLE_GROUP);
        if(string.IsNullOrEmpty(roleGroupName))
            roleGroupName = _roleGroups[0].name;

        string roleKeyPrefix = $"{NAME_SPACE_USER_ROLE_GROUP}{roleGroupName}", 
            accessoryKeyPrefix = $"{roleKeyPrefix}{UserData.SEPARATOR}";

        string cacheSkill;
        SkillInfo skillInfo;
        int i, j;
        bool isContains = true;
        {
            Dictionary<string, int> accessoryStyleSlotIndices = null;
            for (i = 0; i < numCacheSkills; ++i)
            {
                cacheSkill = cacheSkills[i];
                if (!__TryGetSkill(cacheSkill, out skillInfo))
                    continue;

                switch (skillInfo.belongTo)
                {
                    case SkillInfo.BelongTo.Card:
                        if (PlayerPrefs.GetInt($"{cardKeyPrefix}{_cards[skillInfo.index].name}", -1) == -1)
                            isContains = false;
                        break;
                    case SkillInfo.BelongTo.Role:
                        if (PlayerPrefs.GetString(roleKeyPrefix) != _roles[skillInfo.index].name)
                            isContains = false;
                        break;
                    case SkillInfo.BelongTo.Accessory:
                        string accessoryStyleName = _accessories[skillInfo.index].styleName;
                        if (accessoryStyleSlotIndices == null ||
                            !accessoryStyleSlotIndices.TryGetValue(accessoryStyleName, out int accessoryStyleSlotCount))
                            accessoryStyleSlotCount = 1;
                        
                        AccessoryInfo accessoryInfo = default;
                        int accessoryID = 0, 
                            accessoryStyleSlotIndex = 0, 
                            numAccessorySlots = _accessorySlots.Length;
                        for (j = 0; j < numAccessorySlots; ++j)
                        {
                            ref var accessorySlot = ref _accessorySlots[j];
                            if(accessorySlot.styleName != accessoryStyleName)
                                continue;
                            
                            accessoryID = PlayerPrefs.GetInt(
                                $"{accessoryKeyPrefix}{accessorySlot.name}");

                            if (!__TryGetAccessory((uint)accessoryID, out accessoryInfo))
                            {
                                accessoryID = 0;
                                
                                continue;
                            }

                            if (accessoryInfo.index == skillInfo.index)
                                break;

                            if (++accessoryStyleSlotIndex < accessoryStyleSlotCount)
                                accessoryID = 0;
                        }

                        if (j == numAccessorySlots && accessoryID != 0)
                        {
                            var skillName = _accessories[accessoryInfo.index].skillName;
                            string cacheSkillGroupName = __GetSkillGroupName(cacheSkill);
                            if (string.IsNullOrEmpty(cacheSkillGroupName))
                                cacheSkills[i] = skillName;
                            else
                            {
                                int skillIndex = __GetSkillGroupSkillNames(cacheSkillGroupName).IndexOf(cacheSkill);
                                
                                cacheSkills[i] = __GetSkillGroupSkillNames(__GetSkillGroupName(skillName))[skillIndex];
                            }

                            if (accessoryStyleSlotIndices == null)
                                accessoryStyleSlotIndices = new Dictionary<string, int>();

                            accessoryStyleSlotIndices[accessoryStyleName] = accessoryStyleSlotCount + 1;
                        }

                        break;
                }
            }
        }
        
        IUserData.Property result;
        if (isContains)
        {
            result = __ApplyProperty(userID);

            string skillGroupName;
            SkillInfo temp;
            int numSkills = result.skills == null ? 0 : result.skills.Length;
            for(i = 0; i < numSkills; ++i)
            {
                ref var skill = ref result.skills[i];
                if(skill.type != UserSkillType.Individual)
                    continue;
                
                if (!__TryGetSkill(skill.name, out skillInfo))
                    continue;

                skillGroupName = __GetSkillGroupName(skill.name);

                for (j = 0; j < numCacheSkills; ++j)
                {
                    cacheSkill = cacheSkills[j];
                    
                    if (!__TryGetSkill(cacheSkill, out temp))
                        continue;
                    
                    if(!temp.Equals(skillInfo))
                        continue;
                    
                    if(__GetSkillGroupName(cacheSkill) != skillGroupName)
                        continue;

                    skill.name = cacheSkill;

                    break;
                }
            }
        }
        else
        {
            result.spawnerLayerMaskAndTags = default;
            
            var skills = new List<IUserData.Skill>();
            var attributes = new List<UserAttributeData>();
            List<UserPropertyData.Attribute> attributeResults = null;
            List<UserPropertyData.Skill> skillResults = null;
            string[] roleSkillNames = null;
            List<int> indices;
            UserPropertyData property;
            IUserData.Skill skill;
            string instanceName = null;
            int level, styleIndex, hpMax = 0;
            for (i = 0; i < numCacheSkills; ++i)
            {
                cacheSkill = cacheSkills[i];

                if (!__TryGetSkill(cacheSkill, out skillInfo))
                    continue;

                skill.type = UserSkillType.Individual;
                skill.name = cacheSkill;

                switch (skillInfo.belongTo)
                {
                    case SkillInfo.BelongTo.Card:
                        ref var card = ref _cards[skillInfo.index];

                        level = __GetCardLevel(card.name, out _);
                        if (level > 0)
                        {
                            styleIndex = __GetCardStyleIndex(card.styleName);
                            indices = __GetCardLevelIndices(styleIndex);
                            skill.damage = _cardLevels[indices[level - 1]].skillGroupDamage;
                        }
                        else
                            skill.damage = card.skillGroupDamage;

                        skills.Add(skill);

                        skill.type = UserSkillType.Group;
                        skill.name = __GetSkillGroupName(card.skillName);
                        skills.Add(skill);
                        break;
                    case SkillInfo.BelongTo.Role:
                        ref var role = ref _roles[skillInfo.index];

                        hpMax = role.hpMax;

                        instanceName = role.instanceName;

                        roleSkillNames = role.skillNames;

                        attributes = __CollectRoleAttributes(
                            role.name,
                            roleGroupName, 
                            attributes,
                            out skill.damage);

                        skills.Add(skill);

                        foreach (var skillName in role.skillNames)
                        {
                            skill.name = __GetSkillGroupName(skillName);
                            if (string.IsNullOrEmpty(skill.name))
                            {
                                skill.name = skillName;
                                skill.type = UserSkillType.Individual;
                            }
                            else
                                skill.type = UserSkillType.Group;

                            skills.Add(skill);
                        }

                        var roleRankIndices = __GetRoleRankIndices(skillInfo.index);
                        int rank = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_RANK}{role.name}");
                        if (rank > 0)
                        {
                            property = _roleRanks[roleRankIndices[rank - 1]].property;
            
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
                        break;
                    case SkillInfo.BelongTo.Accessory:

                        ref var accessory = ref _accessories[skillInfo.index];

                        result.spawnerLayerMaskAndTags |= accessory.spawnerLayerMaskAndTags;

                        level = 0;
                        int numAccessorySlots = _accessorySlots.Length;
                        for (j = 0; j < numAccessorySlots; ++j)
                        {
                            ref var accessorySlot = ref _accessorySlots[j];
                            if (accessorySlot.styleName != accessory.styleName)
                                continue;

                            level = Mathf.Max(level,
                                PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}"));
                        }

                        int numAttributes = attributes.Count,
                            accessoryStyleIndex = __GetAccessoryStyleIndex(accessory.styleName);
                        ref var accessoryStyle = ref _accessoryStyles[accessoryStyleIndex];
                        for (j = 0; j < numAttributes; ++j)
                        {
                            if (attributes[j].type == accessoryStyle.attributeType)
                                break;
                        }

                        UserAttributeData attribute;
                        if (j < numAttributes)
                            attribute = attributes[j];
                        else
                        {
                            ++numAttributes;

                            attribute.type = accessoryStyle.attributeType;
                            attribute.value = 0.0f;
                            attributes.Add(attribute);
                        }

                        if (level > 0)
                        {
                            indices = __GetAccessoryStyleLevelIndices(accessoryStyleIndex);

                            ref var accessoryLevel = ref _accessoryLevels[indices[level - 1]];
                            attribute.value += accessoryLevel.attributeValue;

                            skill.damage = accessoryLevel.skillDamage;
                        }
                        else
                        {
                            attribute.value += accessory.attributeValue;

                            skill.damage = accessory.skillDamage;
                        }

                        attributes[j] = attribute;
                        skills.Add(skill);

                        skill.type = UserSkillType.Group;
                        skill.name = __GetSkillGroupName(cacheSkill);
                        if (!string.IsNullOrEmpty(skill.name))
                            skills.Add(skill);

                        indices = __GetAccessoryStageIndices(skillInfo.index);
                        int numIndices = indices.Count;
                        string userAccessoryIDs;
                        for (j = numIndices; j >= 0; --j)
                        {
                            userAccessoryIDs = PlayerPrefs.GetString(
                                $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessory.name}{UserData.SEPARATOR}{j}");

                            if (string.IsNullOrEmpty(userAccessoryIDs))
                                continue;

                            break;
                        }

                        property = j > 0 ? _accessoryStages[indices[j - 1]].property : accessory.property;
                        if (property.attributes != null && property.attributes.Length > 0)
                        {
                            if (attributeResults == null)
                                attributeResults = new List<UserPropertyData.Attribute>();

                            attributeResults.AddRange(property.attributes);
                        }

                        if (property.skills != null && property.skills.Length > 0)
                        {
                            if (skillResults == null)
                                skillResults = new List<UserPropertyData.Skill>();

                            skillResults.AddRange(property.skills);
                        }

                        break;
                }
            }

            __ApplyCardBonds(ref attributeResults, ref skillResults);
            
            if (attributeResults != null)
                __ApplyAttributes(attributes, attributeResults);

            if (skillResults != null)
                __ApplySkills(roleSkillNames, skills, skillResults);

            result.name = instanceName;
            result.hpMax = hpMax;
            result.attributes = attributes.ToArray();
            result.skills = skills.ToArray();
        }

        return result;
    }

    private System.Collections.IEnumerator __CreateEnumerator()
    {
        return null;
    }
}
