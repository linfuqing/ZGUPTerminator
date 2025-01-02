using Unity.Entities;

public struct DropToDamage : IComponentData, IEnableableComponent
{
    public bool isGrounded;
    public int value;

    public int layerMask;
}