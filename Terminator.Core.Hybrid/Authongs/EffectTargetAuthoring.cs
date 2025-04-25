using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Scenes;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
public class EffectTargetAuthoring : MonoBehaviour, IMessageOverride
{
    [Serializable]
    public struct MessageData
    {
        [Tooltip("根据伤害类型决定播放哪一个动画")]
        public LayerMask layerMask;

        [Tooltip("填Play")]
        public string messageName;
        [Tooltip("受击Transition")]
        public Object messageValue;
        
        public GameObject receiverPrefab;
    }

    [Serializable]
    public struct InvulnerabilityData
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
            target.invincibleTime = 0.0f;
            AddComponent(entity, target);

            EffectTargetLevel level;
            level.value = authoring._level;
            level.exp = authoring._exp;
            level.gold = authoring._gold;
            AddComponent(entity, level);
            
            var messages = AddBuffer<EffectTargetMessage>(entity);
            
            int numMessages = authoring._messages.Length;
            
            messages.Resize(numMessages, NativeArrayOptions.UninitializedMemory);
            
            var prefabLoaders = new Dictionary<GameObject, EntityPrefabReference>();
            for (int i = 0; i < numMessages; ++i)
            {
                ref var source = ref authoring._messages[i];
                ref var destination = ref messages.ElementAt(i);

                destination.layerMask = (uint)source.layerMask.value;
                destination.delayTime = 0.0f;
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

            if (authoring._attributeParameter != null)
            {
                EffectTargetMessage message;
                message.layerMask = ~0u;
                message.delayTime = 0.0f;
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

            int numInvulnerabilities = authoring._invulnerabilities == null ? 0 : authoring._invulnerabilities.Length;
            if (numInvulnerabilities > 0)
            {
                EffectTargetInvulnerabilityDefinitionData invulnerability;
                using(var builder = new BlobBuilder(Allocator.Temp))
                {
                    ref var definition = ref builder.ConstructRoot<EffectTargetInvulnerabilityDefinition>();
                    var invulnerabilitys = builder.Allocate(ref definition.invulnerabilities, numInvulnerabilities);
                    for(int i = 0; i < numInvulnerabilities; i++)
                    {
                        ref var source = ref authoring._invulnerabilities[i];
                        ref var destination = ref invulnerabilitys[i];

                        destination.count = source.count;
                        destination.times = source.times;
                        destination.damage = source.damage;
                        destination.time = source.time;
                    }

                    invulnerability.definition = builder.CreateBlobAssetReference<EffectTargetInvulnerabilityDefinition>(Allocator.Persistent);
                }

                AddBlobAsset(ref invulnerability.definition, out _);

                AddComponent(entity, invulnerability);
                AddComponent<EffectTargetInvulnerabilityStatus>(entity);
            }
        }
    }
    
    [SerializeField]
    internal int _times = 0;

    [SerializeField]
    internal int _hp = 1;

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

    [Tooltip("无敌"), SerializeField]
    internal InvulnerabilityData[] _invulnerabilities;

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
        
        return true;
    }
}
#endif
