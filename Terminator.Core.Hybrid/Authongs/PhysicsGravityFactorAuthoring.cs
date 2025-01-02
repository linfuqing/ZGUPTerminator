using Unity.Entities;
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
            physicsGravityFactor.Value = 1.0f;
            AddComponent(entity, physicsGravityFactor);
        }
    }
}
