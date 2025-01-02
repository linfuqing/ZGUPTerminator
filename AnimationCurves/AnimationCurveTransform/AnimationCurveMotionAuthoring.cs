using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

#if UNITY_EDITOR
public class AnimationCurveMotionAuthoring : MonoBehaviour
{
    class Baker : Baker<AnimationCurveMotionAuthoring>
    {
        public override void Bake(AnimationCurveMotionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AnimationCurveMotion motion;
            motion.localTransform = LocalTransform.Identity;
            AddComponent(entity, motion);
        }
    }
}
#endif

public struct AnimationCurveMotion : IComponentData
{
    public LocalTransform localTransform;
}
