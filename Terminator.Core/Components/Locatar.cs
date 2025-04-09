using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using UnityEngine;

public enum LocatorDirection
{
    Forward, 
    Backward
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
        
        public int messageIndex;

        public float time;
        
        public float startTime;
        
        public BlobArray<int> areaIndices;
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
    
    public int messageIndex;
    
    public double time;
    
    public float3 value;
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
    public FixedString128Bytes name;

    public UnityObjectRef<Object> value;
}
