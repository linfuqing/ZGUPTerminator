using Unity.Entities;
using Unity.Transforms;

public struct LocalTransformToParent : IComponentData
{
    public float horizontal;
}

public struct LocalTransformToParentStatus : IComponentData, IEnableableComponent
{
    public LocalTransform motion;
}
