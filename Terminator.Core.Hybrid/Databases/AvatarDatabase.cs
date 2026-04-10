using System;
using Unity.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZG;

[CreateAssetMenu(fileName = "New AvatarDatabase", menuName = "Game/AvatarDatabase")]
public class AvatarDatabase : ScriptableObject
{
    [Serializable]
    public struct Item
    {
        public string name;
        public Sprite sprite;
        
#if UNITY_EDITOR
        [CSVField]
        public string 头像名称
        {
            set
            {
                name = value;
            }
        }

        [CSVField]
        public string 头像图标
        {
            set
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(value);
            }
        }
#endif
    }
    
    [SerializeField]
    internal Sprite _defaultSprite;
    
    [SerializeField]
    internal Item[] _items;

#if UNITY_EDITOR
    [CSV("_items", guidIndex = -1, nameIndex = 0)] 
    [SerializeField]
    internal string _itemsPath;
#endif
    
    private Dictionary<FixedString32Bytes, Sprite> __items;

    public Sprite Get(in FixedString32Bytes name)
    {
        if (__items == null)
        {
            __items = new Dictionary<FixedString32Bytes, Sprite>();
            
            foreach (var item in _items)
                __items.Add(item.name, item.sprite);
        }
        
        return __items.TryGetValue(name, out var sprite) ? sprite : _defaultSprite;
    }
}
