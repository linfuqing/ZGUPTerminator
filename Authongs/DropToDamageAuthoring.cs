using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

#if UNITY_EDITOR
public class DropToDamageAuthoring : MonoBehaviour
{
    class Baker : Baker<DropToDamageAuthoring>
    {
        public override void Bake(DropToDamageAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            DropToDamage instance;
            instance.isGrounded = true;
            instance.value = authoring._value;
            instance.layerMask = authoring._layerMask.value;
            AddComponent(entity, instance);
            
            SetComponentEnabled<DropToDamage>(entity, false);

            /*DropToDamageRange range;
            range.distance = authoring._range;
            AddComponent(entity, range);*/
        }
    }
    [SerializeField]
    internal int _value;

    [SerializeField]
    internal LayerMask _layerMask;

    //[SerializeField] 
    //internal float _range;
}
#endif

public struct DropToDamage : IComponentData, IEnableableComponent
{
    public bool isGrounded;
    public int value;

    public int layerMask;
}

/*public struct DropToDamageRange : IComponentData
{
    public float distance;
}*/