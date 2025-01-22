using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Scenes;
using UnityEngine;

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

    private struct ClearBulletEntitiesUnmanaged
    {
        public double time;
        
        [ReadOnly]
        public NativeArray<BulletEntity> bulletEntities;

        [ReadOnly]
        public ComponentLookup<BulletDefinitionData> bulletDefinitions;

        public BufferLookup<BulletStatus> bulletStates;

        public void Execute(int index)
        {
            var bulletEntity = this.bulletEntities[index];
            if (!this.bulletStates.TryGetBuffer(bulletEntity.parent, out var bulletStates) || 
                bulletStates.Length <= bulletEntity.index)
                return;

            float startTime = 0.0f;
            if (bulletDefinitions.TryGetComponent(bulletEntity.parent, out var bulletDefinition))
            {
                ref var definition = ref bulletDefinition.definition.Value;
                if(definition.bullets.Length > bulletEntity.index)
                    startTime = definition.bullets[bulletEntity.index].startTime;
            }

            ref var bulletStatus = ref bulletStates.ElementAt(bulletEntity.index);
            bulletStatus = default;
            bulletStatus.cooldown = time + startTime;
        }
    }

    [BurstCompile]
    private struct ClearBulletEntitiesUnmanagedEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentTypeHandle<BulletEntity> bulletEntityType;

        [ReadOnly]
        public ComponentLookup<BulletDefinitionData> bulletDefinitions;

        public BufferLookup<BulletStatus> bulletStates;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ClearBulletEntitiesUnmanaged clearBulletEntitiesUnmanaged;
            clearBulletEntitiesUnmanaged.time = time;
            clearBulletEntitiesUnmanaged.bulletEntities = chunk.GetNativeArray(ref bulletEntityType);
            clearBulletEntitiesUnmanaged.bulletDefinitions = bulletDefinitions;
            clearBulletEntitiesUnmanaged.bulletStates = bulletStates;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                clearBulletEntitiesUnmanaged.Execute(i);
        }
    }
    
    private struct CollectBulletEntities
    {
        public Entity parent;

        public BlobAssetReference<SkillDefinition> skillDefinition;
        
        [ReadOnly] 
        public BufferAccessor<LinkedEntityGroup> linkedEntityGroups;

        [ReadOnly]
        public NativeArray<int> skillIndices;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<BulletEntity> bulletEntities;

        public NativeList<Entity> entities;

        public ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs;

        public void Execute(int index)
        {
            var bulletEntity = bulletEntities[index];
            if (bulletEntity.parent != parent)
                return;

            int numBulletIndices, i;
            ref var skillDefinition = ref this.skillDefinition.Value;
            foreach (var skillIndex in skillIndices)
            {
                ref var skill = ref skillDefinition.skills[skillIndex];
                numBulletIndices = skill.bulletIndices.Length;
                for (i = 0; i < numBulletIndices; ++i)
                {
                    if (skillDefinition.bullets[skill.bulletIndices[i]].index == bulletEntity.index)
                    {
                        var entity = entityArray[index];
                        entities.Add(entity);

                        Disable(entity);

                        if (index <　linkedEntityGroups.Length)
                        {
                            var linkedEntityGroups = this.linkedEntityGroups[index];
                            foreach (var linkedEntityGroup in linkedEntityGroups)
                                Disable(linkedEntityGroup.Value);
                        }

                        return;
                    }
                }
            }
        }

        public void Disable(in Entity entity)
        {
            if (copyMatrixToTransformInstanceIDs.TryGetComponent(entity, out var copyMatrixToTransformInstanceID))
            {
                copyMatrixToTransformInstanceID.isSendMessageOnDestroy = false;
                
                copyMatrixToTransformInstanceIDs[entity] = copyMatrixToTransformInstanceID;
            }
        }
    }

    [BurstCompile]
    private struct CollectBulletEntitiesEx : IJobChunk
    {
        public Entity parent;

        [ReadOnly]
        public NativeArray<int> skillIndices;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly] 
        public BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType;

        [ReadOnly]
        public ComponentTypeHandle<BulletEntity> bulletEntityType;

        [ReadOnly]
        public ComponentLookup<SkillDefinitionData> skills;

        public ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs;

        public NativeList<Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CollectBulletEntities collectBulletEntities;
            collectBulletEntities.parent = parent;
            collectBulletEntities.skillDefinition = skills[parent].definition;
            collectBulletEntities.skillIndices = skillIndices;
            collectBulletEntities.linkedEntityGroups = chunk.GetBufferAccessor(ref linkedEntityGroupType);
            collectBulletEntities.entityArray = chunk.GetNativeArray(entityType);
            collectBulletEntities.bulletEntities = chunk.GetNativeArray(ref bulletEntityType);
            collectBulletEntities.copyMatrixToTransformInstanceIDs = copyMatrixToTransformInstanceIDs;
            collectBulletEntities.entities = entities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collectBulletEntities.Execute(i);
        }
    }

    private readonly struct CollectBulletEntitiesWrapper : ICollectLinkedEntitiesWrapper
    {
        public readonly Entity Parent;
        public readonly NativeArray<int> SkillIndices;
        public readonly ComponentTypeHandle<BulletEntity> BulletEntityType;
        public readonly ComponentLookup<SkillDefinitionData> Skills;

        public CollectBulletEntitiesWrapper(
            in Entity parent, 
            in NativeArray<int> skillIndices, 
            in ComponentTypeHandle<BulletEntity> bulletEntityType, 
            in ComponentLookup<SkillDefinitionData> skills)
        {
            Parent = parent;
            SkillIndices = skillIndices;
            BulletEntityType = bulletEntityType;
            Skills = skills;
        }

        public void Run(
            in EntityQuery group,
            in EntityTypeHandle entityType,
            in BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType,
            ref ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs,
            ref NativeList<Entity> entities)
        {
            CollectBulletEntitiesEx collectBulletEntities;
            collectBulletEntities.skillIndices = SkillIndices;
            collectBulletEntities.parent = Parent;
            collectBulletEntities.linkedEntityGroupType = linkedEntityGroupType;
            collectBulletEntities.entityType = entityType;
            collectBulletEntities.bulletEntityType = BulletEntityType;
            collectBulletEntities.skills = Skills;
            collectBulletEntities.copyMatrixToTransformInstanceIDs = copyMatrixToTransformInstanceIDs;
            collectBulletEntities.entities = entities;
            
            collectBulletEntities.RunByRef(group);
        }
    }

    private struct SkillSelection
    {
        public SkillSelectionStatus status;
        
        public SkillVersion version;

        private int __stage;
    
        private ComponentTypeHandle<BulletEntity> __bulletEntityType;

        private ComponentLookup<BulletDefinitionData> __bulletDefinitions;

        private ComponentLookup<SkillDefinitionData> __skills;

        public BufferLookup<BulletStatus> __bulletStates;

        private EntityQuery __bulletGroup;
        private EntityQuery __bulletGroupUnmanaged;

        private NativeHashMap<WeakObjectReference<Sprite>, int> __spriteRefCounts; 

        public SkillSelection(SystemBase system)
        {
            status = SkillSelectionStatus.None;
            version = default;
            __stage = 0;
            __bulletEntityType = system.GetComponentTypeHandle<BulletEntity>(true);
            __bulletDefinitions = system.GetComponentLookup<BulletDefinitionData>(true);
            __skills = system.GetComponentLookup<SkillDefinitionData>(true);
            __bulletStates = system.GetBufferLookup<BulletStatus>();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __bulletGroup = builder
                    .WithAll<BulletEntity>()
                    .Build(system);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __bulletGroupUnmanaged = builder
                    .WithAll<BulletEntity>()
                    .WithNone<BulletEntityManaged>()
                    .Build(system);
            
            //system.RequireForUpdate<LevelSkillVersion>();
            //system.RequireForUpdate<LevelSkillDesc>();
            //system.RequireForUpdate<ThirdPersonPlayer>();

            __spriteRefCounts = new NativeHashMap<WeakObjectReference<Sprite>, int>(1, Allocator.Persistent);
        }

        public void Dispose()
        {
            __spriteRefCounts.Dispose();
        }

        public void Reset(LevelSystemManaged system)
        {
            version = default;
            
            system.__DestroyEntities(__bulletGroup);
        }

        public void Release()
        {
            status = SkillSelectionStatus.None;
            
            foreach (var spriteRefCount in __spriteRefCounts)
                spriteRefCount.Key.Release();
            
            __spriteRefCounts.Clear();
        }

        public void Retain(in WeakObjectReference<Sprite> sprite)
        {
            if (__spriteRefCounts.TryGetValue(sprite, out int refCount))
                ++refCount;
            else
            {
                refCount = 1;

                sprite.LoadAsync();
            }

            __spriteRefCounts[sprite] = refCount;
        }

        public void Retain(in LevelSkillDesc desc)
        {
            Retain(desc.sprite);
            Retain(desc.icon);
        }

        public void SetStage(int value, LevelSystemManaged system)
        {
            if (value == __stage)
                return;
            
            __stage = value;
            
            __bulletEntityType.Update(system);
            __bulletDefinitions.Update(system);
            __bulletStates.Update(system);

            ClearBulletEntitiesUnmanagedEx clearBulletEntitiesUnmanaged;
            clearBulletEntitiesUnmanaged.time = system.World.Time.ElapsedTime;
            clearBulletEntitiesUnmanaged.bulletEntityType = __bulletEntityType;
            clearBulletEntitiesUnmanaged.bulletDefinitions = __bulletDefinitions;
            clearBulletEntitiesUnmanaged.bulletStates = __bulletStates;
            clearBulletEntitiesUnmanaged.RunByRef(__bulletGroupUnmanaged);
            
            system.__DestroyEntities(__bulletGroupUnmanaged);
        }

        public void UpdateBullets(in Entity parent, in NativeArray<int> skillIndices, LevelSystemManaged system)
        {
            __bulletEntityType.Update(system);
            __skills.Update(system);

            var collectBulletEntitiesWrapper =
                new CollectBulletEntitiesWrapper(parent, skillIndices, __bulletEntityType, __skills);

            system.__DestroyLinkedEntities(__bulletGroup, ref collectBulletEntitiesWrapper);
        }
    }

    private SkillSelection __skillSelection;

    private void __UpdateSkillSelection(
        ref DynamicBuffer<SkillActiveIndex> activeIndices, 
        in DynamicBuffer<SkillStatus> states, 
        in DynamicBuffer<LevelSkillDesc> descs, 
        in BlobAssetReference<SkillDefinition> definition, 
        in Entity player, 
        int stage, 
        LevelManager manager)
    {
        if (manager.isRestart || !SystemAPI.Exists(player))
        {
            __skillSelection.Reset(this);

            return;
        }

        __skillSelection.SetStage(stage, this);
        
        if (SystemAPI.TryGetSingletonEntity<LevelSkillVersion>(out Entity entity))
        {
            var skillVersion = SystemAPI.GetComponent<LevelSkillVersion>(entity);
            if (__skillSelection.version.Equals(skillVersion))
            {
                if (SystemAPI.IsBufferEnabled<LevelSkill>(entity))
                {
                    var selectedSkillIndices = manager.selectedSkillIndices;
                    int numSelectedSkillIndices = selectedSkillIndices == null ? 0 : selectedSkillIndices.Length;
                    if (numSelectedSkillIndices > 0)
                    {
                        if (__skillSelection.status == SkillSelectionStatus.End)
                            __skillSelection.status = SkillSelectionStatus.Finish;

                        if (definition.IsCreated)
                        {
                            var skillIndices = new NativeList<int>(numSelectedSkillIndices * (activeIndices.Length + 1),
                                Allocator.TempJob);
                            skillIndices.CopyFromNBC(selectedSkillIndices);

                            var skills = SystemAPI.GetBuffer<LevelSkill>(entity);
                            var bulletStates = SystemAPI.GetBuffer<BulletStatus>(player);
                            LevelSkill.Apply(
                                SystemAPI.Time.ElapsedTime,
                                skillIndices,
                                skills,
                                ref activeIndices,
                                ref bulletStates,
                                ref SystemAPI.GetComponent<BulletDefinitionData>(player).definition.Value,
                                ref definition.Value,
                                ref skillIndices);

                            SystemAPI.SetBufferEnabled<LevelSkill>(entity, false);

                            int numActiveSkillIndices = skillIndices.Length;
                            if (numActiveSkillIndices > numSelectedSkillIndices)
                                __skillSelection.UpdateBullets(
                                    entity,
                                    skillIndices.AsArray()
                                        .GetSubArray(numSelectedSkillIndices,
                                            numActiveSkillIndices - numSelectedSkillIndices),
                                    this);

                            skillIndices.Dispose();
                        }
                    }
                }
            }
            else
            {
                //var player = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
                //var skillActiveIndices = SystemAPI.GetBuffer<SkillActiveIndex> (player);

                var skills = SystemAPI.GetBuffer<LevelSkill>(entity);
                //var skillDescs = SystemAPI.GetSingletonBuffer<LevelSkillDesc>();
                LevelSkillDesc desc, activeDesc;
                int numSkills = skills.Length;
                bool isAllDone = true;
                for (int i = 0; i < numSkills; ++i)
                {
                    ref var skill = ref skills.ElementAt(i);
                    if (skill.originIndex != -1)
                    {
                        activeDesc = descs[skill.originIndex];
                        switch (activeDesc.loadingStatus)
                        {
                            case LevelSkillDesc.LoadingStatus.None:
                                __skillSelection.Retain(activeDesc);
                                
                                isAllDone = false;
                                break;
                            case LevelSkillDesc.LoadingStatus.Completed:
                                break;
                            default:
                                isAllDone = false;
                                break;
                        }
                    }

                    desc = descs[skill.index];
                    switch (desc.loadingStatus)
                    {
                        case LevelSkillDesc.LoadingStatus.None:
                            __skillSelection.Retain(desc);
                            
                            isAllDone = false;
                            break;
                        case LevelSkillDesc.LoadingStatus.Completed:
                            break;
                        default:
                            isAllDone = false;
                            break;
                    }
                }

                if (isAllDone)
                {
                    //int skillActiveIndex;
                    LevelSkillData result;
                    //result.styleIndex = skillVersion.priority;
                    var skillNames = new Dictionary<int, string>(numSkills);
                    var results = new List<LevelSkillData>(numSkills);
                    for (int i = 0; i < numSkills; ++i)
                    {
                        ref var skill = ref skills.ElementAt(i);
                        //result.styleIndex = skill.priority;
                        result.parentName = null;
                        if (skill.originIndex != -1)
                        {
                            if (!skillNames.TryGetValue(skill.originIndex, out result.value.name))
                            {
                                activeDesc = descs[skill.originIndex];
                                result.selectIndex = -1;
                                result.value = activeDesc.ToAsset();

                                results.Add(result);

                                skillNames.Add(skill.originIndex, result.value.name);
                            }

                            result.parentName = result.value.name;
                        }

                        desc = descs[skill.index];
                        result.selectIndex = i;
                        result.value = desc.ToAsset();

                        results.Add(result);

                        skillNames.Add(skill.index, result.value.name);
                    }

                    if (skillVersion.index == 0)
                    {
                        __skillSelection.status = SkillSelectionStatus.Begin;

                        manager.SelectSkillBegin(skillVersion.selection);
                    }

                    manager.SelectSkills(skillVersion.priority, results.ToArray());

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
