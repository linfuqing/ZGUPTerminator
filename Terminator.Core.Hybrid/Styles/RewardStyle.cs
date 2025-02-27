using System;
using UnityEngine;

public class RewardStyle : MonoBehaviour
{
    public bool isDestroyOnDisable;
    
    public SpriteEvent onSprite;

    public StringEvent onTitle;

    public StringEvent onCount;

    private void OnDisable()
    {
        if(isDestroyOnDisable)
            Destroy(gameObject);
    }
}
