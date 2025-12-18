using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[BurstCompile, UpdateAfter(typeof(PickableSystem))]
public partial struct LevelPickableSystem : ISystem
{
    private struct Result
    {
        public int selection;
        public int priorityToStyleIndex;
        public int index;
        public int count;
        public Entity entity;
        public Entity version;
    }
    
    private struct Collect
    {
        public Random random;
        
        [ReadOnly] 
        public NativeArray<Entity> entityArray;

        [ReadOnly] 
        public NativeArray<PickableStatus> states;

        [ReadOnly] 
        public NativeArray<LevelPickableSkill> skills;

        [ReadOnly] 
        public NativeArray<LevelPickableItem> items;

        public DynamicBuffer<LevelItem> levelItems;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            Entity entity = states[index].entity;
            if (entity == Entity.Null)
                return;

            if (index < skills.Length)
            {
                var skill = skills[index];
                int count = random.NextInt(skill.min, skill.max + 1);

                Result result;
                result.version = entityArray[index];
                result.selection = skill.selection;
                result.priorityToStyleIndex = skill.priorityToStyleIndex;
                result.count = count;
                for (int i = 0; i < count; ++i)
                {
                    result.index = i;
                    result.entity = entity;
                    results.Enqueue(result);
                }
            }

            if (index < items.Length && levelItems.IsCreated)
            {
                var item = items[index];
                
                int numLevelItems = levelItems.Length, i;
                for (i = 0; i < numLevelItems; ++i)
                {
                    ref var levelItem = ref levelItems.ElementAt(i);
                    if (levelItem.name == item.name)
                    {
                        levelItem.count += random.NextInt(item.min, item.max);
                        
                        if(levelItem.count < 0)
                            levelItems.RemoveAtSwapBack(i);
                        
                        break;
                    }
                }

                if (i == numLevelItems)
                {
                    LevelItem levelItem;
                    levelItem.count = random.NextInt(item.min, item.max);
                    if (levelItem.count > 0)
                    {
                        levelItem.name = item.name;

                        levelItems.Add(levelItem);
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public double time;

        public Entity levelEntity;
        
        [NativeDisableParallelForRestriction]
        public BufferLookup<LevelItem> levelItems;

        [ReadOnly] 
        public EntityTypeHandle entityType;
        
        [ReadOnly] 
        public ComponentTypeHandle<PickableStatus> statusType;

        [ReadOnly] 
        public ComponentTypeHandle<LevelPickableSkill> skillType;

        [ReadOnly] 
        public ComponentTypeHandle<LevelPickableItem> itemType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            ulong hash = math.asulong(time);
            
            Collect collect;
            collect.random = Random.CreateFromIndex((uint)(hash >> 32) ^ (uint)hash ^ (uint)unfilteredChunkIndex);
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.states = chunk.GetNativeArray(ref statusType);
            collect.skills = chunk.GetNativeArray(ref skillType);
            collect.items = chunk.GetNativeArray(ref itemType);
            collect.levelItems = levelItems.HasBuffer(levelEntity) ? levelItems[levelEntity] :　default;
            collect.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

#if !DEBUG
    [BurstCompile]
#endif
    private struct Select : IJob
    {
        public double time;
        public Entity entity;

        [ReadOnly]
        public ComponentLookup<LevelSkillDefinitionData> definitions;
        
        [ReadOnly]
        public BufferLookup<SkillActiveIndex> skillActiveIndices;
        
        [ReadOnly]
        public BufferLookup<LevelSkillGroup> skillGroups;

        public BufferLookup<LevelSkill> skills;

        public ComponentLookup<LevelSkillVersion> versions;

        public NativeQueue<Result> results;

        public void Execute()
        {
            if (!this.skills.TryGetBuffer(entity, out var skills) || 
                !definitions.TryGetComponent(entity, out var definition))
            {
                results.Clear();

                return;
            }

            if (this.skills.IsBufferEnabled(entity))
                return;

            versions.TryGetComponent(entity, out var version);
            
            skills.Clear();

            //int i, numSkills, skillIndex, skillCount = 0;
            var hash = math.aslong(time);
            var random = Random.CreateFromIndex((uint)hash ^ (uint)(hash >> 32));
            LevelSkill skill;
            DynamicBuffer<LevelSkillGroup> skillGroups;
            DynamicBuffer<SkillActiveIndex> skillActiveIndices;
            while (results.TryDequeue(out Result result))
            {
                if(!this.skillActiveIndices.TryGetBuffer(result.entity, out skillActiveIndices))
                    continue;

                definition.definition.Value.Select(
                    skillActiveIndices.AsNativeArray(), 
                    this.skillGroups.TryGetBuffer(result.entity, out skillGroups) ? skillGroups.AsNativeArray() : default,
                    ref skills, 
                    ref random, 
                    out version.priority, 
                    result.priorityToStyleIndex == 0 ? 0 : 1);

                if (skills.Length < 1 && 
                    (result.count == 1 || result.index == 0 || result.version != version.entity))
                    continue;

                if (result.priorityToStyleIndex != 0)
                {
                    if (version.priority == 0 && !skills.IsEmpty)
                    {
                        skill = skills[random.NextInt(skills.Length)];
                            
                        skills.ResizeUninitialized(1);
                        skills[0] = skill;
                    }
                    
                    version.priority += result.priorityToStyleIndex;
                }

                version.selection = result.selection;
                version.count = result.count;
                version.index = result.index;
                if(version.index == 0)
                    ++version.value;

                version.timeScale = 0.0f;
                version.entity = result.version;

                versions[entity] = version;
                
                this.skills.SetBufferEnabled(entity, true);
                
                break;
            }
        }
    }

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<PickableStatus> __statusType;

    private ComponentTypeHandle<LevelPickableItem> __itemType;
    private ComponentTypeHandle<LevelPickableSkill> __skillType;

    private ComponentLookup<LevelSkillVersion> __versions;
    private ComponentLookup<LevelSkillDefinitionData> __definitions;

    private BufferLookup<LevelItem> __items;

    private BufferLookup<LevelSkill> __skills;

    private BufferLookup<LevelSkillGroup> __skillGroups;

    private BufferLookup<SkillActiveIndex> __skillActiveIndices;
    
    private EntityQuery __group;

    private NativeQueue<Result> __results;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __statusType = state.GetComponentTypeHandle<PickableStatus>(true);
        __itemType = state.GetComponentTypeHandle<LevelPickableItem>(true);
        __skillType = state.GetComponentTypeHandle<LevelPickableSkill>(true);
        
        __versions = state.GetComponentLookup<LevelSkillVersion>();
        __definitions = state.GetComponentLookup<LevelSkillDefinitionData>(true);
        __items = state.GetBufferLookup<LevelItem>();
        __skills = state.GetBufferLookup<LevelSkill>();
        __skillGroups = state.GetBufferLookup<LevelSkillGroup>(true);
        __skillActiveIndices = state.GetBufferLookup<SkillActiveIndex>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SimulationEvent>()
                .WithAny<LevelPickableSkill, LevelPickableItem>()
                .WithNone<Pickable>()
                .Build(ref state);
        
        //state.RequireForUpdate<LevelSkillDefinitionData>();

        __results = new NativeQueue<Result>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __results.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __entityType.Update(ref state);
        __statusType.Update(ref state);
        __skillType.Update(ref state);
        __itemType.Update(ref state);
        __items.Update(ref state);

        double time = SystemAPI.Time.ElapsedTime;
        
        CollectEx collect;
        collect.time = time;
        SystemAPI.TryGetSingletonEntity<LevelItem>(out collect.levelEntity);
        collect.levelItems = __items;
        
        collect.entityType = __entityType;
        collect.itemType = __itemType;
        collect.skillType = __skillType;
        collect.statusType = __statusType;
        collect.results = __results.AsParallelWriter();

        var jobHandle = collect.ScheduleParallelByRef(__group, state.Dependency);

        __versions.Update(ref state);
        __definitions.Update(ref state);
        __skills.Update(ref state);
        __skillGroups.Update(ref state);
        __skillActiveIndices.Update(ref state);
        
        Select select;
        select.time = time;
        select.entity = SystemAPI.TryGetSingletonEntity<LevelSkillDefinitionData>(out Entity entity) ? entity : Entity.Null;
        select.definitions = __definitions;
        select.skillActiveIndices = __skillActiveIndices;
        select.skillGroups = __skillGroups;
        select.skills = __skills;
        select.versions = __versions;
        select.results = __results;
        state.Dependency = select.ScheduleByRef(jobHandle);
    }
}