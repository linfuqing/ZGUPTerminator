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

    private struct CollectBulletEntities
    {
        public Entity parent;

        public BlobAssetReference<SkillDefinition> skillDefinition;
        
        [ReadOnly]
        public NativeArray<int> skillIndices;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<BulletEntity> bulletEntities;

        public NativeList<Entity> entities;

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
                        entities.Add(entityArray[index]);

                        return;
                    }
                }
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
        public ComponentTypeHandle<BulletEntity> bulletEntityType;

        [ReadOnly]
        public ComponentLookup<SkillDefinitionData> skills;

        public NativeList<Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CollectBulletEntities collectBulletEntities;
            collectBulletEntities.parent = parent;
            collectBulletEntities.skillDefinition = skills[parent].definition;
            collectBulletEntities.skillIndices = skillIndices;
            collectBulletEntities.entityArray = chunk.GetNativeArray(entityType);
            collectBulletEntities.bulletEntities = chunk.GetNativeArray(ref bulletEntityType);
            collectBulletEntities.entities = entities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collectBulletEntities.Execute(i);
        }
    }

    private struct SkillSelection
    {
        public SkillSelectionStatus status;
        
        public SkillVersion version;
    
        public EntityTypeHandle entityType;

        public ComponentTypeHandle<BulletEntity> bulletEntityType;

        public ComponentLookup<SkillDefinitionData> skills;

        public EntityQuery bulletGroup;

        public NativeHashMap<WeakObjectReference<Sprite>, int> spriteRefCounts; 
        
        public SkillSelection(SystemBase system)
        {
            status = SkillSelectionStatus.None;
            version = default;
            entityType = system.GetEntityTypeHandle();
            bulletEntityType = system.GetComponentTypeHandle<BulletEntity>(true);
            skills = system.GetComponentLookup<SkillDefinitionData>(true);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                bulletGroup = builder
                    .WithAll<BulletEntity>()
                    .Build(system);

            //system.RequireForUpdate<LevelSkillVersion>();
            //system.RequireForUpdate<LevelSkillDesc>();
            system.RequireForUpdate<ThirdPersonPlayer>();

            spriteRefCounts = new NativeHashMap<WeakObjectReference<Sprite>, int>(1, Allocator.Persistent);
        }

        public void Dispose()
        {
            spriteRefCounts.Dispose();
        }

        public void UpdateBullets(in Entity parent, in NativeArray<int> skillIndices, SystemBase system)
        {
            entityType.Update(system);
            bulletEntityType.Update(system);
            skills.Update(system);

            using (var entities = new NativeList<Entity>(Allocator.TempJob))
            {
                CollectBulletEntitiesEx collectBulletEntities;
                collectBulletEntities.skillIndices = skillIndices;
                collectBulletEntities.parent = parent;
                collectBulletEntities.entityType = entityType;
                collectBulletEntities.bulletEntityType = bulletEntityType;
                collectBulletEntities.skills = skills;
                collectBulletEntities.entities = entities;
                
                collectBulletEntities.RunByRef(bulletGroup);
                
                system.EntityManager.DestroyEntity(entities.AsArray());
            }
        }
    }

    private SkillSelection __skillSelection;

    private void __UpdateSkillSelection(
        ref DynamicBuffer<SkillActiveIndex> activeIndices, 
        in DynamicBuffer<SkillStatus> states, 
        in DynamicBuffer<LevelSkillDesc> descs, 
        in BlobAssetReference<SkillDefinition> definition, 
        in Entity player, 
        LevelManager manager)
    {
        
        if (manager.isRestart)
        {
            __skillSelection.version = default;
            __DestroyEntities(__skillSelection.bulletGroup);

            if (SystemAPI.Exists(player))
            {
                if (SystemAPI.HasComponent<CopyMatrixToTransformInstanceID>(player))
                {
                    var instanceID = SystemAPI.GetComponent<CopyMatrixToTransformInstanceID>(player);
                    instanceID.isSendMessageOnDestroy = false;
                    SystemAPI.SetComponent(player, instanceID);
                }
                
                EntityManager.DestroyEntity(player);
            }

            EntityManager.RemoveComponent<ThirdPersonPlayer>(SystemAPI.GetSingletonEntity<ThirdPersonPlayer>());

            return;
        }
        
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
                        switch (activeDesc.sprite.LoadingStatus)
                        {
                            case ObjectLoadingStatus.None:
                                if (__skillSelection.spriteRefCounts.TryGetValue(activeDesc.sprite, out int refCount))
                                    ++refCount;
                                else
                                {
                                    refCount = 1;

                                    activeDesc.sprite.LoadAsync();
                                }

                                __skillSelection.spriteRefCounts[activeDesc.sprite] = refCount;

                                isAllDone = false;
                                break;
                            case ObjectLoadingStatus.Error:
                                UnityEngine.Debug.LogError($"Sprite {activeDesc.name} loaded failed!");
                                break;
                            case ObjectLoadingStatus.Completed:
                                break;
                            default:
                                isAllDone = false;
                                break;
                        }
                    }

                    desc = descs[skill.index];
                    switch (desc.sprite.LoadingStatus)
                    {
                        case ObjectLoadingStatus.None:
                            if (__skillSelection.spriteRefCounts.TryGetValue(desc.sprite, out int refCount))
                                ++refCount;
                            else
                            {
                                refCount = 1;

                                desc.sprite.LoadAsync();
                            }

                            __skillSelection.spriteRefCounts[desc.sprite] = refCount;

                            isAllDone = false;
                            break;
                        case ObjectLoadingStatus.Error:
                            UnityEngine.Debug.LogError($"Sprite {desc.name} loaded failed!");
                            break;
                        case ObjectLoadingStatus.Completed:
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
                                result.value = activeDesc.ToAsset(true);

                                results.Add(result);

                                skillNames.Add(skill.originIndex, result.value.name);
                            }

                            result.parentName = result.value.name;
                        }

                        desc = descs[skill.index];
                        result.selectIndex = i;
                        result.value = desc.ToAsset(true);

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
        {
            __skillSelection.status = SkillSelectionStatus.None;
            
            foreach (var spriteRefCount in __skillSelection.spriteRefCounts)
                spriteRefCount.Key.Release();
            
            __skillSelection.spriteRefCounts.Clear();
        }
    }
}
