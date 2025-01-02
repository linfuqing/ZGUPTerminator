using UnityEngine;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Scenes;

#if UNITY_EDITOR
public class LevelPlayerAuthoring : MonoBehaviour
{
    public class Baker : Baker<LevelPlayerAuthoring>
    {
        public override void Bake(LevelPlayerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent<LevelPlayer>(entity);

            RequestEntityPrefabLoaded requestEntityPrefabLoaded;
            requestEntityPrefabLoaded.Prefab = new EntityPrefabReference(authoring.prefab);
        
            AddComponent(entity, requestEntityPrefabLoaded);
        }
    }

    [SerializeField]
    internal GameObject prefab;
}
#endif