using System;
using System.Collections;

public interface ILevelData
{
    public struct Item
    {
        public string name;
        public int count;
    }

    public struct StageResult
    {
        public int rankFlag;

        public int energyStage;
        public int energyMax;
    }

    /*[Flags]
    public enum Flag
    {
        HasBeenDamaged = 0x01
    }*/

    public static ILevelData instance;
    
    IEnumerator SubmitStage(
        //Flag flag, 
        int stage, 
        int time, 
        int hpPercentage,
        int killCount, 
        int killBossCount, 
        int gold, 
        int rage, 
        int exp, 
        int expMax, 
        string[] skills,
        Item[] items,
        Action<StageResult> onComplete);
    
    IEnumerator SubmitLevel(
        //Flag flag, 
        int stage, 
        int time, 
        int hpPercentage,
        int killCount, 
        int killBossCount, 
        int gold, 
        Action<bool> onComplete);
}
