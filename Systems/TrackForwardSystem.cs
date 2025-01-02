using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

[BurstCompile, UpdateInGroup(typeof(PhysicsSystemGroup), OrderFirst = true)]
public partial struct TrackForwardSystem : ISystem
{
    private struct Track
    {
        [ReadOnly]
        public NativeArray<LocalTransform> localTransforms;
        public NativeArray<PhysicsVelocity> velocities;
        
        public void Execute(int index)
        {
            var velocity = velocities[index];
            velocity.Linear = math.length(velocity.Linear) * localTransforms[index].Forward();
            velocities[index] = velocity;
        }
    }

    [BurstCompile]
    private struct TrackEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<LocalTransform> localTransformType;
        public ComponentTypeHandle<PhysicsVelocity> velocityType;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Track track;
            track.localTransforms = chunk.GetNativeArray(ref localTransformType);
            track.velocities = chunk.GetNativeArray(ref velocityType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                track.Execute(i);
        }
    }

    private ComponentTypeHandle<LocalTransform> __localTransformType;
    private ComponentTypeHandle<PhysicsVelocity> __velocityType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>(true);
        __velocityType = state.GetComponentTypeHandle<PhysicsVelocity>();
        
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LocalTransform, PhysicsVelocity, TrackForward>()
                .WithNone<Parent>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __localTransformType.Update(ref state);
        __velocityType.Update(ref state);
        
        TrackEx track;
        track.localTransformType = __localTransformType;
        track.velocityType = __velocityType;

        state.Dependency = track.ScheduleParallelByRef(__group, state.Dependency);
    }
}
