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
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public ComponentLookup<EffectDamageParent> effectDamageParents;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<LocalToWorld> localToWorlds;

        [ReadOnly]
        public NativeArray<FollowPlayer> instances;

        public NativeArray<FollowTarget> followTargets;
        
        public NativeArray<LookAtTarget> lookAtTargets;
        
        public void Execute(int index)
        {
            var instance = instances[index];

            Entity character;
            if (instance.type == FollowPlayer.Type.Character)
            {
                EffectDamageParent.TryGetComponent(
                    entityArray[index],
                    effectDamageParents,
                    characterBodies,
                    out _,
                    out character);

                character = character == Entity.Null ? playerEntity : character;
            }
            else
                character = playerEntity;
            
            if (index < followTargets.Length)
            {
                FollowTarget followTarget;
                //followTarget.flag = 0;
                followTarget.space = instance.space;
                followTarget.offset = instance.offset;
                followTarget.entity = character;
                followTargets[index] = followTarget;
            }

            if (index < lookAtTargets.Length)
            {
                LookAtTarget lookAtTarget;
                lookAtTarget.time = time;
                lookAtTarget.origin = localToWorlds[index].Rotation;
                lookAtTarget.entity = character;
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
        public ComponentLookup<KinematicCharacterBody> characterBodies;

        [ReadOnly] 
        public ComponentLookup<EffectDamageParent> effectDamageParents;

        [ReadOnly]
        public EntityTypeHandle entityType;

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
            apply.characterBodies = characterBodies;
            apply.effectDamageParents = effectDamageParents;
            apply.entityArray = chunk.GetNativeArray(entityType);
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
    
    private ComponentLookup<KinematicCharacterBody> __characterBodies;

    private ComponentLookup<EffectDamageParent> __effectDamageParents;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    private ComponentTypeHandle<FollowPlayer> __instanceType;
    
    private ComponentTypeHandle<FollowTarget> __followTargetType;

    private ComponentTypeHandle<LookAtTarget> __lookAtTargetType;

    private EntityQuery __group;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>(true);
        __effectDamageParents = state.GetComponentLookup<EffectDamageParent>(true);
        __entityType = state.GetEntityTypeHandle();
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
        __characterBodies.Update(ref state);
        __effectDamageParents.Update(ref state);
        __entityType.Update(ref state);
        __localToWorldType.Update(ref state);
        __instanceType.Update(ref state);
        __followTargetType.Update(ref state);
        __lookAtTargetType.Update(ref state);
        
        ApplyEx apply;
        apply.time = SystemAPI.Time.ElapsedTime;
        apply.playerEntity = SystemAPI.GetSingleton<ThirdPersonPlayer>().ControlledCharacter;
        apply.characterBodies = __characterBodies;
        apply.effectDamageParents = __effectDamageParents;
        apply.entityType = __entityType;
        apply.localToWorldType = __localToWorldType;
        apply.instanceType = __instanceType;
        apply.followTargetType = __followTargetType;
        apply.lookAtTargetType = __lookAtTargetType;

        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
    }
}
