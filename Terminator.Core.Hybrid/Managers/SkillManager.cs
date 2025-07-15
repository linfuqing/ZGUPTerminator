using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using ZG;

public struct SkillAsset
{
    public string name;
    public string detail;
    
    public Sprite sprite;
    public Sprite icon;

    public int level;
    public int rarity;

    public int flag;

    public int priority;
}

public struct SkillKeyAsset
{
    public string name;
    public string detail;

    public Sprite sprite;

    public int capacity;
    
    public int[] ranks;
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

        public int priority;
        
        public int flag;
        
        public string[] keys;
        
        public SkillAsset ToAsset()
        {
            SkillAsset asset;
            asset.name = title;
            asset.detail = detail;
            asset.sprite = sprite;
            asset.icon = icon;
            asset.level = level;
            asset.rarity = rarity;
            asset.flag = flag;
            asset.priority = priority;
            
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
        
        [CSVField]
        public int 关卡技能描述优先级
        {
            set
            {
                priority = value;
            }
        }
        
        [CSVField]
        public int 关卡技能描述标签
        {
            set
            {
                flag = value;
            }
        }
#endif
    }

    [Serializable]
    public struct SkillKeyData
    {
        public string name;

        public string title;
        public string detail;

        public Sprite sprite;

        public int capacity;
    
        public int[] ranks;
        
        public SkillKeyAsset ToAsset()
        {
            SkillKeyAsset asset;
            asset.name = title;
            asset.detail = detail;
            asset.sprite = sprite;
            asset.capacity = capacity;
            asset.ranks = ranks;
            
            //Array.Sort(asset.ranks);
            
            return asset;
        }

#if UNITY_EDITOR
        [CSVField]
        public string 关卡技能词条描述名称
        {
            set
            {
                name = value;

                capacity = 9;

                ranks = new int[]
                {
                    3, 5, 7, 9
                };
            }
        }
        
        [CSVField]
        public string 关卡技能词条描述标题
        {
            set { title = value; }
        }
        
        [CSVField]
        public string 关卡技能词条描述详情
        {
            set { detail = value; }
        }

        [CSVField]
        public string 关卡技能词条描述图标
        {
            set { sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(value); }
        }

#endif
    }

    private struct Asset
    {
        public SkillAsset value;

        public string[] keyNames;
        
        public Sprite[] keySprites;
    }
    
    [SerializeField]
    internal SkillData[] _skills;

    [SerializeField]
    internal SkillKeyData[] _keys;
    
    #region CSV
    [SerializeField]
    [CSV("_skills", guidIndex = -1, nameIndex = 0)]
    internal string _skillsPath;
    #endregion

    private static Dictionary<FixedString128Bytes, Asset> __assets = new Dictionary<FixedString128Bytes, Asset>();

    private static Dictionary<string, SkillKeyAsset> __keyAssets = new Dictionary<string, SkillKeyAsset>();

    public static bool TryGetAsset(string name, out SkillAsset result)
    {
        return TryGetAsset(name.ToString(), out result);
    }

    public static bool TryGetAsset(in FixedString128Bytes name, out SkillAsset result)
    {
        if (__assets.TryGetValue(name, out var asset))
        {
            result = asset.value;

            return true;
        }

        result = default;
        
        return false;
    }
    
    public static bool TryGetAsset(
        in FixedString128Bytes name, 
        out SkillAsset result, 
        out string[] keyNames, 
        out Sprite[] keySprites)
    {
        if (__assets.TryGetValue(name, out var asset))
        {
            result = asset.value;
            keyNames = asset.keyNames;
            keySprites = asset.keySprites;
            
            return true;
        }
        
        result = default;
        keyNames = null;
        keySprites = null;

        return true;
    }
    
    public static bool TryGetAsset(in string name, out SkillKeyAsset result)
    {
        return __keyAssets.TryGetValue(name, out result);
    }

    void OnEnable()
    {
        if (_keys != null)
        {
            foreach (var key in _keys)
                __keyAssets.Add(key.name, key.ToAsset());
        }
        
        Asset asset;
        asset.keySprites = null;
        
        SkillKeyAsset keyAsset;
        int i, j, numKeys, numSkills = _skills.Length;
        for (i = 0; i < numSkills; ++i)
        {
            ref var skill = ref _skills[i];
            
            asset.value = skill.ToAsset();
            asset.keyNames = skill.keys;

            numKeys = skill.keys == null ? 0 : skill.keys.Length;
            asset.keySprites = new Sprite[numKeys];

            for (j = 0; j < numKeys; ++j)
                asset.keySprites[j] = __keyAssets.TryGetValue(skill.keys[j], out keyAsset) ? keyAsset.sprite : null;
            
            __assets.Add(skill.name, asset);
        }

    }

    void OnDisable()
    {
        foreach (var skill in _skills)
            __assets.Remove(skill.name);
        
        if (_keys != null)
        {
            foreach (var key in _keys)
                __keyAssets.Remove(key.name);
        }
    }
}
