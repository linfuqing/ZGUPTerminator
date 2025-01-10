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
        private NativeHashMap<int, int> __indices;

        public SkillActive(in AllocatorManager.AllocatorHandle allocator)
        {
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
            //__activeSkillIndexMap.Clear();

            //WeakObjectReference<Sprite> icon;
            SkillStatus status;
            int i, originIndex, index, numActiveIndices = activeIndices.Length;
            bool isComplete;
            for (i = 0; i < numActiveIndices; ++i)
            {
                index = activeIndices[i].value;

                if (__indices.TryGetValue(index, out originIndex))
                {
                    isComplete = originIndex == i;

                    if (!isComplete)
                    {
                        isComplete = __Set(i, index, descs, ref definition, manager);
                        //__indices[index] = i;

                        //manager.SetActiveSkill(i, descs[index].ToAsset(false));
                    }
                }
                else
                {
                    __indices[index] = -1;
                    
                    //icon = descs[index].icon;
                    descs[index].icon.LoadAsync();
                    isComplete = __Set(i, index, descs, ref definition, manager);

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
                    ref var skill = ref definition.skills[index];
                    status = states[index];
                    float cooldown = (float)(Math.Max(status.cooldown, time) - time);
                    manager.SetActiveSkill(i, skill.cooldown,
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
                int temp, level;
                foreach (var key in keys)
                {
                    temp = __indices[key];
                    if(temp != index)
                        continue;

                    level = -1;
                    __GetLevel(value, key, default, ref definition, ref level);
                    if(level == -1)
                        continue;

                    descs[key].icon.Release();
                    
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
                bool result = true;
                int temp, level;
                LevelSkillDesc desc;
                foreach (var key in keys)
                {
                    temp = __indices[key];
                    if(temp != -1)
                        continue;

                    level = -1;
                    __GetLevel(value, key, __indices, ref definition, ref level);
                    if(level == -1)
                        continue;

                    desc = descs[key];
                    if (ObjectLoadingStatus.Completed != desc.icon.LoadingStatus)
                    {
                        result = false;
                        
                        continue;
                    }

                    if (level == 0 || manager.HasActiveSkill(index, level - 1))
                    {
                        __indices[key] = index;

                        manager.SetActiveSkill(index, level, desc.ToAsset(false));
                    }
                }

                return result;
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

            if (targetIndex == index)
            {
                level = 0;
                
                return;
            }

            if (level == 0)
            {
                level = -1;

                return;
            }

            if (level > 0)
                --level;

            ref var preIndices = ref definition.skills[index].preIndices;
            int preIndex, temp, result = -1, numPreIndices = preIndices.Length;
            for(int i = 0; i < numPreIndices; ++i)
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
