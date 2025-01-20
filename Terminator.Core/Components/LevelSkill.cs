using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

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
        //public FixedString32Bytes name;
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
        in NativeArray<int> groupsToFilter, 
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
            if(skill.groupIndex == -1 || 
               groupsToFilter.IsCreated && 
               groupsToFilter.Length > 0 && 
               groupsToFilter.IndexOf(skill.groupIndex) == -1/* || __GetSkillIndices(skill.groupIndex, j).Length < 1*/)
                continue;
                
            weight.activeIndex = i;
            weights.TryAdd(skill.groupIndex, weight);
        }

        bool isFree = weights.Count < maxActiveCount;
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
                           (__GetSkillIndices(group.index, index).Length < 1 || 
                           groupsToFilter.IsCreated && 
                           groupsToFilter.Length > 0 && 
                           groupsToFilter.IndexOf(group.index) == -1))
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
                   (__GetSkillIndices(i, index).Length < 1 || 
                    groupsToFilter.IsCreated && 
                    groupsToFilter.Length > 0 && 
                    groupsToFilter.IndexOf(i) == -1))
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

public struct LevelSkillNameDefinition
{
    public BlobArray<FixedString32Bytes> groups;
    public BlobArray<FixedString32Bytes> skills;
}

public struct LevelSkillDefinitionData : IComponentData
{
    public BlobAssetReference<LevelSkillDefinition> definition;
}

public struct LevelSkillNameDefinitionData : IComponentData
{
    public BlobAssetReference<LevelSkillNameDefinition> definition;
}

public struct LevelSkillVersion : IComponentData
{
    public int value;
    public int index;
    public int count;
    public int priority;
    public int selection;
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

public struct LevelSkillGroup : IBufferElementData
{
    public int value;
}