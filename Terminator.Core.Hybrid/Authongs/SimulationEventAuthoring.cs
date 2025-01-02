using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class SimulationEventAuthoring : MonoBehaviour
{

    class Baker : Baker<SimulationEventAuthoring>
    {
        public override void Bake(SimulationEventAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            
            AddBuffer<SimulationEvent>(entity);
            SetComponentEnabled<SimulationEvent>(entity, false);
        }
    }
}
#endif