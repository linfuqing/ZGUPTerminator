using Unity.Entities;
using UnityEngine;

public struct LevelSkillPickable : IComponentData
{
    public int min;
    public int max;
    public int priorityToStyleIndex;
    public int selection;
}