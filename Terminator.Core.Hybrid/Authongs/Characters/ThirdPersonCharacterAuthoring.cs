using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics.Authoring;
using UnityEngine;
using Unity.CharacterController;
using Unity.Physics;
using UnityEngine.Serialization;

#if UNITY_EDITOR
[DisallowMultipleComponent]
public class ThirdPersonCharacterAuthoring : MonoBehaviour
{
    public AuthoringKinematicCharacterProperties CharacterProperties = AuthoringKinematicCharacterProperties.GetDefault();
    public ThirdPersonCharacterComponent Character = ThirdPersonCharacterComponent.GetDefault();

    public class Baker : Baker<ThirdPersonCharacterAuthoring>
    {
        public override void Bake(ThirdPersonCharacterAuthoring authoring)
        {
            KinematicCharacterUtilities.BakeCharacter(this, authoring, authoring.CharacterProperties);

            Entity selfEntity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(selfEntity, authoring.Character);
            ThirdPersionCharacterGravityFactor gravityFactor;
            gravityFactor.value = 1.0f;
            AddComponent(selfEntity, gravityFactor);
            AddComponent<ThirdPersonCharacterLookAt>(selfEntity);
            AddComponent<ThirdPersonCharacterControl>(selfEntity);
        }
    }

}
#endif