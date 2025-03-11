using Unity.Entities;
using Unity.Mathematics;

public struct MainEntityCamera : IComponentData
{
}

public struct MainCameraTransform : IComponentData
{
    public RigidTransform value;

    public quaternion rotation
    {
        get
        {
            var forward = math.forward(value.rot);
            forward.y = 0.0f;
            forward = math.normalizesafe(forward);
            return quaternion.LookRotationSafe(forward, math.up());
        }
    }
}

public struct MainCameraScreenToWorld : IComponentData
{
    public float pixelHeight;
    public float aspect;
    public float4x4 value;
}