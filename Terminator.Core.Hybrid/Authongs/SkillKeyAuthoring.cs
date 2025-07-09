using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using ZG;

#if UNITY_EDITOR
[RequireComponent(typeof(SkillAuthoring))]
public class SkillKeyAuthoring : MonoBehaviour
{
    [Serializable]
    internal struct KeyData
    {
        [Serializable]
        public struct BulletTag : IComparable<BulletTag>
        {
            public int count;

            [Tooltip("该词条生效时，激活特定的子弹标签")]
            public string value;

            public int CompareTo(BulletTag other)
            {
                return other.count.CompareTo(count);
            }

            public override int GetHashCode()
            {
                return count;
            }
        }
        
        public string name;
        
        public BulletTag[] bulletTags;
        
        [CSVField]
        public string 词条名称
        {
            set
            {
                name = value;
            }
        }

        [CSVField]
        public string 词条标签
        {
            set
            {
                var parameters = value.Split('/');
                
                int numParameters = parameters.Length;
                BulletTag bulletTag;
                bulletTags = new BulletTag[numParameters];
                string[] keywords;
                for (int i = 0; i < numParameters; ++i)
                {
                    keywords = parameters[i].Split(':');
                    bulletTag.count = int.Parse(keywords[0]);
                    bulletTag.value = keywords[1];
                    
                    bulletTags[i] = bulletTag;
                }
            }
        }
    }

    [Serializable]
    internal struct SkillData
    {
        public string name;

        public string[] keyNames;
        
        [CSVField]
        public string 词条技能名称
        {
            set
            {
                name = value;
            }
        }

        [CSVField]
        public string 词条技能词条
        {
            set
            {
                keyNames = value.Split("/");
            }
        }
    }

    class Baker : Baker<SkillKeyAuthoring>
    {
        public override void Bake(SkillKeyAuthoring authoring)
        {
            SkillKeyDefinitionData instance;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<SkillKeyDefinition>();

                int i, j, numBulletTags, numKeys = authoring._keys == null ? 0 : authoring._keys.Length;
                BlobBuilderArray<SkillKeyDefinition.BulletTag> bulletTags;
                var keys = builder.Allocate(ref root.keys, numKeys);
                for (i = 0; i < numKeys; ++i)
                {
                    ref var sourceKey = ref authoring._keys[i];
                    ref var destinationKey = ref keys[i];

                    Array.Sort(sourceKey.bulletTags);
                    
                    numBulletTags = sourceKey.bulletTags.Length;
                    bulletTags = builder.Allocate(ref destinationKey.bulletTags, numBulletTags);
                    for (j = 0; j < numBulletTags; ++j)
                    {
                        ref var sourceBulletTag = ref sourceKey.bulletTags[j];
                        ref var destinationBulletTag = ref bulletTags[j];

                        destinationBulletTag.count = sourceBulletTag.count;
                        destinationBulletTag.value = sourceBulletTag.value;
                    }
                }

                var skillAuthoring = GetComponent<SkillAuthoring>();
                int skillLength = skillAuthoring.skills.Length;
                
                int k, numKeyIndices, numSkills = authoring._skills == null ? 0 : authoring._skills.Length;
                string keyName;
                BlobBuilderArray<int> keyIndices;
                var skills = builder.Allocate(ref root.skills, skillLength);
                for (i = 0; i < skillLength; ++i)
                    skills[i] = default;
                
                for (i = 0; i < numSkills; ++i)
                {
                    ref var sourceSkill = ref authoring._skills[i];
                    
                    for (j = 0; j < skillLength; ++j)
                    {
                        if (skillAuthoring.skills[j].name == sourceSkill.name)
                            break;
                    }

                    if (j == skillLength)
                    {
                        Debug.LogError($"The key of skill {sourceSkill.name} can not been found!");
                        
                        continue;
                    }
                    
                    ref var destinationSkill = ref skills[j];

                    numKeyIndices = sourceSkill.keyNames == null ? 0 : sourceSkill.keyNames.Length;
                    keyIndices = builder.Allocate(ref destinationSkill.keyIndices, numKeyIndices);
                    for (j = 0; j < numKeyIndices; ++j)
                    {
                        keyIndices[j] = -1;
                        
                        keyName = sourceSkill.keyNames[j];
                        for (k = 0; k < numKeys; ++k)
                        {
                            if (authoring._keys[k].name == keyName)
                            {
                                keyIndices[j] = k;
                                
                                break;
                            }
                        }

                        if (keyIndices[j] == -1)
                            Debug.LogError($"Key {keyName} of skill {sourceSkill.name} can not been found!");
                    }
                }
                
                instance.definition = builder.CreateBlobAssetReference<SkillKeyDefinition>(Allocator.Persistent);
            }

            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            AddBlobAsset(ref instance.definition, out _);
            
            AddComponent(entity, instance);
        }
    }
    
    [SerializeField]
    internal KeyData[] _keys;
    
    #region CSV
    [SerializeField]
    [CSV("_keys", guidIndex = -1, nameIndex = 0)]
    internal string _keysPath;
    #endregion
    
    [SerializeField]
    internal SkillData[] _skills;
    
    #region CSV
    [SerializeField]
    [CSV("_skills", guidIndex = -1, nameIndex = 0)]
    internal string _skillsPath;
    #endregion
}
#endif
