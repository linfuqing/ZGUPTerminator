using System;
using System.Collections;

public interface ILevelData
{
    public static ILevelData instance;
    
    IEnumerator SubmitLevel(
        int stage, 
        int gold, 
        int exp, 
        int expMax, 
        string[] skills,
        Action<bool> onComplete);
}
