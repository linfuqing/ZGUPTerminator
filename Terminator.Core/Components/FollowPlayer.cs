using Unity.Entities;
using Unity.Mathematics;


public struct FollowPlayer : IComponentData
{
    public enum Type
    {
        Local,
        Character, 
    }

    public Type type;
    public FollowTargetSpace space;
    public float3 offset;
}
