using System;
using Unity.Entities;

[Flags]
public enum LookAtLocation
{
    Ground = 0x01,
    Air = 0x02//, 
    //All = Ground | Air
}

public struct LookAt : IComponentData
{
    public LookAtLocation location;
    public int layerMask;
    
    public float minDistance;
    public float maxDistance;
}

public struct LookAtTarget : IComponentData, IEnableableComponent
{
    public Entity entity;
}