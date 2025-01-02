using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

#if UNITY_EDITOR
public interface IMessageOverride
{
    bool Apply(ref DynamicBuffer<Message> messages, ref DynamicBuffer<MessageParameter> messageParameters);
}

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

            var overrides = authoring.GetComponents<IMessageOverride>();
            if (overrides != null)
            {
                bool result = false;
                foreach (var @override in overrides)
                {
                    //GetComponent<MessageAuthoring>();
                
                    result = @override.Apply(ref messages, ref messageParameters) || result;
                }
                
                SetComponentEnabled<Message>(entity, result);
            }
            else
                SetComponentEnabled<Message>(entity, false);
        }
    }
}
#endif