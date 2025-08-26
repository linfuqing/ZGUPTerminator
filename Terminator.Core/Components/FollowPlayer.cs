using Unity.Entities;
using Unity.Mathematics;

public struct FollowPlayer : IComponentData
{
    public FollowTargetSpace space;
    public float3 offset;
}
