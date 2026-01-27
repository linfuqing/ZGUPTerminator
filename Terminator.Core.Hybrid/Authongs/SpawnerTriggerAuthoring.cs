using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
//[RequireComponent(typeof(SimulationEventAuthoring))]
public class SpawnerTriggerAuthoring : MonoBehaviour
{
    [System.Serializable]
    internal struct SpawnerTriggerData
    {
        public LayerMask belongs;

        public LayerMaskAndTagsAuthoring layerMaskAndTags;
    }
    
    class Baker : Baker<SpawnerTriggerAuthoring>
    {
        public override void Bake(SpawnerTriggerAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.
            
            var entity = GetEntity(TransformUsageFlags.None);

            var triggers = AddBuffer<SpawnerTrigger>(entity);
            
            LayerMaskAndTagsAuthoring layerMaskAndTags;
            int numTriggers = authoring._triggers == null ? 0 : authoring._triggers.Length;
            if (numTriggers > 0)
            {
                triggers.ResizeUninitialized(numTriggers);
                for (int i = 0; i < numTriggers; ++i)
                {
                    ref var source = ref authoring._triggers[i];
                    ref var destination = ref triggers.ElementAt(i);

                    destination.belongs = (uint)source.belongs.value;
                    destination.layerMaskAndTags = source.layerMaskAndTags;
                }
            }
            else
            {
                triggers.ResizeUninitialized(1);
                ref var trigger = ref triggers.ElementAt(0);
                layerMaskAndTags.layerMask = authoring._layerMask;
                layerMaskAndTags.tags = authoring._tags;

                trigger.layerMaskAndTags = layerMaskAndTags;
                trigger.belongs = 0;
            }
        }
    }
    
    [SerializeField]
    internal LayerMask _layerMask;
    
    [SerializeField]
    internal string[] _tags;

    [SerializeField, Tooltip("填了这个默认标签将不再生效")]
    internal SpawnerTriggerData[] _triggers;
}
#endif