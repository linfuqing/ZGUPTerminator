using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

public interface ILevelPlayer
{
    
}

public struct RemotePlayer : IComponentData, ILevelPlayer
{
    public enum Status
    {
        Disabled, 
        Waiting,
        Joined, 
        StandBy
    }
    
    private static readonly SharedStatic<Status> StatusValue = SharedStatic<Status>.GetOrCreate<RemotePlayer>();

    public static Status status
    {
        get => StatusValue.Data;

        set => StatusValue.Data = value;
    }
}

public struct LocalPlayer : IComponentData, ILevelPlayer
{
    private static readonly SharedStatic<int> InstanceID = SharedStatic<int>.GetOrCreate<LevelPlayer>();

    public static int instanceID
    {
        get => InstanceID.Data;

        set => InstanceID.Data = value;
    }
}

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

public struct LevelPlayerSkillOpcode
{
    public FixedString32Bytes name;

    public LevelSkillOpcode.Type type;
    
    public float value;
}

public struct LevelPlayerProperty
{
    public int effectTargetRecoveryTimes;

    public int effectRage;

    public int effectTargetHP;

    public float effectTargetHPScale;

    public float effectTargetDamageScale;

    public float effectDamageScale;

    public float effectTargetRecovery;

    public FixedString32Bytes instanceName;

    public FixedList4096Bytes<LevelPlayerActiveSkill> activeSkills;
    
    public FixedList4096Bytes<LevelPlayerSkillGroup> skillGroups;
    
    public FixedList4096Bytes<LevelPlayerSkillOpcode> skillOpcodes;

    public LevelPlayerProperty(ref DataStreamReader reader, StreamCompressionModel streamCompressionModel)
    {
        effectTargetRecoveryTimes = reader.ReadPackedInt(streamCompressionModel);
        effectRage = reader.ReadPackedInt(streamCompressionModel);
        effectTargetHP = reader.ReadPackedInt(streamCompressionModel);
        effectTargetHPScale = reader.ReadPackedFloat(streamCompressionModel);
        effectTargetDamageScale = reader.ReadPackedFloat(streamCompressionModel);
        effectDamageScale = reader.ReadPackedFloat(streamCompressionModel);
        effectTargetRecovery = reader.ReadPackedFloat(streamCompressionModel);
        instanceName = reader.ReadFixedString32();
        
        activeSkills = new FixedList4096Bytes<LevelPlayerActiveSkill>();
        LevelPlayerActiveSkill activeSkill;
        int numActiveSkills = reader.ReadByte();
        for (int i = 0; i < numActiveSkills; ++i)
        {
            activeSkill.name = reader.ReadFixedString32();
            activeSkill.damageScale = reader.ReadPackedFloat(streamCompressionModel);
            activeSkills.Add(activeSkill);
        }
        
        skillGroups = new FixedList4096Bytes<LevelPlayerSkillGroup>();
        LevelPlayerSkillGroup skillGroup;
        int numSkillGroups = reader.ReadByte();
        for (int i = 0; i < numSkillGroups; ++i)
        {
            skillGroup.name = reader.ReadFixedString32();
            skillGroup.damageScale = reader.ReadPackedFloat(streamCompressionModel);
            skillGroups.Add(skillGroup);
        }
        
        skillOpcodes = new FixedList4096Bytes<LevelPlayerSkillOpcode>();
        LevelPlayerSkillOpcode skillOpcode;
        int numSkillOpcodes = reader.ReadByte();
        for (int i = 0; i < numSkillOpcodes; ++i)
        {
            skillOpcode.name = reader.ReadFixedString32();
            skillOpcode.type = (LevelSkillOpcode.Type)reader.ReadByte();
            skillOpcode.value = reader.ReadPackedFloat(streamCompressionModel);
            skillOpcodes.Add(skillOpcode);
        }
    }

    public void Write(ref DataStreamWriter writer, StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedInt(effectTargetRecoveryTimes, streamCompressionModel);
        writer.WritePackedInt(effectRage, streamCompressionModel);
        writer.WritePackedInt(effectTargetHP, streamCompressionModel);
        writer.WritePackedFloat(effectTargetHPScale, streamCompressionModel);
        writer.WritePackedFloat(effectTargetDamageScale, streamCompressionModel);
        writer.WritePackedFloat(effectDamageScale, streamCompressionModel);
        writer.WritePackedFloat(effectTargetRecovery, streamCompressionModel);
        writer.WriteFixedString32(instanceName);
        
        writer.WriteByte((byte)activeSkills.Length);
        foreach (var activeSkill in activeSkills)
        {
            writer.WriteFixedString32(activeSkill.name);
            writer.WritePackedFloat(activeSkill.damageScale, streamCompressionModel);
        }
        
        writer.WriteByte((byte)skillGroups.Length);
        foreach (var skillGroup in skillGroups)
        {
            writer.WriteFixedString32(skillGroup.name);
            writer.WritePackedFloat(skillGroup.damageScale, streamCompressionModel);
        }
        
        writer.WriteByte((byte)skillOpcodes.Length);
        foreach (var skillOpcode in skillOpcodes)
        {
            writer.WriteFixedString32(skillOpcode.name);
            writer.WriteByte((byte)skillOpcode.type);
            writer.WritePackedFloat(skillOpcode.value, streamCompressionModel);
        }
    }
}

public static class LevelPlayerShared<T> where T : ILevelPlayer
{
    private class Property
    {
        public static readonly SharedStatic<LevelPlayerProperty> Value = SharedStatic<LevelPlayerProperty>.GetOrCreate<Property>();
    }

    private class ID
    {
        public static readonly SharedStatic<uint> Value = SharedStatic<uint>.GetOrCreate<ID>();
    }

    public static ref LevelPlayerProperty property => ref Property.Value.Data;
    
    public static ref uint id => ref ID.Value.Data;
}