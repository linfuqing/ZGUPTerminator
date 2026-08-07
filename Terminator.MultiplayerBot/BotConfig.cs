using Unity.Collections;
using UnityEngine;

[CreateAssetMenu(fileName = "BotConfig", menuName = "MultiplayerBot/Bot Config")]
public class BotConfig : ScriptableObject
{
    [System.Serializable]
    public struct BotProfile
    {
        public uint userID;
        public string userName;
        public string userAvatar;
        public int power;
        /// <summary>排位段位，对应真人 <c>ClientMessageMatchToSend.level</c> / <c>sourceRank</c>。</summary>
        public int matchLevel;
    }

    [Header("Bot 账号")]
    public BotProfile[] botProfiles;

    [Header("录制假队友（无配置时使用首项）")]
    public uint recordingFakeRemoteUserID;

    [Header("共部署（ReplyServer 同进程）")]
    public bool enableCoDeploy = true;
    public ushort botRelayPort = 1390;

    [Header("ReplyServer（真实服务器测试与录制工具）")]
    public string replyServerAddress = "127.0.0.1";
    public ushort replyServerPort = 1386;

    [Header("世界邀请")]
    public float inviteTimeoutMin = 5f;
    public float inviteTimeoutMax = 10f;

    [Header("匹配（观察到非 Bot 进入 matchIDs 后的派遣等待）")]
    public float matchTimeoutMin = 3f;
    public float matchTimeoutMax = 10f;

    [Header("Remote player offline policy")]
    [Min(0.1f)]
    [Tooltip("Seconds a human squad peer may remain offline before the Bot leaves the squad. Keep this longer than the client's reconnect delay.")]
    public float remoteOfflineLeaveTimeout = 5f;

    [Header("Bot 规模")]
    public int minBots = 1;
    public int maxBots = 32;

    /// <summary>
    /// ClientMessageMatchToSend.level is the client's zero-based sourceRank index.
    /// Zero is valid and must never be replaced with a sentinel such as 100.
    /// </summary>
    public static int ResolveMatchLevel(in BotProfile profile) =>
        profile.matchLevel >= 0 ? profile.matchLevel : 0;

    public bool TryGetProfile(uint userID, out BotProfile profile)
    {
        if (botProfiles != null)
        {
            for (int i = 0; i < botProfiles.Length; ++i)
            {
                if (botProfiles[i].userID == userID)
                {
                    profile = botProfiles[i];
                    return true;
                }
            }
        }

        profile = default;
        return false;
    }

    public BotProfile GetRecordingFakeProfile()
    {
        if (recordingFakeRemoteUserID != 0 &&
            TryGetProfile(recordingFakeRemoteUserID, out var profile))
            return profile;

        if (botProfiles != null && botProfiles.Length > 0)
            return botProfiles[0];

        return new BotProfile
        {
            userID = 999000001,
            userName = "RecordingBot",
            userAvatar = string.Empty,
            power = 0,
            matchLevel = 0
        };
    }

    public ClientHeader ToClientHeader(in BotProfile profile)
    {
        ClientHeader header;
        header.userID = profile.userID;
        header.power = profile.power;
        header.userName = new FixedString32Bytes(profile.userName ?? string.Empty);
        header.userAvatar = new FixedString32Bytes(profile.userAvatar ?? string.Empty);
        return header;
    }

    public LevelPlayerHeader ToLevelPlayerHeader(in BotProfile profile)
    {
        LevelPlayerHeader header;
        header.power = profile.power;
        header.name = new FixedString32Bytes(profile.userName ?? string.Empty);
        header.avatar = new FixedString32Bytes(profile.userAvatar ?? string.Empty);
        return header;
    }
}
