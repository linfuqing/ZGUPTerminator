using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Scenes;

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct LevelPlayerSystem : ISystem
{
    [BurstCompile]
    private struct Apply : IJobParallelFor
    {
        public float bulletDamageScale;
        public float effectTargetDamageScale;
        public float effectTargetHPScale;

        public FixedList512Bytes<LevelPlayerActiveSkill> activeSkills;
        public FixedList512Bytes<LevelPlayerSkillGroup> skillGroups;

        [ReadOnly, DeallocateOnJobCompletion] 
        public NativeArray<Entity> entityArray;

        [ReadOnly, DeallocateOnJobCompletion] 
        public NativeArray<Entity> players;

        [ReadOnly] 
        public ComponentLookup<LevelSkillNameDefinitionData> levelSkillNameDefinitions;

        [NativeDisableParallelForRestriction]
        public BufferLookup<LevelSkillGroup> levelSkillGroups;

        [NativeDisableParallelForRestriction]
        public BufferLookup<SkillActiveIndex> skillActiveIndices;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTarget> effectTargets;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EffectTargetDamageScale> effectTargetDamageScales;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<BulletDamageScale> bulletDamageScales;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ThirdPersonPlayer> instances;
        
        public void Execute(int index)
        {
            var player = players[index];
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
                            if (definition.skills[i] == skillGroup.name)
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
                            levelSkillGroups.Add(levelSkillGroup);
                        }
                    }
                }
            }

            if (effectTargets.TryGetComponent(player, out var effectTarget))
            {
                effectTarget.hp += (int)math.round(effectTarget.hp * effectTargetHPScale);

                effectTargets[player] = effectTarget;
            }
            
            if (effectTargetDamageScales.TryGetComponent(player, out var effectTargetDamageScale))
            {
                effectTargetDamageScale.value += effectTargetDamageScale.value * this.effectTargetDamageScale;

                effectTargetDamageScales[player] = effectTargetDamageScale;
            }
            
            if (bulletDamageScales.TryGetComponent(player, out var bulletDamageScale))
            {
                bulletDamageScale.value += bulletDamageScale.value * this.bulletDamageScale;

                bulletDamageScales[player] = bulletDamageScale;
            }

            ThirdPersonPlayer instance;
            instance.ControlledCamera = Entity.Null;
            instance.ControlledCharacter = player;
            instances[entityArray[index]] = instance;
        }
    }
    
    private ComponentLookup<LevelSkillNameDefinitionData> __levelSkillNameDefinitions;

    private BufferLookup<LevelSkillGroup> __levelSkillGroups;

    private BufferLookup<SkillActiveIndex> __skillActiveIndices;

    private ComponentLookup<EffectTarget> __effectTargets;

    private ComponentLookup<EffectTargetDamageScale> __effectTargetDamageScales;

    private ComponentLookup<BulletDamageScale> __bulletDamageScales;

    private ComponentLookup<ThirdPersonPlayer> __instances;
    
    private EntityQuery __group;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __levelSkillNameDefinitions = state.GetComponentLookup<LevelSkillNameDefinitionData>();
        __levelSkillGroups = state.GetBufferLookup<LevelSkillGroup>();
        __skillActiveIndices = state.GetBufferLookup<SkillActiveIndex>();
        __effectTargets = state.GetComponentLookup<EffectTarget>();
        __effectTargetDamageScales = state.GetComponentLookup<EffectTargetDamageScale>();
        __bulletDamageScales = state.GetComponentLookup<BulletDamageScale>();
        __instances = state.GetComponentLookup<ThirdPersonPlayer>();
        
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
                players[i] = state.EntityManager.Instantiate(prefabLoadResults[i].PrefabRoot);
        }
        
        __levelSkillNameDefinitions.Update(ref state);
        __levelSkillGroups.Update(ref state);
        __skillActiveIndices.Update(ref state);
        __effectTargets.Update(ref state);
        __effectTargetDamageScales.Update(ref state);
        __bulletDamageScales.Update(ref state);
        __instances.Update(ref state);
            
        Apply apply;
        apply.bulletDamageScale = LevelPlayerShared.bulletDamageScale;
        apply.effectTargetDamageScale = LevelPlayerShared.effectTargetDamageScale;
        apply.effectTargetHPScale = LevelPlayerShared.effectTargetHPScale;
        apply.activeSkills = LevelPlayerShared.activeSkills;
        apply.skillGroups = LevelPlayerShared.skillGroups;
        apply.entityArray = entityArray;
        apply.players = players;
        apply.levelSkillNameDefinitions = __levelSkillNameDefinitions;
        apply.levelSkillGroups = __levelSkillGroups;
        apply.skillActiveIndices = __skillActiveIndices;
        apply.effectTargets = __effectTargets;
        apply.effectTargetDamageScales = __effectTargetDamageScales;
        apply.bulletDamageScales = __bulletDamageScales;
        apply.instances = __instances;
        state.Dependency = apply.ScheduleByRef(entityArray.Length, 1, state.Dependency);
    }
}
