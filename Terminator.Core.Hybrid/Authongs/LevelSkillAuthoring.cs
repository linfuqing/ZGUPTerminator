using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using ZG;

#if UNITY_EDITOR
public class LevelSkillAuthoring : MonoBehaviour
{
    [Serializable]
    public struct SkillGroupData : IEquatable<SkillGroupData>
    {
        [Tooltip("技能组名称")]
        public string name;

        [Tooltip("对于技能组的权重增减")]
        public float weight;

        public bool Equals(SkillGroupData other)
        {
            return name == other.name && Mathf.Approximately(weight, other.weight);
        }
    }
    
    [Serializable]
    public struct PriorityData
    {
        [Tooltip("优先级名称")]
        public string name;

        [Tooltip("该优先级最大的选卡数量")]
        public int maxResultCount;
    }

    [Serializable]
    public struct GroupData
    {
        [Tooltip("技能组名称")]
        public string name;

        [Tooltip("该技能组的对应优先级的基础权重（优先级为0），根据已获得的技能的分组权重和所有技能组的的基础权重，取优先级最高的所有技能分组权重进行累加，进而得出对应技能卡出现的概率。")]
        public float weight;
        
        [Tooltip("该技能组的一级技能")]
        public string[] firstSkillNames;
    }
    
    [Serializable]
    public struct SkillData
    {
        [Tooltip("对应技能的名称。")]
        public string name;
        [Tooltip("属于的技能组。")]
        public string groupName;

        [Tooltip("技能优先级。当一次选卡中存在多个优先级时，只取最高的优先级，然后计算相同优先级影响的技能分组权重。主要用来处理技能进阶的情况")]
        public int priority;

        [Tooltip("技能分组权重，选卡开始时，根据已获得的技能的分组权重和所有技能组的的基础权重（基础权重的优先级为0），取优先级最高的所有技能分组权重进行累加，进而得出对应技能卡出现的概率。")]
        public SkillGroupData[] groups;
        
        [Tooltip("下一级技能名称，进阶的时候会有多个。")]
        public string[] nextSkillNames;
        
        #region CSV
        [CSVField]
        public string 关卡技能名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 关卡技能分组
        {
            set
            {
                groupName = value;
            }
        }
        
        [CSVField]
        public int 关卡技能优先级
        {
            set
            {
                priority = value;
            }
        }
        
        [CSVField]
        public string 关卡技能分组权重
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    groups = null;
                    return;
                }

                var parameters = value.Split("/");
                int numParameters = parameters == null ? 0 : parameters.Length, index;

                groups = new SkillGroupData[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    ref var parameter = ref parameters[i];
                    ref var group = ref groups[i];

                    index = parameter.IndexOf(':');
                    if (index == -1)
                    {
                        group.name = parameter;
                        group.weight = 0;
                    }
                    else
                    {
                        group.name = parameter.Remove(index);
                        group.weight = int.Parse(parameter.Substring(index + 1));
                    }
                }
            }
        }
        
        [CSVField]
        public string 关卡下一级技能名称
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    nextSkillNames = null;
                    return;
                }

                nextSkillNames = value.Split("/");
            }
        }
        #endregion
    }
    
    class Baker : Baker<LevelSkillAuthoring>
    {
        public override void Bake(LevelSkillAuthoring authoring)
        {
            if(authoring._skillAuthoring == null)
                authoring._skillAuthoring = GetComponent<SkillAuthoring>();
            if (authoring._skillAuthoring == null)
            {
                Debug.LogError("LevelSkillAuthoring Need a ref of SkillAuthoring to bake!", authoring);
                
                return;
            }
            
            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            
            int numSkills = authoring._skillAuthoring._skills.Length, numGroups = authoring._groups.Length;
            LevelSkillDefinitionData instance;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<LevelSkillDefinition>();

                root.maxActiveCount = authoring._maxActiveCount;

                int i, numPriorities = authoring._priorities.Length;
                var priorities = builder.Allocate(ref root.priorities, numPriorities);
                for (i = 0; i < numPriorities; ++i)
                {
                    ref var source = ref authoring._priorities[i];
                    ref var destination = ref priorities[i];
                    
                    destination.maxResultCount = source.maxResultCount;
                }
                
                var skillNameIndices = new Dictionary<string, int>(numSkills);
                for(i = 0; i < numSkills; ++i)
                    skillNameIndices.Add(authoring._skillAuthoring._skills[i].name, i);
                
                int j, k, numFirstSkillIndices;
                BlobBuilderArray<int> firstSkillIndices;
                var groups = builder.Allocate(ref root.groups, numGroups);
                for (i = 0; i < numGroups; ++i)
                {
                    ref var source = ref authoring._groups[i];
                    ref var destination = ref groups[i];

                    destination.weight = source.weight;
                    //destination.name = source.name;

                    numFirstSkillIndices = source.firstSkillNames == null ? 0 : source.firstSkillNames.Length;
                    firstSkillIndices = builder.Allocate(ref destination.firstSkillIndices, numFirstSkillIndices);
                    for (j = 0; j < numFirstSkillIndices; ++j)
                    {
                        if (!skillNameIndices.TryGetValue(source.firstSkillNames[j], out firstSkillIndices[j]))
                        {
                            firstSkillIndices[j] = -1;
                            Debug.LogError(
                                $"First Skill {source.firstSkillNames[j]} of skill {source.name} can not been found!");
                        }
                    }
                }
                
                var skillGroupList = new List<LevelSkillDefinition.SkillGroup>();
                var skillGroupDataIndices = new Dictionary<SkillGroupData, int>();

                int skillGroupDataIndex;
                LevelSkillDefinition.SkillGroup destinationSkillGroup;
                BlobBuilderArray<int> skillGroupIndices, nextSkillIndices;
                int skillIndex, numSkillGroups, numNextSkillIndices;
                var skills = builder.Allocate(ref root.skills, numSkills);
                for (i = 0; i < numSkills; ++i)
                    skills[i].groupIndex = -1;
                
                foreach (var source in authoring._skills)
                {
                    if (!skillNameIndices.TryGetValue(source.name, out skillIndex))
                        continue;
                    
                    ref var destination = ref skills[skillIndex];

                    destination.priority = source.priority;
                    destination.groupIndex = -1;
                    for (j = 0; j < numGroups; ++j)
                    {
                        if (authoring._groups[j].name == source.groupName)
                        {
                            destination.groupIndex = j;

                            break;
                        }
                    }
                    
                    if (destination.groupIndex == -1)
                        Debug.LogError(
                            $"Level skill group {source.groupName} of skill {source.name} can not been found!");
                    
                    numSkillGroups = source.groups == null ? 0 : source.groups.Length;
                    skillGroupIndices = builder.Allocate(ref destination.skillGroupIndices, numSkillGroups);
                    for (j = 0; j < numSkillGroups; ++j)
                    {
                        ref var sourceGroup = ref source.groups[j];
                        if (!skillGroupDataIndices.TryGetValue(sourceGroup, out skillGroupDataIndex))
                        {
                            destinationSkillGroup.index = -1;
                            for (k = 0; k < numGroups; ++k)
                            {
                                if (authoring._groups[k].name == sourceGroup.name)
                                {
                                    destinationSkillGroup.index = k;

                                    break;
                                }
                            }

                            if (destinationSkillGroup.index == -1)
                            {
                                Debug.LogError(
                                    $"Group {sourceGroup.name} of skill {source.name} can not been found!");

                                skillGroupDataIndex = -1;
                            }
                            else
                            {
                                destinationSkillGroup.weight = sourceGroup.weight;

                                skillGroupDataIndex = skillGroupList.Count;
                                skillGroupDataIndices[sourceGroup] = skillGroupDataIndex;
                                
                                skillGroupList.Add(destinationSkillGroup);
                            }
                        }

                        skillGroupIndices[j] = skillGroupDataIndex;
                    }
                    
                    numNextSkillIndices = source.nextSkillNames == null ? 0 : source.nextSkillNames.Length;
                    nextSkillIndices = builder.Allocate(ref destination.nextSkillIndices, numNextSkillIndices);
                    for (j = 0; j < numNextSkillIndices; ++j)
                    {
                        if (!skillNameIndices.TryGetValue(source.nextSkillNames[j], out nextSkillIndices[j]))
                        {
                            nextSkillIndices[j] = -1;
                            
                            Debug.LogError(
                                $"First Skill {source.nextSkillNames[j]} of skill {source.name} can not been found!");
                        }
                    }
                }

                numSkillGroups = skillGroupList.Count;
                var skillGroups = builder.Allocate(ref root.skillGroups, numSkillGroups);
                for (i = 0; i < numSkillGroups; ++i)
                    skillGroups[i] = skillGroupList[i];

                instance.definition = builder.CreateBlobAssetReference<LevelSkillDefinition>(Allocator.Persistent);
            }

            AddBlobAsset(ref instance.definition, out _);
            AddComponent(entity, instance);

            LevelSkillNameDefinitionData name;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<LevelSkillNameDefinition>();
                var skills = builder.Allocate(ref root.skills, numSkills);
                for (int i = 0; i < numSkills; ++i)
                    skills[i] = authoring._skillAuthoring._skills[i].name;
                
                var groups = builder.Allocate(ref root.groups, numGroups);
                for (int i = 0; i < numGroups; ++i)
                    groups[i] = authoring._groups[i].name;

                name.definition = builder.CreateBlobAssetReference<LevelSkillNameDefinition>(Allocator.Persistent);
            }
            
            AddBlobAsset(ref name.definition, out _);
            AddComponent(entity, name);
            
            AddComponent<LevelSkillVersion>(entity);
            AddComponent<LevelSkill>(entity);
            SetComponentEnabled<LevelSkill>(entity, false);
            
            AddComponent<LevelSkillGroup>(entity);
        }
    }

    [Tooltip("最大能同时激活的技能数。")]
    [SerializeField]
    internal int _maxActiveCount;
    
    [SerializeField] 
    internal PriorityData[] _priorities;

    [SerializeField] 
    internal GroupData[] _groups;

    [SerializeField] 
    internal SkillData[] _skills;

    #region CSV
    [SerializeField]
    [CSV("_skills", guidIndex = -1, nameIndex = 0)]
    internal string _skillsPath;
    #endregion

    [SerializeField] 
    internal SkillAuthoring _skillAuthoring;
}
#endif