using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class UserDataMain
{
    [Serializable]
    public struct Tip
    {
        public int max;
        public float uintTime;

        public int value => GetValue(out _);

        public int GetValue(int value, uint utcTime, out uint time)
        {
            var timeUnix = DateTime.UtcNow - Utc1970;

            uint now = (uint)timeUnix.TotalSeconds;
            
            time = now;
            if (uintTime > Mathf.Epsilon)
            {
                float tipFloat = (time - (utcTime > 0 ? utcTime : time)) / uintTime;
                int tipInt =  Mathf.FloorToInt(tipFloat);
                value += tipInt;

                time -= (uint)Mathf.RoundToInt((tipFloat - tipInt) * uintTime);
            }
        
            if (value >= max)
            {
                value = max;

                time = now;
            }

            return value;
        }

        public int GetValue(out uint time)
        {
            return GetValue(PlayerPrefs.GetInt(NAME_SPACE_USER_TIP), 
                (uint)PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME),
                out time);
        }
    }

    private const string NAME_SPACE_USER_TIP = "UserTip";
    private const string NAME_SPACE_USER_TIP_TIME = "UserTipTime";
    private static readonly DateTime Utc1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SerializeField]
    internal Tip _tip;

    public IEnumerator QueryTip(
        uint userID,
        Action<IUserData.Tip> onComplete)
    {
        yield return null;

        var timeUnix = DateTime.UtcNow - Utc1970;
        int time = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP_TIME);
        if (time == 0)
        {
            time = (int)timeUnix.TotalSeconds;
            
            PlayerPrefs.SetInt(NAME_SPACE_USER_TIP_TIME, time);
        }

        UserTip userTip;
        userTip.value = PlayerPrefs.GetInt(NAME_SPACE_USER_TIP);
        userTip.max = _tip.max;
        userTip.unitTime = (uint)Mathf.RoundToInt(_tip.uintTime * 1000);
        userTip.tick = (uint)time * TimeSpan.TicksPerSecond + Utc1970.Ticks;
        
        onComplete(default);
    }

    public IEnumerator CollectTip(
        uint userID,
        Action<Memory<UserRewardData>> onComplete)
    {
        yield return null;

        onComplete(null);
    }

}


public partial class UserData
{
    public IEnumerator QueryTip(
        uint userID,
        Action<IUserData.Tip> onComplete)
    {
        return UserDataMain.instance.QueryTip(userID, onComplete);
    }
    
    public IEnumerator CollectTip(
        uint userID,
        Action<Memory<UserRewardData>> onComplete)
    {
        return UserDataMain.instance.CollectTip(userID, onComplete);
    }
}