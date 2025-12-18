using System;
using System.Collections;

public interface IGameData
{
    public struct Notice
    {
        public string name;
        public string text;
        public string code;
        public long ticks;
        public UserRewardData[] rewards;
    }

    public static IGameData instance;
    
    IEnumerator QueryNotice(uint userID, string version, string language, Action<Memory<Notice>> callback);
    
    IEnumerator ApplyCode(string code, Action<Memory<UserReward>> callback);
}
