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

[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
public partial class LevelSystemManaged : SystemBase
{
    [BurstCompile]
    private struct CollectLinkedEntities : IJobChunk
    {
        [ReadOnly]
        public BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupType;

        public NativeList<Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            if (!chunk.Has(ref linkedEntityGroupType))
                return;
            
            var linkedEntityGroups = chunk.GetBufferAccessor(ref linkedEntityGroupType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                entities.AddRange(linkedEntityGroups[i].AsNativeArray().Reinterpret<Entity>());
        }
    }

    private BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupType;

    private EntityQuery __group;
    
    protected override void OnCreate()
    {
        base.OnCreate();

        World.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>().Timestep = UnityEngine.Time.fixedDeltaTime;

        __linkedEntityGroupType = GetBufferTypeHandle<LinkedEntityGroup>(true);
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SpawnerEntity>()
                .Build(this);

        RequireForUpdate<LevelStatus>();
        
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
        var manager = LevelManager.instance;
        if (manager == null)
            return;
        
        CompleteDependency();
        
        var status = SystemAPI.GetSingleton<LevelStatus>();

        Entity player = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        if (manager.isRestart)
        {
            //manager.Pause();
            __DestroyEntities(__group);
            
            status.count = 0;
            if (!SystemAPI.Exists(player))
                status.gold = 0;
            
            SystemAPI.SetSingleton(status);
        }

        manager.Set(
            status.value, 
            status.max, 
            status.expMax, 
            status.exp, 
            status.gold, 
            status.count);
        
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

    private void __DestroyEntities(in EntityQuery group)
    {
        __linkedEntityGroupType.Update(this);
            
        var entities = new NativeList<Entity>(Allocator.TempJob);
        CollectLinkedEntities collectLinkedEntities;
        collectLinkedEntities.linkedEntityGroupType = __linkedEntityGroupType;
        collectLinkedEntities.entities = entities;
        collectLinkedEntities.RunByRef(group);
        var entityManager = EntityManager;
        entityManager.DestroyEntity(entities.AsArray());
        entities.Dispose();
        
        entityManager.DestroyEntity(group);
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
