using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using ZG;

public struct AnimationCurveSingleton : IComponentData
{
    public NativeQueue<Entity> entitiesToEnable;
    public NativeQueue<Entity> entitiesToDisable;
}

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct AnimationCurveInitSystem : ISystem
{
    private NativeQueue<Entity> __entitiesToEnable;
    private NativeQueue<Entity> __entitiesToDisable;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entitiesToEnable = new NativeQueue<Entity>(Allocator.Persistent);
        __entitiesToDisable = new NativeQueue<Entity>(Allocator.Persistent);

        AnimationCurveSingleton singleton;
        singleton.entitiesToEnable = __entitiesToEnable;
        singleton.entitiesToDisable = __entitiesToDisable;
        state.EntityManager.CreateSingleton(singleton);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __entitiesToEnable.Dispose();
        __entitiesToDisable.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();

        var entityManager = state.EntityManager;
        NativeList<Entity> entities = default;
        while (__entitiesToEnable.TryDequeue(out Entity entity))
        {
            if(!entityManager.Exists(entity))
                continue;

            if (!entities.IsCreated)
                entities = new NativeList<Entity>(Allocator.Temp);
            
            entities.Add(entity);
        }

        if (!entities.IsEmpty)
        {
            entityManager.RemoveComponent<Disabled>(entities.AsArray());
            
            entities.Clear();
        }

        while (__entitiesToDisable.TryDequeue(out Entity entity))
        {
            if(!entityManager.Exists(entity))
                continue;

            if (!entities.IsCreated)
                entities = new NativeList<Entity>(Allocator.Temp);
            
            entities.Add(entity);
        }

        if (!entities.IsEmpty)
            entityManager.AddComponent<Disabled>(entities.AsArray());
        
        if (entities.IsCreated)
            entities.Dispose();
        
        AnimationCurveSingleton singleton;
        singleton.entitiesToEnable = __entitiesToEnable;
        singleton.entitiesToDisable = __entitiesToDisable;
        
        SystemAPI.SetSingleton(singleton);
    }
}

[BurstCompile, UpdateAfter(typeof(TransformSystemGroup))]
public partial struct AnimationCurveUpdateSystem : ISystem
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
        public NativeArray<AnimationCurveDelta> deltas;

        [ReadOnly]
        public NativeArray<AnimationCurveSpeed> speeds;

        [ReadOnly]
        public NativeArray<AnimationCurveActive> actives;

        [ReadOnly]
        public BufferAccessor<AnimationCurveEntity> entities;

        public NativeQueue<Entity>.ParallelWriter entitiesToEnable;
        public NativeQueue<Entity>.ParallelWriter entitiesToDisable;
        
        public void Execute(int index)
        {
            actives[index].Evaluate(
                index < roots.Length ? roots[index].length : 0.0f,
                times[index].value,
                deltas[index].Update(this.time) * speeds[index].value,
                entities[index],
                children,
                ref entitiesToEnable, 
                ref entitiesToDisable);
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
        public ComponentTypeHandle<AnimationCurveDelta> deltaType;

        [ReadOnly]
        public ComponentTypeHandle<AnimationCurveSpeed> speedType;

        [ReadOnly]
        public ComponentTypeHandle<AnimationCurveActive> activeType;

        [ReadOnly]
        public BufferTypeHandle<AnimationCurveEntity> entityType;

        public NativeQueue<Entity>.ParallelWriter entitiesToEnable;
        public NativeQueue<Entity>.ParallelWriter entitiesToDisable;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Active active;
            active.time = time;
            active.children = children;
            active.roots = chunk.GetNativeArray(ref rootType);
            active.times = chunk.GetNativeArray(ref timeType);
            active.deltas = chunk.GetNativeArray(ref deltaType);
            active.speeds = chunk.GetNativeArray(ref speedType);
            active.actives = chunk.GetNativeArray(ref activeType);
            active.entities = chunk.GetBufferAccessor(ref entityType);
            active.entitiesToEnable = entitiesToEnable;
            active.entitiesToDisable = entitiesToDisable;

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

        public NativeArray<AnimationCurveDelta> deltas;

        [ReadOnly]
        public BufferAccessor<Message> outputMessages;

        public bool Execute(int index)
        {
            bool isSendMessage = false;
            float speed = speeds[index].value;
            var delta = deltas[index];
            var time = times[index];
            double oldTime = time.value;
            time.value += delta.Update(this.time) * speed;
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

                        delta.start = this.time - time.value / speed;

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

            delta.elapsed = this.time;

            deltas[index] = delta;
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

        public ComponentTypeHandle<AnimationCurveDelta> deltaType;

        public BufferTypeHandle<Message> outputMessageType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Update update;
            update.time = time;
            update.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            update.roots = chunk.GetNativeArray(ref rootType);
            update.speeds = chunk.GetNativeArray(ref speedType);
            update.times = chunk.GetNativeArray(ref timeType);
            update.deltas = chunk.GetNativeArray(ref deltaType);
            update.outputMessages = chunk.GetBufferAccessor(ref outputMessageType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (update.Execute(i))
                    chunk.SetComponentEnabled(ref outputMessageType, i, true);
            }
        }
    }

    private BufferLookup<Child> __children;

    private ComponentTypeHandle<AnimationCurveTime> __timeType;

    private ComponentTypeHandle<AnimationCurveDelta> __deltaType;

    private ComponentTypeHandle<AnimationCurveRoot> __rootType;
    private ComponentTypeHandle<AnimationCurveSpeed> __speedType;

    private ComponentTypeHandle<AnimationCurveActive> __activeType;

    private BufferTypeHandle<AnimationCurveEntity> __curveEntityType;

    private BufferTypeHandle<AnimationCurveMessage> __inputMessageType;
    private BufferTypeHandle<Message> __outputMessageType;

    private EntityQuery __groupToActive;
    private EntityQuery __groupToUpdate;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __children = state.GetBufferLookup<Child>(true);
        __timeType = state.GetComponentTypeHandle<AnimationCurveTime>();
        __deltaType = state.GetComponentTypeHandle<AnimationCurveDelta>();
        __rootType = state.GetComponentTypeHandle<AnimationCurveRoot>(true);
        __speedType = state.GetComponentTypeHandle<AnimationCurveSpeed>(true);
        __activeType = state.GetComponentTypeHandle<AnimationCurveActive>(true);
        __curveEntityType = state.GetBufferTypeHandle<AnimationCurveEntity>(true);
        __inputMessageType = state.GetBufferTypeHandle<AnimationCurveMessage>(true);
        __outputMessageType = state.GetBufferTypeHandle<Message>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToActive = builder
                .WithAll<AnimationCurveTime, AnimationCurveActive, AnimationCurveEntity>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToUpdate = builder
                .WithAll<AnimationCurveSpeed>()
                .WithAllRW<AnimationCurveTime>()
                .Build(ref state);

        state.RequireForUpdate<AnimationCurveSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double time = SystemAPI.Time.ElapsedTime;
        __children.Update(ref state);
        __rootType.Update(ref state);
        __timeType.Update(ref state);
        __deltaType.Update(ref state);
        __speedType.Update(ref state);
        __activeType.Update(ref state);
        __curveEntityType.Update(ref state);

        ActiveEx active;
        active.time = time;
        active.children = __children;
        active.rootType = __rootType;
        active.timeType = __timeType;
        active.deltaType = __deltaType;
        active.speedType = __speedType;
        active.activeType = __activeType;
        active.entityType = __curveEntityType;

        var singleton = SystemAPI.GetSingleton<AnimationCurveSingleton>();
        active.entitiesToEnable = singleton.entitiesToEnable.AsParallelWriter();
        active.entitiesToDisable = singleton.entitiesToDisable.AsParallelWriter();
        var jobHandle = active.ScheduleParallelByRef(__groupToActive, state.Dependency);

        __inputMessageType.Update(ref state);
        __outputMessageType.Update(ref state);

        UpdateEx update;
        update.time = time;
        update.inputMessageType = __inputMessageType;
        update.rootType = __rootType;
        update.speedType = __speedType;
        update.timeType = __timeType;
        update.deltaType = __deltaType;
        update.outputMessageType = __outputMessageType;
        state.Dependency = update.ScheduleParallelByRef(__groupToUpdate, jobHandle);
    }
}

//[UpdateBefore(typeof(TransformSystemGroup))]
[BurstCompile, UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true), UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
public partial struct AnimationCurveSystem : ISystem
{
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

    private ComponentLookup<Parent> __parents;
    
    private ComponentLookup<AnimationCurveTime> __times;
    
    private ComponentTypeHandle<AnimationCurveTransform> __instanceType;

    private ComponentTypeHandle<AnimationCurveMotion> __motionType;

    private ComponentTypeHandle<LocalTransform> __localTransformType;

    private EntityTypeHandle __entityType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __parents = state.GetComponentLookup<Parent>(true);
        __times = state.GetComponentLookup<AnimationCurveTime>(true);
        __instanceType = state.GetComponentTypeHandle<AnimationCurveTransform>(true);
        __motionType = state.GetComponentTypeHandle<AnimationCurveMotion>();
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>();
        __entityType = state.GetEntityTypeHandle();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<AnimationCurveTransform>()
                .WithAllRW<LocalTransform>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup)
                .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __parents.Update(ref state);
        __times.Update(ref state);
        __instanceType.Update(ref state);
        __motionType.Update(ref state);
        __localTransformType.Update(ref state);
        __entityType.Update(ref state);

        EvaluateEx evaluate;
        evaluate.parents = __parents;
        evaluate.times = __times;
        evaluate.entityType = __entityType;
        evaluate.instanceType = __instanceType;
        evaluate.motionType = __motionType;
        evaluate.localTransformType = __localTransformType;

        state.Dependency = evaluate.ScheduleParallelByRef(__group, state.Dependency);
    }
}
