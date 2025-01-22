using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public enum FollowTargetSpace
{
    World,
    Camera, 
}

/*[Flags]
public enum FollowTargetFlag
{
    KeepVelocity = 0x01
}*/

[WriteGroup(typeof(LocalTransform))]
public struct FollowTarget : IComponentData, IEnableableComponent
{
    //public FollowTargetFlag flag;
    public FollowTargetSpace space;
    public float3 offset;
    public Entity entity;
}

public struct FollowTargetSpeed : IComponentData
{
    public float scale;
}

public struct FollowTargetUp : IComponentData
{
    public float3 value;
}

public struct FollowTargetVelocity : IComponentData
{
    public int version;
    
    public float value;

    public float3 direction;

    public float3 target;

    public quaternion lookAt;
    
    //public float4x4 targetTransform;
}

public struct FollowTargetDistance : IBufferElementData, IComparable<FollowTargetDistance>
{
    public float value;
    public float speed;

    public int CompareTo(FollowTargetDistance other)
    {
        int result = value.CompareTo(other.value);
        if (result == 0)
            return speed.CompareTo(other.speed);

        return result;
    }
}

public struct FollowTargetParent : IComponentData
{
    public Entity entity;
}

public struct FollowTargetParentMotion : IComponentData
{
    public int version;
    public float4x4 matrix;
}
