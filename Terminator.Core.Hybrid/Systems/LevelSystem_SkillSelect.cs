using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities;
using ZG;

public partial class LevelSystemManaged
{
    public enum SkillSelectionStatus
    {
        None, 
        Begin, 
        End, 
        Finish
    }
    
    private struct SkillVersion : IEquatable<LevelSkillVersion>
    {
        public int value;

        public int index;

        public static implicit operator SkillVersion(LevelSkillVersion value)
        {
            SkillVersion result;
            result.value = value.value;
            result.index = value.index;

            return result;
        }

        public bool Equals(LevelSkillVersion other)
        {
            return value == other.value && index == other.index;
        }
    }

    private struct SkillSelection
    {
        public SkillSelectionStatus status;
        
        public SkillVersion version;

        //private int __stage;
    
        public SkillSelection(SystemBase system)
        {
            status = SkillSelectionStatus.None;
            version = default;
            //__stage = 0;
        }

        public void Dispose()
        {
            //__spriteRefCounts.Dispose();
        }

        public void Reset()
        {
            version = default;
        }

        public void Release()
        {
            status = SkillSelectionStatus.None;
        }
    }

    private SkillSelection __skillSelection;

    private void __UpdateSkillSelection(
        ref DynamicBuffer<SkillActiveIndex> activeIndices, 
        in DynamicBuffer<SkillStatus> states, 
        //in DynamicBuffer<LevelSkillDesc> descs, 
        in BlobAssetReference<SkillDefinition> definition, 
        in BlobAssetReference<LevelSkillNameDefinition> nameDefinition, 
        in Entity player, 
        int stage, 
        LevelManager manager)
    {
        if (manager.isRestart || !SystemAPI.Exists(player))
        {
            __skillSelection.Release();
            __skillSelection.Reset();

            return;
        }

        //__skillSelection.SetStage(stage, this);
        
        if (SystemAPI.TryGetSingletonEntity<LevelSkillVersion>(out Entity entity))
        {
            var skillVersion = SystemAPI.GetComponent<LevelSkillVersion>(entity);

            bool isWaiting = false;
            switch (__skillSelection.status)
            {
                case SkillSelectionStatus.End:
                case SkillSelectionStatus.Finish:
                    isWaiting = true;
                    break;
                default:
                    isWaiting = __skillSelection.version.Equals(skillVersion);
                    break;
            }
            
            if (isWaiting)
            {
                if (SystemAPI.IsBufferEnabled<LevelSkill>(entity))
                {
                    var skills = SystemAPI.GetBuffer<LevelSkill>(entity);
                    var selectedSkillIndices = manager.CollectSelectedSkillIndices();
                    if (skills.IsEmpty || selectedSkillIndices != null)
                    {
                        if (__skillSelection.status == SkillSelectionStatus.End)
                            __skillSelection.status = SkillSelectionStatus.Finish;

                        if (definition.IsCreated)
                        {
                            int numSelectedSkillIndices = selectedSkillIndices == null ? 0 : selectedSkillIndices.Length;
                            if (numSelectedSkillIndices > 0)
                            {
                                var skillIndices = new NativeList<int>(
                                    numSelectedSkillIndices * (activeIndices.Length + 1),
                                    Allocator.TempJob);
                                skillIndices.CopyFromNBC(selectedSkillIndices);

                                var bulletStates = SystemAPI.GetBuffer<BulletStatus>(player);
                                LevelSkill.Apply(
                                    SystemAPI.Time.ElapsedTime,
                                    skillIndices,
                                    skills,
                                    ref activeIndices,
                                    ref bulletStates,
                                    //ref SystemAPI.GetComponent<BulletDefinitionData>(player).definition.Value,
                                    ref definition.Value,
                                    ref skillIndices);

                                int numActiveSkillIndices = skillIndices.Length;
                                if (numActiveSkillIndices > numSelectedSkillIndices)
                                    __UpdateBullets(
                                        entity,
                                        skillIndices.AsArray()
                                            .GetSubArray(numSelectedSkillIndices,
                                                numActiveSkillIndices - numSelectedSkillIndices));

                                skillIndices.Dispose();
                            }
                        }

                        SystemAPI.SetBufferEnabled<LevelSkill>(entity, false);
                    }
                }
            }
            else if(nameDefinition.IsCreated)
            {
                ref var skillAssetNames = ref nameDefinition.Value.skills;
                var skills = SystemAPI.GetBuffer<LevelSkill>(entity);
                //LevelSkillDesc desc, activeDesc;
                //SkillAsset asset;
                int numSkills = skills.Length;
                bool isAllDone = true;
                for (int i = 0; i < numSkills; ++i)
                {
                    ref var skill = ref skills.ElementAt(i);
                    if (skill.originIndex != -1)
                    {
                        //activeDesc = descs[skill.originIndex];
                        //if (!activeDesc.WaitForCompletion())
                        if(!SkillManager.TryGetAsset(skillAssetNames[skill.originIndex].ToString(), out _))
                        {
                            isAllDone = false;

                            break;
                            //__skillSelection.Retain(activeDesc);
                        }
                    }

                    //desc = descs[skill.index];
                    //if (!desc.WaitForCompletion())
                    if(!SkillManager.TryGetAsset(skillAssetNames[skill.index].ToString(), out _))
                    {
                        isAllDone = false;

                        break;
                        //__skillSelection.Retain(desc);
                    }
                }

                if (isAllDone)
                {
                    if (skillVersion.index == 0)
                    {
                        __skillSelection.status = SkillSelectionStatus.Begin;

                        manager.SelectSkillBegin(skillVersion.selection, skillVersion.timeScale);
                    }

                    if (numSkills > 0)
                    {
                        LevelSkillData result;
                        var results = new List<LevelSkillData>(numSkills);
                        var skillNames = new Dictionary<int, string>(numSkills);
                        for (int i = 0; i < numSkills; ++i)
                        {
                            ref var skill = ref skills.ElementAt(i);
                            result.parentName = null;
                            if (skill.originIndex != -1)
                            {
                                if (!skillNames.TryGetValue(skill.originIndex, out result. /*value.*/name))
                                {
                                    result.selectIndex = -1;
                                    result.name = skillAssetNames[skill.originIndex].ToString();
                                    //SkillManager.TryGetAsset(result.name, out result.value);

                                    results.Add(result);

                                    skillNames.Add(skill.originIndex, result. /*value.*/name);
                                }

                                result.parentName = result. /*value.*/name;
                            }

                            result.selectIndex = i;
                            result.name = skillAssetNames[skill.index].ToString();
                            //SkillManager.TryGetAsset(result.name, out result.value);

                            results.Add(result);

                            skillNames.Add(skill.index, result. /*value.*/name);
                        }
                        
                        manager.SelectSkills(skillVersion.priority, results.ToArray());
                    }
                    
                    if (skillVersion.index + 1 == skillVersion.count)
                    {
                        manager.SelectSkillEnd();

                        __skillSelection.status = SkillSelectionStatus.End;
                    }

                    __skillSelection.version = skillVersion;
                }
            }
        }

        if (manager.isClear && 
            manager.selectedSkillSelectionIndex == -1 && 
            SkillSelectionStatus.Finish == __skillSelection.status)
            __skillSelection.Release();
    }
}
