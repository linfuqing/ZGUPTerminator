using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
public class BezierAuthoring : MonoBehaviour
{
    class Baker : Baker<BezierAuthoring>
    {
        public override void Bake(BezierAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

            var controlPoints = AddBuffer<BezierControlPoint>(entity);

            int numControlPoints = authoring._controlPoints == null ? 0 : authoring._controlPoints.Length;
            controlPoints.ResizeUninitialized(numControlPoints);

            for (int i = 0; i < numControlPoints; ++i)
                controlPoints.ElementAt(i).value = authoring._controlPoints[i];

            BezierDistance distance;
            distance.motion = RigidTransform.identity;
            distance.value = 0.0f;
            AddComponent(entity, distance);

            BezierSpeed speed;
            speed.value = authoring._speed;
            AddComponent(entity, speed);
        }
    }

    [Header("GUI")]
    [SerializeField] 
    internal float _step = 0.02f;
    
    [Header("Data")]
    [SerializeField] 
    internal float _speed = 1.0f;

    [SerializeField] 
    internal Vector3 _targetPosition = new Vector3(0, 0, 5);

    [SerializeField] 
    internal Vector3[] _controlPoints;
    
    void OnDrawGizmosSelected()
    {
        int numControlPoints = _controlPoints == null ? 0 : _controlPoints.Length;
        if (numControlPoints < 1)
            return;
        
        var controlPoints = new NativeArray<float3>(numControlPoints, Allocator.Temp);
        {
            for (int i = 0; i < numControlPoints; ++i)
            {
                ref var controlPoint = ref _controlPoints[i];
                
                Gizmos.DrawSphere(controlPoint, _step);
                
                controlPoints[i] = controlPoint;
            }

            float3 oldPosition = BezierUtility.Calculate(0, Vector3.zero, _targetPosition, controlPoints), position;
            float t;
            int numPoints = Mathf.CeilToInt(1.0f / _step);
            for (int i = 1; i < numPoints; ++i)
            {
                t = Mathf.Min(1.0f, i * _step);
                position = BezierUtility.Calculate(t, Vector3.zero, _targetPosition, controlPoints);
                
                Gizmos.DrawLine(oldPosition, position);

                oldPosition = position;
            }
        }

        controlPoints.Dispose();
    }
}
#endif