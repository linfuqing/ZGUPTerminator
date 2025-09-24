using Unity.Entities;
using Unity.Entities.Content;
using Unity.Physics;
using UnityEngine;

#if UNITY_EDITOR
public class PickableAuthoring : MonoBehaviour
{
    class Baker : Baker<PickableAuthoring>
    {
        public override void Bake(PickableAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);

            //AddComponent<SimulationEvent>(entity);

            Pickable pickable;
            pickable.pickedUpTime = authoring._pickedUpTime;
            pickable.startTime = authoring._startTime;
            pickable.speed = authoring._speed;
            pickable.layerMask = (uint)(int)authoring._layerMask;
            pickable.messageName = authoring._messageName;
            pickable.messageValue = authoring._messageValue;
            pickable.startMessageName = authoring._startMessageName;
            pickable.startMessageValue = authoring._startMessageValue;
            AddComponent(entity, pickable);
            
            AddComponent<PickableStatus>(entity);
            //SetComponentEnabled<PickableStatus>(entity, false);

            if (GetComponent<Rigidbody>() == null)
            {
                AddComponent<PhysicsVelocity>(entity);

                AddComponent<Unity.Physics.GraphicsIntegration.PhysicsGraphicalSmoothing>(entity);
                AddComponent<Unity.Physics.GraphicsIntegration.PhysicsGraphicalInterpolationBuffer>(entity);
            }
        }
    }
    
    [SerializeField] 
    internal float _pickedUpTime;
    
    [SerializeField] 
    internal float _startTime;
    
    [SerializeField] 
    internal float _speed;
    
    [SerializeField] 
    internal LayerMask _layerMask;

    [SerializeField] 
    internal string _messageName;
    
    [SerializeField] 
    internal Object _messageValue;
    
    [SerializeField] 
    internal string _startMessageName;
    
    [SerializeField] 
    internal Object _startMessageValue;

}
#endif