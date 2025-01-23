using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class MessageParentAuthoring : MonoBehaviour
{
    class Baker : Baker<MessageParentAuthoring>
    {
        public override void Bake(MessageParentAuthoring authoring)
        {
            Transform transform = authoring.transform.parent;
            while (transform != null)
            {
                if (GetComponent<MessageAuthoring>(transform) != null)
                {
                    // Create an EntityPrefabReference from a GameObject.
                    // By using a reference, we only need one baked prefab entity instead of
                    // duplicating the prefab entity everywhere it is used.
                    var entity = GetEntity(TransformUsageFlags.None);

                    MessageParent parent;
                    parent.entity = GetEntity(transform, TransformUsageFlags.None);
                    
                    AddComponent(entity, parent);
                    break;
                }

                transform = transform.parent;
            }
            
            
        }
    }
}
#endif