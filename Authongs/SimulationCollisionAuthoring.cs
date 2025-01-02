using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

#if UNITY_EDITOR
public class SimulationCollisionAuthoring : MonoBehaviour
{
    class Baker : Baker<SimulationCollisionAuthoring>
    {
        public override void Bake(SimulationCollisionAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            
            AddComponent<SimulationCollision>(entity);
            SetComponentEnabled<SimulationCollision>(entity, false);
        }
    }
}
#endif

public struct SimulationCollision : IComponentData, IEnableableComponent
{
    public float3 position;
    public ColliderCastHit closestHit;
}