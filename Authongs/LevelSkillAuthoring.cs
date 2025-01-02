using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;
using Random = Unity.Mathematics.Random;

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
                
                int numSkills = authoring._skillAuthoring._skills.Length;
                var skillNameIndices = new Dictionary<string, int>(numSkills);
                for(i = 0; i < numSkills; ++i)
                    skillNameIndices.Add(authoring._skillAuthoring._skills[i].name, i);
                
                int j, k, numFirstSkillIndices, numGroups = authoring._groups.Length;
                BlobBuilderArray<int> firstSkillIndices;
                var groups = builder.Allocate(ref root.groups, numGroups);
                for (i = 0; i < numGroups; ++i)
                {
                    ref var source = ref authoring._groups[i];
                    ref var destination = ref groups[i];

                    destination.weight = source.weight;

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

            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            AddComponent(entity, instance);
            
            AddComponent<LevelSkillVersion>(entity);
            AddComponent<LevelSkill>(entity);
            SetComponentEnabled<LevelSkill>(entity, false);
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

public struct LevelSkillDefinition
{
    private struct SkillWeight
    {
        public int activeIndex;
        public float value;
    }

    private struct GroupSkillWeight : IComparable<GroupSkillWeight>
    {
        public int groupIndex;
        public SkillWeight value;

        public int CompareTo(GroupSkillWeight other)
        {
            return value.value.CompareTo(other.value.value);
        }

    }

    public struct Priority
    {
        public int maxResultCount;
    }

    public struct Group
    {
        public float weight;
        public BlobArray<int> firstSkillIndices;
    }

    public struct SkillGroup
    {
        public int index;

        public float weight;
    }

    public struct Skill
    {
        public int priority;
        public int groupIndex;

        public BlobArray<int> skillGroupIndices;
        public BlobArray<int> nextSkillIndices;
    }

    public int maxActiveCount;

    public BlobArray<Priority> priorities;
    public BlobArray<Group> groups;
    public BlobArray<Skill> skills;
    public BlobArray<SkillGroup> skillGroups;

    public void Select(
        in NativeArray<SkillActiveIndex> activeIndices, 
        ref DynamicBuffer<LevelSkill> results, 
        ref Random random, 
        out int priority)
    {
        priority = 0;
        foreach (var activeIndex in activeIndices)
        {
            ref var skill = ref skills[activeIndex.value];
            priority = math.max(priority, skill.priority);
        }

        int i, j, numGroups, numActiveIndices = activeIndices.Length;
        SkillWeight weight;
        var weights = new UnsafeHashMap<int, SkillWeight>(1, Allocator.Temp);

        weight.value = 0.0f;
        for (i = 0; i < numActiveIndices; ++i)
        {
            j = activeIndices[i].value;
            if(j == -1)
                continue;

            ref var skill = ref skills[j];
            if(__GetSkillIndices(skill.groupIndex, j).Length < 1)
                continue;
                
            weight.activeIndex = i;
            weights.TryAdd(skill.groupIndex, weight);
        }

        bool isFree = numActiveIndices < maxActiveCount;
        int k, index;
        for (i = 0; i < numActiveIndices; ++i)
        {
            j = activeIndices[i].value;
            if (j == -1)
                continue;

            ref var skill = ref skills[j];
            if (skill.priority != priority)
                continue;

            numGroups = skill.skillGroupIndices.Length;
            for (j = 0; j < numGroups; ++j)
            {
                ref var group = ref skillGroups[skill.skillGroupIndices[j]];
                if (!weights.TryGetValue(group.index, out weight))
                {
                    if (isFree)
                    {
                        index = -1;
                        weight.activeIndex = -1;
                        for (k = 0; k < numActiveIndices; ++k)
                        {
                            index = activeIndices[k].value;
                            if(index == -1)
                                continue;
                            
                            if (skills[index].groupIndex == group.index)
                            {
                                weight.activeIndex = k;

                                break;
                            }
                        }
                        
                        if(weight.activeIndex != -1 && 
                           __GetSkillIndices(group.index, index).Length < 1)
                            continue;
                        
                        weight.value = 0.0f;
                    }
                    else
                        continue;
                }

                weight.value += group.weight;
                weights[group.index] = weight;
            }
        }

        if (priority == 0)
        {
            numGroups = groups.Length;
            for (i = 0; i < numGroups; ++i)
            {
                ref var group = ref groups[i];

                if (weights.TryGetValue(i, out weight))
                {
                    if(weight.value > math.FLT_MIN_NORMAL)
                        continue;
                    
                    index = weight.activeIndex == -1 ? -1 : activeIndices[weight.activeIndex].value;
                }
                else if (isFree)
                {
                    index = -1;
                    weight.activeIndex = -1;
                    for (j = 0; j < numActiveIndices; ++j)
                    {
                        index = activeIndices[j].value;
                        if(index == -1)
                            continue;
                            
                        if (skills[index].groupIndex == i)
                        {
                            weight.activeIndex = j;

                            break;
                        }
                    }
                }
                else
                    continue;
                
                if(weight.activeIndex != -1 && 
                   __GetSkillIndices(i, index).Length < 1)
                    continue;
                
                weight.value = group.weight;

                weights[i] = weight;
            }
        }

        int numWeights = weights.Count;
        var groupSkillWeights = new NativeList<GroupSkillWeight>(numWeights, Allocator.Temp);
        float totalWeight = 0.0f;
        GroupSkillWeight groupSkillWeight;
        foreach (var pair in weights)
        {
            groupSkillWeight.groupIndex = pair.Key;

            groupSkillWeight.value = pair.Value;
            if (groupSkillWeight.value.value > math.FLT_MIN_NORMAL &&
                __GetSkillIndices(groupSkillWeight.groupIndex,
                    groupSkillWeight.value.activeIndex == -1 ? -1 : activeIndices[groupSkillWeight.value.activeIndex].value).Length > 0)
            {
                groupSkillWeights.Add(groupSkillWeight);

                totalWeight += groupSkillWeight.value.value;
            }
        }

        numWeights = groupSkillWeights.Length;
        if (numWeights > 0)
        {
            groupSkillWeights.Sort();

            LevelSkill result;
            //result.priority = priority;
            
            int numSkillIndices, numResults = priority < priorities.Length ? priorities[priority].maxResultCount : int.MaxValue;
            if (numResults < numWeights)
            {
                float chance;
                for (i = 0; i < numResults; ++i)
                {
                    chance = random.NextFloat() * totalWeight;
                    for (j = 0; j < numWeights; ++j)
                    {
                        groupSkillWeight = groupSkillWeights[j];
                        if (!weights.ContainsKey(groupSkillWeight.groupIndex))
                            continue;

                        if (groupSkillWeight.value.value < chance)
                            chance -= groupSkillWeight.value.value;
                        else
                        {
                            result.activeIndex = groupSkillWeight.value.activeIndex;
                            result.originIndex =
                                result.activeIndex == -1 ? -1 : activeIndices[result.activeIndex].value;
                            ref var skillIndices = ref __GetSkillIndices(groupSkillWeight.groupIndex,
                                result.originIndex);
                            numSkillIndices = math.min(skillIndices.Length, numResults - i);
                            for (k = 0; k < numSkillIndices; ++k)
                            {
                                result.index = skillIndices[k];

                                results.Add(result);
                            }

                            numResults -= numSkillIndices - 1;
                            if (numResults > i)
                            {
                                weights.Remove(groupSkillWeight.groupIndex);
                                totalWeight -= groupSkillWeight.value.value;
                            }

                            break;
                        }
                    }
                }
            }
            else
            {
                for (i = 0; i < numWeights; ++i)
                {
                    groupSkillWeight = groupSkillWeights[i];
                    result.activeIndex = groupSkillWeight.value.activeIndex;
                    result.originIndex =
                        result.activeIndex == -1 ? -1 : activeIndices[result.activeIndex].value;
                    ref var skillIndices = ref __GetSkillIndices(groupSkillWeight.groupIndex,
                        result.originIndex);
                    numSkillIndices = math.min(skillIndices.Length, numResults);

                    for (j = 0; j < numSkillIndices; ++j)
                    {
                        result.index = skillIndices[j];

                        results.Add(result);
                    }

                    if (numResults > numSkillIndices)
                        numResults -= numSkillIndices;
                    else
                        break;
                }
            }
        }

        groupSkillWeights.Dispose();
        weights.Dispose();
    }

    private ref BlobArray<int> __GetSkillIndices(int groupIndex, int skillIndex)
    {
        if (skillIndex == -1)
            return ref groups[groupIndex].firstSkillIndices;

        return ref skills[skillIndex].nextSkillIndices;
    }
}

public struct LevelSkillDefinitionData : IComponentData
{
    public BlobAssetReference<LevelSkillDefinition> definition;
}

public struct LevelSkillVersion : IComponentData
{
    public int value;
    public int index;
    public int count;
    public int priority;
    public int selection;
    public Entity entity;
}

public struct LevelSkill : IBufferElementData, IEnableableComponent
{
    public int index;
    public int originIndex;
    public int activeIndex;
    //public int priority;

    public void Apply(ref NativeList<SkillActiveIndex> activeIndices)
    {
        if (activeIndex == -1)
        {
            SkillActiveIndex activeIndex;
            activeIndex.value = index;

            activeIndices.Add(activeIndex);
        }
        else
        {
            UnityEngine.Assertions.Assert.AreEqual(originIndex, activeIndices.ElementAt(activeIndex).value);
            
            activeIndices.ElementAt(activeIndex).value = index;
        }
    }

    public void Apply(
        double time, 
        ref DynamicBuffer<SkillActiveIndex> activeIndices, 
        ref DynamicBuffer<BulletStatus> bulletStates,
        ref BulletDefinition bulletDefinition, 
        ref SkillDefinition skillDefinition)
    {
        if (activeIndex == -1)
        {
            SkillActiveIndex activeIndex;
            activeIndex.value = index;

            activeIndices.Add(activeIndex);
        }
        else
        {
            UnityEngine.Assertions.Assert.AreEqual(originIndex, activeIndices.ElementAt(activeIndex).value);

            activeIndices.ElementAt(activeIndex).value = index;
            
            ref var skill = ref skillDefinition.skills[index];
            int bulletIndex, numBullets = skill.bulletIndices.Length;
            for (int i = 0; i < numBullets; ++i)
            {
                bulletIndex = skillDefinition.bullets[skill.bulletIndices[i]].index;
                ref var bulletStatus = ref bulletStates.ElementAt(bulletIndex);
                ref var bullet = ref bulletDefinition.bullets[bulletIndex];
                bulletStatus.cooldown = time + bullet.startTime;
                bulletStatus.count = 0;
                bulletStatus.times = bullet.times;
            }
        }
    }

    public static void Apply(
        double time, 
        in NativeList<int> selectedIndices, 
        in DynamicBuffer<LevelSkill> instances, 
        ref DynamicBuffer<SkillActiveIndex> activeIndices, 
        ref DynamicBuffer<BulletStatus> bulletStates,
        ref BulletDefinition bulletDefinition, 
        ref SkillDefinition skillDefinition, 
        ref NativeList<int> originSkillIndices)
    {
        LevelSkill instance;
        foreach (var selectedIndex in selectedIndices)
        {
            instance = instances[selectedIndex];
            
            if (instance.activeIndex != -1)
                originSkillIndices.Add(activeIndices[instance.activeIndex].value);

            instance.Apply(time, ref activeIndices, ref bulletStates, ref bulletDefinition, ref skillDefinition);
        }

    }
}