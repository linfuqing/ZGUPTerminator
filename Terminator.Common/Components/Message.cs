using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

public interface IMessage
{
    void Clear();

    void Set(int id, int value);
}

public struct Message : IBufferElementData, IEnableableComponent
{
    public int key;
    public FixedString128Bytes name;
    public UnityObjectRef<Object> value;
}

public struct MessageParameter : IBufferElementData
{
    public int messageKey;
    public int value;
    public int id;
}

public struct MessageParent : IComponentData
{
    public Entity entity;
}