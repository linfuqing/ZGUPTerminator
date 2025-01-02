using System;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

#if UNITY_EDITOR
public class SimulationEventAuthoring : MonoBehaviour
{

    class Baker : Baker<SimulationEventAuthoring>
    {
        public override void Bake(SimulationEventAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            
            AddBuffer<SimulationEvent>(entity);
            SetComponentEnabled<SimulationEvent>(entity, false);
        }
    }
}
#endif

public struct SimulationEvent : IBufferElementData, IEnableableComponent, IEquatable<SimulationEvent>
{
    public Entity entity;
    public ColliderKey colliderKey;

    public static bool Append(ref DynamicBuffer<SimulationEvent> values, in SimulationEvent value)
    {
        foreach (var temp in values)
        {
            if (temp.entity == value.entity)
                return false;
        }

        values.Add(value);

        return true;
    }
    
    public static bool AppendOrReplace(ref DynamicBuffer<SimulationEvent> values, in SimulationEvent value)
    {
        int numValues = values.Length;
        for(int i = 0; i < numValues; ++i)
        {
            ref var temp = ref values.ElementAt(i);
            
            if (temp.entity == value.entity)
            {
                temp.colliderKey = value.colliderKey;
                
                return false;
            }
        }

        values.Add(value);

        return true;
    }

    public bool Equals(SimulationEvent other)
    {
        return entity == other.entity && colliderKey == other.colliderKey;
    }

    public override int GetHashCode()
    {
        return entity.GetHashCode() ^ colliderKey.GetHashCode();
    }
}