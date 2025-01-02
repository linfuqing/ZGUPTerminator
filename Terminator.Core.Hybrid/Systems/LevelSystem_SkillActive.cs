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

            WeakObjectReference<Sprite> icon;
            SkillStatus status;
            int i, originIndex, index, numActiveIndices = activeIndices.Length;
            bool isComplete;
            for (i = 0; i < numActiveIndices; ++i)
            {
                index = activeIndices[i].value;

                if (__indices.TryGetValue(index, out originIndex))
                {
                    isComplete = true;

                    if (originIndex != i)
                    {
                        __indices[index] = i;

                        manager.SetActiveSkill(i, descs[index].ToAsset(false));
                    }
                }
                else
                {
                    isComplete = false;

                    icon = descs[index].icon;
                    icon.LoadAsync();
                    switch (icon.LoadingStatus)
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
                    }
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

            using (var activeSkillIndices = __indices.GetKeyValueArrays(Allocator.Temp))
            {
                bool isRemove;
                int j, value, numActiveSkillIndices = activeSkillIndices.Length;
                for (i = 0; i < numActiveSkillIndices; ++i)
                {
                    isRemove = false;
                    value = activeSkillIndices.Values[i];
                    if (value >= numActiveIndices)
                    {
                        manager.SetActiveSkill(value, null);

                        isRemove = true;
                    }

                    index = activeSkillIndices.Keys[i];
                    for (j = 0; j < numActiveIndices; ++j)
                    {
                        if (activeIndices[j].value == index)
                            break;
                    }

                    if (j == numActiveIndices)
                    {
                        isRemove = true;

                        descs[index].icon.Release();
                    }

                    if (isRemove)
                        __indices.Remove(index);
                }
            }
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
