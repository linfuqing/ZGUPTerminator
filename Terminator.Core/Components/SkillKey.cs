using Unity.Collections;
using Unity.Entities;

public struct SkillKeyDefinition
{
    public struct Skill
    {
        public int index;
        
        public BlobArray<int> keyIndices;
    }

    public struct BulletLayerMask
    {
        public int count;

        public int include;
        public int exclude;
    }

    public struct Key
    {
        public BlobArray<BulletLayerMask> bulletLayerMasks;
    }
    
    public BlobArray<Skill> skills;
    public BlobArray<Key> keys;
}

public struct SkillKeyDefinitionData : IComponentData
{
    public BlobAssetReference<SkillKeyDefinition> definition;
}