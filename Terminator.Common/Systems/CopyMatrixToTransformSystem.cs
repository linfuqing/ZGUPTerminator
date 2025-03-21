using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine.Jobs;

[BurstCompile, UpdateInGroup(typeof(TransformSystemGroup), OrderLast = true)]
public partial struct CopyMatrixToTransformSystem : ISystem
{
    [BurstCompile]
    private struct Apply : IJobParallelForTransform
    {
        [ReadOnly] 
        public NativeArray<int> indices;

        [ReadOnly] 
        public NativeArray<LocalToWorld> localToWorlds;
        
        public void Execute(int index, TransformAccess transform)
        {
            if (!transform.isValid)
                return;

            var localToWorld = localToWorlds[indices[index]];
            transform.SetPositionAndRotation(localToWorld.Position, localToWorld.Rotation);
        }
    }

    private EntityQuery __group;
    private TransformAccessArray __transformAccessArray;
    private NativeList<int> __indices;
    private uint __version;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<CopyMatrixToTransformInstanceID, LocalToWorld>()
                .Build(ref state);

        __indices = new NativeList<int>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __indices.Dispose();
        
        if(__transformAccessArray.isCreated)
            __transformAccessArray.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        uint version = (uint)__group.GetCombinedComponentOrderVersion(false);//(uint)entityManager.GetComponentOrderVersion<CopyMatrixToTransformInstanceID>();

        if (ChangeVersionUtility.DidChange(version, __version)/* || 
            __group.CalculateEntityCount() != (__transformAccessArray.isCreated ? __transformAccessArray.length : 0)*/)
        {
            __version = version;
            
            using (var ids = __group.ToComponentDataArray<CopyMatrixToTransformInstanceID>(Allocator.Temp))
            {
                int numIDs = ids.Length;

                if (__transformAccessArray.isCreated)
                    __transformAccessArray.Dispose();
                
                __transformAccessArray = new TransformAccessArray(numIDs);

                __indices.Clear();
                for (int i = 0; i < numIDs; ++i)
                {
                    __transformAccessArray.Add(ids[i].value);
                    
                    if(__transformAccessArray.length > __indices.Length)
                        __indices.Add(i);
                }
            }
        }
        
        UnityEngine.Assertions.Assert.AreEqual(__transformAccessArray.length, __indices.Length);
        
        Apply apply;
        apply.indices = __indices.AsArray();
        apply.localToWorlds = __group
            .ToComponentDataListAsync<LocalToWorld>(state.WorldUpdateAllocator, out var localToWorldJobHandle)
            .AsDeferredJobArray();
        state.Dependency = apply.ScheduleByRef(__transformAccessArray, 
            JobHandle.CombineDependencies(localToWorldJobHandle, state.Dependency));
    }
}