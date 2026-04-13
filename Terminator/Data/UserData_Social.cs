using System;
using System.Collections;

public struct UserFriend
{
    public string name;
    public string avatar;
    public uint id;
    
    /// <summary>
    /// 到达的最大章节数
    /// </summary>
    public int chapter;

    /// <summary>
    /// 战力
    /// </summary>
    public int power;

    /// <summary>
    /// 最后登录时间
    /// </summary>
    public long ticks;

    public UserFriend(string text, out string[] parameters)
    {
        parameters = text.Split(':');

        name = string.Empty;
        avatar = string.Empty;
        id = uint.Parse(parameters[0]);
        chapter = 1;
        power = 0;
        ticks = DateTime.UtcNow.Ticks;
    }

    public override string ToString()
    {
        return id.ToString();
    }
}

public partial interface IUserData
{
    public struct Friend
    {
        /// <summary>
        /// 战力
        /// </summary>
        public int power;
        
        /// <summary>
        /// 排位赛积分
        /// </summary>
        public int rankedPoints;
        
        public UserRole role;
        public UserCard[] cards;
        public UserCardBond[] cardBonds;
        public UserAccessory[] accessories;
        public UserAccessorySlot[] accessorySlots;
        public UserTalent[] talents;
    }
    
    public struct FriendRequest
    {
        public UserFriend friend;
        public string description;

        public FriendRequest(string text)
        {
            friend = new UserFriend(text, out var parameters);
            
            description = parameters[1];
        }

        public override string ToString()
        {
            return $"{friend}:{description}";
        }
    }

    public struct FriendMessage
    {
        public uint userID;
        public string value;

        public FriendMessage(string text)
        {
            var parameters = text.Split(':');
            userID = uint.Parse(parameters[0]);
            value = parameters[1];
            value = value.Substring(1, value.Length - 2);
        }

        public override string ToString()
        {
            return $"{userID}:\"{value}\"";
        }
    }

    /// <summary>
    /// 查询好友信息
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriend(uint userID, uint targetUserID, Action<Friend> onComplete);
    
    /// <summary>
    /// 查询
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriends(uint userID, Action<Memory<UserFriend>> onComplete);

    /// <summary>
    /// 组队邀请列表，客户端根据最近登录的时间来筛选邀请查询，服务器下发可匹配的人。该查询可以查询陌生人
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="levelID"></param>
    /// <param name="stage"></param>
    /// <param name="targetUserIDs"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriendsToSquadInvite(uint userID, uint levelID, int stage, int[] targetUserIDs, Action<Memory<UserFriend>> onComplete);
    
    /// <summary>
    /// 好友推荐，每次点换一批查询一次
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="filter">筛选，有值的时候对名字进行模糊搜索</param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriendRecommendations(uint userID, string filter, Action<Memory<UserFriend>> onComplete);
    
    /// <summary>
    /// 好友消息记录
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriendMessages(uint userID, uint targetUserID, Action<Memory<FriendMessage>> onComplete);
    
    /// <summary>
    /// 好友申请列表
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriendRequests(uint userID, Action<Memory<FriendRequest>> onComplete);
    
    /// <summary>
    /// 好友申请
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendRequestApply(uint userID, uint targetUserID, string description, Action<bool> onComplete);
    
    /// <summary>
    /// 同意好友申请
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserIDs"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendRequestAgree(uint userID, uint[] targetUserIDs, Action<bool> onComplete);
    
    /// <summary>
    /// 拒绝好友申请
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserIDs"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendRequestDisagree(uint userID, uint[] targetUserIDs, Action<bool> onComplete);
    
    /// <summary>
    /// 发送好友消息并记录
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID">聊天目标ID</param>
    /// <param name="senderUserID">发消息的人的ID</param>
    /// <param name="value"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendMessageSend(uint userID, uint targetUserID, uint senderUserID, string value, Action<bool> onComplete);
    
    /// <summary>
    /// 删除好友
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendDelete(uint userID, uint targetUserID, Action<bool> onComplete);

    /// <summary>
    /// 更新自己的信息
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="power"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator UpdatePowerForFriends(uint userID, int power, Action<bool> onComplete);
}
