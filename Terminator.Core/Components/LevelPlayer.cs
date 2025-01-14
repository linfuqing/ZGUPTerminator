using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

public struct LevelPlayer : IComponentData
{
}

public static class LevelPlayerShared
{
    private class Value<T>
    {
        private static readonly SharedStatic<float> __value = SharedStatic<float>.GetOrCreate<T>();

        public static float value
        {
            get => __value.Data;

            set => __value.Data = value;
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
        private static readonly SharedStatic<FixedList512Bytes<FixedString32Bytes>> __values =
            SharedStatic<FixedList512Bytes<FixedString32Bytes>>.GetOrCreate<LevelSkills>();

        public static ref FixedList512Bytes<FixedString32Bytes> names => ref __values.Data;
    }
    
    private struct LevelSkills
    {
        private static readonly SharedStatic<FixedList512Bytes<FixedString32Bytes>> __values =
            SharedStatic<FixedList512Bytes<FixedString32Bytes>>.GetOrCreate<LevelSkills>();

        public static ref FixedList512Bytes<FixedString32Bytes> names => ref __values.Data;
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

    public static ref FixedList512Bytes<FixedString32Bytes> activeSkillNames => ref ActiveSkills.names;
    
    public static ref FixedList512Bytes<FixedString32Bytes> levelSkillNames => ref LevelSkills.names;
}