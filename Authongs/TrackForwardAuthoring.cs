using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class TrackForwardAuthoring : MonoBehaviour
{
    class Baker : Baker<TrackForwardAuthoring>
    {
        public override void Bake(TrackForwardAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent<TrackForward>(entity);
        }
    }
}
#endif

public struct TrackForward : IComponentData
{
    
}