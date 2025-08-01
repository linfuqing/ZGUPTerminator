using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Object = UnityEngine.Object;

public enum EffectAttributeID
{
    RageMax, 
    Rage, 
    HPMax, 
    HP,
    Damage
}

public enum EffectSpace
{
    World, 
    Local
}

public struct EffectTargetImmunityDefinition
{
    public struct Immunity
    {
        public int count;
        public int times;
        public int damage;
        public float time;
    }

    public BlobArray<Immunity> immunities;
}

public struct EffectDefinition
{
    public struct Prefab
    {
        public EffectSpace space;
        public int index;
        public float chance;
        public float damageScale;
        public BulletLayerMask bulletLayerMask;
    }
    
    public struct Damage
    {
        public int layerMask;
        public int entityLayerMask;
        public int messageLayerMask;

        public int value;

        public int valueToDrop;

        public int valueImmunized;
        
        public float goldMultiplier;

        public float spring;

        public float explosion;

        public float delayDestroyTime;
        
        public BulletLayerMask bulletLayerMask;
        
        public BlobArray<int> messageIndices;
        
        public BlobArray<Prefab> prefabs;
    }
    
    public struct Effect
    {
        public int count;

        public float time;
        
        public float startTime;

        public BlobArray<int> damageIndices;
        
        public BlobArray<Prefab> prefabs;
    }

    public BlobArray<Damage> damages;
    public BlobArray<Effect> effects;
    public BlobArray<Prefab> prefabs;
}

public struct EffectDefinitionData : IComponentData
{
    public BlobAssetReference<EffectDefinition> definition;
}

public struct EffectDamageParent : IComponentData
{
    public int index;
    public Entity entity;

    public EffectDamageParent GetRoot(
        in ComponentLookup<EffectDamageParent> damageParents,
        in ComponentLookup<EffectDamage> damages)
    {
        if(damageParents.TryGetComponent(entity, out EffectDamageParent parent))
            return parent.GetRoot(damageParents, damages);

        return this;
    }
    
    public static bool TryGetComponent<T>(
        in Entity entity, 
        in ComponentLookup<EffectDamageParent> damageParents,
        in ComponentLookup<T> damages, 
        out T damage, 
        out Entity result) where T : unmanaged, IComponentData
    {
        if (damages.TryGetComponent(entity, out damage))
        {
            result = entity;
            
            return true;
        }

        if (damageParents.TryGetComponent(entity, out var damageParent))
            return TryGetComponent(damageParent.entity, damageParents, damages, out damage, out result);

        result = Entity.Null;
        
        return false;
    }
}

public struct EffectDamage : IComponentData
{
    public float scale;

    public BulletLayerMask bulletLayerMask;
}

public struct EffectDamageStatistic : IBufferElementData
{
    public int count;
    public int value;

    public static void Add(
        int count, 
        int value,
        in EffectDamageParent damageParent,
        in ComponentLookup<EffectDamageParent> damageParents,
        ref BufferLookup<EffectDamageStatistic> instances)
    {
        if (damageParent.index >= 0 &&
            instances.TryGetBuffer(damageParent.entity, out var buffer) &&
            buffer.Length > damageParent.index)
        {
            ref var result = ref buffer.ElementAt(damageParent.index);
            Interlocked.Add(ref result.count, count);
            Interlocked.Add(ref result.value, value);
        }

        if (damageParents.TryGetComponent(damageParent.entity, out var temp))
            Add(
                count,
                value, 
                temp, 
                damageParents, 
                ref instances);
    }
}

public struct EffectStatus : IComponentData, IEnableableComponent
{
    public int index;
    public int count;
    public double time;
}

public struct EffectPrefab : IBufferElementData
{
    public EntityPrefabReference entityPrefabReference;
}

public struct EffectMessage : IBufferElementData
{
    public FixedString128Bytes name;
    public UnityObjectRef<Object> value;
    public EntityPrefabReference entityPrefabReference;
}

public struct EffectStatusTarget : IBufferElementData, IEnableableComponent
{
    public Entity entity;
}

public struct EffectTargetData : IComponentData
{
    public enum TargetType
    {
        Normal, 
        Boss
    }

    public TargetType targetType;
    public int hpMax;
    public float recoveryChance;
    public float recoveryTime;
    public float recoveryInvincibleTime;

    public FixedString128Bytes recoveryMessageName;
    public UnityObjectRef<Object> recoveryMessageValue;
}

public struct EffectTarget : IComponentData, IEnableableComponent
{
    public int times;
    public int hp;
    public float immunizedTime;
    public float invincibleTime;
}

public struct EffectTargetHP : IComponentData, IEnableableComponent
{
    public int layerMask;
    public int value;
    
    public void Add(int value, int layerMask)
    {
        Interlocked.Add(ref this.value, value);

        if (layerMask == -1)
            this.layerMask = -1;
        else
        {
            if (layerMask == 0)
                layerMask = 1;

            int origin;
            do
            {
                origin = this.layerMask;
            } while (Interlocked.CompareExchange(ref this.layerMask, origin | layerMask, origin) != origin);
        }
    }
}

public struct EffectTargetDamage : IComponentData, IEnableableComponent
{
    public int layerMask;
    public int value;
    public int valueImmunized;
    
    public void Add(int value, int valueImmunized, int layerMask)
    {
        Interlocked.Add(ref this.value, value);
        Interlocked.Add(ref this.valueImmunized, valueImmunized);

        if (layerMask == -1)
            this.layerMask = -1;
        else
        {
            if (layerMask == 0)
                layerMask = 1;

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

public struct EffectTargetImmunityDefinitionData : IComponentData
{
    public BlobAssetReference<EffectTargetImmunityDefinition> definition;
}

public struct EffectTargetImmunityStatus : IComponentData
{
    public int count;
    public int index;
    public int times;
    public int damage;
}

public struct EffectTargetMessage : IBufferElementData
{
    public uint layerMask;
    public float chance;
    public float delayTime;
    public float deadTime;
    public EntityPrefabReference entityPrefabReference;
    public FixedString128Bytes messageName;
    public UnityObjectRef<Object> messageValue;
}