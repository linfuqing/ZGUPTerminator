using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

#if UNITY_EDITOR
public class MessageAuthoring : MonoBehaviour
{
    class Baker : Baker<MessageAuthoring>
    {
        public override void Bake(MessageAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.
            var entity = GetEntity(TransformUsageFlags.None);
            
            var messages = AddBuffer<Message>(entity);
            var messageParameters = AddBuffer<MessageParameter>(entity);

            var effectTargetAuthoring = authoring.GetComponent<EffectTargetAuthoring>();
            if (effectTargetAuthoring != null && effectTargetAuthoring._attributeParameter != null)
            {
                //GetComponent<MessageAuthoring>();
                
                Message message;
                message.key = Random.Range(int.MinValue, int.MaxValue);
                message.name = "UpdateAttribute";
                message.value = new WeakObjectReference<Object>(effectTargetAuthoring._attributeParameter);
                messages.Add(message);

                MessageParameter parameter;
                parameter.messageKey = message.key;
                parameter.id = (int)EffectAttributeID.HPMax;
                parameter.value = effectTargetAuthoring._hp;
                messageParameters.Add(parameter);
                
                SetComponentEnabled<Message>(entity, true);
            }
            else
                SetComponentEnabled<Message>(entity, false);
        }
    }
}
#endif

public interface IMessage
{
    void Clear();

    void Set(int id, int value);
}

public struct Message : IBufferElementData, IEnableableComponent
{
    public int key;
    public FixedString128Bytes name;
    public WeakObjectReference<Object> value;
}

public struct MessageParameter : IBufferElementData
{
    public int messageKey;
    public int value;
    public int id;
}