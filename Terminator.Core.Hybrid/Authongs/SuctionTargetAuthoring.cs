using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class SuctionTargetAuthoring : MonoBehaviour
{
    class Baker : Baker<SuctionTargetAuthoring>
    {
        public override void Bake(SuctionTargetAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent<SuctionTargetVelocity>(entity);
            SetComponentEnabled<SuctionTargetVelocity>(entity, false);
        }
    }
}
#endif