using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAnalytics
{
    public static IAnalytics instance;

    void Set(
        int value,
        int max,
        int maxExp,
        int exp,
        int count,
        int gold,
        int stage);
    
    void EnableStage(string name);
    
    void DisableStage(string name);
    
    void SetActiveSkill(string name);

    void SelectSkills(int styleIndex, LevelSkillData[] skills);

    void SelectSkillBegin(int selectionIndex);
    
    void SelectSkillEnd();

    void Pause();

    void Restart();

    void Quit();
}

public class Analytics : MonoBehaviour
{
    
}
