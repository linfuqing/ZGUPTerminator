using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

//[UpdateBefore(typeof(TransformSystemGroup))]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true), UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
public partial struct AnimationCurveSystem : ISystem
{
    private struct Active
    {
        public double time;

        [ReadOnly] 
        public BufferLookup<Child> children;
        
        [ReadOnly] 
        public NativeArray<AnimationCurveRoot> roots;

        [ReadOnly]
        public NativeArray<AnimationCurveTime> times;

        [ReadOnly] 
        public NativeArray<AnimationCurveSpeed> speeds;

        [ReadOnly]
        public NativeArray<AnimationCurveActive> actives;
        
        [ReadOnly]
        public BufferAccessor<AnimationCurveEntity> entities;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var time = times[index];
            actives[index].Evaluate(
                index < roots.Length ? roots[index].length : 0.0f,
                time.value,
                time.GetDelta(this.time) * speeds[index].value, 
                entities[index], 
                children, 
                ref entityManager);
        }
    }

    [BurstCompile]
    private struct ActiveEx : IJobChunk
    {
        public double time;
        
        [ReadOnly] 
        public BufferLookup<Child> children;

        [ReadOnly] 
        public ComponentTypeHandle<AnimationCurveRoot> rootType;

        [ReadOnly]
        public ComponentTypeHandle<AnimationCurveTime> timeType;

        [ReadOnly] 
        public ComponentTypeHandle<AnimationCurveSpeed> speedType;

        [ReadOnly]
        public ComponentTypeHandle<AnimationCurveActive> activeType;
        
        [ReadOnly]
        public BufferTypeHandle<AnimationCurveEntity> entityType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Active active;
            active.time = time;
            active.children = children;
            active.roots = chunk.GetNativeArray(ref rootType);
            active.times = chunk.GetNativeArray(ref timeType);
            active.speeds = chunk.GetNativeArray(ref speedType);
            active.actives = chunk.GetNativeArray(ref activeType);
            active.entities = chunk.GetBufferAccessor(ref entityType);
            active.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                active.Execute(i);
        }
    }
    
    private struct Update
    {
        public double time;

        [ReadOnly] 
        public BufferAccessor<AnimationCurveMessage> inputMessages;
        
        [ReadOnly] 
        public NativeArray<AnimationCurveRoot> roots;

        [ReadOnly] 
        public NativeArray<AnimationCurveSpeed> speeds;
        
        public NativeArray<AnimationCurveTime> times;

        [ReadOnly] 
        public BufferAccessor<Message> outputMessages;
        
        public bool Execute(int index)
        {
            bool isSendMessage = false;
            float speed = speeds[index].value;
            var time = times[index];
            double oldTime = time.value;
            time.value += time.GetDelta(this.time) * speed;
            if (time.value > oldTime)
            {
                Message message;
                var outputMessages = index < this.outputMessages.Length ? this.outputMessages[index] : default;
                var inputMessages = index < this.inputMessages.Length ? this.inputMessages[index] : default;
                if (index < roots.Length)
                {
                    var root = roots[index];
                    if (time.value > root.length)
                    {
                        if (inputMessages.IsCreated && outputMessages.IsCreated)
                        {
                            foreach (var inputMessage in inputMessages)
                            {
                                if (inputMessage.time > oldTime)
                                {
                                    message.key = 0;
                                    message.name = inputMessage.messageName;
                                    message.value = inputMessage.messageValue;

                                    outputMessages.Add(message);

                                    isSendMessage = true;
                                }
                            }
                        }

                        time.value -= root.length;

                        time.start = this.time - time.value / speed;

                        oldTime = 0;
                    }
                }

                if (inputMessages.IsCreated && outputMessages.IsCreated)
                {
                    foreach (var inputMessage in inputMessages)
                    {
                        if (inputMessage.time > oldTime && inputMessage.time <= time.value)
                        {
                            message.key = 0;
                            message.name = inputMessage.messageName;
                            message.value = inputMessage.messageValue;

                            outputMessages.Add(message);

                            isSendMessage = true;
                        }
                    }
                }
            }
            
            time.elapsed = this.time;
            times[index] = time;

            return isSendMessage;
        }
    }

    [BurstCompile]
    private struct UpdateEx : IJobChunk
    {
        public double time;
        
        [ReadOnly] 
        public BufferTypeHandle<AnimationCurveMessage> inputMessageType;
        
        [ReadOnly] 
        public ComponentTypeHandle<AnimationCurveRoot> rootType;

        [ReadOnly] 
        public ComponentTypeHandle<AnimationCurveSpeed> speedType;
        
        public ComponentTypeHandle<AnimationCurveTime> timeType;

        public BufferTypeHandle<Message> outputMessageType;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Update update;
            update.time = time;
            update.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            update.roots = chunk.GetNativeArray(ref rootType);
            update.speeds = chunk.GetNativeArray(ref speedType);
            update.times = chunk.GetNativeArray(ref timeType);
            update.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(update.Execute(i))
                    chunk.SetComponentEnabled(ref outputMessageType, i, true);
            }
        }
    }
    
    private struct Evaluate
    {
        [ReadOnly]
        public ComponentLookup<Parent> parents;
        
        [ReadOnly]
        public ComponentLookup<AnimationCurveTime> times;
        
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<AnimationCurveTransform> instances;

        public NativeArray<AnimationCurveMotion> motions;

        public NativeArray<LocalTransform> localTransforms;

        public void Execute(int index)
        {
            LocalTransform source = localTransforms[index], destination = source;
            if (instances[index].Evaluate(entityArray[index], parents, times, ref destination))
            {
                if (index < motions.Length)
                {
                    var motion = motions[index];

                    var localTransform = motion.localTransform.InverseTransformTransform(destination);

                    motion.localTransform = destination;
                    motions[index] = motion;
                    
                    destination = source.TransformTransform(localTransform);
                }
                
                localTransforms[index] = destination;
            }
        }
    }

    [BurstCompile]
    private struct EvaluateEx : IJobChunk
    {
        [ReadOnly]
        public ComponentLookup<Parent> parents;
        
        [ReadOnly]
        public ComponentLookup<AnimationCurveTime> times;
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<AnimationCurveTransform> instanceType;

        public ComponentTypeHandle<AnimationCurveMotion> motionType;

        public ComponentTypeHandle<LocalTransform> localTransformType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Evaluate evaluate;
            evaluate.parents = parents;
            evaluate.times = times;
            evaluate.entityArray = chunk.GetNativeArray(entityType);
            evaluate.instances = chunk.GetNativeArray(ref instanceType);
            evaluate.motions = chunk.GetNativeArray(ref motionType);
            evaluate.localTransforms = chunk.GetNativeArray(ref localTransformType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                evaluate.Execute(i);
        }
    }

    private BufferLookup<Child> __children;

    private ComponentLookup<Parent> __parents;
    
    private ComponentLookup<AnimationCurveTime> __times;
    
    private ComponentTypeHandle<AnimationCurveTime> __timeType;

    private ComponentTypeHandle<AnimationCurveRoot> __rootType;
    private ComponentTypeHandle<AnimationCurveSpeed> __speedType;
    
    private ComponentTypeHandle<AnimationCurveTransform> __instanceType;

    private ComponentTypeHandle<AnimationCurveMotion> __motionType;

    private ComponentTypeHandle<LocalTransform> __localTransformType;

    private ComponentTypeHandle<AnimationCurveActive> __activeType;
        
    private BufferTypeHandle<AnimationCurveEntity> __curveEntityType;

    private BufferTypeHandle<AnimationCurveMessage> __inputMessageType;
    private BufferTypeHandle<Message> __outputMessageType;

    private EntityTypeHandle __entityType;

    private EntityQuery __groupToActive;
    private EntityQuery __groupToUpdate;
    private EntityQuery __groupToEvaluate;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __children = state.GetBufferLookup<Child>(true);
        __parents = state.GetComponentLookup<Parent>(true);
        __times = state.GetComponentLookup<AnimationCurveTime>(true);
        __timeType = state.GetComponentTypeHandle<AnimationCurveTime>();
        __rootType = state.GetComponentTypeHandle<AnimationCurveRoot>(true);
        __speedType = state.GetComponentTypeHandle<AnimationCurveSpeed>(true);
        __instanceType = state.GetComponentTypeHandle<AnimationCurveTransform>(true);
        __motionType = state.GetComponentTypeHandle<AnimationCurveMotion>();
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>();
        __activeType = state.GetComponentTypeHandle<AnimationCurveActive>(true);
        __curveEntityType = state.GetBufferTypeHandle<AnimationCurveEntity>(true);
        __inputMessageType = state.GetBufferTypeHandle<AnimationCurveMessage>(true);
        __outputMessageType = state.GetBufferTypeHandle<Message>();
        __entityType = state.GetEntityTypeHandle();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToActive = builder
                .WithAll<AnimationCurveTime, AnimationCurveActive, AnimationCurveEntity>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToUpdate = builder
                .WithAll<AnimationCurveSpeed>()
                .WithAllRW<AnimationCurveTime>()
                .Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToEvaluate = builder
                .WithAll<AnimationCurveTransform>()
                .WithAllRW<LocalTransform>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup)
                .Build(ref state);
        
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double time = SystemAPI.Time.ElapsedTime;
        __children.Update(ref state);
        __rootType.Update(ref state);
        __timeType.Update(ref state);
        __speedType.Update(ref state);
        __activeType.Update(ref state);
        __curveEntityType.Update(ref state);
        
        ActiveEx active;
        active.time = time;
        active.children = __children;
        active.rootType = __rootType;
        active.timeType = __timeType;
        active.speedType = __speedType;
        active.activeType = __activeType;
        active.entityType = __curveEntityType;
        active.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var jobHandle = active.ScheduleParallelByRef(__groupToActive, state.Dependency);

        __inputMessageType.Update(ref state);
        __outputMessageType.Update(ref state);
        
        UpdateEx update;
        update.time = time;
        update.inputMessageType = __inputMessageType;
        update.rootType = __rootType;
        update.speedType = __speedType;
        update.timeType = __timeType;
        update.outputMessageType = __outputMessageType;
        jobHandle = update.ScheduleParallelByRef(__groupToUpdate, jobHandle);

        __parents.Update(ref state);
        __times.Update(ref state);
        __entityType.Update(ref state);
        __instanceType.Update(ref state);
        __motionType.Update(ref state);
        __localTransformType.Update(ref state);
        
        EvaluateEx evaluate;
        evaluate.parents = __parents;
        evaluate.times = __times;
        evaluate.entityType = __entityType;
        evaluate.instanceType = __instanceType;
        evaluate.motionType = __motionType;
        evaluate.localTransformType = __localTransformType;

        state.Dependency = evaluate.ScheduleParallelByRef(__groupToEvaluate, jobHandle);
    }
}
