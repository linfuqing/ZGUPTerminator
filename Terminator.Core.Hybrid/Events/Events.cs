using System;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class ActiveEvent : UnityEvent<bool>
{
        
}

[Serializable]
public class StringEvent : UnityEvent<string>
{
        
}
    
[Serializable]
public class SpriteEvent : UnityEvent<Sprite>
{
        
}

