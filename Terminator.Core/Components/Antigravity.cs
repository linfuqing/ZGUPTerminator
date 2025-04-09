using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

public struct Antigravity : IComponentData
{
    public FixedString128Bytes startMessageName;
    public UnityObjectRef<Object> startMessageValue;

    public FixedString128Bytes endMessageName;
    public UnityObjectRef<Object> endMessageValue;

    public float cooldown;
    public float duration;
}

public struct AntigravityStatus : IComponentData
{
    public enum Value
    {
        Disable,
        Enable, 
        Cooldown, 
        FallDown
    }

    public Value value;

    public double time;
}
