using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using Unity.Scenes;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true), UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial class LevelSystemManaged : SystemBase
{
    private interface ICollectLinkedEntitiesWrapper
    {
        void Run(
            in EntityQuery group, 
            in EntityTypeHandle entityType,
            in BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType,
            ref ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs,
            ref NativeList<Entity> entities);
    }
    
    private struct CollectLinkedEntitiesWrapper : ICollectLinkedEntitiesWrapper
    {
        public void Run(
            in EntityQuery group,
            in EntityTypeHandle entityType,
            in BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType,
            ref ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs,
            ref NativeList<Entity> entities)
        {
            CollectLinkedEntities collectLinkedEntities;
            collectLinkedEntities.entityType = entityType;
            collectLinkedEntities.linkedEntityGroupType = linkedEntityGroupType;
            collectLinkedEntities.copyMatrixToTransformInstanceIDs = copyMatrixToTransformInstanceIDs;
            collectLinkedEntities.entities = entities;
            collectLinkedEntities.RunByRef(group);
        }
    }
    
    [BurstCompile]
    private struct CollectLinkedEntities : IJobChunk
    {
        [ReadOnly] 
        public EntityTypeHandle entityType;
        
        [ReadOnly]
        public BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType;

        public ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs;

        public NativeList<Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            if (!chunk.Has(ref linkedEntityGroupType))
                return;

            NativeArray<Entity> entityArray = chunk.GetNativeArray(entityType), entities;
            var linkedEntityGroups = chunk.GetBufferAccessor(ref linkedEntityGroupType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                Disable(entityArray[i]);
                
                entities = linkedEntityGroups[i].AsNativeArray().Reinterpret<Entity>();
                foreach (var entity in entities)
                    Disable(entity);
                
                this.entities.AddRange(entities);
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

    private EntityTypeHandle __entityType;

    private BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupType;

    private ComponentLookup<CopyMatrixToTransformInstanceID> __copyMatrixToTransformInstanceIDs;

    private EntityQuery __group;
    
    protected override void OnCreate()
    {
        base.OnCreate();

        World.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>().Timestep = UnityEngine.Time.fixedDeltaTime;

        __entityType = GetEntityTypeHandle();
        __linkedEntityGroupType = GetBufferTypeHandle<LinkedEntityGroup>(true);
        __copyMatrixToTransformInstanceIDs = GetComponentLookup<CopyMatrixToTransformInstanceID>();
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LevelObject>()
                .Build(this);

        //RequireForUpdate<LevelStatus>();
        
        __stage = new Stage(this);
        __skillSelection = new SkillSelection(this);
        __skillActive = new SkillActive(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __stage.Dispose();
        __skillSelection.Dispose();
        __skillActive.Dispose();
        
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        CompleteDependency();

        var manager = LevelManager.instance;
        if (manager == null || !SystemAPI.TryGetSingleton<LevelStatus>(out var status))
        {
            __DestroyEntities(__group);
            
            return;
        }

        Entity player = SystemAPI.TryGetSingletonEntity<ThirdPersonPlayer>(out Entity thirdPersonPlayerEntity) ? 
            SystemAPI.GetComponent<ThirdPersonPlayer>(thirdPersonPlayerEntity).ControlledCharacter : Entity.Null;
        if (manager.isRestart)
        {
            //manager.Pause();
            __DestroyEntities(__group);

            status.exp = LevelShared.exp;
            status.expMax = LevelShared.expMax;
            status.count = 0;
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
            /*else
                status.gold = 0;*/

            status.stage = LevelShared.stage;
            SystemAPI.SetSingleton(status);
            
            if(thirdPersonPlayerEntity != Entity.Null)
                EntityManager.RemoveComponent<ThirdPersonPlayer>(thirdPersonPlayerEntity);
        }
        /*else if (thirdPersonPlayerEntity != Entity.Null && !SystemAPI.Exists(player))
        {
            EntityManager.RemoveComponent<ThirdPersonPlayer>(thirdPersonPlayerEntity);

            return;
        }*/

        manager.Set(
            status.value, 
            status.max, 
            status.expMax, 
            status.exp, 
            status.count, 
            status.gold, 
            status.stage);
        
        __UpdateStage(manager);
        
        __GetSkill(player, 
            out var skillDefinition, 
            out var activeIndices, 
            out var skillStates,
            out var skillDescs);

        __UpdateSkillActive(skillDefinition, activeIndices, skillStates, skillDescs, manager);

        __UpdateSkillSelection(
            ref activeIndices, 
            skillStates, 
            skillDescs, 
            skillDefinition, 
            player, 
            status.stage, 
            manager);

#if DEBUG
        if (manager.debugLevelUp)
        {
            manager.debugLevelUp = false;
            
            status.exp = status.expMax;
            SystemAPI.SetSingleton(status);
        }
#endif
        manager.isRestart = false;
    }

    private void __DestroyLinkedEntities<T>(in EntityQuery group, ref T collectLinkedEntitiesWrapper) where T : ICollectLinkedEntitiesWrapper
    {
        __entityType.Update(this);
        __linkedEntityGroupType.Update(this);
        __copyMatrixToTransformInstanceIDs.Update(this);
        
        var entities = new NativeList<Entity>(Allocator.TempJob);
        collectLinkedEntitiesWrapper.Run(
            group, 
            __entityType, 
            __linkedEntityGroupType, 
            ref __copyMatrixToTransformInstanceIDs,
            ref entities);
        
        var entityManager = EntityManager;
        entityManager.DestroyEntity(entities.AsArray());
        entities.Dispose();
    }

    private void __DestroyEntities<T>(in EntityQuery group, ref T collectLinkedEntitiesWrapper) where T : ICollectLinkedEntitiesWrapper
    {
        __DestroyLinkedEntities(group, ref collectLinkedEntitiesWrapper);
        
        EntityManager.DestroyEntity(group);
    }

    private void __DestroyEntities(in EntityQuery group)
    {
        CollectLinkedEntitiesWrapper collectLinkedEntitiesWrapper;
        __DestroyEntities(group, ref collectLinkedEntitiesWrapper);
    }
    
    private void __GetSkill(
        in Entity player, 
        out BlobAssetReference<SkillDefinition> definition, 
        out DynamicBuffer<SkillActiveIndex> activeIndices, 
        out DynamicBuffer<SkillStatus> states, 
        out DynamicBuffer<LevelSkillDesc> descs)
    {
        //player = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        definition = SystemAPI.HasComponent<SkillDefinitionData>(player) ? SystemAPI.GetComponent<SkillDefinitionData>(player).definition : default;
        activeIndices = SystemAPI.HasBuffer<SkillActiveIndex>(player) ? SystemAPI.GetBuffer<SkillActiveIndex>(player) : default;
        states = SystemAPI.HasBuffer<SkillStatus>(player) ? SystemAPI.GetBuffer<SkillStatus> (player) : default;
        
        SystemAPI.TryGetSingletonBuffer(out descs, true);
    }

}
