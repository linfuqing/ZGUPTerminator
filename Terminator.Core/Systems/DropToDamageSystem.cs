using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;

//[UpdateBefore(typeof(EffectSystem))]
[UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
public partial struct DropToDamageSystem : ISystem
{
    private struct Collect
    {
        [ReadOnly] 
        public NativeArray<KinematicCharacterBody> characterBodies;

        public NativeArray<DropToDamage> instances;
        
        public NativeArray<EffectTargetDamage> effectTargetDamages;

        public bool Execute(int index)
        {
            var instance = instances[index];
            if (instance.isGrounded == characterBodies[index].IsGrounded)
                return false;

            if (instance.isGrounded)
            {
                instance.isGrounded = false;

                instances[index] = instance;

                return false;
            }

            var effectTargetDamage = effectTargetDamages[index];
            effectTargetDamage.Add(instance.value, instance.valueImmunized, instance.layerMask);
            effectTargetDamages[index] = effectTargetDamage;

            instances[index] = default;
            
            return true;
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        [ReadOnly] 
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        public ComponentTypeHandle<DropToDamage> instanceType;
        
        public ComponentTypeHandle<EffectTargetDamage> effectTargetDamageType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.characterBodies = chunk.GetNativeArray(ref characterBodyType);
            collect.instances = chunk.GetNativeArray(ref instanceType);
            collect.effectTargetDamages = chunk.GetNativeArray(ref effectTargetDamageType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (collect.Execute(i))
                {
                    chunk.SetComponentEnabled(ref instanceType, i, false);
                    chunk.SetComponentEnabled(ref effectTargetDamageType, i, true);
                }
            }
        }
    }
    
    private ComponentTypeHandle<KinematicCharacterBody> __characterBodyType;

    private ComponentTypeHandle<DropToDamage> __instanceType;
        
    private ComponentTypeHandle<EffectTargetDamage> __effectTargetDamageType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __characterBodyType = state.GetComponentTypeHandle<KinematicCharacterBody>(true);
        __instanceType = state.GetComponentTypeHandle<DropToDamage>();
        __effectTargetDamageType = state.GetComponentTypeHandle<EffectTargetDamage>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<KinematicCharacterBody>()
                .WithAllRW<DropToDamage>()
                .WithPresentRW<EffectTargetDamage>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __characterBodyType.Update(ref state);
        __instanceType.Update(ref state);
        __effectTargetDamageType.Update(ref state);
        
        CollectEx collect;
        collect.characterBodyType = __characterBodyType;
        collect.instanceType = __instanceType;
        collect.effectTargetDamageType = __effectTargetDamageType;
        state.Dependency = collect.ScheduleParallelByRef(__group, state.Dependency);
    }
}
