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

        public string prerequisiteName;

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
        public string 技能前置名字
        {
            set
            {
                prerequisiteName = value;
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

    private Dictionary<string, int> __skillNameIndices;
    
    private int __GetSkillNameIndex(string skillName)
    {
        if (__skillNameIndices == null)
        {
            __skillNameIndices = new Dictionary<string, int>();

            int numSkills = _skills.Length;
            for(int i = 0; i < numSkills; ++i)
                __skillNameIndices.Add(_skills[i].name, i);
        }

        return __skillNameIndices.TryGetValue(skillName, out int index) ? index : -1;
    }


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

    private Dictionary<string, int> __skillGroupIndices;

    private int __GetSkillGroupIndex(string skillName)
    {
        if (__skillGroupIndices == null)
            __skillGroupIndices = new Dictionary<string, int>();

        if (__skillGroupIndices.TryGetValue(skillName, out int index))
            return index;
        
        ref readonly var skill = ref _skills[__GetSkillNameIndex(skillName)];

        index = string.IsNullOrEmpty(skill.prerequisiteName) ? 0 : __GetSkillGroupIndex(skill.prerequisiteName) + 1;
        __skillGroupIndices[skillName] = index;

        return index;
    }

    private string __GetSkillName(int skillGroupIndex, string skillGroupName, params string[] maskSkillNames)
    {
        bool isContains;
        string prerequisiteName;
        var skillNames = __GetSkillGroupSkillNames(skillGroupName);
        foreach (var skillName in skillNames)
        {
            if(__GetSkillGroupIndex(skillName) != skillGroupIndex)
                continue;

            isContains = false;
            prerequisiteName = skillName;
            do
            {
                if (Array.IndexOf(maskSkillNames, prerequisiteName) != -1)
                {
                    isContains = true;
                    break;
                }

                prerequisiteName = _skills[__GetSkillNameIndex(skillName)].prerequisiteName;
            } while (!string.IsNullOrEmpty(prerequisiteName));

            if(isContains)
                continue;

            return skillName;
        }

        return null;
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
        public int groupIndex;

        public bool Equals(SkillInfo other)
        {
            return belongTo == other.belongTo && index == other.index && groupIndex == other.groupIndex;
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

            string skillName;
            int i, j, numSkillNames, numCards = _cards.Length;
            for (i = 0; i < numCards; ++i)
            {
                ref var card = ref _cards[i];
                
                info.index = i;

                skillNames = __GetSkillGroupSkillNames(__GetSkillGroupName(card.skillName));
                numSkillNames = skillNames.Count;
                for (j = 0; j < numSkillNames; ++j)
                {
                    skillName = skillNames[j];
                    
                    info.groupIndex = __GetSkillGroupIndex(skillName);
                    __skillNameToInfos.Add(skillName, info);
                }
                
                __CollectSkillNameToInfo(SkillInfo.BelongTo.Card,
                    i, card.property.skills);
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
                        numSkillNames = skillNames.Count;
                        for (j = 0; j < numSkillNames; ++j)
                        {
                            skillName = skillNames[j];

                            info.groupIndex = __GetSkillGroupIndex(skillName);
                            __skillNameToInfos.Add(skillName, info);
                        }
                    }
                }
            }
            
            int numRoleRanks = _roleRanks.Length;
            for (i = 0; i < numRoleRanks; ++i)
            {
                ref var roleRank = ref _roleRanks[i];
                __CollectSkillNameToInfo(SkillInfo.BelongTo.Role,
                    __GetRoleIndex(roleRank.roleName), roleRank.property.skills);
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
                    {
                        info.groupIndex = -1;
                        __skillNameToInfos.Add(accessory.skillName, info);
                    }
                    else
                    {
                        skillNames = __GetSkillGroupSkillNames(skillGroupName);
                        numSkillNames = skillNames.Count;
                        for (j = 0; j < numSkillNames; ++j)
                        {
                            skillName = skillNames[j];

                            info.groupIndex = __GetSkillGroupIndex(skillName);
                            __skillNameToInfos.Add(skillName, info);
                        }
                    }
                }
                
                __CollectSkillNameToInfo(SkillInfo.BelongTo.Accessory,
                    i, accessory.property.skills);
            }

            int numAccessoryStages = _accessoryStages.Length;
            for (i = 0; i < numAccessoryStages; ++i)
            {
                ref var accessoryStage = ref _accessoryStages[i];
                __CollectSkillNameToInfo(SkillInfo.BelongTo.Accessory,
                    __GetAccessoryIndex(accessoryStage.accessoryName), accessoryStage.property.skills);
            }
        }

        return __skillNameToInfos.TryGetValue(name, out info);
    }

    private void __CollectSkillNameToInfo(SkillInfo.BelongTo belongTo, int index, UserPropertyData.Skill[] skills)
    {
        if (skills == null || skills.Length < 1)
            return;
            
        SkillInfo info;
        info.belongTo = belongTo;
        info.index = index;
        
        int i, numSkillNames;
        string skillName, skillGroupName;
        List<string> skillNames;
        foreach (var skill in skills)
        {
            if (string.IsNullOrEmpty(skill.name) || __skillNameToInfos.ContainsKey(skill.name))
                continue;

            info.groupIndex = -1;
            if(UserSkillType.Group != skill.type)
            {
                skillGroupName = __GetSkillGroupName(skill.name);
                if(!string.IsNullOrEmpty(skillGroupName))
                {
                    skillNames = __GetSkillGroupSkillNames(skillGroupName);
                    numSkillNames = skillNames.Count;
                    for (i = 0; i < numSkillNames; ++i)
                    {
                        skillName = skillNames[i];

                        info.groupIndex = __GetSkillGroupIndex(skillName);
                        __skillNameToInfos.Add(skillName, info);
                    }
                }
            }
                    
            if(info.groupIndex == -1)
                __skillNameToInfos.Add(skill.name, info);
        }
    }
}
