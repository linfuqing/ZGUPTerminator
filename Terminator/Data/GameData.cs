using System;
using System.Collections;
using System.Text;
using UnityEngine;

public interface IGameData
{
    public struct Notice
    {
        [Flags]
        public enum Flag
        {
            Used = 0x01, 
            Important = 0x02,
            NotBeClosed = 0x04
        }
        
        public string name;
        public string text;
        public string code;
        public Flag flag;
        public uint index;
        public long ticks;
        public UserRewardData[] rewards;
    }

    public static IGameData instance;
    
    IEnumerator QueryNotice(uint userID, uint version, string language, Action<Memory<Notice>> callback);
    
    IEnumerator ApplyCode(uint userID, uint version, string code, Action<Memory<UserReward>> callback);
}

public class GameData : IGameData
{
    [SerializeField] 
    internal string _url;
    
    public IEnumerator QueryNotice(uint userID, uint version, string language, Action<Memory<IGameData.Notice>> callback)
    {
        var form = new WWWForm();
        form.AddField("user_id", (int)userID);
        form.AddField("version", (int)version);
        form.AddField("language", language);

        yield return WWWUtility.MD5Request(x =>
        {
            int numNotices = (int)x.ReadUInt32(), numRewards, i, j;
            string text;
            UserRewardData reward;
            IGameData.Notice notice;
            var notices = new IGameData.Notice[numNotices];
            StringBuilder sb = new StringBuilder();
            for (i = 0; i < numNotices; ++i)
            {
                notice.name = x.ReadString();

                sb.Clear();
                do
                {
                    text = x.ReadString();

                    sb.Append(text);
                } while (!string.IsNullOrEmpty(text));
                
                notice.text = sb.ToString();
                notice.code = x.ReadString();
                notice.index = x.ReadUInt32();
                notice.ticks = ZG.DateTimeUtility.GetTicks(x.ReadUInt32());
                notice.flag = (IGameData.Notice.Flag)x.ReadByte();
                numRewards = x.ReadByte();
                notice.rewards = new UserRewardData[numRewards];
                for (j = 0; j < numRewards; ++j)
                {
                    reward.name = x.ReadString();
                    reward.type = (UserRewardType)x.ReadByte();
                    reward.count = (int)x.ReadUInt32();

                    notice.rewards[j] = reward;
                }

                notices[i] = notice;
            }

            callback(notices);

            return true;
        }, form, _url);
    }

    public IEnumerator ApplyCode(uint userID, uint version, string code, Action<Memory<UserReward>> callback)
    {
        var form = new WWWForm();
        form.AddField("user_id", (int)userID);
        form.AddField("version", (int)version);
        form.AddField("code", code);
        
        yield return WWWUtility.MD5Request(x =>
        {
            int numRewards = (int)x.ReadUInt32();
            UserReward reward;
            var rewards = new UserReward[numRewards];
            for (int i = 0; i < numRewards; ++i)
            {
                reward.id = x.ReadUInt32();
                reward.name = x.ReadString();
                reward.type = (UserRewardType)x.ReadByte();
                reward.count = (int)x.ReadUInt32();

                rewards[i] = reward;
            }

            callback(rewards);

            return true;
        }, form, _url);
    }

    void Awake()
    {
        IGameData.instance = this;
    }
}
