using Unity.Collections;

public enum ClientChannel
{
    /// <summary>
    /// 世界
    /// </summary>
    Public,
    /// <summary>
    /// 私聊
    /// </summary>
    Private,  
    /// <summary>
    /// 队伍
    /// </summary>
    Squad
}

public enum ClientMessageType
{
    None, 
    /// <summary>
    /// 聊天
    /// </summary>
    Chat, 
    /// <summary>
    /// 组队邀请
    /// </summary>
    SquadInvite, 
    /// <summary>
    /// 加入队伍（先发送申请，要等待Read到之后才正式加入）
    /// </summary>
    SquadJoin, 
    /// <summary>
    /// 离开队伍（不管是主动还是被踢，ReadMessageType之后才生效，本类型没有Message结构体，不需要ReadMessage）
    /// </summary>
    SquadLeave
}

public interface IClientMessageToRead
{
    
}

public interface IClientMessageToSend
{
    ClientMessageType messageType { get; }
}

public struct ClientHeader
{
    public uint userID;
    public FixedString32Bytes userName;
    public FixedString32Bytes userIcon;
}

public struct ClientMessageChat : IClientMessageToRead
{
    public ClientChannel channel;
    public FixedString512Bytes value;
}

public struct ClientMessageChatToSend : IClientMessageToSend
{
    public ClientChannel channel;
    public uint userID;
    public FixedString512Bytes value;
    
    public ClientMessageType messageType => ClientMessageType.Chat;
}

public struct ClientMessageSquadInvite : IClientMessageToRead, IClientMessageToSend
{
    public uint squadInviteID;
    public uint levelID;
    public int stage;
    
    /// <summary>
    /// 邀请描述
    /// </summary>
    public FixedString512Bytes text;
    
    public ClientMessageType messageType => ClientMessageType.SquadInvite;
}

public struct ClientMessageSquadJoin : IClientMessageToRead, IClientMessageToSend
{
    public uint squadInviteID;
    
    public ClientMessageType messageType => ClientMessageType.SquadJoin;
}

public struct ClientMessageSquadLeave : IClientMessageToSend
{
    public ClientMessageType messageType => ClientMessageType.SquadLeave;
}

public interface IClientData
{
    public static IClientData instance;
    
    /// <summary>
    /// int messageType;
    /// while((messageType = IClientData.instance.ReadMessageType(out ClientHeader header)) != 0)
    /// {
    ///     switch((ClientMessageType)messageType)
    ///     {
    ///         case ...
    ///     }
    /// }
    /// </summary>
    /// <returns></returns>
    int ReadMessageType(out ClientHeader header);

    T ReadMessage<T>() where T : unmanaged, IClientMessageToRead;

    void SendMessage<T>(in T message) where T : unmanaged, IClientMessageToSend;
}

