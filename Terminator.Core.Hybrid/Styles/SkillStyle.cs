using System;
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

    [UnityEngine.Serialization.FormerlySerializedAs("onImage")]
    public SpriteEvent onSprite;
    
    [UnityEngine.Serialization.FormerlySerializedAs("onImage")]
    public SpriteEvent onIcon;
    
    //public ZG.UI.Progressbar cooldown;
    
    public GameObject[] levels;
    public GameObject[] rarities;
    
    public ParentOverride[] parentOverrides;

    public Transform GetParent(int flag)
    {
        if (parentOverrides != null)
        {
            foreach (var parentOverride in parentOverrides)
            {
                if (((int)parentOverride.type & flag) != 0)
                    return parentOverride.transform;
            }
        }

        return transform.parent;
    }

    public void SetAsset(in SkillAsset value)
    {
        if(onName != null)
            onName.Invoke(value.name);
            
        if(onDetail != null)
            onDetail.Invoke(value.detail);
            
        if(onSprite != null)
            onSprite.Invoke(value.sprite);

        if(onIcon != null)
            onIcon.Invoke(value.icon);

        __SetActive(levels, value.level);
        
        __SetActive(rarities, value.rarity);
        
        var parent = GetParent(value.flag);
        if(parent != transform.parent)
            transform.SetParent(parent);
        
        gameObject.SetActive(true);
    }
    
    private static void __SetActive(GameObject[] gameObjects, int index)
    {
        int numGameObjects = gameObjects.Length;
        for(int i = 0; i < numGameObjects; ++i)
            gameObjects[i].SetActive(i == index);
    }
}
