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
        public Entity version;
        public Random random;
        
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
            result.version = version;
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
        public Entity version;
        public double time;
        
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
            collect.version = version;
            collect.random = Random.CreateFromIndex((uint)(hash >> 32) ^ (uint)hash ^ (uint)unfilteredChunkIndex);
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

        public BlobAssetReference<LevelSkillDefinition> definition;
        
        [ReadOnly]
        public BufferLookup<SkillActiveIndex> skillActiveIndices;
        
        public BufferLookup<LevelSkill> skills;

        public ComponentLookup<LevelSkillVersion> versions;

        public NativeQueue<Result> results;

        public void Execute()
        {
            if (!this.skills.TryGetBuffer(this.entity, out var skills) || this.skills.IsBufferEnabled(this.entity))
                return;

            versions.TryGetComponent(this.entity, out var version);
            
            skills.Clear();

            //int i, numSkills, skillIndex, skillCount = 0;
            var hash = math.aslong(time);
            var random = Random.CreateFromIndex((uint)hash ^ (uint)(hash >> 32));
            LevelSkill skill;
            //NativeList<SkillActiveIndex> results = default;
            DynamicBuffer<SkillActiveIndex> skillActiveIndices;
            while (this.results.TryDequeue(out Result result))
            {
                if(result.version != entity)
                    continue;
                
                if(!this.skillActiveIndices.TryGetBuffer(result.entity, out skillActiveIndices))
                    continue;

                /*if(result.count == 1)
                    instance.definition.Value.Select(skillActiveIndices.AsNativeArray(), ref skills, ref random, out priority);
                else
                {
                    if (results.IsCreated)
                        results.Clear();
                    else
                        results = new NativeList<SkillActiveIndex>(Allocator.Temp);
                    
                    results.AddRange(skillActiveIndices.AsNativeArray());
                    for (i = 0; i < result.count; ++i)
                    {
                        instance.definition.Value.Select(
                            results.AsArray(),
                            ref skills,
                            ref random,
                            out priority);
                        numSkills = skills.Length;
                        if (priority == 0)
                        {
                            skillIndex = random.NextInt(skillCount, numSkills);
                            skill = skills[skillIndex];
                            
                            skill.Apply(ref results);
                            //skill.priority = result.priority;
                            
                            skills.ResizeUninitialized(skillCount + 1);
                            skills[skillCount] = skill;

                            numSkills = 1;
                        }

                        skillCount += numSkills;
                    }
                }*/
                
                definition.Value.Select(skillActiveIndices.AsNativeArray(), ref skills, ref random, out version.priority);

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

                versions[this.entity] = version;
                
                this.skills.SetBufferEnabled(this.entity, true);
                
                break;
            }
        }
    }

    private ComponentTypeHandle<PickableStatus> __statusType;

    private ComponentTypeHandle<LevelSkillPickable> __instanceType;

    private ComponentLookup<LevelSkillVersion> __versions;

    private BufferLookup<LevelSkill> __skills;

    private BufferLookup<SkillActiveIndex> __skillActiveIndices;
    
    private EntityQuery __group;

    private NativeQueue<Result> __results;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __statusType = state.GetComponentTypeHandle<PickableStatus>(true);
        __instanceType = state.GetComponentTypeHandle<LevelSkillPickable>(true);
        
        __versions = state.GetComponentLookup<LevelSkillVersion>();
        __skills = state.GetBufferLookup<LevelSkill>();
        __skillActiveIndices = state.GetBufferLookup<SkillActiveIndex>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SimulationEvent, LevelSkillPickable>()
                .WithNone<Pickable>()
                .Build(ref state);
        
        state.RequireForUpdate<LevelSkillDefinitionData>();

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
        __statusType.Update(ref state);
        __instanceType.Update(ref state);
        
        var entity = SystemAPI.GetSingletonEntity<LevelSkillDefinitionData>();
        var definition = SystemAPI.GetComponent<LevelSkillDefinitionData>(entity).definition;

        double time = SystemAPI.Time.ElapsedTime;
        CollectEx collect;
        collect.version = entity;
        collect.time = time;
        collect.instanceType = __instanceType;
        collect.statusType = __statusType;
        collect.results = __results.AsParallelWriter();

        var jobHandle = collect.ScheduleParallelByRef(__group, state.Dependency);
        
        __versions.Update(ref state);
        __skills.Update(ref state);
        __skillActiveIndices.Update(ref state);
        
        Select select;
        select.time = time;
        select.definition = definition;
        select.entity = entity;
        select.skillActiveIndices = __skillActiveIndices;
        select.skills = __skills;
        select.versions = __versions;
        select.results = __results;
        state.Dependency = select.ScheduleByRef(jobHandle);
    }
}