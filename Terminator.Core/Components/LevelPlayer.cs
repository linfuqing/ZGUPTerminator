using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

public struct LevelPlayer : IComponentData
{
}

public struct LevelPlayerActiveSkill
{
    public FixedString32Bytes name;
}

public struct LevelPlayerSkillGroup
{
    public FixedString32Bytes name;
}

public static class LevelPlayerShared
{
    private class Value<T>
    {
        private static readonly SharedStatic<float> Result = SharedStatic<float>.GetOrCreate<T>();

        public static float value
        {
            get => Result.Data;

            set => Result.Data = value;
        }
    }
    
    private class EffectDamageScale : Value<EffectDamageScale>
    {
    }
    
    private class EffectTargetDamageScale : Value<EffectTargetDamageScale>
    {
    }
    
    private class EffectTargetHPScale : Value<EffectTargetHPScale>
    {
    }

    private struct ActiveSkills
    {
        private static readonly SharedStatic<FixedList512Bytes<LevelPlayerActiveSkill>> Values =
            SharedStatic<FixedList512Bytes<LevelPlayerActiveSkill>>.GetOrCreate<ActiveSkills>();

        public static ref FixedList512Bytes<LevelPlayerActiveSkill> values => ref Values.Data;
    }
    
    private struct SkillGroup
    {
        private static readonly SharedStatic<FixedList512Bytes<LevelPlayerSkillGroup>> Values =
            SharedStatic<FixedList512Bytes<LevelPlayerSkillGroup>>.GetOrCreate<SkillGroup>();

        public static ref FixedList512Bytes<LevelPlayerSkillGroup> names => ref Values.Data;
    }

    public static float effectDamageScale
    {
        get => EffectDamageScale.value;

        set => EffectDamageScale.value = value;
    }
    
    public static float effectTargetDamageScale
    {
        get => EffectTargetDamageScale.value;

        set => EffectTargetDamageScale.value = value;
    }
    
    public static float effectTargetHPScale
    {
        get => EffectTargetHPScale.value;

        set => EffectTargetHPScale.value = value;
    }

    public static ref FixedList512Bytes<LevelPlayerActiveSkill> activeSkills => ref ActiveSkills.values;
    
    public static ref FixedList512Bytes<LevelPlayerSkillGroup> skillGroups => ref SkillGroup.names;
}