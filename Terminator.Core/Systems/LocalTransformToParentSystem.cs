using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.CharacterController;

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct LocalTransformToParentInitSystem : ISystem
{
    private struct Init
    {
        [ReadOnly]
        public NativeArray<LocalTransform> localTransforms;

        public NativeArray<LocalTransformToParentStatus> localTransformsToParentStates;

        public void Execute(int index)
        {
            LocalTransformToParentStatus localTransformToParentStatus;
            localTransformToParentStatus.motion = localTransforms[index];
            localTransformsToParentStates[index] = localTransformToParentStatus;
        }
    }

    [BurstCompile]
    private struct InitEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<LocalTransform> localTransformType;

        public ComponentTypeHandle<LocalTransformToParentStatus> localTransformsToParentStatusType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!chunk.Has(ref localTransformsToParentStatusType))
                return;

            Init init;
            init.localTransforms = chunk.GetNativeArray(ref localTransformType);
            init.localTransformsToParentStates = chunk.GetNativeArray(ref localTransformsToParentStatusType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                init.Execute(i);
                chunk.SetComponentEnabled(ref localTransformsToParentStatusType, i, true);
            }
        }
    }
    
    private ComponentTypeHandle<LocalTransform> __localTransformType;

    private ComponentTypeHandle<LocalTransformToParentStatus> __localTransformsToParentStatusType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>(true);
        __localTransformsToParentStatusType = state.GetComponentTypeHandle<LocalTransformToParentStatus>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LocalTransform, LocalTransformToParent>()
                .WithNone<LocalTransformToParentStatus>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __localTransformType.Update(ref state);
        __localTransformsToParentStatusType.Update(ref state);
        
        InitEx init;
        init.localTransformType = __localTransformType;
        init.localTransformsToParentStatusType = __localTransformsToParentStatusType;
        state.Dependency = init.ScheduleParallelByRef(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true), UpdateAfter(typeof(AnimationCurveSystem))]
public partial struct LocalTransformToParentSystem : ISystem
{
    private struct Update
    {
        public float deltaTimeR;
        //public double time;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<BulletEntity> bulletEntities;

        [ReadOnly]
        public NativeArray<LocalTransformToParent> instances;

        public NativeArray<LocalTransformToParentStatus> states;

        public NativeArray<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<KinematicCharacterBody> characterBodies;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<ThirdPersionCharacterGravityFactor> characterGravityFactors;

        public void Execute(int index)
        {
            var instance = instances[index];
            var status = states[index];
            
            Apply(index, instance.horizontal, ref status.motion);

            states[index] = status;
        }

        public Entity GetCharacterBody(int index)
        {
            Entity entity = index < bulletEntities.Length ? bulletEntities[index].parent : entityArray[index];
            while (!characterBodies.HasComponent(entity))
            {
                if (parents.TryGetComponent(entity, out var parent))
                    entity = parent.Value;
                else
                {
                    entity = Entity.Null;

                    break;
                }
            }

            return entity;
        }

        public void Apply(
            int index, 
            float horizontal, 
            ref LocalTransform motion)
        {
            Entity characterBodyEntity = GetCharacterBody(index);
            if (characterBodyEntity == Entity.Null)
                return;

            ref var characterBody = ref characterBodies.GetRefRW(characterBodyEntity).ValueRW;

            var localTransform = localTransforms[index];
            bool result = math.dot(localTransform.Position, characterBody.GroundingUp) > horizontal;
            if(result)
                characterBody.IsGrounded = false;
            
            if (characterGravityFactors.HasComponent(characterBodyEntity))
            {
                ThirdPersionCharacterGravityFactor characterGravityFactor;
                characterGravityFactor.value = result
                    ? 0.0f
                    : 1.0f;
                characterGravityFactors[characterBodyEntity] = characterGravityFactor;
            }

            var delta = localTransform.Position - motion.Position;//motion.InverseTransformTransform(localTransform);
            ZG.Mathematics.Math.InterlockedAdd(ref characterBody.RelativeVelocity,
                delta * deltaTimeR -
                math.projectsafe(characterBody.RelativeVelocity, characterBody.GroundingUp));
            
            motion = localTransform;

            localTransforms[index] = LocalTransform.Identity;
        }
    }

    [BurstCompile]
    private struct UpdateEx : IJobChunk
    {
        public float deltaTimeR;
        
        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<BulletEntity> bulletEntityType;

        [ReadOnly]
        public ComponentTypeHandle<LocalTransformToParent> instanceType;

        public ComponentTypeHandle<LocalTransformToParentStatus> statusType;

        public ComponentTypeHandle<LocalTransform> localTransformType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<KinematicCharacterBody> characterBodies;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<ThirdPersionCharacterGravityFactor> characterGravityFactors;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Update update;
            update.deltaTimeR = deltaTimeR;
            update.parents = parents;
            update.entityArray = chunk.GetNativeArray(entityType);
            update.bulletEntities = chunk.GetNativeArray(ref bulletEntityType);
            update.instances = chunk.GetNativeArray(ref instanceType);
            update.states = chunk.GetNativeArray(ref statusType);
            update.localTransforms = chunk.GetNativeArray(ref localTransformType);
            update.characterBodies = characterBodies;
            update.characterGravityFactors = characterGravityFactors;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                update.Execute(i);
        }
    }

    private ComponentLookup<Parent> __parents;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<BulletEntity> __bulletEntityType;
    
    private ComponentTypeHandle<LocalTransformToParent> __instanceType;

    private ComponentTypeHandle<LocalTransformToParentStatus> __statusType;
    private ComponentTypeHandle<LocalTransform> __localTransformType;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;
    private ComponentLookup<ThirdPersionCharacterGravityFactor> __characterGravityFactors;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __parents = state.GetComponentLookup<Parent>(true);
        __entityType = state.GetEntityTypeHandle();
        __bulletEntityType = state.GetComponentTypeHandle<BulletEntity>(true);
        __instanceType = state.GetComponentTypeHandle<LocalTransformToParent>(true);
        __statusType = state.GetComponentTypeHandle<LocalTransformToParentStatus>();
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>();
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>();
        __characterGravityFactors = state.GetComponentLookup<ThirdPersionCharacterGravityFactor>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LocalTransformToParent>()
                .WithAllRW<LocalTransformToParentStatus, LocalTransform>()
                .WithAny<Parent, BulletEntity>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __parents.Update(ref state);
        __entityType.Update(ref state);
        __bulletEntityType.Update(ref state);
        __instanceType.Update(ref state);
        __statusType.Update(ref state);
        __localTransformType.Update(ref state);
        __characterBodies.Update(ref state);
        __characterGravityFactors.Update(ref state);

        UpdateEx update;
        update.deltaTimeR = math.rcp(SystemAPI.Time.DeltaTime);
        update.parents = __parents;
        update.entityType = __entityType;
        update.bulletEntityType = __bulletEntityType;
        update.instanceType = __instanceType;
        update.statusType = __statusType;
        update.localTransformType = __localTransformType;
        update.characterBodies = __characterBodies;
        update.characterGravityFactors = __characterGravityFactors;

        state.Dependency = update.ScheduleParallelByRef(__group, state.Dependency);
    }
}
