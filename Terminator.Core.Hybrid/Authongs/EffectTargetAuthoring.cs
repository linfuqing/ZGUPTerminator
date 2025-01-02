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

    class Baker : Baker<EffectTargetAuthoring>
    {
        public override void Bake(EffectTargetAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);

            EffectTarget instance;
            instance.times = authoring._times;
            instance.hp = authoring._hp;
            AddComponent(entity, instance);

            EffectTargetLevel level;
            level.value = authoring._level;
            level.exp = authoring._exp;
            level.gold = authoring._gold;
            AddComponent(entity, level);
            
            var messages = AddBuffer<EffectTargetMessage>(entity);
            
            int numMessages = authoring._messages.Length;
            
            messages.Resize(numMessages, NativeArrayOptions.UninitializedMemory);
            
            var prefabLoaders = new Dictionary<GameObject, Entity>();
            RequestEntityPrefabLoaded requestEntityPrefabLoaded;
            for (int i = 0; i < numMessages; ++i)
            {
                ref var source = ref authoring._messages[i];
                ref var destination = ref messages.ElementAt(i);

                destination.layerMask = (uint)source.layerMask.value;
                destination.messageName = source.messageName;
                destination.messageValue = new WeakObjectReference<Object>(source.messageValue);
                
                if (source.receiverPrefab == null)
                    destination.receiverPrefabLoader = Entity.Null;
                else if (!prefabLoaders.TryGetValue(source.receiverPrefab, out destination.receiverPrefabLoader))
                {
                    destination.receiverPrefabLoader = CreateAdditionalEntity(TransformUsageFlags.ManualOverride, false,
                        source.receiverPrefab.name);

                    requestEntityPrefabLoaded.Prefab = new EntityPrefabReference(source.receiverPrefab);

                    AddComponent(destination.receiverPrefabLoader, requestEntityPrefabLoaded);

                    prefabLoaders[source.receiverPrefab] = destination.receiverPrefabLoader;
                }

            }
            
            AddComponent<EffectTargetDamage>(entity);
            SetComponentEnabled<EffectTargetDamage>(entity, false);
            
            /*else
            {
                AddComponent<Message>(entity);
                AddComponent<MessageParameter>(entity);
            }*/
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

    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("attributeParameter")] 
    internal Object _attributeParameter;

    [SerializeField] 
    internal Object _resetMessageValue;
    
    [SerializeField] 
    internal string _resetMessageName;
    
    [SerializeField]
    internal MessageData[] _messages;

    public bool Apply(ref DynamicBuffer<Message> messages, ref DynamicBuffer<MessageParameter> messageParameters)
    {
        if (_attributeParameter == null)
            return false;
        
        Message message;
        message.key = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        message.name = "UpdateAttribute";
        message.value = new WeakObjectReference<Object>(_attributeParameter);
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
