using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;
using ZG;
using Random = Unity.Mathematics.Random;

[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true), UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial class LevelSystemManaged : SystemBase
{
    private interface ICollectLinkedEntitiesWrapper
    {
        void Run(
            in EntityQuery group, 
            in EntityTypeHandle entityType,
            in BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType,
            //ref ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs,
            ref NativeList<Entity> entities);
    }
    
    private struct CollectLinkedEntitiesWrapper : ICollectLinkedEntitiesWrapper
    {
        public void Run(
            in EntityQuery group,
            in EntityTypeHandle entityType,
            in BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType,
            //ref ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs,
            ref NativeList<Entity> entities)
        {
            CollectLinkedEntities collectLinkedEntities;
            //collectLinkedEntities.entityType = entityType;
            collectLinkedEntities.linkedEntityGroupType = linkedEntityGroupType;
            //collectLinkedEntities.copyMatrixToTransformInstanceIDs = copyMatrixToTransformInstanceIDs;
            collectLinkedEntities.entities = entities;
            collectLinkedEntities.RunByRef(group);
        }
    }
    
    [BurstCompile]
    private struct CollectLinkedEntities : IJobChunk
    {
        //[ReadOnly] 
        //public EntityTypeHandle entityType;
        
        [ReadOnly]
        public BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType;

        //public ComponentLookup<CopyMatrixToTransformInstanceID> copyMatrixToTransformInstanceIDs;

        public NativeList<Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            if (!chunk.Has(ref linkedEntityGroupType))
                return;

            NativeArray<Entity> /*entityArray = chunk.GetNativeArray(entityType), */entities;
            var linkedEntityGroups = chunk.GetBufferAccessor(ref linkedEntityGroupType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                //Disable(entityArray[i]);
                
                entities = linkedEntityGroups[i].AsNativeArray().Reinterpret<Entity>();
                /*foreach (var entity in entities)
                    Disable(entity);*/
                
                this.entities.AddRange(entities);
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

    private EntityTypeHandle __entityType;

    private BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupType;

    //private ComponentLookup<CopyMatrixToTransformInstanceID> __copyMatrixToTransformInstanceIDs;

    private EntityQuery __group;
    
    protected override void OnCreate()
    {
        base.OnCreate();

        World.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>().Timestep = UnityEngine.Time.fixedDeltaTime;

        __entityType = GetEntityTypeHandle();
        __linkedEntityGroupType = GetBufferTypeHandle<LinkedEntityGroup>(true);
        //__copyMatrixToTransformInstanceIDs = GetComponentLookup<CopyMatrixToTransformInstanceID>();
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LevelObject>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(this);

        //RequireForUpdate<LevelStatus>();
        
        __stage = new Stage(Allocator.Persistent);
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
    
#if ENABLE_PROFILER
    private static readonly ProfilerMarker RestartProfilerMarker = new ProfilerMarker("Restart");
    private static readonly ProfilerMarker SetProfilerMarker = new ProfilerMarker("Set");
    private static readonly ProfilerMarker UpdateStageProfilerMarker = new ProfilerMarker("UpdateStage");
    private static readonly ProfilerMarker SkillsProfilerMarker = new ProfilerMarker("Skills");
    private static readonly ProfilerMarker UpdateSkillActiveProfilerMarker = new ProfilerMarker("UpdateSkillActive");
    private static readonly ProfilerMarker UpdateSkillSelectionProfilerMarker = new ProfilerMarker("UpdateSkillSelection");
#endif
    
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
#if ENABLE_PROFILER
            using (RestartProfilerMarker.Auto())
#endif
            {
                //manager.Pause();
                status.exp = LevelShared.exp;
                status.expMax = LevelShared.expMax;
                status.killCount = 0;
                status.killBossCount = 0;
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

                if (thirdPersonPlayerEntity != Entity.Null)
                    EntityManager.RemoveComponent<ThirdPersonPlayer>(thirdPersonPlayerEntity);
                
                __DestroyEntities(__group);
            }
        }
        /*else if (thirdPersonPlayerEntity != Entity.Null && !SystemAPI.Exists(player))
        {
            EntityManager.RemoveComponent<ThirdPersonPlayer>(thirdPersonPlayerEntity);

            return;
        }*/

#if ENABLE_PROFILER
        using(SetProfilerMarker.Auto())
#endif
            manager.Set(
                status.value, 
                status.max, 
                status.expMax, 
                status.exp, 
                status.killCount, 
                status.killBossCount, 
                status.gold, 
                status.stage);
        
#if ENABLE_PROFILER
        using(UpdateStageProfilerMarker.Auto())
#endif
            __UpdateStage(manager);

#if ENABLE_PROFILER
        using (SkillsProfilerMarker.Auto())
#endif
        {
            __GetSkill(player,
                out var skillDefinition,
                out var skillNameDefinition,
                out var activeIndices,
                out var skillStates /*,
                out var skillDescs*/);
            
#if ENABLE_PROFILER
            using (UpdateSkillActiveProfilerMarker.Auto())
#endif
                __UpdateSkillActive(skillDefinition, skillNameDefinition, activeIndices, skillStates, /*skillDescs, */
                    manager);

#if ENABLE_PROFILER
            using (UpdateSkillSelectionProfilerMarker.Auto())
#endif
                __UpdateSkillSelection(
                    ref activeIndices,
                    skillStates,
                    //skillDescs, 
                    skillDefinition,
                    skillNameDefinition,
                    player,
                    status.stage,
                    manager);
        }
        
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
        //__copyMatrixToTransformInstanceIDs.Update(this);
        
        var entities = new NativeList<Entity>(Allocator.TempJob);
        collectLinkedEntitiesWrapper.Run(
            group, 
            __entityType, 
            __linkedEntityGroupType, 
            //ref __copyMatrixToTransformInstanceIDs,
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
        out BlobAssetReference<LevelSkillNameDefinition> nameDefinition, 
        out DynamicBuffer<SkillActiveIndex> activeIndices, 
        out DynamicBuffer<SkillStatus> states/*, 
        out DynamicBuffer<LevelSkillDesc> descs*/)
    {
        definition = SystemAPI.HasComponent<SkillDefinitionData>(player) ? SystemAPI.GetComponent<SkillDefinitionData>(player).definition : default;
        nameDefinition = SystemAPI.HasComponent<LevelSkillNameDefinitionData>(player) ? SystemAPI.GetComponent<LevelSkillNameDefinitionData>(player).definition : default;
        activeIndices = SystemAPI.HasBuffer<SkillActiveIndex>(player) ? SystemAPI.GetBuffer<SkillActiveIndex>(player) : default;
        states = SystemAPI.HasBuffer<SkillStatus>(player) ? SystemAPI.GetBuffer<SkillStatus> (player) : default;
        
        //SystemAPI.TryGetSingletonBuffer(out descs, true);
    }

}
