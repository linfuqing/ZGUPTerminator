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

    void SetPlayerHPMax(int value);
    
    void SetPlayerHP(int value);
}