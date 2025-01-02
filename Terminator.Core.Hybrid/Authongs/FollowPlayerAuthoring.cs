using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class FollowPlayerAuthoring : MonoBehaviour
{
    class Baker : Baker<FollowPlayerAuthoring>
    {
        public override void Bake(FollowPlayerAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.
            
            var entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent<FollowPlayer>(entity);
        }
    }
}

#endif