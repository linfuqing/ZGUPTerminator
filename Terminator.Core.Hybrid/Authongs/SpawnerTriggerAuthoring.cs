using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
//[RequireComponent(typeof(SimulationEventAuthoring))]
public class SpawnerTriggerAuthoring : MonoBehaviour
{
    [SerializeField]
    internal LayerMask _layerMask;
    
    [SerializeField]
    internal string[] _tags;
    
    class Baker : Baker<SpawnerTriggerAuthoring>
    {
        public override void Bake(SpawnerTriggerAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.
            
            var entity = GetEntity(TransformUsageFlags.None);

            LayerMaskAndTagsAuthoring layerMaskAndTags;
            layerMaskAndTags.layerMask = authoring._layerMask;
            layerMaskAndTags.tags = authoring._tags;
            
            SpawnerTrigger trigger;
            trigger.layerMaskAndTags = layerMaskAndTags;

            AddComponent(entity, trigger);
            /*AddBuffer<SimulationEvent>(entity);
            SetComponentEnabled<SimulationEvent>(entity, false);*/
        }
    }
}
#endif