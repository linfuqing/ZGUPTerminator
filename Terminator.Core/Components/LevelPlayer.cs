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

    private class EffectRage : Value<EffectRage, int>
    {
        
    }
    
    private class EffectTargetHP : Value<EffectTargetHP, int>
    {
    }

    private class EffectTargetHPScale : Value<EffectTargetHPScale, float>
    {
    }

    private class EffectTargetRecovery : Value<EffectTargetRecovery, float>
    {
    }

    private class EffectTargetDamageScale : Value<EffectTargetDamageScale, float>
    {
    }

    private class EffectDamageScale : Value<EffectDamageScale, float>
    {
    }

    private class InstanceName : Value<InstanceName, FixedString32Bytes>
    {
    }
    
    private struct ActiveSkills
    {
        private static readonly SharedStatic<FixedList4096Bytes<LevelPlayerActiveSkill>> Values =
            SharedStatic<FixedList4096Bytes<LevelPlayerActiveSkill>>.GetOrCreate<ActiveSkills>();

        public static ref FixedList4096Bytes<LevelPlayerActiveSkill> values => ref Values.Data;
    }
    
    private struct SkillGroup
    {
        private static readonly SharedStatic<FixedList4096Bytes<LevelPlayerSkillGroup>> Values =
            SharedStatic<FixedList4096Bytes<LevelPlayerSkillGroup>>.GetOrCreate<SkillGroup>();

        public static ref FixedList4096Bytes<LevelPlayerSkillGroup> names => ref Values.Data;
    }

    public static int effectRage
    {
        get => EffectRage.value;
        
        set => EffectRage.value = value;
    }
    
    public static int effectTargetHP
    {
        get => EffectTargetHP.value;

        set => EffectTargetHP.value = value;
    }

    public static float effectTargetHPScale
    {
        get => EffectTargetHPScale.value;

        set => EffectTargetHPScale.value = value;
    }

    public static float effectTargetDamageScale
    {
        get => EffectTargetDamageScale.value;

        set => EffectTargetDamageScale.value = value;
    }

    public static float effectDamageScale
    {
        get => EffectDamageScale.value;

        set => EffectDamageScale.value = value;
    }
    
    public static float effectTargetRecovery
    {
        get => EffectTargetRecovery.value;

        set => EffectTargetRecovery.value = value;
    }

    public static FixedString32Bytes instanceName
    {
        get => InstanceName.value;

        set => InstanceName.value = value;
    }

    public static ref FixedList4096Bytes<LevelPlayerActiveSkill> activeSkills => ref ActiveSkills.values;
    
    public static ref FixedList4096Bytes<LevelPlayerSkillGroup> skillGroups => ref SkillGroup.names;
}