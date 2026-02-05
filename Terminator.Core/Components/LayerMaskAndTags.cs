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
    
    public bool Overlaps(in LayerMaskAndTags layerMaskAndTags)
    {
        if (isEmpty)
            return true;
        
        if (layerMask != 0 && (layerMask & layerMaskAndTags.layerMask) != 0)
            return true;

        if (!tags.IsEmpty)
        {
            foreach (var destination in tags)
            {
                foreach (var source in layerMaskAndTags.tags)
                {
                    if (source == destination)
                        return true;
                }
            }
        }

        return false;
    }

    public bool BelongsTo(in LayerMaskAndTags layerMaskAndTags)
    {
        if (layerMask != 0 && (layerMask & layerMaskAndTags.layerMask) == 0)
            return false;

        if (tags.IsEmpty)
            return true;
        
        foreach (var destination in tags)
        {
            foreach (var source in layerMaskAndTags.tags)
            {
                if (source == destination)
                    return true;
            }
        }

        return false;
    }

    public bool IsSupersetOf(in LayerMaskAndTags layerMaskAndTags)
    {
        if ((layerMaskAndTags.layerMask & layerMask) != layerMaskAndTags.layerMask)
            return false;

        if (!layerMaskAndTags.tags.IsEmpty)
        {
            bool isContains;
            foreach (var destination in layerMaskAndTags.tags)
            {
                isContains = false;
                foreach (var source in tags)
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

    public void InterOr(in LayerMaskAndTags layerMaskAndTags)
    {
        int origin, layerMask = layerMaskAndTags.layerMask;
        do
        {
            origin = this.layerMask;
        } while (System.Threading.Interlocked.CompareExchange(ref this.layerMask, origin | layerMask, origin) != origin);

        foreach (var tag in layerMaskAndTags.tags)
            CollectionUtility.FixedListInterlockedAdd(ref tags, tag);
            //tags.Add(tag);
    }
    
    public static LayerMaskAndTags Except(in LayerMaskAndTags x, in LayerMaskAndTags y)
    {
        LayerMaskAndTags result;
        result.layerMask = x.layerMask & ~y.layerMask;
        result.tags = default;
        
        bool isContains;
        foreach (var tagX in x.tags)
        {
            isContains = false;
            foreach (var tagY in y.tags)
            {
                if (tagY == tagX)
                {
                    isContains = true;
                    
                    break;
                }
            }
            
            if(isContains)
                continue;
            
            result.tags.Add(tagX);
        }

        return result;
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
    
    public static LayerMaskAndTags operator &(in LayerMaskAndTags x, in LayerMaskAndTags y)
    {
        LayerMaskAndTags result;
        result.layerMask = x.layerMask & y.layerMask;
        result.tags = default;

        foreach (var tagX in x.tags)
        {
            foreach (var tagY in y.tags)
            {
                if (tagY == tagX)
                {
                    result.tags.Add(tagY);
                    
                    break;
                }
            }
        }

        return result;
    }
}


[Serializable]
public struct LayerMaskAndTagsAuthoring : IEquatable<LayerMaskAndTagsAuthoring>
{
    [UnityEngine.Serialization.FormerlySerializedAs("value")]
    public LayerMask layerMask;

    public string[] tags;

    public LayerMaskAndTagsAuthoring(LayerMask layerMask, string[] tags)
    {
        this.layerMask = layerMask;
        this.tags = tags;
    }

    public bool Equals(LayerMaskAndTagsAuthoring other)
    {
        return layerMask == other.layerMask && Array.Equals(tags, other.tags);
    }

    public override int GetHashCode()
    {
        return layerMask;
    }

    public static LayerMaskAndTagsAuthoring operator |(in LayerMaskAndTagsAuthoring x, in LayerMaskAndTagsAuthoring y)
    {
        LayerMaskAndTagsAuthoring result;
        result.layerMask = x.layerMask | y.layerMask;
        var tags = new System.Collections.Generic.List<string>();

        if (x.tags != null)
        {
            foreach (var tag in x.tags)
                tags.Add(tag);
        }

        if (y.tags != null)
        {
            foreach (var tag in y.tags)
                tags.Add(tag);
        }

        result.tags = tags.ToArray();
        return result;
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
