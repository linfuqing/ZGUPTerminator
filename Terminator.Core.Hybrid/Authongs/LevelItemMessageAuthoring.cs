using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using ZG;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
public class LevelItemMessageAuthoring : MonoBehaviour, IMessageOverride
{
    [Serializable]
    internal struct MessageData
    {
        [Tooltip("物品名")]
        public string itemName;
        [Tooltip("填UpdateAttribute")]
        public string messageName;
        [Tooltip("引用Parameters")]
        public Object messageValue;

        [Tooltip("物品ID， 对应AttributeEventReceiver.Attributes")]
        public int id;
        [Tooltip("物品最大值ID， 对应AttributeEventReceiver.Attributes")]
        public int idMax;
        [Tooltip("物品最大值")]
        public int max;
    }
    
    class Baker : Baker<LevelItemMessageAuthoring>
    {
        public override void Bake(LevelItemMessageAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);

            var messages = AddBuffer<LevelItemMessage>(entity);
            
            int numMessages = authoring._messages.Length;
            
            messages.Resize(numMessages, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < numMessages; ++i)
            {
                ref var source = ref authoring._messages[i];
                ref var destination = ref messages.ElementAt(i);
                destination.itemName =  source.itemName;
                destination.messageName = source.messageName;
                destination.messageValue = source.messageValue;
                destination.id = source.id;
            }
        }
    }
    
    [SerializeField]
    internal MessageData[] _messages;
    
    public bool Apply(ref DynamicBuffer<Message> messages, ref DynamicBuffer<MessageParameter> messageParameters)
    {
        MessageParameter messageParameter;
        Message destination;
        int numMessages = _messages.Length;
        for (int i = 0; i < numMessages; ++i)
        {
            ref var source = ref _messages[i];
            destination.key =  UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            destination.name = source.messageName;
            destination.value = source.messageValue;

            messages.Add(destination);

            messageParameter.messageKey = destination.key;
            messageParameter.id = source.id;
            messageParameter.value = 0;
            messageParameters.Add(messageParameter);
            
            messageParameter.messageKey = destination.key;
            messageParameter.id = source.idMax;
            messageParameter.value = source.max;
            messageParameters.Add(messageParameter);
        }

        return true;
    }
}
#endif