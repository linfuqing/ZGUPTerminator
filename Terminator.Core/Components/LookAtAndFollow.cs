using Unity.Entities;

public struct LookAtAndFollow : IComponentData
{
    public float minDistance;
    public float maxDistance;
}
