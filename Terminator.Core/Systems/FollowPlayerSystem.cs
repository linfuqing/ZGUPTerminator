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
        public double time;
        public Entity playerEntity;
        
        [ReadOnly]
        public NativeArray<LocalToWorld> localToWorlds;

        [ReadOnly]
        public NativeArray<FollowPlayer> instances;

        public NativeArray<FollowTarget> followTargets;
        
        public NativeArray<LookAtTarget> lookAtTargets;
        
        public void Execute(int index)
        {
            if (index < followTargets.Length)
            {
                var instance = instances[index];
                
                FollowTarget followTarget;
                //followTarget.flag = 0;
                followTarget.space = instance.space;
                followTarget.offset = instance.offset;
                followTarget.entity = playerEntity;
                followTargets[index] = followTarget;
            }

            if (index < lookAtTargets.Length)
            {
                LookAtTarget lookAtTarget;
                lookAtTarget.time = time;
                lookAtTarget.origin = localToWorlds[index].Rotation;
                lookAtTarget.entity = playerEntity;
                lookAtTargets[index] = lookAtTarget;
            }
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public double time;
        public Entity playerEntity;
        
        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        [ReadOnly]
        public ComponentTypeHandle<FollowPlayer> instanceType;

        public ComponentTypeHandle<FollowTarget> followTargetType;

        public ComponentTypeHandle<LookAtTarget> lookAtTargetType;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.time = time;
            apply.playerEntity = playerEntity;
            apply.localToWorlds = chunk.GetNativeArray(ref localToWorldType);
            apply.instances = chunk.GetNativeArray(ref instanceType);
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
    
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    private ComponentTypeHandle<FollowPlayer> __instanceType;
    
    private ComponentTypeHandle<FollowTarget> __followTargetType;

    private ComponentTypeHandle<LookAtTarget> __lookAtTargetType;

    private EntityQuery __group;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>(true);
        __instanceType = state.GetComponentTypeHandle<FollowPlayer>(true);
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
        __localToWorldType.Update(ref state);
        __instanceType.Update(ref state);
        __followTargetType.Update(ref state);
        __lookAtTargetType.Update(ref state);
        
        ApplyEx apply;
        apply.time = SystemAPI.Time.ElapsedTime;
        apply.playerEntity = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        apply.localToWorldType = __localToWorldType;
        apply.instanceType = __instanceType;
        apply.followTargetType = __followTargetType;
        apply.lookAtTargetType = __lookAtTargetType;

        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
    }
}
