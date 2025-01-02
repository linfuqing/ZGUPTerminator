using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

#if UNITY_EDITOR
public class LocalTransformToParentAuthoring : MonoBehaviour
{
    class Baker : Baker<LocalTransformToParentAuthoring>
    {
        public override void Bake(LocalTransformToParentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            LocalTransformToParent instance;
            instance.horizontal = authoring._horizontal;
            AddComponent(entity, instance);

            LocalTransformToParentStatus status;
            status.motion = LocalTransform.Identity;
            AddComponent(entity, status);
        }
    }

    [SerializeField]
    internal float _horizontal = 0.1f;
}
#endif