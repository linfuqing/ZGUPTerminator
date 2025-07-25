using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using ZG;

[BurstCompile, 
 UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true), UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
public partial struct SkillSystem : ISystem
{
    private struct Collect
    {
        public double time;

        [ReadOnly] 
        public NativeArray<BulletLayerMask> bulletLayerMasks;
        [ReadOnly] 
        public NativeArray<BulletDefinitionData> bulletDefinitions;
        [ReadOnly]
        public NativeArray<SkillDefinitionData> instances;
        [ReadOnly]
        public NativeArray<SkillCooldownScale> cooldownScales;
        [ReadOnly] 
        public BufferAccessor<SkillMessage> inputMessages;
        [ReadOnly]
        public BufferAccessor<SkillActiveIndex> activeIndices;

        public BufferAccessor<BulletActiveIndex> bulletActiveIndices;

        public BufferAccessor<BulletStatus> bulletStates;

        public BufferAccessor<SkillStatus> states;

        public BufferAccessor<Message> outputMessages;
        
        public BufferAccessor<MessageParameter> outputMessageParameters;

        public NativeArray<SkillRage> rages;

        //public NativeArray<SkillLayerMask> skillLayerMasks;
        
        public bool Execute(int index)
        {
            var rage = index < rages.Length ? rages[index] : default;

            var outputMessageParameters = index < this.outputMessageParameters.Length
                ? this.outputMessageParameters[index]
                : default;
            var outputMessages = index < this.outputMessages.Length ? this.outputMessages[index] : default;
            var states = this.states[index];
            var bulletStates = this.bulletStates[index];
            var bulletActiveIndices = this.bulletActiveIndices[index];
            int layerMask = index < bulletLayerMasks.Length ? bulletLayerMasks[index].value : 0;
            bool result = instances[index].definition.Value.Update(
                layerMask, 
                index < cooldownScales.Length ? cooldownScales[index].value : 1.0f, 
                time,
                inputMessages[index],
                activeIndices[index], 
                ref bulletActiveIndices, 
                ref bulletStates, 
                ref states, 
                ref outputMessages, 
                ref outputMessageParameters, 
                ref bulletDefinitions[index].definition.Value, 
                ref rage.value);

            /*if (index < bulletLayerMasks.Length)
            {
                BulletLayerMask bulletLayerMask;
                if (index < skillLayerMasks.Length)
                {
                    var skillLayerMask = skillLayerMasks[index];
                    bulletLayerMask = bulletLayerMasks[index];
                    bulletLayerMask.value &= ~skillLayerMask.value;
                    bulletLayerMask.value |= layerMask;

                    skillLayerMask.value = layerMask;
                    skillLayerMasks[index] = skillLayerMask;
                }
                else
                    bulletLayerMask.value = layerMask;

                bulletLayerMasks[index] = bulletLayerMask;
            }*/

            if (index < rages.Length)
                rages[index] = rage;

            return result;
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentTypeHandle<BulletLayerMask> bulletLayerMaskType;
        [ReadOnly]
        public ComponentTypeHandle<BulletDefinitionData> bulletDefinitionType;
        [ReadOnly]
        public ComponentTypeHandle<SkillDefinitionData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<SkillCooldownScale> cooldownScaleType;
        [ReadOnly] 
        public BufferTypeHandle<SkillMessage> inputMessageType;
        [ReadOnly]
        public BufferTypeHandle<SkillActiveIndex> activeIndexType;

        public BufferTypeHandle<BulletActiveIndex> bulletActiveIndexType;

        public BufferTypeHandle<BulletStatus> bulletStatusType;

        public BufferTypeHandle<SkillStatus> statusType;

        public BufferTypeHandle<Message> outputMessageType;
        
        public BufferTypeHandle<MessageParameter> outputMessageParameterType;

        public ComponentTypeHandle<SkillRage> rageType;

        //public ComponentTypeHandle<SkillLayerMask> skillLayerMaskType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            //long hash = math.aslong(time);
            
            Collect collect;
            collect.time = time;
            //collect.random = Random.CreateFromIndex((uint)(unfilteredChunkIndex ^ (int)(hash >> 32) ^ (int)hash));
            collect.bulletLayerMasks = chunk.GetNativeArray(ref bulletLayerMaskType);
            collect.bulletDefinitions = chunk.GetNativeArray(ref bulletDefinitionType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.cooldownScales = chunk.GetNativeArray(ref cooldownScaleType);
            collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            collect.activeIndices = chunk.GetBufferAccessor(ref activeIndexType);
            collect.bulletActiveIndices = chunk.GetBufferAccessor(ref bulletActiveIndexType);
            collect.bulletStates = chunk.GetBufferAccessor(ref bulletStatusType);
            collect.states = chunk.GetBufferAccessor(ref statusType);
            collect.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            collect.outputMessageParameters = chunk.GetBufferAccessor(ref outputMessageParameterType);
            collect.rages = chunk.GetNativeArray(ref rageType);
            //collect.skillLayerMasks = chunk.GetNativeArray(ref skillLayerMaskType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(collect.Execute(i))
                    chunk.SetComponentEnabled(ref outputMessageType, i, true);
            }
        }
    }
    
    private ComponentTypeHandle<BulletLayerMask> __bulletLayerMaskType;

    private ComponentTypeHandle<BulletDefinitionData> __bulletDefinitionType;

    private ComponentTypeHandle<SkillDefinitionData> __instanceType;

    private ComponentTypeHandle<SkillCooldownScale> __cooldownScaleType;
    
    private BufferTypeHandle<SkillMessage> __inputMessageType;
    
    private BufferTypeHandle<SkillActiveIndex> __activeIndexType;

    private BufferTypeHandle<BulletActiveIndex> __bulletActiveIndexType;

    private BufferTypeHandle<BulletStatus> __bulletStatusType;

    private BufferTypeHandle<SkillStatus> __statusType;
    
    private BufferTypeHandle<Message> __outputMessageType;

    private BufferTypeHandle<MessageParameter> __outputMessageParameterType;

    private ComponentTypeHandle<SkillRage> __rageType;

    //private ComponentTypeHandle<SkillLayerMask> __skillLayerMaskType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __bulletLayerMaskType = state.GetComponentTypeHandle<BulletLayerMask>(true);
        __bulletDefinitionType = state.GetComponentTypeHandle<BulletDefinitionData>(true);
        __instanceType = state.GetComponentTypeHandle<SkillDefinitionData>(true);
        __cooldownScaleType = state.GetComponentTypeHandle<SkillCooldownScale>(true);
        __inputMessageType = state.GetBufferTypeHandle<SkillMessage>(true);
        __activeIndexType = state.GetBufferTypeHandle<SkillActiveIndex>(true);
        __bulletActiveIndexType = state.GetBufferTypeHandle<BulletActiveIndex>();
        __bulletStatusType = state.GetBufferTypeHandle<BulletStatus>();
        __statusType = state.GetBufferTypeHandle<SkillStatus>();
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __outputMessageParameterType = state.GetBufferTypeHandle<MessageParameter>();
        __rageType = state.GetComponentTypeHandle<SkillRage>();
        //__skillLayerMaskType = state.GetComponentTypeHandle<SkillLayerMask>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SkillDefinitionData, SkillActiveIndex>()
                .WithAllRW<BulletActiveIndex, SkillStatus>()
                .Build(ref state);
        
        state.RequireForUpdate<FixedFrame>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __bulletLayerMaskType.Update(ref state);
        __bulletDefinitionType.Update(ref state);
        __instanceType.Update(ref state);
        __cooldownScaleType.Update(ref state);
        __inputMessageType.Update(ref state);
        __activeIndexType.Update(ref state);
        __bulletActiveIndexType.Update(ref state);
        __bulletStatusType.Update(ref state);
        __statusType.Update(ref state);
        __outputMessageType.Update(ref state);
        __outputMessageParameterType.Update(ref state);
        __rageType.Update(ref state);
        //__skillLayerMaskType.Update(ref state);
        
        CollectEx collect;
        collect.time = SystemAPI.GetSingleton<FixedFrame>().elapsedTime;//SystemAPI.Time.ElapsedTime;
        collect.bulletLayerMaskType = __bulletLayerMaskType;
        collect.bulletDefinitionType = __bulletDefinitionType;
        collect.instanceType = __instanceType;
        collect.cooldownScaleType = __cooldownScaleType;
        collect.inputMessageType = __inputMessageType;
        collect.activeIndexType = __activeIndexType;
        collect.bulletActiveIndexType = __bulletActiveIndexType;
        collect.bulletStatusType = __bulletStatusType;
        collect.statusType = __statusType;
        collect.outputMessageType = __outputMessageType;
        collect.outputMessageParameterType = __outputMessageParameterType;
        collect.rageType = __rageType;
        //collect.skillLayerMaskType = __skillLayerMaskType;
        
        state.Dependency = collect.ScheduleParallelByRef(__group, state.Dependency);
    }
}
