using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;
using ZG;

#if UNITY_EDITOR
using UnityEditor;
public class LevelSkillDescAuthoring : MonoBehaviour
{
    [Serializable]
    public struct SkillData
    {
        public string name;

        public string preSkillName;

        public string title;

        public string detail;
        
        public Sprite sprite;

        public Sprite icon;

        public int level;
        
        public int rarity;

        #region CSV
        [CSVField]
        public string 关卡技能描述名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 关卡技能描述前置技能
        {
            set
            {
                preSkillName = value;
            }
        }

        [CSVField]
        public string 关卡技能描述标题
        {
            set
            {
                title = value;
            }
        }

        [CSVField]
        public string 关卡技能描述详情
        {
            set
            {
                detail = value;
            }
        }
        
        [CSVField]
        public string 关卡技能描述精灵
        {
            set
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(value);
            }
        }
        
        [CSVField]
        public string 关卡技能描述图标
        {
            set
            {
                icon = AssetDatabase.LoadAssetAtPath<Sprite>(value);
            }
        }
        
        [CSVField]
        public int 关卡技能描述等级
        {
            set
            {
                level = value;
            }
        }
        
        [CSVField]
        public int 关卡技能描述稀有度
        {
            set
            {
                rarity = value;
            }
        }
        #endregion
    }

    class Baker : Baker<LevelSkillDescAuthoring>
    {
        public override void Bake(LevelSkillDescAuthoring authoring)
        {
            if(authoring._skillAuthoring == null)
                authoring._skillAuthoring = GetComponent<SkillAuthoring>();
            if (authoring._skillAuthoring == null)
            {
                Debug.LogError("LevelSkillAuthoring Need a ref of SkillAuthoring to bake!", authoring);
                
                return;
            }

            int numSkills = authoring._skillAuthoring._skills.Length;
            var skillNameIndices = new Dictionary<string, int>(numSkills);
            for(int i = 0; i < numSkills; ++i)
                skillNameIndices.Add(authoring._skillAuthoring._skills[i].name, i);

            var entity = GetEntity(TransformUsageFlags.None);

            var skills = AddBuffer<LevelSkillDesc>(entity);

            skills.Resize(numSkills, NativeArrayOptions.ClearMemory);

            int skillNameIndex;
            foreach (var source in authoring._skills)
            {
                if (!skillNameIndices.TryGetValue(source.name, out skillNameIndex))
                {
                    Debug.LogError(
                    $"The skill {source.name} can not been found!");
                    
                    continue;
                }
                
                ref var destination = ref skills.ElementAt(skillNameIndex);

                destination.name = source.title;
                destination.detail = source.detail;
                destination.sprite = new WeakObjectReference<Sprite>(source.sprite);
                destination.icon = new WeakObjectReference<Sprite>(source.icon);
                destination.level = source.level;
                destination.rarity = source.rarity;
                destination.preSkillIndex = skillNameIndices.TryGetValue(source.preSkillName, out skillNameIndex) ? skillNameIndex : -1;
            }
        }
    }
    
    [SerializeField]
    internal SkillData[] _skills;
    
    #region CSV
    [SerializeField]
    [CSV("_skills", guidIndex = -1, nameIndex = 0)]
    internal string _skillsPath;
    #endregion

    [SerializeField] 
    internal SkillAuthoring _skillAuthoring;
}
#endif

public struct SkillAsset
{
    public string name;
    public string detail;
    
    public Sprite sprite;

    public int level;
    public int rarity;
}

public struct LevelSkillDesc : IBufferElementData
{
    public FixedString128Bytes name;

    public FixedString512Bytes detail;
    
    public WeakObjectReference<Sprite> sprite;
    
    public WeakObjectReference<Sprite> icon;

    public int level;

    public int rarity;

    public int preSkillIndex;

    public SkillAsset ToAsset(bool isSpriteOrIcon)
    {
        SkillAsset result;
        result.name = name.ToString();
        result.detail = detail.ToString();
        result.sprite = isSpriteOrIcon ? sprite.Result : icon.Result;
        result.level = level;
        result.rarity = rarity;

        return result;
    }
}