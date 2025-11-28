using System;
using System.Collections.Generic;
using UnityEngine;

public class SkillStyle : MonoBehaviour
{
    public enum ParentType
    {
        Main, 
        Extend, 
        Skill, 
        Weapon
    }
    
    [Serializable]
    public struct ParentOverride
    {
        public ParentType type;
        
        public Transform transform;
    }

    public StringEvent onName;
    public StringEvent onDetail;
    public StringEvent onInfo;

    public SpriteEvent onSprite;
    
    public SpriteEvent onIcon;
    
    public SkillKeyStyle keyStyle;
    
    public GameObject[] levels;
    public GameObject[] rarities;
    
    public ParentOverride[] parentOverrides;

    private List<SkillKeyStyle> __keyStyles;
    
    public IReadOnlyList<SkillKeyStyle> keyStyles => __keyStyles;

    public static void SetActive(GameObject[] gameObjects, int index)
    {
        int numGameObjects = gameObjects.Length;
        for(int i = 0; i < numGameObjects; ++i)
            gameObjects[i].SetActive(i == index);
    }

    public Transform GetParent(int flag)
    {
        if (parentOverrides != null)
        {
            foreach (var parentOverride in parentOverrides)
            {
                if ((int)parentOverride.type == flag)
                    return parentOverride.transform;
            }
        }

        return transform.parent;
    }

    public void SetAsset(in SkillAsset value, Sprite[] keys = null)
    {
        OnDestroy();
        
        if(onName != null)
            onName.Invoke(value.name);
            
        if(onDetail != null)
            onDetail.Invoke(value.detail);
            
        if(onInfo != null)
            onInfo.Invoke(value.info);

        if(onSprite != null)
            onSprite.Invoke(value.sprite);

        if(onIcon != null)
            onIcon.Invoke(value.icon);

        SetActive(levels, value.level);
        
        SetActive(rarities, value.rarity);

        if (keyStyle != null && keys != null && keys.Length > 0)
        {
            SkillKeyStyle keyStyle;
            Transform keyStyleParent = this.keyStyle.transform.parent;
            foreach (var key in keys)
            {
                keyStyle = Instantiate(this.keyStyle, keyStyleParent);
                
                keyStyle.onSprite.Invoke(key);
                
                keyStyle.gameObject.SetActive(true);
                
                if(__keyStyles == null)
                    __keyStyles = new List<SkillKeyStyle>();
                
                __keyStyles.Add(keyStyle);
            }
        }
        
        var parent = GetParent(value.flag);
        if(parent != transform.parent)
            transform.SetParent(parent);
        
        gameObject.SetActive(value.sprite != null);
    }
    
    private void OnDestroy()
    {
        if (__keyStyles != null)
        {
            foreach (var keyStyle in __keyStyles)
                Destroy(keyStyle.gameObject);
            
            __keyStyles.Clear();
        }
    }
}
