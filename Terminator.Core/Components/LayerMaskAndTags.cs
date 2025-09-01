using System;
using Unity.Collections;
using UnityEngine;

public struct LayerMaskAndTags
{
    public int layerMask;
    public FixedList512Bytes<FixedString32Bytes> tags;

    public static readonly LayerMaskAndTags AllLayers = new ()
    {
        layerMask = -1,
        tags = default
    };
    
    public bool isEmpty => layerMask == 0 && tags.IsEmpty;
    
    public bool BelongsTo(in LayerMaskAndTags layerMaskAndTags)
    {
        if (layerMask != 0 && (layerMask & layerMaskAndTags.layerMask) == 0)
            return false;

        if (!tags.IsEmpty)
        {
            bool isContains;
            foreach (var destination in tags)
            {
                isContains = false;
                foreach (var source in layerMaskAndTags.tags)
                {
                    if (source == destination)
                    {
                        isContains = true;
                        
                        break;
                    }
                }

                if (!isContains)
                    return false;
            }
        }

        return true;
    }

    public static LayerMaskAndTags operator |(in LayerMaskAndTags x, in LayerMaskAndTags y)
    {
        LayerMaskAndTags result;
        result.layerMask = x.layerMask | y.layerMask;
        result.tags = default;
        
        foreach (var tag in x.tags)
            result.tags.Add(tag);
        
        foreach (var tag in y.tags)
            result.tags.Add(tag);

        return result;
    }
}


[Serializable]
public struct LayerMaskAndTagsAuthoring : IEquatable<LayerMaskAndTagsAuthoring>
{
    [UnityEngine.Serialization.FormerlySerializedAs("value")]
    public LayerMask layerMask;

    public string[] tags;

    public bool Equals(LayerMaskAndTagsAuthoring other)
    {
        return layerMask == other.layerMask && Array.Equals(tags, other.tags);
    }

    public static implicit operator LayerMaskAndTags(LayerMaskAndTagsAuthoring data)
    {
        LayerMaskAndTags result;
        result.layerMask = data.layerMask;
        result.tags = default;
        if (data.tags != null)
        {
            foreach (var tag in data.tags)
                result.tags.Add(tag);
        }

        return result;
    }
}
