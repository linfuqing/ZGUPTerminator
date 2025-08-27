using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class UserDataMain
{
    [Serializable]
    internal struct Role
    {
        public string name;

        public string instanceName;
        
        [Tooltip("技能")]
        public string[] skillNames;
    }

    [Header("Roles")]
    [SerializeField] 
    internal string[] _roleDefaults;
    [SerializeField, Tooltip("套装")] 
    internal Group[] _roleGroups;
    [SerializeField, Tooltip("角色")] 
    internal Role[] _roles;

    private const string NAME_SPACE_USER_ROLE_COUNT = "UserRoleCount";
    private const string NAME_SPACE_USER_ROLE_GROUP = "UserRoleGroup";
    
    public IEnumerator QueryRoles(
        uint userID,
        Action<IUserData.Roles> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.Roles result;
        result.flag = 0;

        var flag = UserDataMain.flag;
        if ((flag & Flag.RolesUnlockFirst) == Flag.RolesUnlockFirst)
            result.flag |= IUserData.Roles.Flag.UnlockFirst;
        else if ((flag & Flag.RolesUnlock) != 0)
            result.flag |= IUserData.Roles.Flag.Unlock;
        
        /*if((flag & Flag.RoleUnlockFirst) == Flag.RoleUnlockFirst)
            result.flag |= IUserData.Roles.Flag.RoleUnlockFirst;
        else if ((flag & Flag.RoleUnlock) != 0)
            result.flag |= IUserData.Roles.Flag.RoleUnlock;*/
        
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

            userRole.attributes = __CollectRoleAttributes(role.name, groupName, null, out userRole.skillGroupDamage)?.ToArray();

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
            int accessoryIndex;
            UserRewardData reward;
            reward.type = UserRewardType.Accessory;
            foreach (var accessoryDefault in _accessoryDefaults)
            {
                reward.name = accessoryDefault.name;
                reward.count = accessoryDefault.stage;

                id = __ApplyReward(reward);

                accessoryIndex = __GetAccessoryIndex(accessoryDefault.name);
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

            userAccessory.skillDamage = accessory.skillDamage;

            userAccessory.attributeValue = accessory.attributeValue;

            userAccessory.property = accessory.property;
            
            accessoryStageIndices = __GetAccessoryStageIndices(i);

            numAccessoryStages = accessoryStageIndices.Count;
            for (j = 0; j <= numAccessoryStages; ++j)
            {
                key =
                    $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessory.name}{UserData.SEPARATOR}{j}";
                key = PlayerPrefs.GetString(key);
                ids = string.IsNullOrEmpty(key) ? null : key.Split(UserData.SEPARATOR);
                if (ids == null || ids.Length < 1)
                    continue;

                userAccessory.stage = j;

                accessoryStage = j < numAccessoryStages ? _accessoryStages[accessoryStageIndices[j]] : default;

                userAccessory.stageDesc.name = accessoryStage.name;
                //userAccessory.stageDesc.count = accessoryStage.count;
                userAccessory.stageDesc.property = accessoryStage.property;
                userAccessory.stageDesc.materials = accessoryStage.materials;
                
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
        yield return __CreateEnumerator();
        
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

        result.attributes = __CollectRoleAttributes(role.name, null, null, out result.skillGroupDamage)?.ToArray();

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
        yield return __CreateEnumerator();

        PlayerPrefs.SetString(NAME_SPACE_USER_ROLE_GROUP, _roleGroups[__ToIndex(groupID)].name);

        onComplete(true);
    }

    public IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();
        
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
        yield return __CreateEnumerator();

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

        //flag &= ~Flag.RoleUnlockFirst;

        onComplete(userTalents.ToArray());
    }

    public IEnumerator UpgradeRoleTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

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
}

public partial class UserData
{
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

}