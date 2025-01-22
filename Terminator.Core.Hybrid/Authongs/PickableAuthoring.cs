using Unity.Entities;
using Unity.Entities.Content;
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
            pickable.speed = authoring._speed;
            pickable.startTime = authoring._startTime;
            pickable.messageName = authoring._messageName;
            pickable.messageValue = authoring._messageValue == null ? default : new WeakObjectReference<Object>(authoring._messageValue);
            AddComponent(entity, pickable);
            
            AddComponent<PickableStatus>(entity);
            //SetComponentEnabled<PickableStatus>(entity, false);
        }
    }
    
    [SerializeField] 
    internal float _speed;
    
    [SerializeField] 
    internal float _startTime;
    
    [SerializeField] 
    internal string _messageName;
    
    [SerializeField] 
    internal Object _messageValue;
}
#endif