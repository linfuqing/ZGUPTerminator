using Unity.Entities;

public struct DelayDestroy : IComponentData, IEnableableComponent
{
    public double startTime;
    public float time;
}
