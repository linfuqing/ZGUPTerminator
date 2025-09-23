using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class LevelPickableItemAuthoring : MonoBehaviour
{
    class Baker : Baker<LevelPickableItemAuthoring>
    {
        public override void Bake(LevelPickableItemAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);

            LevelPickableItem instance;
            instance.name = string.IsNullOrEmpty(authoring._nameOverride) ? authoring.name : authoring._nameOverride;
            instance.min =  Mathf.Max(authoring._min, 1);
            instance.max = Mathf.Max(authoring._max, 1);
            AddComponent(entity, instance);
        }
    }

    [SerializeField] 
    [Tooltip("最小個数")]
    internal int _min = 1;

    [SerializeField] 
    [Tooltip("最大個数")]
    internal int _max = 1;

    [SerializeField] 
    internal string _nameOverride;
}
#endif