using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
public class FollowTargetParentAuthoring : MonoBehaviour
{
    [SerializeField]
    internal GameObject _target;

    class Baker : Baker<FollowTargetParentAuthoring>
    {
        public override void Bake(FollowTargetParentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.WorldSpace);
            //var transform = authoring.transform;

            FollowTargetParent parent;
            //parent.localToParent = float4x4.TRS(transform.localPosition, transform.rotation, transform.localScale);
            parent.entity = GetEntity(authoring._target == null ? GetParent() : authoring._target, TransformUsageFlags.Dynamic);

            AddComponent(entity, parent);

            FollowTargetParentMotion motion;
            motion.version = -1;
            motion.matrix = float4x4.identity;
            AddComponent(entity, motion);
        }
    }
}
#endif