using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;

public struct Instance : IComponentData, IEnableableComponent
{
    public FixedString128Bytes name;
}

public struct InstanceEntity : ICleanupComponentData
{
    
}

public struct InstancePrefab : IBufferElementData, IEnableableComponent
{
    public EntityPrefabReference reference;
}
