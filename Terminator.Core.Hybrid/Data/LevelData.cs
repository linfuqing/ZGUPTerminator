using System;
using System.Collections;

public interface ILevelData
{
    [Flags]
    public enum Flag
    {
        HasBeenDamaged = 0x01
    }

    public static ILevelData instance;
    
    IEnumerator SubmitStage(
        Flag flag, 
        int stage, 
        int gold, 
        int exp, 
        int expMax, 
        string[] skills,
        Action<bool> onComplete);
    
    IEnumerator SubmitLevel(
        Flag flag, 
        int stage, 
        int gold, 
        Action<bool> onComplete);
}
