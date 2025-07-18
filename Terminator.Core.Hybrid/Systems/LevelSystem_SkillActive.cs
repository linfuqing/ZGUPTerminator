using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;

public partial class LevelSystemManaged
{
    private struct SkillActive
    {
        private int __instanceID;
        private NativeHashMap<int, int> __indices;

        public SkillActive(in AllocatorManager.AllocatorHandle allocator)
        {
            __instanceID = 0;
            __indices = new NativeHashMap<int, int>(1, allocator);
        }

        public void Dispose()
        {
            __indices.Dispose();
        }

        private static ProfilerMarker __reset = new ProfilerMarker("Reset");
        private static ProfilerMarker __update = new ProfilerMarker("Update");
        private static ProfilerMarker __collect = new ProfilerMarker("Collect");
        private static ProfilerMarker __remove = new ProfilerMarker("Remove");

        public void Update(
            double time,
            ref SkillDefinition definition,
            ref BlobArray<FixedString32Bytes> skillNames, 
            in DynamicBuffer<SkillActiveIndex> activeIndices,
            in DynamicBuffer<SkillStatus> states,
            //in DynamicBuffer<LevelSkillDesc> descs,
            LevelManager manager)
        {
            int instanceID = manager.GetInstanceID();
            if (__instanceID == 0)
                __instanceID = instanceID;

            if (__instanceID != instanceID)
            {
                __instanceID = instanceID;
                using(__reset.Auto())
                using(var keys = __indices.GetKeyArray(Allocator.Temp))
                {
                    foreach (int key in keys)
                        __indices[key] = -1;
                }
            }

            //LevelSkillDesc desc;
            SkillStatus status;
            int i, level, originIndex, index, numActiveIndices = activeIndices.Length;
            bool isComplete;
            using (__update.Auto())
            {
                for (i = 0; i < numActiveIndices; ++i)
                {
                    index = activeIndices[i].value;

                    level = -1;
                    if (__indices.TryGetValue(index, out originIndex))
                    {
                        isComplete = originIndex == i;

                        if (!isComplete)
                        {
                            if (originIndex >= 0 && originIndex < numActiveIndices)
                            {
                                level = __GetLevel(
                                    activeIndices[originIndex].value,
                                    index,
                                    __indices,
                                    ref definition);

                                isComplete = level != -1;
                            }

                            if (!isComplete)
                            {
                                isComplete = __Set(i, index, /*descs, */ref definition, ref skillNames, manager);
                                if (isComplete)
                                    originIndex = __indices[index];
                            }
                        }
                    }
                    else
                    {
                        __indices[index] = -1;

                        //desc = descs[index];
                        //desc.Retain();
                        isComplete = __Set(i, index, /*descs, */ref definition, ref skillNames, manager);
                        if (isComplete)
                            originIndex = __indices[index];
                    }

                    if (isComplete)
                    {
                        if (level == -1)
                            level = __GetLevel(
                                activeIndices[__indices[index]].value,
                                index,
                                __indices,
                                ref definition);

                        ref var skill = ref definition.skills[index];
                        status = states[index];
                        float cooldown = (float)(Math.Max(status.cooldown /* - skill.duration*/, time) - time);
                        manager.SetActiveSkill(
                            originIndex,
                            level,
                            skill.cooldown,
                            skill.cooldown > cooldown ? skill.cooldown - cooldown : skill.cooldown);
                    }
                }
            }

            NativeHashMap<int, int> indicesToRemove = default;
            using (__collect.Auto())
            {
                int value;
                foreach (var pair in __indices)
                {
                    value = pair.Value;
                    index = pair.Key;

                    if (value < numActiveIndices)
                    {
                        if (value != -1 && activeIndices[value].value != index)
                        {
                            if (!indicesToRemove.IsCreated)
                                indicesToRemove = new NativeHashMap<int, int>(1, Allocator.Temp);

                            indicesToRemove.Add(index, -1);
                        }
                    }
                    else
                    {
                        if (!indicesToRemove.IsCreated)
                            indicesToRemove = new NativeHashMap<int, int>(1, Allocator.Temp);

                        indicesToRemove.Add(index, value);
                    }
                }
            }

            if (indicesToRemove.IsCreated)
            {
                using (__remove.Auto())
                {
                    foreach (var pair in indicesToRemove)
                        __Unset(pair.Value, pair.Key, /*descs, */ref definition, ref skillNames, manager);

                    foreach (var pair in indicesToRemove)
                        __indices.Remove(pair.Key);

                    indicesToRemove.Dispose();
                }
            }
        }

        private static ProfilerMarker __unset = new ProfilerMarker("Unset");
        
        private void __Unset(
            int index, 
            int value, 
            //in DynamicBuffer<LevelSkillDesc> descs, 
            ref SkillDefinition definition, 
            ref BlobArray<FixedString32Bytes> skillNames,
            LevelManager manager)
        {
            using(__unset.Auto())
            using (var keys = __indices.GetKeyArray(Allocator.Temp))
            {
                int temp, level;
                foreach (var key in keys)
                {
                    temp = __indices[key];
                    if(temp != index)
                        continue;

                    level = __GetLevel(value, key, default, ref definition);
                    if(level == -1)
                        continue;

                    //descs[key].Release();
                    
                    __indices[key] = -1;
                    
                    manager.UnsetActiveSkill(index, level, skillNames[key]);//, null);
                }
            }
        }
        
        private static ProfilerMarker __set = new ProfilerMarker("Set");

        private bool __Set(
            int index, 
            int value, 
            ref SkillDefinition definition, 
            ref BlobArray<FixedString32Bytes> skillNames, 
            LevelManager manager)
        {
            using(__set.Auto())
            using (var keys = __indices.GetKeyArray(Allocator.Temp))
            {
                bool result = false;//, isCompleted = true;
                int temp, level;
                //FixedString128Bytes skillName;
                //SkillAsset asset;
                foreach (var key in keys)
                {
                    temp = __indices[key];
                    if(temp != -1)
                        continue;

                    level = __GetLevel(value, key, __indices, ref definition);
                    if(level == -1)
                        continue;

                    /*skillName = skillNames[key];
                    if (!SkillManager.TryGetAsset(skillName, out asset))
                    {
                        isCompleted = false;
                        
                        continue;
                    }*/
                    
                    if (level == 0 || manager.HasActiveSkill(index, level - 1))
                    {
                        __indices[key] = index;

                        manager.SetActiveSkill(index, level, skillNames[key]);//skillName, asset/*desc.ToAsset()*/);
                    }

                    result = true;
                }

                return result;// && isCompleted;
            }
        }
        
        private static ProfilerMarker __getLevel = new ProfilerMarker("GetLevel");

        private static void __GetLevel(
            int targetIndex, 
            int index, 
            in NativeHashMap<int, int> indices, 
            ref SkillDefinition definition, 
            ref int level)
        {
            using (__getLevel.Auto())
            {
                if (index < 0 || index >= definition.skills.Length)
                {
                    level = -1;

                    return;
                }

                if (level == 0)
                {
                    if (targetIndex != index)
                        level = -1;

                    return;
                }

                ref var preIndices = ref definition.skills[index].preIndices;
                int numPreIndices = preIndices.Length;
                if (numPreIndices > 0)
                {
                    if (level > 0)
                        --level;

                    int preIndex, temp, result = -1;
                    for (int i = 0; i < numPreIndices; ++i)
                    {
                        preIndex = preIndices[i];
                        if (indices.IsCreated && !indices.ContainsKey(preIndex))
                            continue;

                        temp = level;
                        __GetLevel(targetIndex, preIndex, indices, ref definition, ref temp);
                        if (result == -1)
                            result = temp;
                        else if (temp != -1 && temp < result)
                            result = temp;
                    }

                    level = result == -1 ? -1 : result + 1;

                    return;
                }

                if (targetIndex == index)
                    level = 0;
            }
        }

        private static int __GetLevel(
            int targetIndex,
            int index,
            in NativeHashMap<int, int> indices,
            ref SkillDefinition definition)
        {
            int level = -1;
            
            __GetLevel(targetIndex, index, indices, ref definition, ref level);

            return level;
        }
    }

    private SkillActive __skillActive;
    
    private void __UpdateSkillActive(
        in BlobAssetReference<SkillDefinition> definition, 
        in BlobAssetReference<LevelSkillNameDefinition> nameDefinition, 
        in DynamicBuffer<SkillActiveIndex> activeIndices, 
        in DynamicBuffer<SkillStatus> states, 
        //in DynamicBuffer<LevelSkillDesc> descs, 
        LevelManager manager)
    {
        if(definition.IsCreated)
            __skillActive.Update(
                SystemAPI.Time.ElapsedTime, 
                ref definition.Value, 
                ref nameDefinition.Value.skills, 
                activeIndices, 
                states, 
                /*descs, */manager);
    }
}
