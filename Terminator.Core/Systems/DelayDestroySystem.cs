using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

[BurstCompile, 
 UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true), 
 UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial struct DelayDestroySystem : ISystem
{
    private struct Element : IComparable<Element>
    {
        public double time;

        public Entity entity;

        public int CompareTo(Element other)
        {
            return other.time.CompareTo(time);
        }

        public override int GetHashCode()
        {
            return entity.GetHashCode();
        }
    }
    
    private struct Apply
    {
        public double time;
        
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        
        [ReadOnly]
        public NativeArray<DelayDestroy> delayDestroys;
        
        public NativeList<Element> elements;
        
        public void Execute(int index)
        {
            Element element;
            element.entity = entityArray[index];
            int numElements = elements.Length, elementIndex = -1;
            for (int i = 0; i < numElements; ++i)
            {
                if (elements[i].entity == element.entity)
                {
                    elementIndex = i;

                    break;
                }
            }
            
            var delayDestroy = delayDestroys[index];
            element.time = delayDestroy.startTime > math.DBL_MIN_NORMAL ? delayDestroy.startTime : time;
            element.time += delayDestroy.time;
            if (elementIndex == -1)
                elements.Add(element);
            else
                elements[elementIndex] = element;
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public EntityTypeHandle entityType;
        
        public ComponentTypeHandle<DelayDestroy> delayDestroyType;

        public NativeList<Element> elements;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.time = time;
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.delayDestroys = chunk.GetNativeArray(ref delayDestroyType);
            apply.elements = elements;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                apply.Execute(i);
            
            chunk.SetComponentEnabledForAll(ref delayDestroyType, false);
        }
    }

    private double __time;
    private EntityTypeHandle __entityType;
    private BufferLookup<Child> __children;
    private ComponentLookup<DelayDestroy> __delayDestroys;
    private ComponentTypeHandle<DelayDestroy> __delayDestroyType;
    private NativeList<Element> __elements;
    private NativeList<Entity> __entities;
    
    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __delayDestroyType = state.GetComponentTypeHandle<DelayDestroy>();
        __delayDestroys = state.GetComponentLookup<DelayDestroy>(true);
        __children = state.GetBufferLookup<Child>(true);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAllRW<DelayDestroy>()
                .Build(ref state);
        
        state.RequireForUpdate<FixedFrame>();
        
        __elements = new NativeList<Element>(Allocator.Persistent);
        
        __entities = new NativeList<Entity>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __entities.Dispose();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double time = SystemAPI.GetSingleton<FixedFrame>().elapsedTime;
        if (time > __time)
        {
            __time = time;
            
            __entities.Clear();
            __children.Update(ref state);
            __delayDestroys.Update(ref state);

            DelayDestroy delayDestroy;
            int numElements = __elements.Length;
            for (int i = numElements - 1; i >= 0; --i)
            {
                ref var element = ref __elements.ElementAt(i);
                if (element.time > time)
                {
                    if (++i < numElements)
                        __elements.RemoveRange(i, numElements - i);

                    break;
                }

                if (__delayDestroys.TryGetComponent(element.entity, out delayDestroy))
                {
                    element.time = delayDestroy.time + delayDestroy.startTime;
                    if (element.time > time)
                    {
                        if (++i < numElements)
                            __elements.RemoveRange(i, numElements - i);

                        __elements.Sort();
                        numElements = __elements.Length;
                        i = numElements;
                    }
                    else
                        __Destroy(element.entity, __children, ref __entities);
                }
            }

            state.EntityManager.DestroyEntity(__entities.AsArray());

            __entityType.Update(ref state);
            __delayDestroyType.Update(ref state);

            ApplyEx apply;
            apply.time = time;
            apply.entityType = __entityType;
            apply.delayDestroyType = __delayDestroyType;
            apply.elements = __elements;
            state.Dependency = apply.ScheduleByRef(__group, state.Dependency);
        }
    }
    
    private static void __Destroy(
        in Entity entity, 
        in BufferLookup<Child> children, 
        ref NativeList<Entity> entities)
    {
        if (children.TryGetBuffer(entity, out var buffer))
        {
            foreach (var child in buffer)
                __Destroy(child.Value, children, ref entities);
        }
        
        entities.Add(entity);
    }
}
