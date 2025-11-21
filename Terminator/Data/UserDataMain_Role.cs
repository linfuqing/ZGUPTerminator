using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    internal struct Role
    {
        public string name;

        public int hpMax;

        public string instanceName;
        
        [Tooltip("技能")]
        public string[] skillNames;
        
#if UNITY_EDITOR
        [CSVField]
        public string 角色名称
        {
            set => name = value;
        }
        
        [CSVField]
        public int 角色血量最大值
        {
            set => hpMax = value;
        }
        
        [CSVField]
        public string 角色实例名字
        {
            set => instanceName = value;
        }
        
        [CSVField]
        public string 角色技能
        {
            set => skillNames = string.IsNullOrEmpty(value) ? null : value.Split('/');
        }
#endif
    }

    [Serializable]
    internal struct RoleRank
    {
        public string name;
        
        public string roleName;

        [Tooltip("升星需要的数量")]
        public int count;

        [Tooltip("升星之后获得的属性")]
        public UserPropertyData property;
        
#if UNITY_EDITOR
        [CSVField]
        public string 角色星级名称
        {
            set => name = value;
        }
        
        [CSVField]
        public string 角色星级对应角色
        {
            set => roleName = value;
        }
        
        [CSVField]
        public int 角色星级碎片数
        {
            set => count = value;
        }

        [CSVField]
        public string 角色星级属性
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
                UserPropertyData.Attribute attribute;
                property.attributes = new UserPropertyData.Attribute[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    attributeParameters = parameters[i].Split(':');
                    attribute.type = (UserAttributeType)int.Parse(attributeParameters[0]);
                    attribute.opcode = (UserPropertyData.Opcode)int.Parse(attributeParameters[1]);
                    attribute.value = float.Parse(attributeParameters[2]);

                    property.attributes[i] = attribute;
                }
            }
        }
        
        [CSVField]
        public string 角色星级技能
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
                UserPropertyData.Skill skill;
                property.skills = new UserPropertyData.Skill[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    skillParameters = parameters[i].Split(':');
                    skill.name = skillParameters[0];
                    skill.type = (UserSkillType)int.Parse(skillParameters[1]);
                    skill.opcode = (UserPropertyData.Opcode)int.Parse(skillParameters[2]);
                    skill.damage = float.Parse(skillParameters[3]);

                    property.skills[i] = skill;
                }
            }
        }
#endif
    }

    [Header("Roles")]
    [SerializeField] 
    internal string[] _roleDefaults;
    [SerializeField, Tooltip("套装")] 
    internal Group[] _roleGroups;
    [SerializeField, Tooltip("角色")] 
    internal Role[] _roles;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_roles", guidIndex = -1, nameIndex = 0)]
    internal string _rolesPath;
#endif

    [SerializeField, Tooltip("角色星级")]
    internal RoleRank[] _roleRanks;

#if UNITY_EDITOR
    [SerializeField, CSV("_roleRanks", guidIndex = -1, nameIndex = 0)]
    internal string _roleRanksPath;
#endif
    
    private const string NAME_SPACE_USER_ROLE_FLAG = "UserRoleFlag";
    private const string NAME_SPACE_USER_ROLE_COUNT = "UserRoleCount";
    private const string NAME_SPACE_USER_ROLE_RANK = "UserRoleRank";
    private const string NAME_SPACE_USER_ROLE_GROUP = "UserRoleGroup";
    
    private const string NAME_SPACE_USER_ROLE_EXP = "UserRoleExp";

    public static int roleExp
    {
        get => PlayerPrefs.GetInt(NAME_SPACE_USER_ROLE_EXP);

        set
        {
            PlayerPrefs.SetInt(NAME_SPACE_USER_ROLE_EXP, value);
        }
    }

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

        result.exp = roleExp;
        
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
        if (isCreated && _roleDefaults != null)
        {
            UserRewardData reward;
            reward.type = UserRewardType.Role;

            foreach (var roleDefault in _roleDefaults)
            {
                reward.name = roleDefault;
                reward.count = 0;

                __ApplyReward(reward);
                
                for (j = 0; j < numRoleGroups; ++j)
                    PlayerPrefs.SetString(
                        $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[j].name}", roleDefault);
            }
        }

        int numRoles = _roles.Length;
        string skillGroupName;
        UserRole userRole;
        List<uint> userRoleGroupIDs = null;
        List<string> skillNames = null;
        var userRoles = new List<UserRole>();
        for (i = 0; i < numRoles; ++i)
        {
            if(__ToUserRole(groupName, i, out userRole, ref userRoleGroupIDs, ref skillNames))
                userRoles.Add(userRole);
        }
        
        result.roles = userRoles.ToArray();

        int numAccessorySlots = _accessorySlots.Length, k;
        Accessory accessory;
        AccessorySlot accessorySlot;
        List<int> accessoryStageIndices;
        if (isCreated && _accessoryDefaults != null && _accessoryDefaults.Length > 0)
        {
            uint id;
            int accessoryIndex;
            UserRewardData reward;
            reward.type = UserRewardType.Accessory;

            var rewards = new List<UserReward>();
            foreach (var accessoryDefault in _accessoryDefaults)
            {
                reward.name = accessoryDefault.name;
                reward.count = accessoryDefault.stage;

                rewards.Clear();
                __ApplyReward(reward, rewards);

                id = rewards[0].id;

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
        
        List<uint> userRoleGroupIDs = null;
        List<string> skillNames = null;
        if (__ToUserRole(
                PlayerPrefs.GetString(NAME_SPACE_USER_ROLE_GROUP), 
                __ToIndex(roleID), 
                out var userRole, 
                ref userRoleGroupIDs, 
                ref skillNames))
        {
            onComplete(userRole);
            
            yield break;
        }
            
        onComplete(default);
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
        else if(((UserRole.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_FLAG}{roleName}") & UserRole.Flag.Unlocked) == UserRole.Flag.Unlocked)
            PlayerPrefs.SetString(key, roleName);

        onComplete(true);
    }

    public IEnumerator UprankRole(uint userID, uint roleID, Action<UserRole.Rank?> onComplete)
    {
        yield return __CreateEnumerator();

        int roleIndex = __ToIndex(roleID);
        var role = _roles[roleIndex];
        string rankKey = $"{NAME_SPACE_USER_ROLE_RANK}{role.name}";
        var roleRankIndices = __GetRoleRankIndices(roleIndex);
        int numRoleRanks = roleRankIndices == null ? 0 : roleRankIndices.Count, rank = PlayerPrefs.GetInt(rankKey);
        if (numRoleRanks <= rank)
        {
            onComplete(null);

            yield break;
        }

        var roleRank = _roleRanks[rank];
        string countKey = $"{NAME_SPACE_USER_ROLE_COUNT}{role.name}";
        int count = PlayerPrefs.GetInt(countKey);
        if(count < roleRank.count)
        {
            onComplete(null);

            yield break;
        }
        
        PlayerPrefs.SetInt(rankKey, ++rank);
        
        count -= roleRank.count;
        PlayerPrefs.SetInt(countKey, count);

        UserRole.Rank userRoleRank;
        if (rank < numRoleRanks)
        {
            roleRank = _roleRanks[roleRankIndices[rank]];

            userRoleRank.name = roleRank.name;
            userRoleRank.count = roleRank.count;
            userRoleRank.property = roleRank.property;
        }
        else
            userRoleRank = default;
        
        onComplete(userRoleRank);
    }

    public IEnumerator UpgradeRoleTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        var talent = _talents[__ToIndex(talentID)];
        if (string.IsNullOrEmpty(talent.roleName))
        {
            onComplete(false);
            
            yield break;
        }
        int roleRank = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_RANK}{talent.roleName}"), 
            roleCount = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_COUNT}{talent.roleName}");
        if(talent.roleRank > roleRank ||
               talent.roleCount > roleCount)
        {
            onComplete(false);
            
            yield break;
        }
        
        string key = $"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}";
        var flag = (UserTalent.Flag)PlayerPrefs.GetInt(key);
        if ((flag & UserTalent.Flag.Collected) == UserTalent.Flag.Collected)
        {
            onComplete(false);
            
            yield break;
        }

        int gold = UserDataMain.gold, exp = roleExp;
        
        if (talent.gold > gold || talent.exp > exp)
        {
            onComplete(false);
            
            yield break;
        }

        UserDataMain.gold = gold - talent.gold;
        roleExp = exp - talent.exp;

        flag |= UserTalent.Flag.Collected;
        PlayerPrefs.SetInt(key, (int)flag);

        onComplete(true);
    }

    public IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID,
        Action<Memory<UserTalent>> onComplete)
    {
        yield return __CreateEnumerator();

        string roleName = _roles[__ToIndex(roleID)].name;
        int numTalents = _talents.Length;
            //roleRank = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_RANK}{roleName}"), 
            //roleCount = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_COUNT}{roleName}");
        Talent talent;
        UserTalent userTalent;
        var userTalents = new List<UserTalent>();
        for (int i = 0; i < numTalents; ++i)
        {
            talent = _talents[i];
            if(talent.roleName != roleName/* || 
               talent.roleRank != roleRank ||
               talent.roleCount > roleCount*/)
                continue;
            
            userTalent.name = talent.name;
            userTalent.id = __ToID(i);
            userTalent.flag = (UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}");
            userTalent.gold = talent.gold;
            userTalent.exp = talent.exp;
            userTalent.skillGroupDamage = talent.skillGroupDamage;
            userTalent.attribute = talent.attribute;
            userTalents.Add(userTalent);
        }

        //flag &= ~Flag.RoleUnlockFirst;

        onComplete(userTalents.ToArray());
    }

    private Dictionary<string, int> __roleGroupNameToIndices;
    
    private int __GetRoleGroupIndex(string name)
    {
        if (__roleGroupNameToIndices == null)
        {
            int numRoleGroups = _roleGroups.Length;
            __roleGroupNameToIndices = new Dictionary<string, int>(numRoleGroups);
            for (int i = 0; i < numRoleGroups; ++i)
                __roleGroupNameToIndices.Add(_roleGroups[i].name, i);
        }

        return __roleGroupNameToIndices[name];
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
    
    private List<int>[] __roleRankIndices;

    private List<int> __GetRoleRankIndices(int index)
    {
        if (__roleRankIndices == null)
        {
            int numRoles = _roles.Length;
            
            __roleRankIndices = new List<int>[numRoles];

            List<int> roleRankIndices;
            int roleIndex, numRoleRanks = _roleRanks.Length;
            for (int i = 0; i < numRoleRanks; ++i)
            {
                roleIndex = __GetRoleIndex(_roleRanks[i].roleName);
                roleRankIndices = __roleRankIndices[roleIndex];
                if (roleRankIndices == null)
                {
                    roleRankIndices = new List<int>();

                    __roleRankIndices[roleIndex] = roleRankIndices;
                }
                
                roleRankIndices.Add(i);
            }
        }
        
        return __roleRankIndices[index];
    }

    private bool __ToUserRole(
        string groupName, 
        int roleIndex, 
        out UserRole userRole, 
        ref List<uint> userRoleGroupIDs, 
        ref List<string> skillNames)
    {
        var role = _roles[roleIndex];
        userRole.flag = (UserRole.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_FLAG}{role.name}");
        userRole.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_COUNT}{role.name}");
        if (userRole.flag == 0 && userRole.count == 0)
        {
            userRole = default;
            
            return false;
        }

        userRole.id = __ToID(roleIndex);
        userRole.name = role.name;
        userRole.rank = PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_RANK}{role.name}");

        var roleRankIndices = __GetRoleRankIndices(roleIndex);
        if (userRole.rank < (roleRankIndices == null ? 0 : roleRankIndices.Count))
        {
            var roleRank = _roleRanks[roleRankIndices[userRole.rank]];
            userRole.rankDesc.name = roleRank.name;
            userRole.rankDesc.count = roleRank.count;
            userRole.rankDesc.property = roleRank.property;
        }
        else
            userRole.rankDesc = default;
            
        userRole.attributes = __CollectRoleAttributes(role.name, groupName, null, out userRole.skillGroupDamage)?.ToArray();

        if(userRoleGroupIDs == null) 
            userRoleGroupIDs = new List<uint>();
        else
            userRoleGroupIDs.Clear();

        string key;
        int numRoleGroups = _roleGroups.Length;
        for (int i = 0; i < numRoleGroups; ++i)
        {
            key = $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[i].name}";
            if (PlayerPrefs.GetString(key) != role.name)
                continue;

            userRoleGroupIDs.Add(__ToID(i));
        }

        userRole.groupIDs = userRoleGroupIDs.ToArray();

        if (skillNames == null)
            skillNames = new List<string>();
        else
            skillNames.Clear();

        string skillGroupName;
        foreach (var skillName in role.skillNames)
        {
            skillGroupName = __GetSkillGroupName(skillName);
            if (string.IsNullOrEmpty(skillGroupName))
                skillNames.Add(skillName);
            else
                skillNames.AddRange(__GetSkillGroupSkillNames(skillGroupName));
        }
            
        userRole.skillNames = skillNames.ToArray();

        return true;
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

    public IEnumerator QueryRole(
        uint userID,
        uint roleID,
        Action<UserRole> onComplete)
    {
        return UserDataMain.instance.QueryRole(userID, roleID, onComplete);
    }

    public IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete)
    {
        return UserDataMain.instance.SetRole(userID, roleID, groupID, onComplete);
    }

    public IEnumerator SetRoleGroup(uint userID, uint groupID, Action<bool> onComplete)
    {
        return UserDataMain.instance.SetRoleGroup(userID, groupID, onComplete);
    }

    public IEnumerator UprankRole(uint userID, uint roleID, Action<UserRole.Rank?> onComplete)
    {
        return UserDataMain.instance.UprankRole(userID, roleID, onComplete);
    }
    
    public IEnumerator UpgradeRoleTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.UpgradeRoleTalent(userID, talentID, onComplete);
    }
    
    public IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID,
        Action<Memory<UserTalent>> onComplete)
    {
        return UserDataMain.instance.QueryRoleTalents(userID, roleID, onComplete);
    }

}