using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;

public struct Pickable : IComponentData, IEnableableComponent
{
    public float pickedUpTime;
    public float startTime;
    public float speed;

    public uint layerMask;

    public FixedString128Bytes messageName;
    public UnityObjectRef<UnityEngine.Object> messageValue;
    
    public FixedString128Bytes startMessageName;
    public UnityObjectRef<UnityEngine.Object> startMessageValue;
}

public struct PickableStatus : IComponentData, IEnableableComponent
{
    public enum Value
    {
        None, 
        Start, 
        Move,
        Picked
    }

    public Value value;
    public double time;
    public Unity.Physics.ColliderKey colliderKey;
    public Entity entity;
}
