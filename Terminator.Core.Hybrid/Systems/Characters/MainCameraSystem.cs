using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(PresentationSystemGroup))]
//[UpdateAfter(typeof(TransformSystemGroup))]
public partial class MainCameraSystem : SystemBase
{
    protected override void OnCreate()
    {
        base.OnCreate();

        MainCameraTransform transform;
        transform.value = RigidTransform.identity;
        EntityManager.CreateSingleton(transform);
    }

    protected override void OnUpdate()
    {
        if (MainGameObjectCamera.Instance != null)
        {
            MainCameraTransform transform;
            if (SystemAPI.TryGetSingletonEntity<MainEntityCamera>(out Entity mainEntityCameraEntity))
            {
                //Entity mainEntityCameraEntity = SystemAPI.GetSingletonEntity<MainEntityCamera>();
                LocalToWorld targetLocalToWorld = SystemAPI.GetComponent<LocalToWorld>(mainEntityCameraEntity);
                MainGameObjectCamera.Instance.transform.SetPositionAndRotation(targetLocalToWorld.Position,
                    targetLocalToWorld.Rotation);

                transform.value = math.RigidTransform(targetLocalToWorld.Value);
            }
            else
                transform.value = math.RigidTransform(MainGameObjectCamera.Instance.transform.localToWorldMatrix);
            
            SystemAPI.SetSingleton(transform);
        }
    }
}