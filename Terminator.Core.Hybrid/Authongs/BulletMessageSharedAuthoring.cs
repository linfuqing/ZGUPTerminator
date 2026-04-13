using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class BulletMessageSharedAuthoring : MonoBehaviour
{
    class Baker : Baker<BulletMessageSharedAuthoring>
    {
        public override void Bake(BulletMessageSharedAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
            BulletMessageShared bulletMessageShared;
            bulletMessageShared.layerMaskAndTags = authoring._layerMaskAndTags;
            AddComponent(entity, bulletMessageShared);
        }
    }
    
    [SerializeField] 
    internal LayerMaskAndTagsAuthoring _layerMaskAndTags;
}
#endif
