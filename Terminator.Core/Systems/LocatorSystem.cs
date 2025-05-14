using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

public partial struct LocatorSystem : ISystem
{
    private struct Locate
    {
        public double time;

        public Random random;
        
        [ReadOnly]
        public NativeArray<LocalToWorld> localToWorlds;

        [ReadOnly]
        public NativeArray<LocatorDefinitionData> instances;

        [ReadOnly]
        public NativeArray<LocatorSpeed> speeds;
        
        public NativeArray<LocatorVelocity> velocities;

        public NativeArray<LocatorStatus> states;

        public NativeArray<LocatorTime> times;
        
        public BufferAccessor<DelayTime> delayTimes;

        public BufferAccessor<MessageParameter> messageParameters;

        public BufferAccessor<Message> outputMessages;

        [ReadOnly]
        public BufferAccessor<LocatorMessage> inputMessages;

        public bool Execute(int index)
        {
            var status = states[index];
            if (math.max(status.time, velocities[index].time) > time)
                return false;
            
            var delayTimes = index < this.delayTimes.Length ? this.delayTimes[index] : default;
            if (DelayTime.IsDelay(ref delayTimes, time, out float delayTime))
            {
                status.time += delayTime;

                states[index] = status;

                return false;
            }

            ref var definition = ref instances[index].definition.Value;
            int numActions = definition.actions.Length;
            if (numActions <= status.actionIndex)
                return false;
            
            ref var action = ref definition.actions[status.actionIndex];

            bool result = false;
            if (status.time > math.FLT_MIN_NORMAL)
            {
                LocatorVelocity velocity;

                velocity.up = action.up;
                velocity.value = float3.zero;

                float speed = speeds[index].value;
                int numAreaIndices = action.areaIndices.Length;
                if (numAreaIndices > 0)
                {
                    int areaIndex = action.areaIndices[random.NextInt(numAreaIndices)];
                    ref var aabb = ref definition.areas[areaIndex].aabb;
                    float3 position = random.NextFloat3(aabb.Min, aabb.Max);
                    var localToWorld = localToWorlds[index];
                    velocity.value = position - localToWorld.Position;
                    velocity.time = 0.0;
                    if (action.time > math.FLT_MIN_NORMAL)
                    {
                        velocity.value /= action.time;

                        velocity.time += action.time;
                    }
                    else
                    {
                        float distanceSQ = math.lengthsq(velocity.value);
                        if (distanceSQ > math.FLT_MIN_NORMAL)
                        {
                            float distanceR = math.rsqrt(distanceSQ), timeR = distanceR * speed;
                            velocity.value *= timeR;
                            velocity.time += 1.0f / timeR;
                        }
                        else
                            velocity.value = float3.zero;
                    }

                    velocity.actionIndex = status.actionIndex;//action.messageIndex;
                    
                    LocatorTime time;
                    time.value = status.time;
                    times[index] = time;

                    status.time += velocity.time;

                    velocity.time = status.time;
                    velocity.direction = action.direction;
                    velocities[index] = velocity;

                    if (++status.actionIndex == numActions)
                    {
                        status.actionIndex = 0;

                        status.time += definition.cooldown;
                    }

                    status.time += definition.actions[status.actionIndex].startTime;

                    result = true;
                }
                
                int numMessageIndices = action.messageIndices.Length;
                    if (numMessageIndices > 0 && 
                        index < outputMessages.Length && 
                        index < messageParameters.Length)
                    {
                        var messageParameters = this.messageParameters[index];
                        var inputMessages = this.inputMessages[index];
                        LocatorMessage inputMessage;
                        Message outputMessage;
                        MessageParameter messageParameter;
                        int numInputMessages = inputMessages.Length, messageIndex;
                        for (int i = 0; i < numMessageIndices; ++i)
                        {
                            messageIndex = action.messageIndices[i];
                            if (messageIndex >= numInputMessages)
                                continue;

                            inputMessage = inputMessages[messageIndex];
                            if ((LocatorMessageType.Start & inputMessage.type) != LocatorMessageType.Start)
                                continue;

                            outputMessage.key = random.NextInt();
                            outputMessage.name = inputMessage.name;
                            outputMessage.value = inputMessage.value;
                            outputMessages[index].Add(outputMessage);

                            messageParameter.messageKey = outputMessage.key;

                            var axis = math.float2(velocity.value.x, velocity.value.z) / speed;
                            messageParameter.value = math.asint(axis.x);
                            messageParameter.id = 0;
                            messageParameters.Add(messageParameter);

                            messageParameter.value = math.asint(axis.y);
                            messageParameter.id = 1;
                            messageParameters.Add(messageParameter);
                        }
                    }
            }
            else
                status.time = time + action.startTime;

            states[index] = status;

            return result;
        }
    }

    [BurstCompile]
    private struct LocateEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        [ReadOnly]
        public ComponentTypeHandle<LocatorDefinitionData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<LocatorSpeed> speedType;
        
        public ComponentTypeHandle<LocatorVelocity> velocityType;

        public ComponentTypeHandle<LocatorTime> timeType;
        
        public ComponentTypeHandle<LocatorStatus> statusType;
        
        public ComponentTypeHandle<LookAtTarget> lookAtTargetType;

        public BufferTypeHandle<DelayTime> delayTimeType;

        public BufferTypeHandle<MessageParameter> messageParameterType;

        public BufferTypeHandle<Message> outputMessageType;

        [ReadOnly]
        public BufferTypeHandle<LocatorMessage> inputMessageType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            long hash = math.aslong(time);
            
            Locate locate;
            locate.time = time;
            locate.random = Random.CreateFromIndex((uint)((int)hash ^ (hash >> 32) ^ unfilteredChunkIndex));
            locate.localToWorlds = chunk.GetNativeArray(ref localToWorldType);
            locate.instances = chunk.GetNativeArray(ref instanceType);
            locate.speeds = chunk.GetNativeArray(ref speedType);
            locate.velocities = chunk.GetNativeArray(ref velocityType);
            locate.times = chunk.GetNativeArray(ref timeType);
            locate.states = chunk.GetNativeArray(ref statusType);
            locate.delayTimes = chunk.GetBufferAccessor(ref delayTimeType);
            locate.messageParameters = chunk.GetBufferAccessor(ref messageParameterType);
            locate.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            locate.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);

            bool hasLookAtTarget = chunk.Has(ref lookAtTargetType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (locate.Execute(i))
                {
                    chunk.SetComponentEnabled(ref velocityType, i, true);
                    
                    if(hasLookAtTarget)
                        chunk.SetComponentEnabled(ref lookAtTargetType, i, false);
                    
                    chunk.SetComponentEnabled(ref outputMessageType, i, true);
                }
            }
        }
    }

    private struct Update
    {
        public double time;

        public Random random;

        //[ReadOnly]
        //public NativeArray<LocalToWorld> localToWorlds;

        [ReadOnly]
        public NativeArray<LocatorDefinitionData> instances;

        [ReadOnly]
        public NativeArray<LocatorVelocity> velocities;

        public NativeArray<LocatorTime> times;

        public NativeArray<LocalTransform> localTransforms;

        public BufferAccessor<DelayTime> delayTimes;

        public BufferAccessor<LocatorMessage> inputMessages;
        
        public BufferAccessor<Message> outputMessages;

        public BufferAccessor<MessageParameter> messageParameters;

        public bool Execute(int index)
        {
            var time = times[index];
            if (time.value > this.time)
                return true;
            
            var velocity = velocities[index];
            var delayTimes = index < this.delayTimes.Length ? this.delayTimes[index] : default;
            if (DelayTime.IsDelay(ref delayTimes, this.time, out float delayTime))
            {
                velocity.time += delayTime;
                velocities[index] = velocity;
                
                time.value += delayTime;
                times[index] = time;
                
                return true;
            }

            double nextTime = math.min(velocity.time, this.time);

            var localTransform = localTransforms[index];
            float3 distance = velocity.value * (float)(nextTime - time.value);

            if (LocatorDirection.DontCare != velocity.direction)
            {
                float3 up = velocity.up,
                    forward = LocatorDirection.Backward == velocity.direction ? -distance : distance;
                if (math.lengthsq(up) > math.FLT_MIN_NORMAL)
                    forward -= math.project(forward, up);
                else
                    up = math.up();

                localTransform.Rotation = quaternion.LookRotationSafe(forward, up);
            }

            localTransform.Position += distance;
            localTransforms[index] = localTransform;
            
            if (nextTime < velocity.time)
            {
                time.value = nextTime;
                times[index] = time;

                return true;
            }

            ref var action = ref instances[index].definition.Value.actions[velocity.actionIndex];
            int numMessageIndices = action.messageIndices.Length;
            if (numMessageIndices > 0 && 
                index < outputMessages.Length && 
                index < messageParameters.Length)
            {
                var messageParameters = this.messageParameters[index];
                var inputMessages = this.inputMessages[index];
                LocatorMessage inputMessage;
                Message outputMessage;
                MessageParameter messageParameter;
                int numInputMessages = inputMessages.Length, messageIndex;
                for (int i = 0; i < numMessageIndices; ++i)
                {
                    messageIndex = action.messageIndices[i];
                    if (messageIndex >= numInputMessages)
                        continue;

                    inputMessage = inputMessages[messageIndex];
                    if ((LocatorMessageType.End & inputMessage.type) != LocatorMessageType.End)
                        continue;

                    outputMessage.key = random.NextInt();
                    outputMessage.name = inputMessage.name;
                    outputMessage.value = inputMessage.value;
                    outputMessages[index].Add(outputMessage);

                    messageParameter.messageKey = outputMessage.key;

                    messageParameter.value = 0;
                    messageParameter.id = 0;
                    messageParameters.Add(messageParameter);

                    messageParameter.value = 0;
                    messageParameter.id = 1;
                    messageParameters.Add(messageParameter);
                }
            }

            return false;
        }
    }

    [BurstCompile]
    private struct UpdateEx : IJobChunk
    {
        public double time;

        //[ReadOnly]
        //public ComponentTypeHandle<LocalToWorld> localToWorldType;

        [ReadOnly]
        public ComponentTypeHandle<LocatorDefinitionData> instanceType;

        public ComponentTypeHandle<LocatorVelocity> velocityType;

        public ComponentTypeHandle<LocatorTime> timeType;

        public ComponentTypeHandle<LocalTransform> localTransformType;

        public ComponentTypeHandle<LookAtTarget> lookAtTargetType;

        public BufferTypeHandle<DelayTime> delayTimeType;

        public BufferTypeHandle<MessageParameter> messageParameterType;

        public BufferTypeHandle<Message> outputMessageType;

        [ReadOnly]
        public BufferTypeHandle<LocatorMessage> inputMessageType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ulong hash = math.asulong(time);
            
            Update update;
            update.time = time;
            update.random = Random.CreateFromIndex((uint)hash ^ (uint)(hash >> 32) ^ (uint)unfilteredChunkIndex);
            //update.localToWorlds = chunk.GetNativeArray(ref localToWorldType);
            update.instances = chunk.GetNativeArray(ref instanceType);
            update.velocities = chunk.GetNativeArray(ref velocityType);
            update.times = chunk.GetNativeArray(ref timeType);
            update.localTransforms = chunk.GetNativeArray(ref localTransformType);
            update.delayTimes = chunk.GetBufferAccessor(ref delayTimeType);
            update.messageParameters = chunk.GetBufferAccessor(ref messageParameterType);
            update.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);
            update.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);

            bool hasLookAtTarget = chunk.Has(ref lookAtTargetType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (!update.Execute(i))
                {
                    chunk.SetComponentEnabled(ref velocityType, i, false);
                    
                    if(hasLookAtTarget)
                        chunk.SetComponentEnabled(ref lookAtTargetType, i, true);

                    chunk.SetComponentEnabled(ref outputMessageType, i, true);
                }
            }
        }
    }
    
    
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    private ComponentTypeHandle<LocalTransform> __localTransformType;

    private ComponentTypeHandle<LocatorDefinitionData> __instanceType;

    private ComponentTypeHandle<LocatorSpeed> __speedType;
        
    private ComponentTypeHandle<LocatorVelocity> __velocityType;

    private ComponentTypeHandle<LocatorTime> __timeType;
        
    private ComponentTypeHandle<LocatorStatus> __statusType;

    private ComponentTypeHandle<LookAtTarget> __lookAtTargetType;

    private BufferTypeHandle<DelayTime> __delayTimeType;

    private BufferTypeHandle<MessageParameter> __messageParameterType;

    private BufferTypeHandle<Message> __outputMessageType;

    private BufferTypeHandle<LocatorMessage> __inputMessageType;

    private EntityQuery __groupToLocate;

    private EntityQuery __groupToUpdate;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>(true);
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>();
        __instanceType = state.GetComponentTypeHandle<LocatorDefinitionData>(true);
        __speedType = state.GetComponentTypeHandle<LocatorSpeed>(true);
        __velocityType = state.GetComponentTypeHandle<LocatorVelocity>();
        __timeType = state.GetComponentTypeHandle<LocatorTime>();
        __statusType = state.GetComponentTypeHandle<LocatorStatus>();
        __lookAtTargetType = state.GetComponentTypeHandle<LookAtTarget>();
        __delayTimeType = state.GetBufferTypeHandle<DelayTime>();
        __messageParameterType = state.GetBufferTypeHandle<MessageParameter>();
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __inputMessageType = state.GetBufferTypeHandle<LocatorMessage>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Persistent))
            __groupToLocate = builder
                .WithAll<LocalToWorld, LocatorDefinitionData>()
                .WithAllRW<LocatorTime, LocatorStatus>()
                .WithPresentRW<LocatorVelocity>()
                .Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Persistent))
            __groupToUpdate = builder
                    .WithAll<LocalToWorld, LocatorDefinitionData, LocatorVelocity>()
                    .WithAllRW<LocalTransform, LocatorTime>()
                    .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __localToWorldType.Update(ref state);
        __instanceType.Update(ref state);
        __speedType.Update(ref state);
        __velocityType.Update(ref state);
        __timeType.Update(ref state);
        __statusType.Update(ref state);
        __lookAtTargetType.Update(ref state);
        __delayTimeType.Update(ref state);
        __messageParameterType.Update(ref state);
        __outputMessageType.Update(ref state);
        __inputMessageType.Update(ref state);
        
        double time = SystemAPI.Time.ElapsedTime;
        
        LocateEx locate;
        locate.time = time;
        locate.localToWorldType = __localToWorldType;
        locate.instanceType = __instanceType;
        locate.speedType = __speedType;
        locate.velocityType = __velocityType;
        locate.timeType = __timeType;
        locate.statusType = __statusType;
        locate.lookAtTargetType = __lookAtTargetType;
        locate.delayTimeType = __delayTimeType;
        locate.messageParameterType = __messageParameterType;
        locate.outputMessageType = __outputMessageType;
        locate.inputMessageType = __inputMessageType;
        var jobHandle = locate.ScheduleParallelByRef(__groupToLocate, state.Dependency);

        __localTransformType.Update(ref state);
        
        UpdateEx update;
        update.time = time;
        //update.localToWorldType = __localToWorldType;
        update.instanceType = __instanceType;
        update.velocityType = __velocityType;
        update.timeType = __timeType;
        update.localTransformType = __localTransformType;
        update.lookAtTargetType = __lookAtTargetType;
        update.delayTimeType = __delayTimeType;
        update.messageParameterType = __messageParameterType;
        update.outputMessageType = __outputMessageType;
        update.inputMessageType = __inputMessageType;

        state.Dependency = update.ScheduleParallelByRef(__groupToUpdate, jobHandle);
    }
}
