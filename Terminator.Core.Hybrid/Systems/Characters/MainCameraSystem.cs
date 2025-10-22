using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

[UpdateInGroup(typeof(PresentationSystemGroup))]
//[UpdateAfter(typeof(TransformSystemGroup))]
public partial class MainCameraSystem : SystemBase
{
    private Entity __entity;
    
    protected override void OnCreate()
    {
        base.OnCreate();

        var entItyManager = EntityManager;

        MainCameraTransform transform;
        transform.value = RigidTransform.identity;
        __entity = entItyManager.CreateSingleton(transform);

        entItyManager.AddComponent<RenderFrustumPlanes>(__entity);

        //ZG.RenderFrustumPlanes
        /*MainCameraScreenToWorld screenToWorld;
        screenToWorld.pixelHeight = 0.0f;
        screenToWorld.aspect = 1.0f;
        screenToWorld.previousViewProjectionMatrix = float4x4.identity;
        EntityManager.CreateSingleton(screenToWorld);*/
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
            
            SystemAPI.SetComponent(__entity, transform);
            SystemAPI.SetComponent(__entity, new RenderFrustumPlanes(camera));
            
            /*MainCameraScreenToWorld screenToWorld;
            screenToWorld.pixelHeight = camera.pixelHeight;
            screenToWorld.aspect = camera.aspect;
            screenToWorld.previousViewProjectionMatrix = camera.previousViewProjectionMatrix.inverse;
            SystemAPI.SetSingleton(screenToWorld);*/
        }
    }
}