using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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
                up.control = authoring._control;
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
    internal FollowTargetControl _control = FollowTargetControl.Pitch;

    [SerializeField] 
    [UnityEngine.Serialization.FormerlySerializedAs("_speeds")]
    internal DistanceData[] _distances;
}
#endif