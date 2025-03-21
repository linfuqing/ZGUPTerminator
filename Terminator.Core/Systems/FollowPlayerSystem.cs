using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.CharacterController;

//[BurstCompile, UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true), UpdateBefore(typeof(FollowTargetSystem))]
[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct FollowPlayerSystem : ISystem
{
    private struct Apply
    {
        public Entity playerEntity;
        
        public NativeArray<FollowTarget> followTargets;
        
        public NativeArray<LookAtTarget> lookAtTargets;
        
        public void Execute(int index)
        {
            if (index < followTargets.Length)
            {
                FollowTarget followTarget;
                //followTarget.flag = 0;
                followTarget.space = FollowTargetSpace.World;
                followTarget.offset = float3.zero;
                followTarget.entity = playerEntity;
                followTargets[index] = followTarget;
            }

            if (index < lookAtTargets.Length)
            {
                LookAtTarget lookAtTarget;
                lookAtTarget.entity = playerEntity;
                lookAtTargets[index] = lookAtTarget;
            }
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public Entity playerEntity;
        
        public ComponentTypeHandle<FollowTarget> followTargetType;

        public ComponentTypeHandle<LookAtTarget> lookAtTargetType;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.playerEntity = playerEntity;
            apply.followTargets = chunk.GetNativeArray(ref followTargetType);
            apply.lookAtTargets = chunk.GetNativeArray(ref lookAtTargetType);

            bool hasFollowTargets = chunk.Has(ref followTargetType), hasLookAtTargets = chunk.Has(ref lookAtTargetType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(hasFollowTargets)
                    chunk.SetComponentEnabled(ref followTargetType, i, true);
                
                if(hasLookAtTargets)
                    chunk.SetComponentEnabled(ref lookAtTargetType, i, true);
                
                apply.Execute(i);
            }
        }
    }
    
    private ComponentTypeHandle<FollowTarget> __followTargetType;

    private ComponentTypeHandle<LookAtTarget> __lookAtTargetType;

    private EntityQuery __group;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __followTargetType = state.GetComponentTypeHandle<FollowTarget>();
        
        __lookAtTargetType = state.GetComponentTypeHandle<LookAtTarget>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<FollowPlayer>()
                //.WithNone<FollowTarget, LookAtTarget>()
                .Build(ref state);
        
        state.RequireForUpdate<ThirdPersonPlayer>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __followTargetType.Update(ref state);
        __lookAtTargetType.Update(ref state);
        
        ApplyEx apply;
        apply.playerEntity = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        apply.followTargetType = __followTargetType;
        apply.lookAtTargetType = __lookAtTargetType;

        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
    }
}
