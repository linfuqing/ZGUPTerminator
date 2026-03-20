using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Scenes;
using ZG;

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct LevelPlayerSystem : ISystem
{
#if !DEBUG
    [BurstCompile]
#endif
    private struct Apply : IJobParallelFor
    {
        [ReadOnly, DeallocateOnJobCompletion] 
        public NativeArray<Entity> entityArray;

        [ReadOnly, DeallocateOnJobCompletion] 
        public NativeArray<Entity> localPlayerEntities;

        [ReadOnly, DeallocateOnJobCompletion] 
        public NativeArray<Entity> remotePlayerEntities;

        [ReadOnly] 
        public ComponentLookup<LevelSkillNameDefinitionData> levelSkillNameDefinitions;

        [NativeDisableParallelForRestriction] 
        public ComponentLookup<Instance> instances;

        [NativeDisableParallelForRestriction]
        public BufferLookup<LevelSkillOpcode> levelSkillOpcodes;

        [NativeDisableParallelForRestriction]
        public BufferLookup<LevelSkillGroup> levelSkillGroups;

        [NativeDisableParallelForRestriction]
        public BufferLookup<SkillActiveIndex> skillActiveIndices;

        [NativeDisableParallelForRestriction]
        public BufferLookup<MessageParameter> messageParameters;

        [NativeDisableParallelForRestriction] 
        public ComponentLookup<RemoteIdentity> remoteIdentities;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetData> effectTargetDatas;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTarget> effectTargets;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetDamageScale> effectTargetDamageScales;

        //[NativeDisableParallelForRestriction]
        //public ComponentLookup<EffectDamage> effectDamages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectRage> effectRages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ThirdPersonPlayer> thirdPersonPlayers;
        
        public void Execute(int index)
        {
            var localPlayerEntity = localPlayerEntities[index];
            
            __Apply(LevelPlayerShared<LocalPlayer>.property, localPlayerEntity);

            ThirdPersonPlayer thirdPersonPlayer;
            thirdPersonPlayer.ControlledCamera = Entity.Null;
            thirdPersonPlayer.ControlledCharacter = localPlayerEntity;
            thirdPersonPlayers[entityArray[index]] = thirdPersonPlayer;

            if (RemotePlayer.Status.Joined == RemotePlayer.status)
            {
                Entity remotePlayerEntity = remotePlayerEntities[index];
                __Apply(LevelPlayerShared<RemotePlayer>.property, remotePlayerEntity);

                RemoteIdentity remoteIdentity;
                remoteIdentity.id = LevelPlayerShared<RemotePlayer>.id;

                remoteIdentities[remotePlayerEntity] = remoteIdentity;
                
                RemotePlayer.status = RemotePlayer.Status.StandBy;
            }
        }

        private void __Apply(in LevelPlayerProperty property, in Entity player)
        {
            if (!property.instanceName.IsEmpty && instances.TryGetComponent(player, out Instance instance))
            {
                instance.name = property.instanceName;
                
                instances[player] = instance;
            }

            if ((property.activeSkills.Length > 0 || property.skillGroups.Length > 0) && 
                levelSkillNameDefinitions.TryGetComponent(player, out var levelSkillNameDefinition))
            {
                ref var definition = ref levelSkillNameDefinition.definition.Value;
                if (property.activeSkills.Length > 0 &&
                    this.skillActiveIndices.TryGetBuffer(player, out var skillActiveIndices))
                {
                    SkillActiveIndex skillActiveIndex;
                    int numSkills = definition.skills.Length, i;
                    bool isClear = true;
                    foreach (var activeSkill in property.activeSkills)
                    {
                        for (i = 0; i < numSkills; ++i)
                        {
                            if (definition.skills[i] == activeSkill.name)
                                break;
                        }

                        if (i < numSkills)
                        {
                            if (isClear)
                            {
                                isClear = false;
                                
                                skillActiveIndices.Clear();
                            }

                            skillActiveIndex.value = i;
                            skillActiveIndex.damageScale = 1.0f + activeSkill.damageScale + property.effectDamageScale;
                            skillActiveIndices.Add(skillActiveIndex);
                        }
                        else
                            UnityEngine.Debug.LogError($"Skill {activeSkill.name} can not been found!");
                    }
                }

                if (property.skillGroups.Length > 0 &&
                    this.levelSkillGroups.TryGetBuffer(player, out var levelSkillGroups))
                {
                    LevelSkillGroup levelSkillGroup;
                    int numGroups = definition.groups.Length, i;
                    bool isClear = true;
                    foreach (var skillGroup in property.skillGroups)
                    {
                        for (i = 0; i < numGroups; ++i)
                        {
                            if (definition.groups[i] == skillGroup.name)
                                break;
                        }

                        if (i < numGroups)
                        {
                            if (isClear)
                            {
                                isClear = false;
                                
                                levelSkillGroups.Clear();
                            }

                            levelSkillGroup.value = i;
                            levelSkillGroup.damageScale = 1.0f + skillGroup.damageScale + property.effectDamageScale;
                            levelSkillGroups.Add(levelSkillGroup);
                        }
                        else
                            UnityEngine.Debug.LogError($"Skill group {skillGroup.name} can not been found!");
                    }
                }
                
                if (property.skillOpcodes.Length > 0 &&
                    this.levelSkillOpcodes.TryGetBuffer(player, out var levelSkillOpcodes))
                {
                    LevelSkillOpcode levelSkillOpcode;
                    int numSkills = definition.skills.Length, i;
                    bool isClear = true;
                    foreach (var skillOpcode in property.skillOpcodes)
                    {
                        for (i = 0; i < numSkills; ++i)
                        {
                            if (definition.skills[i] == skillOpcode.name)
                                break;
                        }

                        if (i < numSkills)
                        {
                            if (isClear)
                            {
                                isClear = false;
                                
                                levelSkillOpcodes.Clear();
                            }

                            levelSkillOpcode.index = i;
                            levelSkillOpcode.type = skillOpcode.type;
                            levelSkillOpcode.value = skillOpcode.value;
                            levelSkillOpcodes.Add(levelSkillOpcode);
                        }
                        else
                            UnityEngine.Debug.LogError($"Skill group {skillOpcode.name} can not been found!");
                    }
                }
            }

            if (effectTargets.TryGetComponent(player, out var effectTarget))
            {
                int hp = property.effectTargetHP == 0 ? effectTarget.hp : property.effectTargetHP;
                hp = effectTarget.hp + (int)math.round(hp * property.effectTargetHPScale);
                if (this.messageParameters.TryGetBuffer(player, out var messageParameters))
                {
                    int numMessageParameters = messageParameters.Length;
                    for (int i = 0; i < numMessageParameters; ++i)
                    {
                        ref var messageParameter = ref messageParameters.ElementAt(i);
                        switch((EffectAttributeID)messageParameter.id)
                        {
                            case EffectAttributeID.HPMax:
                            case EffectAttributeID.HP:
                                if(messageParameter.value == effectTarget.hp)
                                    messageParameter.value = hp;
                                break;
                        }
                    }
                }
                
                effectTarget.hp = hp;
                
                if(property.effectTargetRecovery > math.FLT_MIN_NORMAL)
                    effectTarget.times = (int)math.floor(property.effectTargetRecovery);

                effectTargets[player] = effectTarget;
                
                if (effectTargetDatas.TryGetComponent(player, out var effectTargetData))
                {
                    float recoveryChance = property.effectTargetRecovery - effectTarget.times;
                    if (recoveryChance > math.FLT_MIN_NORMAL)
                        effectTargetData.recoveryChance = recoveryChance;

                    effectTargetData.recoveryTimeBeenKeptOfMaxTimes =
                        effectTarget.times - property.effectTargetRecoveryTimes;
                    effectTargetData.hpMax = hp;
                    effectTargetDatas[player] = effectTargetData;
                }
            }
            
            //float effectTargetDamageScaleValue = 1.0f + this.effectTargetDamageScale;
            if (math.abs(property.effectTargetDamageScale) > math.FLT_MIN_NORMAL && 
                effectTargetDamageScales.TryGetComponent(player, out var effectTargetDamageScale))
            {
                float hpScale = 1.0f + property.effectTargetHPScale;
                effectTargetDamageScale.value = hpScale / (hpScale + property.effectTargetDamageScale);//1.0f / effectTargetDamageScaleValue;

                effectTargetDamageScales[player] = effectTargetDamageScale;
            }
            
            /*EffectDamage effectDamage;
            //if (!bulletLayerMasks.TryGetComponent(player, out effectDamage.bulletLayerMask))
                effectDamage.layerMaskAndTags = default;//BulletLayerMask.AllLayers;
            
            effectDamage.scale = 1.0f + this.effectDamageScale;

            effectDamages[player] = effectDamage;*/

            if (effectRages.HasComponent(player))
            {
                EffectRage effectRage;
                effectRage.value = property.effectRage;
                effectRages[player] = effectRage;
            }
        }
    }
    
    private ComponentLookup<LevelSkillNameDefinitionData> __levelSkillNameDefinitions;

    private ComponentLookup<Instance> __instances;

    private BufferLookup<LevelSkillGroup> __levelSkillGroups;

    private BufferLookup<LevelSkillOpcode> __levelSkillOpcodes;

    private BufferLookup<SkillActiveIndex> __skillActiveIndices;

    private BufferLookup<MessageParameter> __messageParameters;

    private ComponentLookup<RemoteIdentity> __remoteIdentities;

    private ComponentLookup<EffectTargetData> __effectTargetDatas;

    private ComponentLookup<EffectTarget> __effectTargets;

    private ComponentLookup<EffectTargetDamageScale> __effectTargetDamageScales;

    private ComponentLookup<EffectRage> __effectRages;

    private ComponentLookup<ThirdPersonPlayer> __thirdPersonPlayers;
    
    private EntityQuery __group;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __levelSkillNameDefinitions = state.GetComponentLookup<LevelSkillNameDefinitionData>(true);
        __instances = state.GetComponentLookup<Instance>();
        __levelSkillGroups = state.GetBufferLookup<LevelSkillGroup>();
        __levelSkillOpcodes = state.GetBufferLookup<LevelSkillOpcode>();
        __skillActiveIndices = state.GetBufferLookup<SkillActiveIndex>();
        __messageParameters = state.GetBufferLookup<MessageParameter>();
        __remoteIdentities =  state.GetComponentLookup<RemoteIdentity>();
        __effectTargetDatas = state.GetComponentLookup<EffectTargetData>();
        __effectTargets = state.GetComponentLookup<EffectTarget>();
        __effectTargetDamageScales = state.GetComponentLookup<EffectTargetDamageScale>();
        //__effectDamages = state.GetComponentLookup<EffectDamage>();
        __effectRages = state.GetComponentLookup<EffectRage>();
        __thirdPersonPlayers = state.GetComponentLookup<ThirdPersonPlayer>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LevelPlayer, PrefabLoadResult>()
                .WithNone<ThirdPersonPlayer>()
                .Build(ref state);
        
        state.RequireForUpdate(__group);
    }
#if !DEBUG
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state)
    {
        if (LevelShared.unscaledDeltaTime < math.FLT_MIN_NORMAL)
            return;
        
        var remotePlayerStatus = RemotePlayer.status;
        if (RemotePlayer.Status.Waiting == remotePlayerStatus && RemotePlayer.isOnline)
            return;
        
        NativeArray<Entity> entityArray = __group.ToEntityArray(Allocator.TempJob), localPlayers, remotePlayers;
        using (var prefabLoadResults = __group.ToComponentDataArray<PrefabLoadResult>(Allocator.Temp))
        {
            var entityManager = state.EntityManager;
            entityManager.AddComponent(__group, new ComponentTypeSet(
                ComponentType.ReadWrite<ThirdPersonPlayer>(), 
                ComponentType.ReadWrite<ThirdPersonPlayerInputs>()));

            int count = entityArray.Length;
            localPlayers = new NativeArray<Entity>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for(int i = 0; i < count; ++i)
                localPlayers[i] = entityManager.Instantiate(prefabLoadResults[i].PrefabRoot);
            
            remotePlayers = new NativeArray<Entity>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            if (RemotePlayer.Status.Joined == remotePlayerStatus)
            {
                for (int i = 0; i < count; ++i)
                    remotePlayers[i] = entityManager.Instantiate(prefabLoadResults[i].PrefabRoot);

                entityManager.AddComponent(remotePlayers, 
                    new ComponentTypeSet(
                        ComponentType.ReadWrite<RemotePlayer>(), 
                        ComponentType.ReadWrite<RemoteIdentity>(), 
                        ComponentType.ReadWrite<RemotePosition>(), 
                        ComponentType.ReadWrite<RemoteEffectTargetDamage>(), 
                        ComponentType.ReadWrite<RemoteEffectTargetHP>()));
                
                if(LevelPlayerShared<RemotePlayer>.property.skillOpcodes.Length > 0)
                    entityManager.AddComponent<LevelSkillOpcode>(remotePlayers);
            }

            entityManager.AddComponent(localPlayers,
                new ComponentTypeSet(
                    ComponentType.ReadWrite<RemotePosition>(), 
                    ComponentType.ReadWrite<RemoteEffectTargetDamage>(),
                    ComponentType.ReadWrite<RemoteEffectTargetHP>()));
            
            if(LevelPlayerShared<LocalPlayer>.property.skillOpcodes.Length > 0)
                entityManager.AddComponent<LevelSkillOpcode>(localPlayers);

            //entityManager.AddComponent<EffectDamage>(players);
        }
        
        __levelSkillNameDefinitions.Update(ref state);
        __instances.Update(ref state);
        __levelSkillGroups.Update(ref state);
        __levelSkillOpcodes.Update(ref state);
        __skillActiveIndices.Update(ref state);
        __messageParameters.Update(ref state);
        __remoteIdentities.Update(ref state);
        __effectTargetDatas.Update(ref state);
        __effectTargets.Update(ref state);
        __effectTargetDamageScales.Update(ref state);
        __effectRages.Update(ref state);
        __thirdPersonPlayers.Update(ref state);
            
        Apply apply;
        apply.entityArray = entityArray;
        apply.localPlayerEntities = localPlayers;
        apply.remotePlayerEntities = remotePlayers;
        apply.levelSkillNameDefinitions = __levelSkillNameDefinitions;
        apply.instances = __instances;
        apply.levelSkillOpcodes = __levelSkillOpcodes;
        apply.levelSkillGroups = __levelSkillGroups;
        apply.skillActiveIndices = __skillActiveIndices;
        apply.messageParameters = __messageParameters;
        apply.remoteIdentities = __remoteIdentities;
        apply.effectTargetDatas = __effectTargetDatas;
        apply.effectTargets = __effectTargets;
        apply.effectTargetDamageScales = __effectTargetDamageScales;
        //apply.effectDamages = __effectDamages;
        apply.effectRages = __effectRages;
        apply.thirdPersonPlayers = __thirdPersonPlayers;
        state.Dependency = apply.ScheduleByRef(entityArray.Length, 1, state.Dependency);
    }
}
