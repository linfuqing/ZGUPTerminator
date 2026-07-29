using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

/// <summary>
/// Mirrors <see cref="LevelPlayerSystem.Apply.__Apply"/> for replay when spawn ran with empty
/// <see cref="LevelPlayerShared{RemotePlayer}.property"/> or property was re-applied after spawn.
/// </summary>
internal static class LevelRecordingRemotePropertySync
{
    public static bool TryApplyRecordingPropertyToRemoteEntity(List<int> simulatedActiveSkillValues)
    {
        simulatedActiveSkillValues?.Clear();

        if (!LevelRecordingReplayPropertyOps.TryResolveRecordingProperty(out var property))
        {
            BotReplayLog.Warn(
                "Recording has no usable PlayerProperty; cannot sync spawned remote entity.");
            return false;
        }

        if (LevelRecordingReplayPropertyOps.IsPropertyEmpty(in LevelPlayerShared<RemotePlayer>.property))
        {
            ReplyMessageReplayInjector.ApplySharedProperty(in property, promoteJoined: false);
        }

        return __ApplyPropertyToRemoteEntity(in property, simulatedActiveSkillValues);
    }

    public static bool TryApplySharedPropertyToRemoteEntity(List<int> simulatedActiveSkillValues)
    {
        return TryApplyRecordingPropertyToRemoteEntity(simulatedActiveSkillValues);
    }

    private static bool __ApplyPropertyToRemoteEntity(
        in LevelPlayerProperty property,
        List<int> simulatedActiveSkillValues)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        var entityManager = world.EntityManager;
        uint remoteUserId = LevelPlayerShared<RemotePlayer>.id;
        if (remoteUserId == 0)
            return false;

        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<RemoteIdentity>(),
            ComponentType.ReadOnly<LevelSkillNameDefinitionData>());
        if (query.IsEmpty)
            return false;

        entityManager.CompleteAllTrackedJobs();

        using var entities = query.ToEntityArray(Allocator.Temp);
        using var identities = query.ToComponentDataArray<RemoteIdentity>(Allocator.Temp);

        Entity remoteEntity = Entity.Null;
        for (int i = 0; i < entities.Length; ++i)
        {
            if (identities[i].id == remoteUserId)
            {
                remoteEntity = entities[i];
                break;
            }
        }

        if (remoteEntity == Entity.Null)
            return false;

        __ApplySkills(entityManager, remoteEntity, in property);
        __ApplyEffectState(entityManager, remoteEntity, in property);

        if (entityManager.HasBuffer<SkillActiveIndex>(remoteEntity) &&
            simulatedActiveSkillValues != null)
        {
            LevelRecordingSelectSkillReplayOps.SeedFromActiveIndices(
                entityManager.GetBuffer<SkillActiveIndex>(remoteEntity),
                simulatedActiveSkillValues);
        }

        BotReplayLog.Diag(
            $"Synced spawned remote entity from recording property " +
            $"(activeSkills={property.activeSkills.Length}, instance={property.instanceName}).");
        return true;
    }

    private static void __ApplySkills(EntityManager entityManager, Entity player, in LevelPlayerProperty property)
    {
        if (property.activeSkills.Length == 0 && property.skillGroups.Length == 0)
            return;

        if (!entityManager.HasComponent<LevelSkillNameDefinitionData>(player))
            return;

        var definitionData = entityManager.GetComponentData<LevelSkillNameDefinitionData>(player);
        if (!definitionData.definition.IsCreated)
            return;

        ref var definition = ref definitionData.definition.Value;

        if (property.maskSkillNames.Length > 0 && entityManager.HasBuffer<LevelSkillMask>(player))
        {
            var levelSkillMasks = entityManager.GetBuffer<LevelSkillMask>(player);
            levelSkillMasks.Clear();
            int numSkills = definition.skills.Length;
            foreach (var maskSkillName in property.maskSkillNames)
            {
                for (int i = 0; i < numSkills; ++i)
                {
                    if (definition.skills[i] == maskSkillName)
                    {
                        LevelSkillMask levelSkillMask;
                        levelSkillMask.index = i;
                        levelSkillMasks.Add(levelSkillMask);
                        break;
                    }
                }
            }
        }

        if (property.activeSkills.Length > 0 && entityManager.HasBuffer<SkillActiveIndex>(player))
        {
            var skillActiveIndices = entityManager.GetBuffer<SkillActiveIndex>(player);
            skillActiveIndices.Clear();
            int numSkills = definition.skills.Length;
            foreach (var activeSkill in property.activeSkills)
            {
                for (int i = 0; i < numSkills; ++i)
                {
                    if (definition.skills[i] != activeSkill.name)
                        continue;

                    SkillActiveIndex skillActiveIndex;
                    skillActiveIndex.value = i;
                    skillActiveIndex.damageScale = 1.0f + activeSkill.damageScale + property.effectDamageScale;
                    skillActiveIndices.Add(skillActiveIndex);
                    break;
                }
            }
        }

        if (property.skillGroups.Length > 0 && entityManager.HasBuffer<LevelSkillGroup>(player))
        {
            var levelSkillGroups = entityManager.GetBuffer<LevelSkillGroup>(player);
            levelSkillGroups.Clear();
            int numGroups = definition.groups.Length;
            foreach (var skillGroup in property.skillGroups)
            {
                for (int i = 0; i < numGroups; ++i)
                {
                    if (definition.groups[i] != skillGroup.name)
                        continue;

                    LevelSkillGroup levelSkillGroup;
                    levelSkillGroup.value = i;
                    levelSkillGroup.damageScale = 1.0f + skillGroup.damageScale + property.effectDamageScale;
                    levelSkillGroups.Add(levelSkillGroup);
                    break;
                }
            }
        }

        if (property.skillOpcodes.Length > 0 && entityManager.HasBuffer<LevelSkillOpcode>(player))
        {
            var levelSkillOpcodes = entityManager.GetBuffer<LevelSkillOpcode>(player);
            levelSkillOpcodes.Clear();
            int numSkills = definition.skills.Length;
            foreach (var skillOpcode in property.skillOpcodes)
            {
                for (int i = 0; i < numSkills; ++i)
                {
                    if (definition.skills[i] != skillOpcode.name)
                        continue;

                    LevelSkillOpcode levelSkillOpcode;
                    levelSkillOpcode.index = i;
                    levelSkillOpcode.type = skillOpcode.type;
                    levelSkillOpcode.value = skillOpcode.value;
                    levelSkillOpcodes.Add(levelSkillOpcode);
                    break;
                }
            }
        }
    }

    private static void __ApplyEffectState(EntityManager entityManager, Entity player, in LevelPlayerProperty property)
    {
        if (!entityManager.HasComponent<EffectTarget>(player))
            return;

        var effectTarget = entityManager.GetComponentData<EffectTarget>(player);
        int hp = property.effectTargetHP == 0 ? effectTarget.hp : property.effectTargetHP;
        hp = effectTarget.hp + (int)math.round(hp * property.effectTargetHPScale);

        effectTarget.hp = hp;
        if (property.effectTargetRecovery > math.FLT_MIN_NORMAL)
            effectTarget.times = (int)math.floor(property.effectTargetRecovery);

        entityManager.SetComponentData(player, effectTarget);

        if (entityManager.HasComponent<EffectTargetData>(player))
        {
            var effectTargetData = entityManager.GetComponentData<EffectTargetData>(player);
            float recoveryChance = property.effectTargetRecovery - effectTarget.times;
            if (recoveryChance > math.FLT_MIN_NORMAL)
                effectTargetData.recoveryChance = recoveryChance;

            effectTargetData.recoveryTimeBeenKeptOfMaxTimes =
                effectTarget.times - property.effectTargetRecoveryTimes;
            effectTargetData.hpMax = hp;
            entityManager.SetComponentData(player, effectTargetData);
        }

        if (math.abs(property.effectTargetDamageScale) > math.FLT_MIN_NORMAL &&
            entityManager.HasComponent<EffectTargetDamageScale>(player))
        {
            float hpScale = 1.0f + property.effectTargetHPScale;
            var effectTargetDamageScale = entityManager.GetComponentData<EffectTargetDamageScale>(player);
            effectTargetDamageScale.value = hpScale / (hpScale + property.effectTargetDamageScale);
            entityManager.SetComponentData(player, effectTargetDamageScale);
        }

        if (entityManager.HasComponent<EffectRage>(player))
        {
            EffectRage effectRage;
            effectRage.value = property.effectRage;
            entityManager.SetComponentData(player, effectRage);
        }
    }
}
