using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Transforms;
using Object = UnityEngine.Object;

public enum EffectAttributeID
{
    HPMax, 
    HP,
    Damage
}

public enum EffectSpace
{
    Source, 
    Destination, 
    Target
}

public struct EffectTargetInvulnerabilityDefinition
{
    public struct Invulnerability
    {
        public int count;
        public int times;
        public int damage;
        public float time;
    }

    public BlobArray<Invulnerability> invulnerabilities;
}

public struct EffectDefinition
{
    public struct Prefab
    {
        public EffectSpace space;
        public int index;
        public float chance;
    }
    
    public struct Damage
    {
        public int layerMask;
        public int entityLayerMask;
        public int messageLayerMask;

        public int value;

        public int valueToDrop;

        public float spring;

        public float explosion;

        public float delayDestroyTime;
        
        public BlobArray<int> messageIndices;
        
        public BlobArray<Prefab> prefabs;
    }
    
    public struct Effect
    {
        public int count;

        public float time;
        
        public float startTime;

        public BlobArray<int> damageIndices;
    }

    public BlobArray<Damage> damages;
    public BlobArray<Effect> effects;
}

public struct EffectDefinitionData : IComponentData
{
    public BlobAssetReference<EffectDefinition> definition;
}

public struct EffectDamage : IComponentData
{
    public float scale;

    public static float Compute(
        in Entity entity, 
        in ComponentLookup<Parent> parents,
        //in ComponentLookup<FollowTargetParent> followTargetParents,
        in ComponentLookup<EffectDamage> damages)
    {
        float result = damages.TryGetComponent(entity, out var damage) ? damage.scale : 1;
        if (parents.TryGetComponent(entity, out var parent))
            result *= Compute(parent.Value, parents, damages);
        /*else if(followTargetParents.TryGetComponent(entity, out var followTargetParent))
            result *= Compute(followTargetParent.entity, parents, followTargetParents, damages);*/

        return result;
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
    public WeakObjectReference<Object> value;
    public EntityPrefabReference entityPrefabReference;
}

public struct EffectStatusTarget : IBufferElementData, IEnableableComponent
{
    public Entity entity;
}

public struct EffectTargetData : IComponentData
{
    public int hpMax;
    public float resetTime;

    public FixedString128Bytes resetMessageName;
    public WeakObjectReference<Object> resetMessageValue;
}

public struct EffectTarget : IComponentData
{
    public int times;
    public int hp;
    public double invincibleTime;
}

public struct EffectTargetHP : IComponentData, IEnableableComponent
{
    public int layerMask;
    public int value;
    
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

public struct EffectTargetDamage : IComponentData, IEnableableComponent
{
    public int layerMask;
    public int value;
    
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

public struct EffectTargetInvulnerabilityDefinitionData : IComponentData
{
    public BlobAssetReference<EffectTargetInvulnerabilityDefinition> definition;
}

public struct EffectTargetInvulnerabilityStatus : IComponentData
{
    public int count;
    public int index;
    public int times;
    public int damage;
}

public struct EffectTargetMessage : IBufferElementData
{
    public uint layerMask;
    public EntityPrefabReference entityPrefabReference;
    public FixedString128Bytes messageName;
    public WeakObjectReference<Object> messageValue;
}