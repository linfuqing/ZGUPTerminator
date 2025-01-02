using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "AnimatedSkin/Database", fileName = "AnimatedSkin Database")]
public class AnimatedSkinDatabase : ScriptableObject
{
    [Serializable]
    public struct Animation
    {
        public string name;
        public int startFrame;
        public int frameCount;
    }

    public Animation[] animations;

    private Dictionary<string, int> __nameIndices;

    public int FindAnimationIndex(string name)
    {
        if (__nameIndices == null)
        {
            __nameIndices = new Dictionary<string, int>();
            
            int numAnimations = animations.Length;
            for (int i = 0; i < numAnimations; ++i)
                __nameIndices.Add(animations[i].name, i);
        }
        
        return __nameIndices.TryGetValue(name, out int index) ? index : -1;
    }
}
