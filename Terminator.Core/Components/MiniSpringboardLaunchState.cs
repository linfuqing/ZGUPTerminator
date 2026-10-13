using Unity.Entities;

public struct MiniSpringboardLaunchState : IComponentData
{
    public bool isFromSpringboard;
    public bool hasSetSpeed;
}
