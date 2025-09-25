using System;
using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class LookAtAuthoring : MonoBehaviour
{
    class Baker : Baker<LookAtAuthoring>
    {
        public override void Bake(LookAtAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            
            LookAt instance;
            instance.location = authoring._location;
            instance.layerMask = authoring._layerMask.value;
            instance.minDot = Mathf.Cos(authoring._maxAngle * Mathf.Deg2Rad);
            instance.minDistance = authoring._minDistance;
            instance.minDistance = authoring._minDistance;
            instance.maxDistance = authoring._maxDistance;
            instance.speed = authoring._speed;
            AddComponent(entity, instance);

            AddComponent<LookAtTarget>(entity);

            //SetComponentEnabled<LookAtTarget>(entity, false);
        }
    }

    [SerializeField] 
    internal LookAtLocation _location;
    [SerializeField]
    internal LayerMask _layerMask;
    [SerializeField] 
    internal float _maxAngle = 60.0f;
    [SerializeField] 
    internal float _minDistance;
    [SerializeField] 
    internal float _maxDistance;
    [SerializeField] 
    internal float _speed;
}
#endif