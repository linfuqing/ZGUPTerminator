using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct RemoteIdentity : IComponentData
{
    public uint id;
}

public struct RemoteCameraForward : IComponentData
{
    public float2 value;

    public RemoteCameraForward(ref DataStreamReader reader, in StreamCompressionModel streamCompressionModel)
    {
        value.x = reader.ReadPackedFloat(streamCompressionModel);
        value.y = reader.ReadPackedFloat(streamCompressionModel);
    }

    public void Write(ref DataStreamWriter writer, in StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedFloat(value.x, streamCompressionModel);
        writer.WritePackedFloat(value.y, streamCompressionModel);
    }
}

public struct RemotePosition : IBufferElementData
{
    public enum Type
    {
        Normal, 
        Key, 
        Warp
    }

    public Type type;
    public float2 value;

    public RemotePosition(ref DataStreamReader reader, in StreamCompressionModel streamCompressionModel)
    {
        type = (Type)reader.ReadPackedInt(streamCompressionModel);
        value.x = reader.ReadPackedFloat(streamCompressionModel);
        value.y = reader.ReadPackedFloat(streamCompressionModel);
        //value.z = reader.ReadPackedFloat(streamCompressionModel);
    }

    public void Write(ref DataStreamWriter writer, in StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedInt((int)type, streamCompressionModel);
        writer.WritePackedFloat(value.x, streamCompressionModel);
        writer.WritePackedFloat(value.y, streamCompressionModel);
        //writer.WritePackedFloat(value.z, streamCompressionModel);
    }

    public float3 GetPosition(float y) => math.float3(value.x, y, value.y);
}

public struct RemoteEffectTargetDamage : IBufferElementData
{
    public int hp;
    public int shield;
    public int layerMask;
    public int messageLayerMask;

    public RemoteEffectTargetDamage(ref DataStreamReader reader, in StreamCompressionModel streamCompressionModel)
    {
        hp = reader.ReadPackedInt(streamCompressionModel);
        shield = reader.ReadPackedInt(streamCompressionModel);
        layerMask = reader.ReadPackedInt(streamCompressionModel);
        messageLayerMask = reader.ReadPackedInt(streamCompressionModel);
    }

    public void Write(ref DataStreamWriter writer, in StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedInt(hp,  streamCompressionModel);
        writer.WritePackedInt(shield, streamCompressionModel);
        writer.WritePackedInt(layerMask, streamCompressionModel);
        writer.WritePackedInt(messageLayerMask, streamCompressionModel);
    }

    public void Apply(in EffectTarget target, ref EffectTargetDamage damage, ref EffectTargetHP hp)
    {
        damage.value = 0;
        if (this.hp > target.hp)
        {
            damage.valueImmunized = 0;
            damage.layerMask = 0;
            damage.messageLayerMask = 0;
            
            hp.value = this.hp - target.hp;
            hp.messageLayerMask = messageLayerMask;
        }
        else
        {
            damage.valueImmunized = target.hp - this.hp;
            damage.layerMask = layerMask;
            damage.messageLayerMask = messageLayerMask;
            
            hp.value = 0;
            hp.messageLayerMask = 0;
        }

        if (shield < target.shield)
        {
            damage.valueImmunized += target.shield - shield;
            damage.layerMask |= layerMask;
            damage.messageLayerMask |= messageLayerMask;

            hp.shield = 0;
        }
        else
        {
            hp.shield = shield - target.shield;
            if (hp.shield > 0)
                hp.messageLayerMask |= messageLayerMask;
        }
    }
}

/*public struct RemoteEffectTargetHP : IBufferElementData
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
}*/
