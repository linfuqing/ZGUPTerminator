using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

public class PhysicsGravityFactorAuthoring : MonoBehaviour
{
    class Baker : Baker<PhysicsGravityFactorAuthoring>
    {
        public override void Bake(PhysicsGravityFactorAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.None);

            PhysicsGravityFactor physicsGravityFactor;
            physicsGravityFactor.Value = Mathf.Abs(authoring._value) > Mathf.Epsilon ? authoring._value : 1.0f;

            var rigidbody = GetComponent<Rigidbody>();
            if(rigidbody == null || !rigidbody.isKinematic && rigidbody.useGravity)
                AddComponent(entity, physicsGravityFactor);
            else
                SetComponent(entity, physicsGravityFactor);
        }
    }

    [SerializeField] 
    internal float _value = 1.0f;
}
