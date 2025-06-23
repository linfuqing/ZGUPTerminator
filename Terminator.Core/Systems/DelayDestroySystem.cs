using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using ZG;

[BurstCompile]
public partial struct DelayDestroySystem : ISystem
{
    private struct Apply
    {
        public bool isFixedFrameUpdated;
        public float deltaTime;
        
        [ReadOnly]
        public BufferLookup<Child> children;

        [ReadOnly]
        public NativeArray<Entity> entityArray;
        public NativeArray<DelayDestroy> delayDestroys;
        
        public NativeArray<CopyMatrixToTransformInstanceID> instanceIDs;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var delayDestroy = delayDestroys[index];
            delayDestroy.time -= deltaTime;
            if (delayDestroy.time > 0.0f || !isFixedFrameUpdated)
                delayDestroys[index] = delayDestroy;
            else
            {
                if (index < instanceIDs.Length)
                {
                    var instanceID = instanceIDs[index];
                    instanceID.isSendMessageOnDestroy = false;
                    instanceIDs[index] = instanceID;
                }
                
                __Destroy(0, entityArray[index], children, ref entityManager);
            }
        }
        
        private static void __Destroy(
            int sortKey, 
            in Entity entity, 
            in BufferLookup<Child> children, 
            ref EntityCommandBuffer.ParallelWriter entityManager)
        {
            if (children.TryGetBuffer(entity, out var buffer))
            {
                foreach (var child in buffer)
                    __Destroy(sortKey - 1, child.Value, children, ref entityManager);
            }
        
            entityManager.DestroyEntity(sortKey, entity);
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public bool isFixedFrameUpdated;
        public float deltaTime;
        [ReadOnly]
        public BufferLookup<Child> children;
        [ReadOnly]
        public EntityTypeHandle entityType;
        public ComponentTypeHandle<DelayDestroy> delayDestroyType;

        public ComponentTypeHandle<CopyMatrixToTransformInstanceID> instanceIDType;

        public EntityCommandBuffer.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.isFixedFrameUpdated = isFixedFrameUpdated;
            apply.deltaTime = deltaTime;
            apply.children = children;
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.delayDestroys = chunk.GetNativeArray(ref delayDestroyType);
            apply.instanceIDs = chunk.GetNativeArray(ref instanceIDType);
            apply.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                apply.Execute(i);
        }
    }

    private int __fixedFrameCount;
    private EntityTypeHandle __entityType;
    private BufferLookup<Child> __children;
    private ComponentTypeHandle<DelayDestroy> __delayDestroyType;
    private ComponentTypeHandle<CopyMatrixToTransformInstanceID> __instanceIDType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __children = state.GetBufferLookup<Child>();
        __delayDestroyType = state.GetComponentTypeHandle<DelayDestroy>();
        __instanceIDType = state.GetComponentTypeHandle<CopyMatrixToTransformInstanceID>();

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
        
        __children.Update(ref state);
        
        __entityType.Update(ref state);
        __delayDestroyType.Update(ref state);
        __instanceIDType.Update(ref state);
        
        ApplyEx apply;
        apply.isFixedFrameUpdated = fixedFrameCount != __fixedFrameCount;
        apply.deltaTime = SystemAPI.Time.DeltaTime;
        apply.children = __children;
        apply.entityType = __entityType;
        apply.delayDestroyType = __delayDestroyType;
        apply.instanceIDType = __instanceIDType;
        apply.entityManager = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);

        __fixedFrameCount = fixedFrameCount;
    }
}
