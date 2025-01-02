using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
//[RequireComponent(typeof(SimulationEventAuthoring))]
public class SpawnerTriggerAuthoring : MonoBehaviour
{
    [SerializeField]
    internal LayerMask _layerMask;
    
    class Baker : Baker<SpawnerTriggerAuthoring>
    {
        public override void Bake(SpawnerTriggerAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.
            
            var entity = GetEntity(TransformUsageFlags.None);
            
            SpawnerTrigger trigger;
            trigger.layerMask = authoring._layerMask.value;

            AddComponent(entity, trigger);
            /*AddBuffer<SimulationEvent>(entity);
            SetComponentEnabled<SimulationEvent>(entity, false);*/
        }
    }
}
#endif

public struct SpawnerTrigger : IComponentData
{
    public int layerMask;
}