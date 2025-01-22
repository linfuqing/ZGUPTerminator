using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;

public struct Pickable : IComponentData, IEnableableComponent
{
    public float pickedUpTime;
    public float startTime;
    public float speed;

    public FixedString128Bytes messageName;
    public WeakObjectReference<UnityEngine.Object> messageValue;
}

public struct PickableStatus : IComponentData, IEnableableComponent
{
    public enum Value
    {
        None, 
        Move,
        Picked
    }

    public Value value;
    public Entity entity;
    public double time;
}
