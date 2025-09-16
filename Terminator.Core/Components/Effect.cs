using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Transforms;
using Object = UnityEngine.Object;

public enum EffectAttributeID
{
    RageMax, 
    Rage, 
    HPMax, 
    HP,
    Shield, 
    Damage
}

public enum EffectSpace
{
    World, 
    Local,
    Camera
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
    public struct Buff
    {
        public int capacity;
        public float interval;
        public float damageScalePerCount;
        public float damageScale;
        public FixedString32Bytes name;
    }
    
    public struct Prefab
    {
        public EffectSpace space;
        public int index;
        public int buffIndex;
        public int damageLayerMask;
        public float damageScale;
        public float chance;
        public LayerMaskAndTags layerMaskAndTags;
    }
    
    public struct Damage
    {
        public int layerMask;
        public int entityLayerMask;
        public int messageLayerMask;

        public int value;

        public int valueToDrop;

        public int valueImmunized;
        
        public float rageDamageMultiplier;

        public float shieldDamageMultiplier;

        public float hpMultiplier;

        public float goldMultiplier;

        public float spring;

        public float explosion;

        public float delayDestroyTime;
        
        public LayerMaskAndTags layerMaskAndTags;
        
        public BlobArray<int> messageIndices;
        
        public BlobArray<Prefab> prefabs;
    }
    
    public struct Effect
    {
        public int count;

        public float time;
        
        public float startTime;
        public float endTime;

        public BlobArray<int> damageIndices;
        
        public BlobArray<Prefab> prefabs;
    }

    public BlobArray<Buff> buffs;
    public BlobArray<Prefab> prefabs;
    public BlobArray<Damage> damages;
    public BlobArray<Effect> effects;
}

public struct EffectDefinitionData : IComponentData
{
    public BlobAssetReference<EffectDefinition> definition;
}

public struct EffectRage : IComponentData
{
    public float value;
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

    public LayerMaskAndTags layerMaskAndTags;
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
    public int shield;
    public float immunizedTime;
    public float invincibleTime;

    public double time;

    public float Update(double time, float deltaTime)
    {
        deltaTime = this.time > math.DBL_MIN_NORMAL ? (float)(time - this.time) : deltaTime;
        this.time = time;
            
        if (immunizedTime >= 0.0f)
            immunizedTime -= deltaTime;
            
        if (invincibleTime >= 0.0f)
            invincibleTime -= deltaTime;

        return deltaTime;
    }
}

public struct EffectTargetHP : IComponentData, IEnableableComponent
{
    public int value;
    public int layerMask;
    public int messageLayerMask;
    
    public void Add(int value, int layerMask, int messageLayerMask)
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
        
        if (messageLayerMask == -1)
            this.messageLayerMask = -1;
        else
        {
            if (messageLayerMask == 0)
                messageLayerMask = 1;

            int origin;
            do
            {
                origin = this.messageLayerMask;
            } while (Interlocked.CompareExchange(ref this.messageLayerMask, origin | messageLayerMask, origin) != origin);
        }
    }
}

public struct EffectTargetDamage : IComponentData, IEnableableComponent
{
    public int value;
    public int valueImmunized;
    public int layerMask;
    public int messageLayerMask;
    
    public int Add(int value, int valueImmunized, int layerMask, int messageLayerMask)
    {
        int result = Interlocked.Add(ref this.value, value);
        result += Interlocked.Add(ref this.valueImmunized, valueImmunized);

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
        
        if (messageLayerMask == -1)
            this.messageLayerMask = -1;
        else
        {
            if (messageLayerMask == 0)
                messageLayerMask = 1;

            int origin;
            do
            {
                origin = this.messageLayerMask;
            } while (Interlocked.CompareExchange(ref this.messageLayerMask, origin | messageLayerMask, origin) != origin);
        }
        
        return result;
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
    public float goldMultiplier;
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

public struct EffectTargetBuff : ICleanupComponentData
{
    public int times;
    public double time;
    public FixedString32Bytes name;

    public static bool Append(
        ref ComponentLookup<EffectTargetBuff> buffs,
        in DynamicBuffer<Child> children, 
        in FixedString32Bytes name, 
        double time, 
        float interval,
        int capacity, 
        out int times, 
        out Entity entity)
    {
        times = 0;
        entity = Entity.Null;

        EffectTargetBuff buff;
        foreach (var child in children)
        {
            if (buffs.TryGetComponent(child.Value, out buff) && buff.name == name)
            {
                entity = child.Value;
                times = buff.times;
                
                if (buff.time + interval > time)
                    break;

                buff.time = time;

                if(buff.times < capacity)
                    ++buff.times;

                buffs[child.Value] = buff;
                
                return true;
            }
        }

        return false;
    }
}