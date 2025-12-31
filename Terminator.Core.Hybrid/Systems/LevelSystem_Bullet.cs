using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

public partial class LevelSystemManaged
{
    private struct ClearBulletEntitiesUnmanaged
    {
        //public double time;
        
        [ReadOnly]
        public NativeArray<BulletEntity> bulletEntities;

        //[ReadOnly]
        //public ComponentLookup<BulletDefinitionData> bulletDefinitions;

        public BufferLookup<BulletStatus> bulletStates;

        public void Execute(int index)
        {
            var bulletEntity = this.bulletEntities[index];
            if (!this.bulletStates.TryGetBuffer(bulletEntity.parent, out var bulletStates) || 
                bulletStates.Length <= bulletEntity.index)
                return;

            /*float startTime = 0.0f;
            if (bulletDefinitions.TryGetComponent(bulletEntity.parent, out var bulletDefinition))
            {
                ref var definition = ref bulletDefinition.definition.Value;
                if(definition.bullets.Length > bulletEntity.index)
                    startTime = definition.bullets[bulletEntity.index].startTime;
            }*/

            ref var bulletStatus = ref bulletStates.ElementAt(bulletEntity.index);
            bulletStatus = default;
            //bulletStatus.cooldown = time + startTime;
        }
    }

    [BurstCompile]
    private struct ClearBulletEntitiesUnmanagedEx : IJobChunk
    {
        //public double time;

        [ReadOnly]
        public ComponentTypeHandle<BulletEntity> bulletEntityType;

        //[ReadOnly]
        //public ComponentLookup<BulletDefinitionData> bulletDefinitions;

        public BufferLookup<BulletStatus> bulletStates;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ClearBulletEntitiesUnmanaged clearBulletEntitiesUnmanaged;
            //clearBulletEntitiesUnmanaged.time = time;
            clearBulletEntitiesUnmanaged.bulletEntities = chunk.GetNativeArray(ref bulletEntityType);
            //clearBulletEntitiesUnmanaged.bulletDefinitions = bulletDefinitions;
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
        
        //[ReadOnly] 
        //public BufferAccessor<LinkedEntityGroup> linkedEntityGroups;

        [ReadOnly]
        public NativeArray<int> skillIndices;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<BulletEntity> bulletEntities;

        public NativeList<Entity> entities;

        //public ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs;

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

                        //Disable(entity);

                        /*if (index <　linkedEntityGroups.Length)
                        {
                            var linkedEntityGroups = this.linkedEntityGroups[index];
                            foreach (var linkedEntityGroup in linkedEntityGroups)
                                Disable(linkedEntityGroup.Value);
                        }*/

                        return;
                    }
                }
            }
        }

        /*public void Disable(in Entity entity)
        {
            if (copyMatrixToTransformInstanceIDs.TryGetComponent(entity, out var copyMatrixToTransformInstanceID))
            {
                copyMatrixToTransformInstanceID.isSendMessageOnDestroy = false;
                
                copyMatrixToTransformInstanceIDs[entity] = copyMatrixToTransformInstanceID;
            }
        }*/
    }

    [BurstCompile]
    private struct CollectBulletEntitiesEx : IJobChunk
    {
        public Entity parent;

        [ReadOnly]
        public NativeArray<int> skillIndices;

        [ReadOnly]
        public EntityTypeHandle entityType;

        //[ReadOnly] 
        //public BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType;

        [ReadOnly]
        public ComponentTypeHandle<BulletEntity> bulletEntityType;

        [ReadOnly]
        public ComponentLookup<SkillDefinitionData> skills;

        //public ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs;

        public NativeList<Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CollectBulletEntities collectBulletEntities;
            collectBulletEntities.parent = parent;
            collectBulletEntities.skillDefinition = skills[parent].definition;
            collectBulletEntities.skillIndices = skillIndices;
            //collectBulletEntities.linkedEntityGroups = chunk.GetBufferAccessor(ref linkedEntityGroupType);
            collectBulletEntities.entityArray = chunk.GetNativeArray(entityType);
            collectBulletEntities.bulletEntities = chunk.GetNativeArray(ref bulletEntityType);
            //collectBulletEntities.copyMatrixToTransformInstanceIDs = copyMatrixToTransformInstanceIDs;
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
            //ref ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs,
            ref NativeList<Entity> entities)
        {
            CollectBulletEntitiesEx collectBulletEntities;
            collectBulletEntities.skillIndices = SkillIndices;
            collectBulletEntities.parent = Parent;
            //collectBulletEntities.linkedEntityGroupType = linkedEntityGroupType;
            collectBulletEntities.entityType = entityType;
            collectBulletEntities.bulletEntityType = BulletEntityType;
            collectBulletEntities.skills = Skills;
            //collectBulletEntities.copyMatrixToTransformInstanceIDs = copyMatrixToTransformInstanceIDs;
            collectBulletEntities.entities = entities;
            
            collectBulletEntities.RunByRef(group);
        }
    }

    private ComponentTypeHandle<BulletEntity> __bulletEntityType;

    //private ComponentLookup<BulletDefinitionData> __bulletDefinitions;

    private ComponentLookup<SkillDefinitionData> __skills;

    private BufferLookup<BulletStatus> __bulletStates;

    private EntityQuery __bulletGroup;
    private EntityQuery __bulletGroupUnmanaged;

    private void __CreateBulletGroups()
    {
        __bulletEntityType = GetComponentTypeHandle<BulletEntity>(true);
        //__bulletDefinitions = GetComponentLookup<BulletDefinitionData>(true);
        __skills = GetComponentLookup<SkillDefinitionData>(true);
        __bulletStates = GetBufferLookup<BulletStatus>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __bulletGroup = builder
                .WithAll<BulletEntity>()
                .Build(this);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __bulletGroupUnmanaged = builder
                .WithAll<BulletEntity>()
                .WithNone<BulletEntityManaged>()
                .Build(this);
    }

    private void __DestroyBulletEntities()
    {
        __DestroyEntities(__bulletGroup);
    }
    
    private void __DestroyBulletEntitiesUnmanaged()
    {
        __bulletEntityType.Update(this);
        //__bulletDefinitions.Update(system);
        __bulletStates.Update(this);

        ClearBulletEntitiesUnmanagedEx clearBulletEntitiesUnmanaged;
        clearBulletEntitiesUnmanaged.bulletEntityType = __bulletEntityType;
        clearBulletEntitiesUnmanaged.bulletStates = __bulletStates;
        clearBulletEntitiesUnmanaged.RunByRef(__bulletGroupUnmanaged);
            
        __DestroyEntities(__bulletGroupUnmanaged);
    }

    private void __UpdateBullets(in Entity parent, in NativeArray<int> skillIndices)
    {
        __bulletEntityType.Update(this);
        __skills.Update(this);

        var collectBulletEntitiesWrapper =
            new CollectBulletEntitiesWrapper(parent, skillIndices, __bulletEntityType, __skills);

        __DestroyLinkedEntities(__bulletGroup, ref collectBulletEntitiesWrapper);
    }
}
