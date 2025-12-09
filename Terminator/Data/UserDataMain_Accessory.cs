using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
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
    internal struct Accessory
    {
        public string name;

        public string styleName;
        
        [Tooltip("技能，可填空")]
        public string skillName;

        public LayerMaskAndTagsAuthoring spawnerLayerMaskAndTags;
        
        [Tooltip("主技能伤害")]
        public float roleSkillGroupDamage;

        [Tooltip("技能伤害")]
        public float skillDamage;

        [Tooltip("基础属性值")]
        public float attributeValue;
        
        public UserPropertyData property;

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
        public int 装备刷怪圈标签
        {
            set
            {
                spawnerLayerMaskAndTags.layerMask = value;
            }
        }

        [CSVField]
        public float 装备主技能伤害
        {
            set
            {
                roleSkillGroupDamage = value;
            }
        }
        
        [CSVField]
        public float 装备技能伤害
        {
            set
            {
                skillDamage = value;
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

    [Serializable]
    internal struct AccessoryDefault
    {
        public string name;

        public int stage;
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
        
        [Tooltip("下一级技能组伤害加成")]
        public float roleSkillGroupDamage;

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
        
        
        [CSVField]
        public float 装备槽等级下一级主技能伤害
        {
            set
            {
                roleSkillGroupDamage = value;
            }
        }
#endif
    }

    [Serializable]
    internal struct AccessoryStage
    {
        public string name;

        public string accessoryName;

        //public int count;

        /// <summary>
        /// 升阶之后获得的属性
        /// </summary>
        public UserPropertyData property;

        /// <summary>
        /// 升阶需要的装备的品阶
        /// </summary>
        public UserAccessory.StageMaterial[] materials;
        
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
        public string 装备品阶材料
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    materials = null;

                    return;
                }
                
                var parameters = value.Split('/');
                int numParameters = parameters.Length;
                materials = new UserAccessory.StageMaterial[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    materials[i] = new UserAccessory.StageMaterial(parameters[i]);
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

    [Header("Items")]
    [SerializeField] 
    internal ItemDefault[] _itemDefaults;
    [SerializeField, Tooltip("卷轴")] 
    internal Item[] _items;
    
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
    private const string NAME_SPACE_USER_ACCESSORY_IDS = "UserAccessoryIDs";
    private const string NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL = "UserAccessorySlotLevel";
    
    public IEnumerator QueryAccessory(
        uint userID,
        uint accessoryID,
        Action<UserAccessory> onComplete)
    {
        yield return __CreateEnumerator();

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

        result.roleSkillGroupDamage = accessory.roleSkillGroupDamage;
        result.skillDamage = accessory.skillDamage;
        result.attributeValue = accessory.attributeValue;
        result.property = accessory.property;

        var accessoryStageIndices = __GetAccessoryStageIndices(info.index);
        if (info.stage < accessoryStageIndices.Count)
        {
            var accessoryStage = _accessoryStages[accessoryStageIndices[info.stage]];

            result.stageDesc.name = accessoryStage.name;
            //result.stageDesc.count = accessoryStage.count;
            result.stageDesc.property = accessoryStage.property;
            result.stageDesc.materials = accessoryStage.materials;
        }
        else
            result.stageDesc = default;

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
        yield return __CreateEnumerator();

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
            userAccessoryStage.property = accessoryStage.property;
            userAccessoryStage.materials = accessoryStage.materials;
            
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
        yield return __CreateEnumerator();

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

    public IEnumerator UpgradeAccessory(
        uint userID, 
        uint accessorySlotID,
        Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

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

                __AppendQuest(UserQuest.Type.AccessorySlotToUpgrade, 1);

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
        yield return __CreateEnumerator();

        if (!__TryGetAccessory(destinationAccessoryID, out var info))
        {
            onComplete(null);

            yield break;
        }
        
        var stageIndices = __GetAccessoryStageIndices(info.index);
        int numStages = stageIndices.Count;
        if (numStages <= info.stage)
        {
            onComplete(null);

            yield break;
        }

        int numAccessoryIDs = sourceAccessoryIDs.Length;
        var materials = _accessoryStages[stageIndices[info.stage]].materials;
        if((materials == null ? 0 : materials.Length) != numAccessoryIDs)
        {
            onComplete(null);

            yield break;
        }

        bool result;
        int i, index = info.index, stage = info.stage;
        string styleName = _accessories[index].styleName;
        UserAccessory.StageMaterial material;
        var materialIndices = new HashSet<int>();
        foreach (var accessoryID in sourceAccessoryIDs)
        {
            if (!__TryGetAccessory(accessoryID, out info) || 
                //stage != -1 && stage != info.stage || 
                info.stage >= numStages ||
                styleName != null && styleName != _accessories[info.index].styleName)
            {
                onComplete(null);
                
                yield break;
            }

            for (i = 0; i < numAccessoryIDs; ++i)
            {
                material = materials[i];
                result = material.stage == info.stage;
                if (result)
                {
                    switch (material.type)
                    {
                        case UserAccessory.StageMaterialType.Normal:
                            result = info.index == index;
                            break;
                        default:
                            //result = true;
                            break;
                    }
                }

                if (result && materialIndices.Add(i))
                    break;
            }
            
            if(i == numAccessoryIDs)
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
        if (stage < numStages)
        {
            var accessoryStage = _accessoryStages[stageIndices[stage]];

            userAccessoryStage.name = accessoryStage.name;
            //userAccessoryStage.count = accessoryStage.count;
            userAccessoryStage.property = accessoryStage.property;
            userAccessoryStage.materials = accessoryStage.materials;
        }
        else
            userAccessoryStage = default;
        
        __AppendQuest(UserQuest.Type.Accessories, 1);
            
        __AppendQuest(UserQuest.Type.Accessories + stage - 1, 1);
        
        __AppendQuest(UserQuest.Type.AccessoryToUprank, 1);

        onComplete(userAccessoryStage);
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
                for (j = 0; j <= numAccessoryStages; ++j)
                {
                    accessoryInfo.stage = j;
                    
                    key =
                        $"{NAME_SPACE_USER_ACCESSORY_IDS}{name}{UserData.SEPARATOR}{j}";
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

        __accessoryIDToInfos.Remove(id);

        string accessoryName = _accessories[info.index].name, 
            key = $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessoryName}{UserData.SEPARATOR}{info.stage}", 
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
        string accessoryName = _accessories[index].name, 
            key = $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessoryName}{UserData.SEPARATOR}{stage}", 
            idsString = PlayerPrefs.GetString(key);
        
        idsString = string.IsNullOrEmpty(idsString) ? id.ToString() : $"{idsString}{UserData.SEPARATOR}{id}";
        PlayerPrefs.SetString(key, idsString);

        if (__accessoryIDToInfos != null)
        {
            AccessoryInfo accessoryInfo;
            accessoryInfo.index = index;
            accessoryInfo.stage = stage;
            __accessoryIDToInfos.Add(id, accessoryInfo);
        }
    }
}

public partial class UserData
{
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
}