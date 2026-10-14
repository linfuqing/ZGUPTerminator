using Unity.Entities;

/// <summary>蓝条是否达到起飞阈值；地面、死亡等角色条件由起飞逻辑另行判断。</summary>
public struct Mini_FlightTakeoffComponent : IComponentData
{
    public bool canTakeoff;

    public static bool IsEnergyReady(float value, float maxValue, float thresholdPercent)
    {
        return maxValue > 0.0f && value >= maxValue * thresholdPercent;
    }
}
