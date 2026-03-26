using Unity.Collections;
using Unity.Entities;

public struct LevelPickableSkill : IComponentData, IEnableableComponent
{
    public int min;
    public int max;
    public int priorityToStyleIndex;
    public int selection;
}

public struct LevelPickableItem : IComponentData, IEnableableComponent
{
    public FixedString32Bytes name;

    public int min;
    public int max;
}