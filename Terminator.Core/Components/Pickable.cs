using Unity.Entities;

public struct Pickable : IComponentData, IEnableableComponent
{
    public float speed;
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
}
