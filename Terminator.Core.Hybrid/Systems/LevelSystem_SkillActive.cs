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
                        isComplete = __Set(i, index, descs, manager);
                        //__indices[index] = i;

                        //manager.SetActiveSkill(i, descs[index].ToAsset(false));
                    }
                }
                else
                {
                    __indices[index] = -1;
                    
                    //icon = descs[index].icon;
                    descs[index].icon.LoadAsync();
                    isComplete = __Set(i, index, descs, manager);

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
                isRemove = false;
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

            foreach (var keyToRemove in keysToRemove)
            {
                value = __indices[keyToRemove];
                
                __indices.Remove(keyToRemove);
                __Unset(value, keyToRemove, descs, manager);
            }
        }

        private void __Unset(
            int index, 
            int value, 
            in DynamicBuffer<LevelSkillDesc> descs, 
            LevelManager manager)
        {
            using (var keys = __indices.GetKeyArray(Allocator.Temp))
            {
                int temp, level;
                foreach (var key in keys)
                {
                    temp = __indices[key];
                    if(temp == index)
                        continue;

                    level = __GetLevel(value, key, descs);
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
                    if(temp == index)
                        continue;

                    level = __GetLevel(value, key, descs);
                    if(level == -1)
                        continue;

                    desc = descs[key];
                    if (ObjectLoadingStatus.Completed != desc.icon.LoadingStatus)
                    {
                        result = false;
                        
                        continue;
                    }

                    __indices[key] = index;
                    
                    manager.SetActiveSkill(index, level, desc.ToAsset(true));
                }

                return result;
            }
        }
        
        private static int __GetLevel(int targetIndex, int index, in DynamicBuffer<LevelSkillDesc> descs)
        {
            if (index < 0 || index >= descs.Length)
                return -1;
            
            if (targetIndex == index)
                return 0;

            int result = __GetLevel(targetIndex, descs[index].preSkillIndex, descs);
            if (result == -1)
                return -1;

            return result + 1;
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
