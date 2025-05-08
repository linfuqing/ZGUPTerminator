using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.CharacterController;
using ZG;

[BurstCompile, UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup), OrderFirst = true)]
public partial struct AntigravitySystem : ISystem
{
    private struct Update
    {
        public double time;

        [ReadOnly]
        public ComponentLookup<FallToDestroy> fallToDestroies;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Antigravity> instances;

        public NativeArray<AntigravityStatus> states;

        public BufferAccessor<Message> messages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<KinematicCharacterBody> characterBodies;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<ThirdPersionCharacterGravityFactor> characterGravityFactors;

        public bool Execute(int index)
        {
            bool isSendMessage = false, isAir;
            ThirdPersionCharacterGravityFactor characterGravityFactor;
            var instance = instances[index];
            var status = states[index];
            switch(status.value)
            {
                case AntigravityStatus.Value.Disable:
                    bool result = Apply(index, out Entity characterBodyEntity, out isAir);
                    if(result || isAir)
                    {
                        bool hasCharacterGravityFactors =
                            characterGravityFactors.TryGetComponent(characterBodyEntity, out characterGravityFactor);
                        if (hasCharacterGravityFactors &&
                            characterGravityFactor.value > math.FLT_MIN_NORMAL)
                        {
                            if (result)
                            {
                                if (characterGravityFactors.HasComponent(characterBodyEntity))
                                    characterGravityFactors[characterBodyEntity] = default;

                                if (index < messages.Length)
                                {
                                    isSendMessage = true;

                                    Message message;
                                    message.key = 0;
                                    message.name = instance.startMessageName;
                                    message.value = instance.startMessageValue;
                                    messages[index].Add(message);
                                }

                                status.value = AntigravityStatus.Value.Enable;
                                status.time = time;
                            }
                        }
                        else
                        {
                            if (hasCharacterGravityFactors)
                            {
                                characterGravityFactor.value = 1.0f;
                                characterGravityFactors[characterBodyEntity] = characterGravityFactor;
                            }
                            
                            status.value = AntigravityStatus.Value.FallDown;
                        }

                        states[index] = status;
                    }
                    break;
                case AntigravityStatus.Value.Enable:
                    Apply(index, out _, out isAir);

                    if(!isAir || time - status.time > instance.cooldown)
                    {
                        if (index < messages.Length)
                        {
                            isSendMessage = true;

                            Message message;
                            message.key = 0;
                            message.name = instance.endMessageName;
                            message.value = instance.endMessageValue;
                            messages[index].Add(message);
                        }

                        status.time = time - instance.cooldown;
                        status.value = AntigravityStatus.Value.Cooldown;

                        states[index] = status;
                    }
                    break;
                case AntigravityStatus.Value.Cooldown:
                    if (time - status.time > instance.cooldown + instance.duration)
                    {
                        status.value = AntigravityStatus.Value.FallDown;
                        states[index] = status;
                        
                        characterBodyEntity = GetCharacterBody(index);
                        if (characterGravityFactors.HasComponent(characterBodyEntity))
                        {
                            characterGravityFactor.value = 1.0f;
                            characterGravityFactors[characterBodyEntity] = characterGravityFactor;
                        }
                    }
                    else
                        Apply(index, out _, out _);
                    break;
                case AntigravityStatus.Value.FallDown:
                    characterBodyEntity = GetCharacterBody(index);
                    if (characterBodies.TryGetComponent(characterBodyEntity, out var characterBody) && characterBody.IsGrounded)
                    {
                        status.value = AntigravityStatus.Value.Disable;

                        states[index] = status;
                    }
                    break;
            }

            return isSendMessage;
        }

        public Entity GetCharacterBody(int index)
        {
            Entity entity = entityArray[index];
            while (!characterBodies.HasComponent(entity))
            {
                if (parents.TryGetComponent(entity, out var parent))
                    entity = parent.Value;
                else
                    return Entity.Null;
            }

            return fallToDestroies.HasComponent(entity) ? Entity.Null : entity;
        }

        public bool Apply(int index, out Entity characterBodyEntity, out bool isAir)
        {
            characterBodyEntity = GetCharacterBody(index);
            if (characterBodyEntity == Entity.Null)
            {
                isAir = false;
                
                return false;
            }

            ref var characterBody = ref characterBodies.GetRefRW(characterBodyEntity).ValueRW;
            isAir = !characterBody.IsGrounded;
            if (!isAir)
                return false;

            var groundingUp = characterBody.GroundingUp;
            float dot = math.dot(characterBody.RelativeVelocity, groundingUp);
            if (dot > -math.FLT_MIN_NORMAL)
                return false;

            ZG.Mathematics.Math.InterlockedAdd(ref characterBody.RelativeVelocity, -dot / math.dot(groundingUp, groundingUp) * groundingUp);

            return true;
        }
    }

    [BurstCompile]
    private struct UpdateEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentLookup<FallToDestroy> fallToDestroies;

        [ReadOnly]
        public ComponentLookup<Parent> parents;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Antigravity> instanceType;

        public ComponentTypeHandle<AntigravityStatus> statusType;

        public BufferTypeHandle<Message> messageType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<KinematicCharacterBody> characterBodies;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<ThirdPersionCharacterGravityFactor> characterGravityFactors;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Update update;
            update.time = time;
            update.fallToDestroies = fallToDestroies;
            update.parents = parents;
            update.entityArray = chunk.GetNativeArray(entityType);
            update.instances = chunk.GetNativeArray(ref instanceType);
            update.states = chunk.GetNativeArray(ref statusType);
            update.messages = chunk.GetBufferAccessor(ref messageType);
            update.characterBodies = characterBodies;
            update.characterGravityFactors = characterGravityFactors;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (update.Execute(i))
                    chunk.SetComponentEnabled(ref messageType, i, true);
            }
        }
    }

    private ComponentLookup<FallToDestroy> __fallToDestroies;
    private ComponentLookup<Parent> __parents;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Antigravity> __instanceType;

    private ComponentTypeHandle<AntigravityStatus> __statusType;
    private BufferTypeHandle<Message> __messageType;

    private ComponentLookup<KinematicCharacterBody> __characterBodies;
    private ComponentLookup<ThirdPersionCharacterGravityFactor> __characterGravityFactors;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __fallToDestroies = state.GetComponentLookup<FallToDestroy>(true);
        __parents = state.GetComponentLookup<Parent>(true);
        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetComponentTypeHandle<Antigravity>(true);
        __statusType = state.GetComponentTypeHandle<AntigravityStatus>();
        __messageType = state.GetBufferTypeHandle<Message>();
        __characterBodies = state.GetComponentLookup<KinematicCharacterBody>();
        __characterGravityFactors = state.GetComponentLookup<ThirdPersionCharacterGravityFactor>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<Antigravity>()
                .WithAllRW<AntigravityStatus>()
                .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __fallToDestroies.Update(ref state);
        __parents.Update(ref state);
        __entityType.Update(ref state);
        __instanceType.Update(ref state);
        __statusType.Update(ref state);
        __messageType.Update(ref state);
        __characterBodies.Update(ref state);
        __characterGravityFactors.Update(ref state);

        UpdateEx update;
        update.time = SystemAPI.Time.ElapsedTime;
        update.fallToDestroies = __fallToDestroies;
        update.parents = __parents;
        update.entityType = __entityType;
        update.instanceType = __instanceType;
        update.statusType = __statusType;
        update.messageType = __messageType;
        update.characterBodies = __characterBodies;
        update.characterGravityFactors = __characterGravityFactors;

        state.Dependency = update.ScheduleParallelByRef(__group, state.Dependency);
    }
}
