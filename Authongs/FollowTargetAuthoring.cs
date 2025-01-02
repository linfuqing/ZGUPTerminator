using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

#if UNITY_EDITOR
public class FollowTargetAuthoring : MonoBehaviour
{
    [Serializable]
    internal struct DistanceData
    {
        [UnityEngine.Serialization.FormerlySerializedAs("distance")]
        public float value;
        
        [UnityEngine.Serialization.FormerlySerializedAs("value")]
        public float speed;
    }

    class Baker : Baker<FollowTargetAuthoring>
    {
        public override void Bake(FollowTargetAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

            AddComponent<FollowTarget>(entity);
            SetComponentEnabled<FollowTarget>(entity, false);

            if (!authoring._isStatic)
            {
                FollowTargetSpeed speed;
                speed.scale = 1.0f;
                AddComponent(entity, speed);
            
                AddComponent<FollowTargetVelocity>(entity);
            }

            if (authoring._isUp)
            {
                FollowTargetUp up;
                up.value = math.up();
                AddComponent(entity, up);
            }

            int numDistances = authoring._distances == null ? 0 : authoring._distances.Length;
            if (numDistances > 0)
            {
                var distances = AddBuffer<FollowTargetDistance>(entity);
                distances.ResizeUninitialized(numDistances);

                for (int i = 0; i < numDistances; ++i)
                {
                    ref var source = ref authoring._distances[i];
                    ref var destination = ref distances.ElementAt(i);
                    destination.value = source.value;
                    destination.speed = source.speed;
                }
                
                distances.AsNativeArray().Sort();
            }
        }
    }

    [SerializeField] 
    internal bool _isStatic;

    [SerializeField]
    internal bool _isUp;

    [SerializeField] 
    [UnityEngine.Serialization.FormerlySerializedAs("_speeds")]
    internal DistanceData[] _distances;
}
#endif

public enum FollowTargetSpace
{
    World,
    Camera, 
}

/*[Flags]
public enum FollowTargetFlag
{
    KeepVelocity = 0x01
}*/

[WriteGroup(typeof(LocalTransform))]
public struct FollowTarget : IComponentData, IEnableableComponent
{
    //public FollowTargetFlag flag;
    public FollowTargetSpace space;
    public float3 offset;
    public Entity entity;
}

public struct FollowTargetSpeed : IComponentData
{
    public float scale;
}

public struct FollowTargetUp : IComponentData
{
    public float3 value;
}

public struct FollowTargetVelocity : IComponentData
{
    public int version;
    
    public float value;

    public float3 direction;

    public float3 target;

    public quaternion lookAt;
    
    //public float4x4 targetTransform;
}

public struct FollowTargetDistance : IBufferElementData, IComparable<FollowTargetDistance>
{
    public float value;
    public float speed;

    public int CompareTo(FollowTargetDistance other)
    {
        int result = value.CompareTo(other.value);
        if (result == 0)
            return speed.CompareTo(other.speed);

        return result;
    }
}