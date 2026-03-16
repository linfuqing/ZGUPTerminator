using System;
using System.Collections;

public struct UserFriend
{
    public string name;
    public string avatar;
    public uint id;

    public UserFriend(string text, out string[] parameters)
    {
        parameters = text.Split(':');

        name = string.Empty;
        avatar = string.Empty;
        id = uint.Parse(parameters[0]);
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
        public int chapter;
        public UserRole role;
        public UserCard[] cards;
        public UserAccessory[] accessories;
        public UserAccessorySlot[] accessorySlots;
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

    /// <summary>
    /// 查询好友信息
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriend(uint userID, Action<Friend> onComplete);
    
    /// <summary>
    /// 查询
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriends(uint userID, Action<Memory<UserFriend>> onComplete);
    
    /// <summary>
    /// 好友推荐，每次点换一批查询一次
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriendRecommendations(uint userID, Action<Memory<UserFriend>> onComplete);
    
    /// <summary>
    /// 好友消息记录
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryFriendMessages(uint userID, uint targetUserID, Action<Memory<string>> onComplete);
    
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
    IEnumerator FriendRequestApply(uint userID, uint targetUserID, Action<bool> onComplete);
    
    /// <summary>
    /// 同意好友申请
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendRequestAgree(uint userID, uint targetUserID, Action<bool> onComplete);
    
    /// <summary>
    /// 拒绝好友申请
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendRequestDisagree(uint userID, uint targetUserID, Action<bool> onComplete);
    
    /// <summary>
    /// 发送好友消息并记录
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID"></param>
    /// <param name="value"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendMessageSend(uint userID, uint targetUserID, string value, Action<bool> onComplete);
    
    /// <summary>
    /// 删除好友
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="targetUserID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator FriendDelete(uint userID, uint targetUserID, Action<bool> onComplete);
}
