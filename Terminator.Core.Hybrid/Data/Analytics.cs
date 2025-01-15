using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAnalytics
{
    public static IAnalytics instance;
    
    void EnableStage(string name);
    
    void DisableStage(string name);
    
    void SetActiveSkill(string name);
}

public class Analytics : MonoBehaviour
{
    
}
