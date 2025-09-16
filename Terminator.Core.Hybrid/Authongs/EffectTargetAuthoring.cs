using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using ZG;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
public class EffectTargetAuthoring : MonoBehaviour, IMessageOverride
{
    [Serializable]
    public struct MessageData
    {
        [Tooltip("填Play")]
        public string messageName;
        [Tooltip("受击Transition")]
        public Object messageValue;
        
        public GameObject receiverPrefab;
        
        [Tooltip("根据伤害类型决定播放哪一个动画")]
        public LayerMask layerMask;

        public float delayTime;

        [Tooltip("做为死亡消息时等待时间")] 
        public float deadTime;
        
        [Tooltip("概率，0则为1")]
        public float chance;
    }

    [Serializable]
    public struct ImmunityData
    {
        [Tooltip("重复多少次，0为无限次")]
        public int count;

        [Tooltip("受到多少次伤害后无敌，填0不生效")]
        public int times;
        
        [Tooltip("受到多少伤害后无敌，填0不生效")]
        public int damage;

        [Tooltip("每次进入无敌状态的持续时间")]
        public float time;
    }

    class Baker : Baker<EffectTargetAuthoring>
    {
        public override void Bake(EffectTargetAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);

            EffectTargetData instance;
            instance.targetType =
                9 == GetLayer() ? EffectTargetData.TargetType.Boss : EffectTargetData.TargetType.Normal;
            instance.hpMax = authoring._hp;
            instance.recoveryChance = authoring._recoveryChance;
            instance.recoveryTime = authoring._recoveryTime;
            instance.recoveryInvincibleTime = authoring._recoveryInvincibleTime;
            instance.recoveryMessageName = authoring._recoveryMessageName;
            instance.recoveryMessageValue = authoring._recoveryMessageValue;
            AddComponent(entity, instance);

            EffectTarget target;
            target.times = authoring._times;
            target.hp = authoring._hp;
            target.shield = authoring._shield;
            target.immunizedTime = 0.0f;
            target.invincibleTime = 0.0f;
            target.time = 0.0;
            AddComponent(entity, target);

            EffectTargetLevel level;
            level.value = authoring._level;
            level.exp = authoring._exp;
            level.gold = authoring._gold;
            level.goldMultiplier = 0.0f;
            AddComponent(entity, level);
            
            var messages = AddBuffer<EffectTargetMessage>(entity);
            
            int numMessages = authoring._messages.Length;
            
            messages.Resize(numMessages, NativeArrayOptions.UninitializedMemory);

            float delayTime = 0.0f;
            var prefabLoaders = new Dictionary<GameObject, EntityPrefabReference>();
            for (int i = 0; i < numMessages; ++i)
            {
                ref var source = ref authoring._messages[i];
                ref var destination = ref messages.ElementAt(i);
                
                delayTime = Mathf.Max(delayTime, source.delayTime);

                destination.layerMask = (uint)source.layerMask.value;
                destination.chance = source.chance > Mathf.Epsilon ? source.chance : 1.0f;
                destination.delayTime = source.delayTime;
                destination.deadTime = source.deadTime;
                destination.messageName = source.messageName;
                destination.messageValue = source.messageValue;
                
                if (source.receiverPrefab == null)
                    destination.entityPrefabReference = default;
                else if (!prefabLoaders.TryGetValue(source.receiverPrefab, out destination.entityPrefabReference))
                {
                    destination.entityPrefabReference = new EntityPrefabReference(source.receiverPrefab);

                    prefabLoaders[source.receiverPrefab] = destination.entityPrefabReference;
                }
            }

            if (delayTime > 0.0f)
                AddBuffer<DelayTime>(entity);

            if (authoring._attributeParameter != null)
            {
                EffectTargetMessage message;
                message.layerMask = ~0u;
                message.chance = 1.0f;
                message.delayTime = 0.0f;
                message.deadTime = 0.0f;
                message.entityPrefabReference = default;
                message.messageName = "UpdateAttribute";
                message.messageValue = authoring._attributeParameter;

                messages.Add(message);
            }
            
            AddComponent<EffectTargetHP>(entity);
            SetComponentEnabled<EffectTargetHP>(entity, false);
            
            AddComponent<EffectTargetDamage>(entity);
            SetComponentEnabled<EffectTargetDamage>(entity, false);

            EffectTargetDamageScale damageScale;
            damageScale.value = authoring._damageScale;
            AddComponent(entity, damageScale);

            int numImmunities = authoring._immunities == null ? 0 : authoring._immunities.Length;
            if (numImmunities > 0)
            {
                EffectTargetImmunityDefinitionData immunity;
                using(var builder = new BlobBuilder(Allocator.Temp))
                {
                    ref var definition = ref builder.ConstructRoot<EffectTargetImmunityDefinition>();
                    var immunities = builder.Allocate(ref definition.immunities, numImmunities);
                    for(int i = 0; i < numImmunities; i++)
                    {
                        ref var source = ref authoring._immunities[i];
                        ref var destination = ref immunities[i];

                        destination.count = source.count;
                        destination.times = source.times;
                        destination.damage = source.damage;
                        destination.time = source.time;
                    }

                    immunity.definition = builder.CreateBlobAssetReference<EffectTargetImmunityDefinition>(Allocator.Persistent);
                }

                AddBlobAsset(ref immunity.definition, out _);

                AddComponent(entity, immunity);
                AddComponent<EffectTargetImmunityStatus>(entity);
            }
        }
    }
    
    [SerializeField]
    internal int _times = 0;

    [SerializeField]
    internal int _hp = 1;

    [SerializeField]
    internal int _shield = 0;

    [SerializeField] 
    internal int _level = 1;

    [SerializeField] 
    internal int _exp = 1;

    [SerializeField] 
    internal int _gold = 1;
    
    [SerializeField] 
    internal float _damageScale = 1.0f;

    [Tooltip("复活概率"), SerializeField] 
    internal float _recoveryChance = 1.0f;
    
    [Tooltip("复活时间"), SerializeField] 
    internal float _recoveryTime = 3.0f;

    [Tooltip("复活之后的无敌时间"), SerializeField] 
    internal float _recoveryInvincibleTime = 10.0f;

    [Tooltip("复活事件"), SerializeField]
    internal string _recoveryMessageName;
    
    [Tooltip("复活事件"), SerializeField]
    internal Object _recoveryMessageValue;

    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("attributeParameter")] 
    internal Object _attributeParameter;

    [SerializeField]
    internal MessageData[] _messages;

    [Tooltip("无敌"), SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_invulnerabilities")]
    internal ImmunityData[] _immunities;

    public bool Apply(ref DynamicBuffer<Message> messages, ref DynamicBuffer<MessageParameter> messageParameters)
    {
        if (_attributeParameter == null)
            return false;
        
        Message message;
        message.key = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        message.name = "UpdateAttribute";
        message.value = _attributeParameter;
        messages.Add(message);

        MessageParameter parameter;
        parameter.messageKey = message.key;
        parameter.id = (int)EffectAttributeID.HPMax;
        parameter.value = _hp;
        messageParameters.Add(parameter);

        if (_shield != 0)
        {
            parameter.messageKey = message.key;
            parameter.id = (int)EffectAttributeID.Shield;
            parameter.value = _shield;
            messageParameters.Add(parameter);
        }

        return true;
    }
}
#endif
