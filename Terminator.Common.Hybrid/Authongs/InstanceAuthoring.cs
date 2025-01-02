using System;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

#if UNITY_EDITOR
public class InstanceAuthoring : MonoBehaviour
{
    [Serializable]
    internal struct Prefab
    {
        public string name;

        public GameObject gameObject;
    }
    
    [SerializeField] 
    internal string _nameOverride;

    [SerializeField] 
    internal Prefab[] _prefabs;
    
    class Baker : Baker<InstanceAuthoring>
    {
        public override void Bake(InstanceAuthoring authoring)
        {
            UnityEngine.Assertions.Assert.AreEqual(1, authoring.GetComponents<InstanceAuthoring>().Length, authoring.name);

            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.
            Instance instance;
            instance.name = string.IsNullOrEmpty(authoring._nameOverride) ? authoring.name : authoring._nameOverride;
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, instance);

            int numPrefabs = authoring._prefabs == null ? 0 : authoring._prefabs.Length;
            if (numPrefabs > 0)
            {
                var prefabs = AddBuffer<InstancePrefab>(entity);
                prefabs.ResizeUninitialized(numPrefabs);
                for (int i = 0; i < numPrefabs; ++i)
                {
                    ref var source = ref authoring._prefabs[i];
                    ref var destination = ref prefabs.ElementAt(i);
                    destination.reference = new EntityPrefabReference(source.gameObject);
                }
            }
        }
    }
}
#endif