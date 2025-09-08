using Unity.Entities;
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
            instance.valueImmunized = authoring._valueImmunized;
            instance.layerMask = authoring._layerMask.value;
            instance.messageLayerMask = authoring._messageLayerMask.value;
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
    internal int _valueImmunized;
    
    [SerializeField]
    internal LayerMask _layerMask;

    [SerializeField]
    internal LayerMask _messageLayerMask;

    //[SerializeField] 
    //internal float _range;
}
#endif