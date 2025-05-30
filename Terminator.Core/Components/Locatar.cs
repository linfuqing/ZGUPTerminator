using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using Object = UnityEngine.Object;

public enum LocatorDirection
{
    Forward, 
    Backward, 
    DontCare
}

[Flags]
public enum LocatorMessageType
{
    Start = 0x01, 
    End = 0x02, 
    Bold = Start | End, 
}

public struct LocatorDefinition
{
    public struct Area
    {
        public AABB aabb;
    }

    public struct Action
    {
        public LocatorDirection direction;
        
        public float time;
        
        public float startTime;

        public float3 up;
        
        public BlobArray<int> areaIndices;
        public BlobArray<int> messageIndices;
    }

    public float cooldown;

    public BlobArray<Area> areas;
    
    public BlobArray<Action> actions;
}

public struct LocatorDefinitionData : IComponentData
{
    public BlobAssetReference<LocatorDefinition> definition;
}

public struct LocatorSpeed : IComponentData
{
    public float value;
}

public struct LocatorVelocity : IComponentData, IEnableableComponent
{
    public LocatorDirection direction;
    
    public int actionIndex;
    
    public double time;
    
    public float3 value;

    public float3 up;
}

public struct LocatorTime : IComponentData, IEnableableComponent
{
    public double value;
}

public struct LocatorStatus : IComponentData
{
    public int actionIndex;

    public double time;
}

public struct LocatorMessage : IBufferElementData
{
    public LocatorMessageType type;
    
    public FixedString128Bytes name;

    public UnityObjectRef<Object> value;
}
