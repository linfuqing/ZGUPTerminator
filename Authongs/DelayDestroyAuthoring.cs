using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
public class DelayDestroyAuthoring : MonoBehaviour
{
    class Baker : Baker<DelayDestroyAuthoring>
    {
        public override void Bake(DelayDestroyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            DelayDestroy delayDestroy;
            delayDestroy.time = authoring._time;
            AddComponent(entity, delayDestroy);
        }
    }

    [SerializeField]
    internal float _time;
}
#endif

public struct DelayDestroy : IComponentData
{
    public float time;
}