using Unity.Entities;

public struct MiniSpringboardLaunchState : IComponentData
{
    public bool isFromSpringboard;
    public bool hasSetSpeed;
}
public struct MiniCheckPlayerStateInFlyType : IComponentData
{
    public bool InFlyType;
}
public struct MiniChangeTypeNoCheck : IComponentData
{
    
}
