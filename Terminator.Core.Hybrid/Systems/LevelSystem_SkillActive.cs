using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

public partial class LevelSystemManaged
{
    private struct SkillActive
    {
        //private NativeHashMap<WeakObjectReference<Sprite>, int> __loadedIndices;
        private int __instanceID;
        private NativeHashMap<int, int> __indices;

        public SkillActive(in AllocatorManager.AllocatorHandle allocator)
        {
            __instanceID = 0;
            //__loadedIndices = new NativeHashMap<WeakObjectReference<Sprite>, int>(1, allocator);
            __indices = new NativeHashMap<int, int>(1, allocator);
        }

        public void Dispose()
        {
            //__loadedIndices.Dispose();
            __indices.Dispose();
        }

        public void Update(
            double time,
            ref SkillDefinition definition,
            in DynamicBuffer<SkillActiveIndex> activeIndices,
            in DynamicBuffer<SkillStatus> states,
            in DynamicBuffer<LevelSkillDesc> descs,
            LevelManager manager)
        {
            int instanceID = manager.GetInstanceID();
            if (__instanceID == 0)
                __instanceID = instanceID;

            if (__instanceID != instanceID)
            {
                __instanceID = instanceID;
                using(var keys = __indices.GetKeyArray(Allocator.Temp))
                {
                    foreach (int key in keys)
                        __indices[key] = -1;
                }
            }

            LevelSkillDesc desc;
            SkillStatus status;
            int i, level, originIndex, index, numActiveIndices = activeIndices.Length;
            bool isComplete;
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
                            isComplete = __Set(i, index, descs, ref definition, manager);
                            if (isComplete)
                                originIndex = __indices[index];
                        }
                        //__indices[index] = i;

                        //manager.SetActiveSkill(i, descs[index].ToAsset(false));
                    }
                }
                else
                {
                    __indices[index] = -1;

                    desc = descs[index];
                    desc.sprite.LoadAsync();
                    desc.icon.LoadAsync();
                    isComplete = __Set(i, index, descs, ref definition, manager);
                    if (isComplete)
                        originIndex = __indices[index];
                    /*switch (icon.LoadingStatus)
                    {
                        case ObjectLoadingStatus.None:
                            break;
                        case ObjectLoadingStatus.Loading:
                        case ObjectLoadingStatus.Queued:
                            //isLoading = true;
                            break;
                        case ObjectLoadingStatus.Error:
                            Debug.LogError($"Skill desc {descs[index].name} can not been found!");
                            break;
                        case ObjectLoadingStatus.Completed:
                            isComplete = true;

                            __indices[index] = i;

                            manager.SetActiveSkill(i, descs[index].ToAsset(false));
                            break;
                    }*/
                }

                if (isComplete)
                {
                    if(level == -1)
                        level = __GetLevel(
                            activeIndices[__indices[index]].value, 
                            index, 
                            __indices, 
                            ref definition);
                    
                    ref var skill = ref definition.skills[index];
                    status = states[index];
                    float cooldown = (float)(Math.Max(status.cooldown - skill.duration, time) - time);
                    manager.SetActiveSkill(
                        originIndex, 
                        level, 
                        skill.cooldown,
                        skill.cooldown > cooldown ? skill.cooldown - cooldown : skill.cooldown);
                }
            }

            bool isRemove;
            int j, value;
            NativeList<int> keysToRemove = default;
            foreach (var pair in __indices)
            {
                value = pair.Value;
                index = pair.Key;
                
                isRemove = value >= numActiveIndices;
                if (!isRemove)
                {
                    for (j = 0; j < numActiveIndices; ++j)
                    {
                        if (activeIndices[j].value == index)
                            break;
                    }

                    isRemove = j == numActiveIndices;
                }

                if (isRemove)
                {
                    if (!keysToRemove.IsCreated)
                        keysToRemove = new NativeList<int>(Allocator.Temp);
                    
                    keysToRemove.Add(index);
                }
            }

            if (keysToRemove.IsCreated)
            {
                foreach (var keyToRemove in keysToRemove)
                {
                    value = __indices[keyToRemove];

                    __indices.Remove(keyToRemove);
                    __Unset(value, keyToRemove, descs, ref definition, manager);
                }

                keysToRemove.Dispose();
            }
        }

        private void __Unset(
            int index, 
            int value, 
            in DynamicBuffer<LevelSkillDesc> descs, 
            ref SkillDefinition definition, 
            LevelManager manager)
        {
            using (var keys = __indices.GetKeyArray(Allocator.Temp))
            {
                LevelSkillDesc desc;
                int temp, level;
                foreach (var key in keys)
                {
                    temp = __indices[key];
                    if(temp != index)
                        continue;

                    level = __GetLevel(value, key, default, ref definition);
                    if(level == -1)
                        continue;

                    desc = descs[key];
                    desc.sprite.Release();
                    desc.icon.Release();
                    
                    __indices[key] = -1;
                    
                    manager.SetActiveSkill(index, level, null);
                }
            }
        }
        
        private bool __Set(
            int index, 
            int value, 
            in DynamicBuffer<LevelSkillDesc> descs, 
            ref SkillDefinition definition, 
            LevelManager manager)
        {
            using (var keys = __indices.GetKeyArray(Allocator.Temp))
            {
                bool result = false, isCompleted = true;
                int temp, level;
                LevelSkillDesc desc;
                foreach (var key in keys)
                {
                    temp = __indices[key];
                    if(temp != -1)
                        continue;

                    level = __GetLevel(value, key, __indices, ref definition);
                    if(level == -1)
                        continue;

                    desc = descs[key];
                    switch (manager.GetImageType(level))
                    {
                        case LevelManager.SkillActiveImageType.Icon:
                            if (ObjectLoadingStatus.Completed != desc.icon.LoadingStatus)
                            {
                                isCompleted = false;
                        
                                continue;
                            }

                            break;
                        case LevelManager.SkillActiveImageType.Sprite:
                            if (ObjectLoadingStatus.Completed != desc.sprite.LoadingStatus)
                            {
                                isCompleted = false;
                        
                                continue;
                            }

                            break;
                    }
                    
                    if (level == 0 || manager.HasActiveSkill(index, level - 1))
                    {
                        __indices[key] = index;

                        manager.SetActiveSkill(index, level, desc.ToAsset(false));
                    }

                    result = true;
                }

                return result && isCompleted;
            }
        }
        
        private static void __GetLevel(
            int targetIndex, 
            int index, 
            in NativeHashMap<int, int> indices, 
            ref SkillDefinition definition, 
            ref int level)
        {
            if (index < 0 || index >= definition.skills.Length)
            {
                level = -1;

                return;
            }

            if (level == 0)
            {
                if(targetIndex != index)
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
        in DynamicBuffer<SkillActiveIndex> activeIndices, 
        in DynamicBuffer<SkillStatus> states, 
        in DynamicBuffer<LevelSkillDesc> descs, 
        LevelManager manager)
    {
        if(definition.IsCreated)
            __skillActive.Update(SystemAPI.Time.ElapsedTime, ref definition.Value, activeIndices, states, descs, manager);
    }
}
