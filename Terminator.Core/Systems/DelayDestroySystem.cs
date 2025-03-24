using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
public partial struct DelayDestroySystem : ISystem
{
    private struct Apply
    {
        public bool isFixedFrameUpdated;
        public float deltaTime;
        public NativeArray<Entity> entityArray;
        public NativeArray<DelayDestroy> delayDestroys;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var delayDestroy = delayDestroys[index];
            delayDestroy.time -= deltaTime;
            if (delayDestroy.time > 0.0f || !isFixedFrameUpdated)
                delayDestroys[index] = delayDestroy;
            else
                entityManager.DestroyEntity(0, entityArray[index]);
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public bool isFixedFrameUpdated;
        public float deltaTime;
        public EntityTypeHandle entityType;
        public ComponentTypeHandle<DelayDestroy> delayDestroyType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.isFixedFrameUpdated = isFixedFrameUpdated;
            apply.deltaTime = deltaTime;
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.delayDestroys = chunk.GetNativeArray(ref delayDestroyType);
            apply.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                apply.Execute(i);
        }
    }

    private int __fixedFrameCount;
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<DelayDestroy> __delayDestroyType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __delayDestroyType = state.GetComponentTypeHandle<DelayDestroy>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<DelayDestroy>()
                .Build(ref state);
        
        state.RequireForUpdate<FixedFrame>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        int fixedFrameCount = SystemAPI.GetSingleton<FixedFrame>().count;
        
        __entityType.Update(ref state);
        __delayDestroyType.Update(ref state);
        
        ApplyEx apply;
        apply.isFixedFrameUpdated = fixedFrameCount != __fixedFrameCount;
        apply.deltaTime = SystemAPI.Time.DeltaTime;
        apply.entityType = __entityType;
        apply.delayDestroyType = __delayDestroyType;
        apply.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);

        __fixedFrameCount = fixedFrameCount;
    }
}
