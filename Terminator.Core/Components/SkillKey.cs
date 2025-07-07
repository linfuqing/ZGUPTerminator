using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

public struct SkillKeyDefinition
{
    public struct Skill
    {
        public BlobArray<int> keyIndices;
    }

    public struct BulletTag
    {
        public int count;

        public FixedString32Bytes value;
    }

    public struct Key
    {
        public BlobArray<BulletTag> bulletTags;
    }
    
    public BlobArray<Skill> skills;
    public BlobArray<Key> keys;

    public void GetBulletTags(
        in NativeArray<SkillActiveIndex> skillActiveIndices,
        ref DynamicBuffer<global::BulletTag> results, 
        ref UnsafeHashMap<int, int> counts)
    {
        if(counts.IsCreated)
            counts.Clear();
        
        results.Clear();
        
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

        int numBulletTags;
        global::BulletTag result;
        foreach (var pair in counts)
        {
            ref var key = ref keys[pair.Key];
            
            numBulletTags = key.bulletTags.Length;
            for (i = 0; i < numBulletTags; ++i)
            {
                ref var bulletTag = ref key.bulletTags[i];
                if(bulletTag.count > pair.Value)
                    continue;
                
                result.value = bulletTag.value;

                results.Add(result);

                break;
            }
        }
    }
}

public struct SkillKeyDefinitionData : IComponentData
{
    public BlobAssetReference<SkillKeyDefinition> definition;
}