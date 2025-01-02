using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
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
        }
    }
}
#endif

public struct SuctionTargetVelocity : IComponentData, IEnableableComponent
{
    public float3 linear;
    public float3 angular;
    public float3 tangent;
}
