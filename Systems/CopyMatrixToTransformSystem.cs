using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Jobs;

public struct CopyMatrixToTransformInstanceID : ICleanupComponentData
{
    public bool isSendMessageOnDestroy;
    public int value;
}

[BurstCompile, UpdateInGroup(typeof(TransformSystemGroup), OrderLast = true)]
public partial struct CopyMatrixToTransformSystem : ISystem
{
    [BurstCompile]
    private struct Apply : IJobParallelForTransform
    {
        [ReadOnly] 
        public NativeList<LocalToWorld> localToWorlds;
        
        public void Execute(int index, TransformAccess transform)
        {
            if (!transform.isValid)
                return;

            var localToWorld = localToWorlds[index];
            transform.SetPositionAndRotation(localToWorld.Position, localToWorld.Rotation);
        }
    }

    private EntityQuery __group;
    private TransformAccessArray __transformAccessArray;
    private uint __version;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<CopyMatrixToTransformInstanceID, LocalToWorld>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if(__transformAccessArray.isCreated)
            __transformAccessArray.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        uint version = (uint)__group.GetCombinedComponentOrderVersion(false);//(uint)entityManager.GetComponentOrderVersion<CopyMatrixToTransformInstanceID>();

        if (ChangeVersionUtility.DidChange(version, __version))
        {
            __version = version;
            
            using (var ids = __group.ToComponentDataArray<CopyMatrixToTransformInstanceID>(Allocator.Temp))
            {
                int numIDs = ids.Length;

                if (__transformAccessArray.isCreated)
                        __transformAccessArray.Dispose();
                
                __transformAccessArray = new TransformAccessArray(numIDs);

                for (int i = 0; i < numIDs; ++i)
                    __transformAccessArray.Add(ids[i].value);
            }
        }
        
        UnityEngine.Assertions.Assert.AreEqual(__transformAccessArray.length, __group.CalculateEntityCount());
        
        Apply apply;
        apply.localToWorlds = __group.ToComponentDataListAsync<LocalToWorld>(state.WorldUpdateAllocator, out var localToWorldJobHandle);
        state.Dependency = apply.ScheduleByRef(__transformAccessArray, 
            JobHandle.CombineDependencies(localToWorldJobHandle, state.Dependency));
    }
}