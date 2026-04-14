using System;
using System.Collections;
using UnityEngine;

public interface ISensitiveWordData
{
    public static ISensitiveWordData instance;
    
    IEnumerator Check(string text, Action<string> onComplete);
}

