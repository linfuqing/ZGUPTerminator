using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

public struct Antigravity : IComponentData
{
    public FixedString128Bytes startMessageName;
    public WeakObjectReference<Object> startMessageValue;

    public FixedString128Bytes endMessageName;
    public WeakObjectReference<Object> endMessageValue;

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