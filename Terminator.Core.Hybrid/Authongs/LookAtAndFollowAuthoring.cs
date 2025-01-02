using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class LookAtAndFollowAuthoring : MonoBehaviour
{
    class Baker : Baker<LookAtAndFollowAuthoring>
    {
        public override void Bake(LookAtAndFollowAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            
            LookAtAndFollow instance;
            instance.minDistance = authoring._minDistance;
            instance.maxDistance = authoring._maxDistance;
            AddComponent(entity, instance);
        }
    }

    [SerializeField] 
    [Tooltip("最小距离，索敌后，大于等于该距离且小于等于最大距离则关闭索敌，可以播放动画")]
    internal float _minDistance = 0.5f;
    
    [SerializeField] 
    [Tooltip("最大距离，索敌后，大于等于最小距离且小于等于该距离则关闭索敌，可以播放动画")]
    internal float _maxDistance = 1f;
}
#endif