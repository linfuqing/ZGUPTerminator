using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

public interface ILevelPlayer
{
    
}

public struct RemotePlayer : IComponentData, ILevelPlayer
{
    public enum Status
    {
        //Error, 
        Canceled,
        Disabled, 
        Waiting,
        Joined, 
        StandBy
    }

    private static readonly SharedStatic<Status> StatusValue = SharedStatic<Status>.GetOrCreate<Status>();

    private static readonly SharedStatic<int> Version = SharedStatic<int>.GetOrCreate<RemotePlayer>();

    public static Status status
    {
        get => StatusValue.Data;

        set
        {
            StatusValue.Data = value;
        }
    }

    public static int version
    {
        get => Version.Data;
        
        set => Version.Data = value;
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

[StructLayout(LayoutKind.Explicit, Size=68)]
public struct LevelPlayerHeader
{
    [FieldOffset(0)]
    public int power;
    [FieldOffset(4)]
    public FixedString32Bytes name;
    [FieldOffset(36)]
    public FixedString32Bytes avatar;
        
    public LevelPlayerHeader(in FixedBytes80 bytes)
    {
        this = bytes.AsArray().GetSubArray(0, 68).Reinterpret<LevelPlayerHeader>(1)[0];
    }

    public void Write(ref DataStreamWriter writer)
    {
        FixedBytes80 bytes = default;
        var blocks = bytes.AsArray().GetSubArray(0, 68).Reinterpret<LevelPlayerHeader>(1);
        blocks[0] = this;
        bytes.Write(ref writer);
    }
}

public static class LevelPlayerShared<T> where T : ILevelPlayer
{
    private struct Header
    {
        public static readonly SharedStatic<LevelPlayerHeader> Value = SharedStatic<LevelPlayerHeader>.GetOrCreate<Header>();
    }

    private struct Property
    {
        public static readonly SharedStatic<LevelPlayerProperty> Value = SharedStatic<LevelPlayerProperty>.GetOrCreate<Property>();
    }

    private struct ID
    {
        public static readonly SharedStatic<uint> Value = SharedStatic<uint>.GetOrCreate<ID>();
    }

    private struct ChannelFlag
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<ChannelFlag>();
    }

    private struct Version
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<Version>();
    }

    public static ref LevelPlayerHeader header => ref Header.Value.Data;

    public static ref LevelPlayerProperty property => ref Property.Value.Data;
    
    public static ref uint id => ref ID.Value.Data;
    
    public static int channelStatus => channelFlag >> (int)NetworkRelayChannelFlag.ShiftToStatus;
    
    public static int channelFlag
    {
        get => ChannelFlag.Value.Data;

        set
        {
            ChannelFlag.Value.Data = value;
            
            UnityEngine.Debug.Log($"[LevelPlayer] Channel Status: {value}");

            if (value != 0)
                ++version;
        }
    }

    public static int version
    {
        get => Version.Value.Data;

        private set => Version.Value.Data = value;
    }

    public static bool isOnline => ((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Online) ==
                                   NetworkRelayChannelFlag.Online;
}