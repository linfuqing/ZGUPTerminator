using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAnalyticsEx : IAnalytics
{
    void Activate(string channelName, string channelUser);
    
    void Login(uint userID);

    void StartLevel(string name);

    void EnablePlayer();
    
    void DisablePlayer();

    void RespawnPlayer();

    void SetPlayerHPMax(int value);
    
    void SetPlayerHP(int value);
    
    void BeginPage(string name);
    
    void EndPage(string name);
    
    void PageAction(string name);
}

public class Analytics : MonoBehaviour, IAnalyticsEx
{
    public static readonly HashSet<IAnalyticsEx> instances = new HashSet<IAnalyticsEx>();

    public void Activate(string channelName, string channelUser)
    {
        foreach (var instance in instances)
        {
            instance.Activate(channelName, channelUser);
        }
    }

    public void Login(uint userID)
    {
        foreach (var instance in instances)
        {
            instance.Login(userID);
        }
    }

    public void StartLevel(string name)
    {
        foreach (var instance in instances)
        {
            instance.StartLevel(name);
        }
    }

    public void EnablePlayer()
    {
        foreach (var instance in instances)
        {
            instance.EnablePlayer();
        }
    }

    public void DisablePlayer()
    {
        foreach (var instance in instances)
        {
            instance.DisablePlayer();
        }
    }

    public void RespawnPlayer()
    {
        foreach (var instance in instances)
        {
            instance.RespawnPlayer();
        }
    }

    public void SetPlayerHPMax(int value)
    {
        foreach (var instance in instances)
        {
            instance.SetPlayerHPMax(value);
        }
    }

    public void SetPlayerHP(int value)
    {
        foreach (var instance in instances)
        {
            instance.SetPlayerHP(value);
        }
    }

    public void BeginPage(string name)
    {
        foreach (var instance in instances)
        {
            instance.BeginPage(name);
        }
    }

    public virtual void EndPage(string name)
    {
        foreach (var instance in instances)
        {
            instance.EndPage(name);
        }
    }

    public virtual void PageAction(string name)
    {
        foreach (var instance in instances)
        {
            instance.PageAction(name);
        }
    }
    
    public void Set(
        int value,
        int max,
        int maxExp,
        int exp,
        int count,
        int gold,
        int stage)
    {
        foreach (var instance in instances)
        {
            instance.Set(value, max, maxExp, exp, count, gold, stage);
        }
    }

    public void EnableStage(string name)
    {
        foreach (var instance in instances)
        {
            instance.EnableStage(name);
        }
    }

    public void DisableStage(string name)
    {
        foreach (var instance in instances)
        {
            instance.DisableStage(name);
        }
    }

    public void SetActiveSkill(string name)
    {
        foreach (var instance in instances)
        {
            instance.SetActiveSkill(name);
        }
    }

    public void SelectSkills(int styleIndex, LevelSkillData[] skills)
    {
        foreach (var instance in instances)
        {
            instance.SelectSkills(styleIndex, skills);
        }
    }

    public void SelectSkillBegin(int selectionIndex)
    {
        foreach (var instance in instances)
        {
            instance.SelectSkillBegin(selectionIndex);
        }
    }
    
    public void SelectSkillEnd()
    {
        foreach (var instance in instances)
        {
            instance.SelectSkillEnd();
        }
    }

    public void Pause() 
    {
        foreach (var instance in instances)
        {
            instance.Pause();
        }
    }

    public void Restart()
    {
        foreach (var instance in instances)
        {
            instance.Restart();
        }
    }

    public void Quit()
    {
        foreach (var instance in instances)
        {
            instance.Quit();
        }
    }
    
    protected void OnEnable()
    {
        if(IAnalytics.instance == null)
            IAnalytics.instance = this;
    }
    
    protected void OnDisable()
    {
        if(IAnalytics.instance == (IAnalytics)this)
            IAnalytics.instance = null;
    }
}

public abstract class AnalyticsBase : MonoBehaviour, IAnalyticsEx
{
    public virtual void Activate(string channelName, string channelUser)
    {
    }

    public virtual void Login(uint userID)
    {
    }

    public virtual void StartLevel(string name)
    {
    }

    public virtual void EnablePlayer()
    {
    }

    public virtual void DisablePlayer()
    {
    }

    public virtual void RespawnPlayer()
    {
    }

    public virtual void SetPlayerHPMax(int value)
    {
    }

    public virtual void SetPlayerHP(int value)
    {
    }

    public virtual void BeginPage(string name)
    {
        
    }

    public virtual void EndPage(string name)
    {
        
    }
    
    public virtual void PageAction(string name)
    {
    }

    public virtual void Set(
        int value,
        int max,
        int maxExp,
        int exp,
        int count,
        int gold,
        int stage)
    {
    }

    public virtual void EnableStage(string name)
    {
    }

    public virtual void DisableStage(string name)
    {
    }

    public virtual void SetActiveSkill(string name)
    {
    }

    public virtual void SelectSkills(int styleIndex, LevelSkillData[] skills)
    {
    }

    public virtual void SelectSkillBegin(int selectionIndex)
    {
    }
    
    public virtual void SelectSkillEnd()
    {
    }

    public virtual void Pause() 
    {
    }

    public virtual void Restart()
    {
    }

    public virtual void Quit()
    {
    }

    protected void OnEnable()
    {
        Analytics.instances.Add(this);
    }
    
    protected void OnDisable()
    {
        Analytics.instances.Remove(this);
    }
}