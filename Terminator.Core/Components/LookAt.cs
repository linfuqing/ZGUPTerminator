using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[Flags]
public enum LookAtLocation
{
    Ground = 0x01,
    Air = 0x02, 
    Camera = 0x04
}

public struct LookAt : IComponentData
{
    public LookAtLocation location;
    public int layerMask;

    public float minDot;
    
    public float minDistance;
    public float maxDistance;

    public float speed;
}

[WriteGroup(typeof(LocalToWorld))]
public struct LookAtOrigin : IComponentData
{
    public RigidTransform transform;
}

public struct LookAtTarget : IComponentData, IEnableableComponent
{
    public double time;
    public quaternion origin;
    public Entity entity;
}