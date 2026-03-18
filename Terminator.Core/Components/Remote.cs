using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct RemoteIdentity : IComponentData
{
    public uint id;
}

public struct RemoteEffectTargetDamage : IBufferElementData
{
    public EffectTargetDamage value;
    
    public RemoteEffectTargetDamage(ref DataStreamReader reader, in StreamCompressionModel streamCompressionModel)
    {
        value.value = reader.ReadPackedInt(streamCompressionModel);
        value.valueImmunized = reader.ReadPackedInt(streamCompressionModel);
        value.layerMask = reader.ReadPackedInt(streamCompressionModel);
        value.messageLayerMask = reader.ReadPackedInt(streamCompressionModel);
    }

    public void Write(ref DataStreamWriter writer, in StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedInt(value.value,  streamCompressionModel);
        writer.WritePackedInt(value.valueImmunized, streamCompressionModel);
        writer.WritePackedInt(value.layerMask, streamCompressionModel);
        writer.WritePackedInt(value.messageLayerMask, streamCompressionModel);
    }
}

public struct RemoteEffectTargetHP : IBufferElementData
{
    public EffectTargetHP value;

    public RemoteEffectTargetHP(ref DataStreamReader reader, in StreamCompressionModel streamCompressionModel)
    {
        value.value = reader.ReadPackedInt(streamCompressionModel);
        value.shield = reader.ReadPackedInt(streamCompressionModel);
        value.messageLayerMask = reader.ReadPackedInt(streamCompressionModel);
    }

    public void Write(ref DataStreamWriter writer, in StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedInt(value.value,  streamCompressionModel);
        writer.WritePackedInt(value.shield, streamCompressionModel);
        writer.WritePackedInt(value.messageLayerMask, streamCompressionModel);
    }
}

public struct RemotePosition : IBufferElementData
{
    public enum Type
    {
        Normal, 
        Key, 
        Wrap
    }

    public Type type;
    public float2 value;

    public RemotePosition(ref DataStreamReader reader, in StreamCompressionModel streamCompressionModel)
    {
        type = (Type)reader.ReadPackedInt(streamCompressionModel);
        value.x = reader.ReadPackedFloat(streamCompressionModel);
        value.y = reader.ReadPackedFloat(streamCompressionModel);
    }

    public void Write(ref DataStreamWriter writer, in StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedInt((int)type, streamCompressionModel);
        writer.WritePackedFloat(value.x, streamCompressionModel);
        writer.WritePackedFloat(value.y, streamCompressionModel);
    }
}
