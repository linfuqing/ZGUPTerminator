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
    [BurstCompile]
    private struct Apply : IJobParallelFor
    {
        public int skillRage;
        
        public float effectDamageScale;
        public float effectTargetDamageScale;
        public float effectTargetHPScale;
        public float effectTargetRecovery;

        public FixedString32Bytes instanceName;

        public FixedList512Bytes<LevelPlayerActiveSkill> activeSkills;
        public FixedList512Bytes<LevelPlayerSkillGroup> skillGroups;

        [ReadOnly, DeallocateOnJobCompletion] 
        public NativeArray<Entity> entityArray;

        [ReadOnly, DeallocateOnJobCompletion] 
        public NativeArray<Entity> players;

        [ReadOnly] 
        public ComponentLookup<LevelSkillNameDefinitionData> levelSkillNameDefinitions;

        [ReadOnly] 
        public ComponentLookup<BulletLayerMask> bulletLayerMasks;

        [NativeDisableParallelForRestriction] 
        public ComponentLookup<Instance> instances;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SkillRage> skillRages;

        [NativeDisableParallelForRestriction]
        public BufferLookup<LevelSkillGroup> levelSkillGroups;

        [NativeDisableParallelForRestriction]
        public BufferLookup<SkillActiveIndex> skillActiveIndices;

        [NativeDisableParallelForRestriction]
        public BufferLookup<MessageParameter> messageParameters;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetData> effectTargetDatas;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTarget> effectTargets;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetDamageScale> effectTargetDamageScales;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectDamage> effectDamages;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ThirdPersonPlayer> thirdPersonPlayers;
        
        public void Execute(int index)
        {
            var player = players[index];
            if (!instanceName.IsEmpty && instances.TryGetComponent(player, out Instance instance))
            {
                instance.name = instanceName;
                
                instances[player] = instance;
            }

            if (skillRages.HasComponent(player))
            {
                SkillRage skillRage;
                skillRage.value = this.skillRage;
                skillRages[player] = skillRage;
            }

            if ((activeSkills.Length > 0 || skillGroups.Length > 0) && 
                levelSkillNameDefinitions.TryGetComponent(player, out var levelSkillNameDefinition))
            {
                ref var definition = ref levelSkillNameDefinition.definition.Value;
                if (activeSkills.Length > 0 &&
                    this.skillActiveIndices.TryGetBuffer(player, out var skillActiveIndices))
                {
                    SkillActiveIndex skillActiveIndex;
                    int numSkills = definition.skills.Length, i;
                    bool isClear = true;
                    foreach (var activeSkill in activeSkills)
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
                            skillActiveIndex.damageScale = 1.0f + activeSkill.damageScale;
                            skillActiveIndices.Add(skillActiveIndex);
                        }
                    }
                }

                if (skillGroups.Length > 0 &&
                    this.levelSkillGroups.TryGetBuffer(player, out var levelSkillGroups))
                {
                    LevelSkillGroup levelSkillGroup;
                    int numGroups = definition.groups.Length, i;
                    bool isClear = true;
                    foreach (var skillGroup in skillGroups)
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
                            levelSkillGroup.damageScale = 1.0f + skillGroup.damageScale;
                            levelSkillGroups.Add(levelSkillGroup);
                        }
                    }
                }
            }

            if (effectTargets.TryGetComponent(player, out var effectTarget))
            {
                int hp = effectTarget.hp + (int)math.round(effectTarget.hp * effectTargetHPScale);
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
                
                if(effectTargetRecovery > math.FLT_MIN_NORMAL)
                    effectTarget.times = (int)math.floor(effectTargetRecovery);

                effectTargets[player] = effectTarget;
                
                if (effectTargetDatas.TryGetComponent(player, out var effectTargetData))
                {
                    float recoveryChance = effectTargetRecovery - effectTarget.times;
                    if (recoveryChance > math.FLT_MIN_NORMAL)
                        effectTargetData.recoveryChance = recoveryChance;
                    
                    effectTargetData.hpMax = hp;
                    effectTargetDatas[player] = effectTargetData;
                }
            }
            
            float effectTargetDamageScaleValue = 1.0f + this.effectTargetDamageScale;
            if (math.abs(effectTargetDamageScaleValue) > math.FLT_MIN_NORMAL && 
                effectTargetDamageScales.TryGetComponent(player, out var effectTargetDamageScale))
            {
                effectTargetDamageScale.value = 1.0f / effectTargetDamageScaleValue;

                effectTargetDamageScales[player] = effectTargetDamageScale;
            }
            
            EffectDamage effectDamage;
            if (!bulletLayerMasks.TryGetComponent(player, out effectDamage.bulletLayerMask))
                effectDamage.bulletLayerMask = default;//BulletLayerMask.AllLayers;
            
            effectDamage.scale = 1.0f + this.effectDamageScale;

            effectDamages[player] = effectDamage;

            ThirdPersonPlayer thirdPersonPlayer;
            thirdPersonPlayer.ControlledCamera = Entity.Null;
            thirdPersonPlayer.ControlledCharacter = player;
            thirdPersonPlayers[entityArray[index]] = thirdPersonPlayer;
        }
    }
    
    private ComponentLookup<LevelSkillNameDefinitionData> __levelSkillNameDefinitions;

    private ComponentLookup<BulletLayerMask> __bulletLayerMasks;

    private ComponentLookup<Instance> __instances;

    private ComponentLookup<SkillRage> __skillRages;

    private BufferLookup<LevelSkillGroup> __levelSkillGroups;

    private BufferLookup<SkillActiveIndex> __skillActiveIndices;

    private BufferLookup<MessageParameter> __messageParameters;

    private ComponentLookup<EffectTargetData> __effectTargetDatas;

    private ComponentLookup<EffectTarget> __effectTargets;

    private ComponentLookup<EffectTargetDamageScale> __effectTargetDamageScales;

    private ComponentLookup<EffectDamage> __effectDamages;

    private ComponentLookup<ThirdPersonPlayer> __thirdPersonPlayers;
    
    private EntityQuery __group;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __levelSkillNameDefinitions = state.GetComponentLookup<LevelSkillNameDefinitionData>(true);
        __bulletLayerMasks = state.GetComponentLookup<BulletLayerMask>(true);
        __instances = state.GetComponentLookup<Instance>();
        __skillRages = state.GetComponentLookup<SkillRage>();
        __levelSkillGroups = state.GetBufferLookup<LevelSkillGroup>();
        __skillActiveIndices = state.GetBufferLookup<SkillActiveIndex>();
        __messageParameters = state.GetBufferLookup<MessageParameter>();
        __effectTargetDatas = state.GetComponentLookup<EffectTargetData>();
        __effectTargets = state.GetComponentLookup<EffectTarget>();
        __effectTargetDamageScales = state.GetComponentLookup<EffectTargetDamageScale>();
        __effectDamages = state.GetComponentLookup<EffectDamage>();
        __thirdPersonPlayers = state.GetComponentLookup<ThirdPersonPlayer>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LevelPlayer, PrefabLoadResult>()
                .WithNone<ThirdPersonPlayer>()
                .Build(ref state);
        
        state.RequireForUpdate(__group);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NativeArray<Entity> entityArray = __group.ToEntityArray(Allocator.TempJob), players;
        using (var prefabLoadResults = __group.ToComponentDataArray<PrefabLoadResult>(Allocator.Temp))
        {
            var entityManager = state.EntityManager;
            entityManager.AddComponent(__group, new ComponentTypeSet(
                ComponentType.ReadWrite<ThirdPersonPlayer>(), 
                ComponentType.ReadWrite<ThirdPersonPlayerInputs>()));

            int count = entityArray.Length;
            players = new NativeArray<Entity>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for(int i = 0; i < count; ++i)
                players[i] = entityManager.Instantiate(prefabLoadResults[i].PrefabRoot);

            entityManager.AddComponent<EffectDamage>(players);
        }
        
        __levelSkillNameDefinitions.Update(ref state);
        __bulletLayerMasks.Update(ref state);
        __instances.Update(ref state);
        __skillRages.Update(ref state);
        __levelSkillGroups.Update(ref state);
        __skillActiveIndices.Update(ref state);
        __messageParameters.Update(ref state);
        __effectTargetDatas.Update(ref state);
        __effectTargets.Update(ref state);
        __effectTargetDamageScales.Update(ref state);
        __effectDamages.Update(ref state);
        __thirdPersonPlayers.Update(ref state);
            
        Apply apply;
        apply.skillRage = LevelPlayerShared.skillRage;
        apply.effectDamageScale = LevelPlayerShared.effectDamageScale;
        apply.effectTargetDamageScale = LevelPlayerShared.effectTargetDamageScale;
        apply.effectTargetHPScale = LevelPlayerShared.effectTargetHPScale;
        apply.effectTargetRecovery = LevelPlayerShared.effectTargetRecovery;
        apply.instanceName = LevelPlayerShared.instanceName;
        apply.activeSkills = LevelPlayerShared.activeSkills;
        apply.skillGroups = LevelPlayerShared.skillGroups;
        apply.entityArray = entityArray;
        apply.players = players;
        apply.levelSkillNameDefinitions = __levelSkillNameDefinitions;
        apply.bulletLayerMasks = __bulletLayerMasks;
        apply.instances = __instances;
        apply.skillRages = __skillRages;
        apply.levelSkillGroups = __levelSkillGroups;
        apply.skillActiveIndices = __skillActiveIndices;
        apply.messageParameters = __messageParameters;
        apply.effectTargetDatas = __effectTargetDatas;
        apply.effectTargets = __effectTargets;
        apply.effectTargetDamageScales = __effectTargetDamageScales;
        apply.effectDamages = __effectDamages;
        apply.thirdPersonPlayers = __thirdPersonPlayers;
        state.Dependency = apply.ScheduleByRef(entityArray.Length, 1, state.Dependency);
    }
}
