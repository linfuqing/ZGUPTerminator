using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

public struct LevelPlayer : IComponentData
{
}

public struct LevelPlayerActiveSkill
{
    public FixedString32Bytes name;

    public float damageScale;
}

public struct LevelPlayerSkillGroup
{
    public FixedString32Bytes name;
    
    public float damageScale;
}

public static class LevelPlayerShared
{
    private class Value<TChildClass, TValue> where TValue : unmanaged
    {
        private static readonly SharedStatic<TValue> Result = SharedStatic<TValue>.GetOrCreate<TChildClass>();

        public static TValue value
        {
            get => Result.Data;

            set => Result.Data = value;
        }
    }
    
    private class EffectDamageScale : Value<EffectDamageScale, float>
    {
    }
    
    private class EffectTargetDamageScale : Value<EffectTargetDamageScale, float>
    {
    }
    
    private class EffectTargetHPScale : Value<EffectTargetHPScale, float>
    {
    }

    private class InstanceName : Value<InstanceName, FixedString32Bytes>
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

    public static FixedString32Bytes instanceName
    {
        get => InstanceName.value;

        set => InstanceName.value = value;
    }

    public static ref FixedList512Bytes<LevelPlayerActiveSkill> activeSkills => ref ActiveSkills.values;
    
    public static ref FixedList512Bytes<LevelPlayerSkillGroup> skillGroups => ref SkillGroup.names;
}