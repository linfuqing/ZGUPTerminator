using Unity.Entities;

public struct DelayDestroy : IComponentData
{
    public double startTime;
    public float time;

    public float GetTime(double now)
    {
        now -= startTime;
        if (now > time)
            return 0.0f;

        return time - (float)now;
    }
}
