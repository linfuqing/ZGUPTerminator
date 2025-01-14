using System;
using System.Collections;

public interface ILevelData
{
    public static ILevelData instance;
    
    IEnumerator SubmitLevel(
        int stage, 
        int gold, 
        Action<bool> onComplete);
}
