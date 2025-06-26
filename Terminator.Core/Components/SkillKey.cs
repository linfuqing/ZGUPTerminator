using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

public struct SkillKeyDefinition
{
    public struct Skill
    {
        public BlobArray<int> keyIndices;
    }

    public struct BulletLayerMask
    {
        public int count;

        public int value;
    }

    public struct Key
    {
        public BlobArray<BulletLayerMask> bulletLayerMasks;
    }
    
    public BlobArray<Skill> skills;
    public BlobArray<Key> keys;

    public int GetBulletLayerMask(in NativeArray<SkillActiveIndex> skillActiveIndices, ref UnsafeHashMap<int, int> counts)
    {
        if(counts.IsCreated)
            counts.Clear();
        
        int i, j, count, numKeyIndices, numSkills = skills.Length;
        foreach (var skillActiveIndex in skillActiveIndices)
        {
            if(skillActiveIndex.value >= numSkills)
                continue;
            
            ref var skill = ref skills[skillActiveIndex.value];
            numKeyIndices = skill.keyIndices.Length;
            for (j = 0; j < numKeyIndices; ++j)
            {
                ref int keyIndex = ref skill.keyIndices[j];
                
                if(!counts.IsCreated)
                    counts = new UnsafeHashMap<int, int>(1, Allocator.Temp);

                if (counts.TryGetValue(keyIndex, out count))
                    ++count;
                else
                    count = 1;
                
                counts[keyIndex] = count;
            }
        }

        int result = 0, numBulletLayerMasks;
        foreach (var pair in counts)
        {
            ref var key = ref keys[pair.Key];
            
            numBulletLayerMasks = key.bulletLayerMasks.Length;
            for (i = 0; i < numBulletLayerMasks; ++i)
            {
                ref var bulletLayerMask = ref key.bulletLayerMasks[i];
                if(bulletLayerMask.count > pair.Value)
                    continue;
                
                result |= bulletLayerMask.value;

                break;
            }
        }

        return result;
    }
}

public struct SkillKeyDefinitionData : IComponentData
{
    public BlobAssetReference<SkillKeyDefinition> definition;
}

public struct SkillKeyLayerMask : IComponentData
{
    public int value;
}