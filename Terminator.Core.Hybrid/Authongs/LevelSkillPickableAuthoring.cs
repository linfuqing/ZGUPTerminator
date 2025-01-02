using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class LevelSkillPickableAuthoring : MonoBehaviour
{
    class Baker : Baker<LevelSkillPickableAuthoring>
    {
        public override void Bake(LevelSkillPickableAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);

            LevelSkillPickable instance;
            instance.min =  Mathf.Max(authoring._min, 1);
            instance.max = Mathf.Max(authoring._max, 1);
            instance.priorityToStyleIndex = authoring._priorityToStyleIndex;
            instance.selection = authoring._selection;
            /*instance.min = 3;
            instance.max = 3;
            instance.prorityToStyleIndex = 2;
            instance.selection = 0;*/
            AddComponent(entity, instance);
        }
    }

    [SerializeField] 
    [Tooltip("最小选卡次数")]
    internal int _min = 1;

    [SerializeField] 
    [Tooltip("最大选卡次数")]
    internal int _max = 1;

    [SerializeField] 
    [Tooltip("优先级到风格的转换")]
    internal int _priorityToStyleIndex = 0;
    
    [SerializeField]
    [Tooltip("拾取后打开哪一个面板")]
    internal int _selection = -1;
}
#endif