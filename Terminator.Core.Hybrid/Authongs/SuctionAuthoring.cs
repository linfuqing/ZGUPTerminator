using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
public class SuctionAuthoring : MonoBehaviour
{
    class Baker : Baker<SuctionAuthoring>
    {
        public override void Bake(SuctionAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);

            //AddComponent<SimulationEvent>(entity);

            Suction suction;
            suction.minDistance = authoring._minDistance;
            suction.maxDistance = authoring._maxDistance;
            suction.linearSpeed = authoring._linearSpeed;
            suction.angularSpeed = authoring._angularSpeed;
            suction.tangentSpeed = authoring._tangentSpeed;
            suction.center = authoring._center;
            AddComponent(entity, suction);
        }
    }
    
    [SerializeField]
    internal float _minDistance = 0.5f;
    
    [SerializeField]
    internal float _maxDistance = 3.0f;

    [SerializeField]
    internal float _linearSpeed = float.MaxValue;
    
    [SerializeField]
    internal float _angularSpeed = float.MaxValue;
    
    [SerializeField]
    internal float3 _tangentSpeed;
    
    [SerializeField]
    internal float3 _center;
}
#endif