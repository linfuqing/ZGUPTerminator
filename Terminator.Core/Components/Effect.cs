using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Transforms;
using Object = UnityEngine.Object;

public enum EffectAttributeID
{
    HPMax, 
    HP,
    Damage
}

public struct EffectDefinition
{
    public struct Damage
    {
        public int layerMask;
        
        public int value;

        public int valueToDrop;

        public float spring;

        public float explosion;

        public float delayDestroyTime;
    }
    
    public struct Effect
    {
        public int messageLayerMask;

        public int count;

        //public float suction;

        public float time;
        
        public float startTime;

        public BlobArray<int> messageIndices;
        public BlobArray<int> damageIndices;
    }

    public BlobArray<Effect> effects;
    public BlobArray<Damage> damages;
}

public struct EffectDefinitionData : IComponentData
{
    public BlobAssetReference<EffectDefinition> definition;
}

public struct EffectDamage : IComponentData
{
    public int scale;

    public static int Compute(
        in Entity entity, 
        in ComponentLookup<Parent> parents,
        in ComponentLookup<EffectDamage> damages)
    {
        int result = damages.TryGetComponent(entity, out var damage) ? damage.scale : 1;
        if (parents.TryGetComponent(entity, out var parent))
            result *= Compute(parent.Value, parents, damages);

        return result;
    }
}

public struct EffectStatus : IComponentData, IEnableableComponent
{
    public int index;
    public int count;
    public double time;
}

public struct EffectMessage : IBufferElementData
{
    public FixedString128Bytes name;
    public WeakObjectReference<Object> value;
    public Entity receiverPrefabLoader;
}

public struct EffectStatusTarget : IBufferElementData, IEnableableComponent
{
    public Entity entity;
}


public struct EffectTargetData : IComponentData
{
    public int hpMax;
    public float resetTime;

    public Message resetMessage;
}

public struct EffectTarget : IComponentData
{
    public int times;
    public int hp;
}

public struct EffectTargetDamage : IComponentData, IEnableableComponent
{
    public int layerMask;
    public int value;
    
    /*public float3 force;

    public void Add(in float3 value)
    {
        ZG.Mathematics.Math.InterlockedAdd(ref force, value);
    }*/
    
    public void Add(int value, int layerMask)
    {
        Interlocked.Add(ref this.value, value);

        if (layerMask == 0 || layerMask == -1)
            this.layerMask = -1;
        else
        {
            int origin;
            do
            {
                origin = this.layerMask;
            } while (Interlocked.CompareExchange(ref this.layerMask, origin | layerMask, origin) != origin);
        }
    }
}

public struct EffectTargetDamageScale : IComponentData
{
    public float value;
}

public struct EffectTargetLevel : IComponentData
{
    public int value;
    public int exp;
    public int gold;
}

public struct EffectTargetMessage : IBufferElementData
{
    public uint layerMask;
    public Entity receiverPrefabLoader;
    public FixedString128Bytes messageName;
    public WeakObjectReference<Object> messageValue;
}