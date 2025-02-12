using System;
using System.Collections.Generic;
using UnityEngine;
using ZG;

[Serializable]
public struct SkillAsset
{
    public string name;
    public string detail;
    
    public Sprite sprite;
    public Sprite icon;

    public int level;
    public int rarity;
    
#if UNITY_EDITOR
    [CSVField]
    public string 关卡技能描述标题
    {
        set
        {
            name = value;
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
            sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(value);
        }
    }
    
    [CSVField]
    public string 关卡技能描述图标
    {
        set
        {
            icon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(value);
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
#endif
}

public class SkillManager : MonoBehaviour
{
    [Serializable]
    public struct SkillData
    {
        public string name;

        public string title;
        public string detail;

        public Sprite sprite;
        public Sprite icon;

        public int level;
        public int rarity;
        
        public SkillAsset ToAsset()
        {
            SkillAsset asset;
            asset.name = title;
            asset.detail = detail;
            asset.sprite = sprite;
            asset.icon = icon;
            asset.level = level;
            asset.rarity = rarity;
            return asset;
        }

#if UNITY_EDITOR
        [CSVField]
        public string 关卡技能描述名称
        {
            set { name = value; }
        }
        
        [CSVField]
        public string 关卡技能描述标题
        {
            set { title = value; }
        }

        [CSVField]
        public string 关卡技能描述详情
        {
            set { detail = value; }
        }

        [CSVField]
        public string 关卡技能描述精灵
        {
            set { sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(value); }
        }

        [CSVField]
        public string 关卡技能描述图标
        {
            set { icon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(value); }
        }

        [CSVField]
        public int 关卡技能描述等级
        {
            set { level = value; }
        }

        [CSVField]
        public int 关卡技能描述稀有度
        {
            set { rarity = value; }
        }
#endif
    }

    [SerializeField]
    internal SkillData[] _skills;

    #region CSV
    [SerializeField]
    [CSV("_skills", guidIndex = -1, nameIndex = 0)]
    internal string _skillsPath;
    #endregion

    private static Dictionary<string, SkillAsset> __assets = new Dictionary<string, SkillAsset>();

    public static bool TryGetAsset(string name, out SkillAsset result)
    {
        return __assets.TryGetValue(name, out result);
    }

    void OnEnable()
    {
        foreach (var skill in _skills)
            __assets.Add(skill.name, skill.ToAsset());
    }

    void OnDisable()
    {
        foreach (var skill in _skills)
            __assets.Remove(skill.name);
    }
}
