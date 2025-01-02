using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
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

public struct Suction : IComponentData
{
    //public float maxTime;
    public float minDistance;
    public float maxDistance;
    public float linearSpeed;
    public float angularSpeed;
    public float3 tangentSpeed;
    public float3 center;

    /*public float acceleration => maxTime > math.FLT_MIN_NORMAL ? 2.0f * maxDistance / maxTime : 0.0f;

    public float3 GetVelocity(
        in float3 center,
        in float3 position,
        float deltaTime)
    {
        float3 distance = center - position;
        float lengthSQ = math.lengthsq(distance);
        if (lengthSQ > maxDistance * maxDistance)
            return float3.zero;

        if (lengthSQ > math.FLT_MIN_NORMAL)
        {
            float acceleration = this.acceleration;
            if (acceleration > math.FLT_MIN_NORMAL)
            {
                float lengthR = math.rsqrt(lengthSQ), velocity = math.sqrt( 2.0f * lengthR)  / acceleration;

                velocity += deltaTime * acceleration * 0.5f;

                return velocity * distance;
            }

            if(deltaTime > math.FLT_MIN_NORMAL)
                return distance / deltaTime;
        }

        return float3.zero;
    }*/
}