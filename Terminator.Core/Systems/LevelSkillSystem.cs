using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[BurstCompile]
public partial struct LevelSkillPickableSystem : ISystem
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
        public NativeArray<LevelSkillPickable> instances;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            Entity entity = states[index].entity;
            if (entity == Entity.Null)
                return;

            var instance = instances[index];
            int count = random.NextInt(instance.min, instance.max + 1);

            Result result;
            result.version = entityArray[index];
            result.selection = instance.selection;
            result.priorityToStyleIndex = instance.priorityToStyleIndex;
            result.count = count;
            for (int i = 0; i < count; ++i)
            {
                result.index = i;
                result.entity = entity;
                results.Enqueue(result);
            }
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public double time;

        [ReadOnly] 
        public EntityTypeHandle entityType;
        
        [ReadOnly] 
        public ComponentTypeHandle<PickableStatus> statusType;

        [ReadOnly] 
        public ComponentTypeHandle<LevelSkillPickable> instanceType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            ulong hash = math.asulong(time);
            
            Collect collect;
            collect.random = Random.CreateFromIndex((uint)(hash >> 32) ^ (uint)hash ^ (uint)unfilteredChunkIndex);
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.states = chunk.GetNativeArray(ref statusType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    [BurstCompile]
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
                    out version.priority);

                if (skills.Length < 1 && 
                    (result.count == 1 || result.index == 0 || result.version != versions[entity].entity))
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

                versions[entity] = version;
                
                this.skills.SetBufferEnabled(entity, true);
                
                break;
            }
        }
    }

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<PickableStatus> __statusType;

    private ComponentTypeHandle<LevelSkillPickable> __instanceType;

    private ComponentLookup<LevelSkillVersion> __versions;
    private ComponentLookup<LevelSkillDefinitionData> __defintions;

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
        __instanceType = state.GetComponentTypeHandle<LevelSkillPickable>(true);
        
        __versions = state.GetComponentLookup<LevelSkillVersion>();
        __defintions = state.GetComponentLookup<LevelSkillDefinitionData>(true);
        __skills = state.GetBufferLookup<LevelSkill>();
        __skillGroups = state.GetBufferLookup<LevelSkillGroup>(true);
        __skillActiveIndices = state.GetBufferLookup<SkillActiveIndex>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SimulationEvent, LevelSkillPickable>()
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
        __instanceType.Update(ref state);

        double time = SystemAPI.Time.ElapsedTime;
        
        CollectEx collect;
        collect.time = time;
        collect.entityType = __entityType;
        collect.instanceType = __instanceType;
        collect.statusType = __statusType;
        collect.results = __results.AsParallelWriter();

        var jobHandle = collect.ScheduleParallelByRef(__group, state.Dependency);

        __versions.Update(ref state);
        __defintions.Update(ref state);
        __skills.Update(ref state);
        __skillGroups.Update(ref state);
        __skillActiveIndices.Update(ref state);
        
        Select select;
        select.time = time;
        select.entity = SystemAPI.TryGetSingletonEntity<LevelSkillDefinitionData>(out Entity entity) ? entity : Entity.Null;
        select.definitions = __defintions;
        select.skillActiveIndices = __skillActiveIndices;
        select.skillGroups = __skillGroups;
        select.skills = __skills;
        select.versions = __versions;
        select.results = __results;
        state.Dependency = select.ScheduleByRef(jobHandle);
    }
}