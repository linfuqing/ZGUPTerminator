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
        public struct BulletLayerMask
        {
            public int count;

            [Tooltip("该词条生效时，激活特定的子弹标签")]
            public LayerMask value;
        }
        
        public string name;
        
        public BulletLayerMask[] bulletLayerMasks;
        
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
                BulletLayerMask bulletLayerMask;
                bulletLayerMasks = new BulletLayerMask[numParameters];
                string[] keywords;
                for (int i = 0; i < numParameters; ++i)
                {
                    keywords = parameters[i].Split(':');
                    bulletLayerMask.count = int.Parse(keywords[0]);
                    bulletLayerMask.value = int.Parse(keywords[1]);
                    
                    bulletLayerMasks[i] = bulletLayerMask;
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

                int i, j, numBulletLayerMasks, numKeys = authoring._keys == null ? 0 : authoring._keys.Length;
                BlobBuilderArray<SkillKeyDefinition.BulletLayerMask> bulletLayerMasks;
                var keys = builder.Allocate(ref root.keys, numKeys);
                for (i = 0; i < numKeys; ++i)
                {
                    ref var sourceKey = ref authoring._keys[i];
                    ref var destinationKey = ref keys[i];

                    numBulletLayerMasks = sourceKey.bulletLayerMasks.Length;
                    bulletLayerMasks = builder.Allocate(ref destinationKey.bulletLayerMasks, numBulletLayerMasks);
                    for (j = 0; j < numBulletLayerMasks; ++j)
                    {
                        ref var sourceBulletLayerMask = ref sourceKey.bulletLayerMasks[j];
                        ref var destinationBulletLayerMask = ref bulletLayerMasks[j];

                        destinationBulletLayerMask.count = sourceBulletLayerMask.count;
                        destinationBulletLayerMask.value = sourceBulletLayerMask.value;
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
