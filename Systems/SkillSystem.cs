using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Unity.Scenes;

[BurstCompile, 
 UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true), UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
public partial struct SkillSystem : ISystem
{
    private struct Collect
    {
        public double time;

        [ReadOnly] 
        public NativeArray<BulletDefinitionData> bulletDefinitions;
        [ReadOnly]
        public NativeArray<SkillDefinitionData> instances;
        [ReadOnly] 
        public BufferAccessor<SkillMessage> inputMessages;
        [ReadOnly]
        public BufferAccessor<SkillActiveIndex> activeIndices;

        public BufferAccessor<BulletActiveIndex> bulletActiveIndices;

        public BufferAccessor<BulletStatus> bulletStates;

        public BufferAccessor<SkillStatus> states;

        public BufferAccessor<Message> outputMessages;
        
        public bool Execute(int index)
        {
            var outputMessages = index < this.outputMessages.Length ? this.outputMessages[index] : default;
            var states = this.states[index];
            var bulletStates = this.bulletStates[index];
            var bulletActiveIndices = this.bulletActiveIndices[index];
            return instances[index].definition.Value.Update(
                time,
                inputMessages[index],
                activeIndices[index], 
                ref bulletActiveIndices, 
                ref bulletStates, 
                ref states, 
                ref outputMessages, 
                ref bulletDefinitions[index].definition.Value);
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentTypeHandle<BulletDefinitionData> bulletDefinitionType;

        [ReadOnly]
        public ComponentTypeHandle<SkillDefinitionData> instanceType;
        [ReadOnly] 
        public BufferTypeHandle<SkillMessage> inputMessageType;
        [ReadOnly]
        public BufferTypeHandle<SkillActiveIndex> activeIndexType;

        public BufferTypeHandle<BulletActiveIndex> bulletActiveIndexType;

        public BufferTypeHandle<BulletStatus> bulletStatusType;

        public BufferTypeHandle<SkillStatus> statusType;

        public BufferTypeHandle<Message> outputMessageType;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            //long hash = math.aslong(time);
            
            Collect collect;
            collect.time = time;
            //collect.random = Random.CreateFromIndex((uint)(unfilteredChunkIndex ^ (int)(hash >> 32) ^ (int)hash));
            collect.bulletDefinitions = chunk.GetNativeArray(ref bulletDefinitionType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            collect.activeIndices = chunk.GetBufferAccessor(ref activeIndexType);
            collect.bulletActiveIndices = chunk.GetBufferAccessor(ref bulletActiveIndexType);
            collect.bulletStates = chunk.GetBufferAccessor(ref bulletStatusType);
            collect.states = chunk.GetBufferAccessor(ref statusType);
            collect.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(collect.Execute(i))
                    chunk.SetComponentEnabled(ref outputMessageType, i, true);
            }
        }
    }
    
    private ComponentTypeHandle<BulletDefinitionData> __bulletDefinitionType;

    private ComponentTypeHandle<SkillDefinitionData> __instanceType;

    private BufferTypeHandle<SkillMessage> __inputMessageType;
    
    private BufferTypeHandle<SkillActiveIndex> __activeIndexType;

    private BufferTypeHandle<BulletActiveIndex> __bulletActiveIndexType;

    private BufferTypeHandle<BulletStatus> __bulletStatusType;

    private BufferTypeHandle<SkillStatus> __statusType;
    
    private BufferTypeHandle<Message> __outputMessageType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __bulletDefinitionType = state.GetComponentTypeHandle<BulletDefinitionData>(true);
        __instanceType = state.GetComponentTypeHandle<SkillDefinitionData>(true);
        __inputMessageType = state.GetBufferTypeHandle<SkillMessage>(true);
        __activeIndexType = state.GetBufferTypeHandle<SkillActiveIndex>(true);
        __bulletActiveIndexType = state.GetBufferTypeHandle<BulletActiveIndex>();
        __bulletStatusType = state.GetBufferTypeHandle<BulletStatus>();
        __statusType = state.GetBufferTypeHandle<SkillStatus>();
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SkillDefinitionData, SkillActiveIndex>()
                .WithAllRW<BulletActiveIndex, SkillStatus>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __bulletDefinitionType.Update(ref state);
        __instanceType.Update(ref state);
        __inputMessageType.Update(ref state);
        __activeIndexType.Update(ref state);
        __bulletActiveIndexType.Update(ref state);
        __bulletStatusType.Update(ref state);
        __statusType.Update(ref state);
        __outputMessageType.Update(ref state);
        
        CollectEx collect;
        collect.time = SystemAPI.Time.ElapsedTime;
        collect.bulletDefinitionType = __bulletDefinitionType;
        collect.instanceType = __instanceType;
        collect.inputMessageType = __inputMessageType;
        collect.activeIndexType = __activeIndexType;
        collect.bulletActiveIndexType = __bulletActiveIndexType;
        collect.bulletStatusType = __bulletStatusType;
        collect.statusType = __statusType;
        collect.outputMessageType = __outputMessageType;
        
        state.Dependency = collect.ScheduleParallelByRef(__group, state.Dependency);
    }
}
