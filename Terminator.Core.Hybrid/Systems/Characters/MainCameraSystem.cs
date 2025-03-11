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

        MainCameraScreenToWorld screenToWorld;
        screenToWorld.pixelHeight = 0.0f;
        screenToWorld.aspect = 1.0f;
        screenToWorld.previousViewProjectionMatrix = float4x4.identity;
        EntityManager.CreateSingleton(screenToWorld);
    }

    protected override void OnUpdate()
    {
        if (MainGameObjectCamera.Instance != null)
        {
            var camera = MainGameObjectCamera.Instance;
            MainCameraTransform transform;
            if (SystemAPI.TryGetSingletonEntity<MainEntityCamera>(out Entity mainEntityCameraEntity))
            {
                //Entity mainEntityCameraEntity = SystemAPI.GetSingletonEntity<MainEntityCamera>();
                LocalToWorld targetLocalToWorld = SystemAPI.GetComponent<LocalToWorld>(mainEntityCameraEntity);
                camera.transform.SetPositionAndRotation(targetLocalToWorld.Position,
                    targetLocalToWorld.Rotation);

                transform.value = math.RigidTransform(targetLocalToWorld.Value);
            }
            else
                transform.value = math.RigidTransform(camera.transform.localToWorldMatrix);
            
            SystemAPI.SetSingleton(transform);
            
            MainCameraScreenToWorld screenToWorld;
            screenToWorld.pixelHeight = camera.pixelHeight;
            screenToWorld.aspect = camera.aspect;
            screenToWorld.previousViewProjectionMatrix = camera.previousViewProjectionMatrix.inverse;
            SystemAPI.SetSingleton(screenToWorld);
        }
    }
}