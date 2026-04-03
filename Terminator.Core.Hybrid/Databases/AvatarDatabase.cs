using System;
using Unity.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New AvatarDatabase", menuName = "Game/AvatarDatabase")]
public class AvatarDatabase : ScriptableObject
{
    [Serializable]
    public struct Item
    {
        public string name;
        public Sprite sprite;
    }

    [SerializeField]
    internal Item[] _items;

    private Dictionary<FixedString32Bytes, Sprite> __items;

    public Sprite Get(in FixedString32Bytes name)
    {
        if (__items == null)
        {
            __items = new Dictionary<FixedString32Bytes, Sprite>();
            
            foreach (var item in _items)
                __items.Add(item.name, item.sprite);
        }
        
        return __items.TryGetValue(name, out var sprite) ? sprite : null;
    }
}
