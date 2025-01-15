using UnityEngine;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Scenes;

#if UNITY_EDITOR
public class LevelObjectAuthoring : MonoBehaviour
{
    public class Baker : Baker<LevelObjectAuthoring>
    {
        public override void Bake(LevelObjectAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent<LevelObject>(entity);
        }
    }

}
#endif