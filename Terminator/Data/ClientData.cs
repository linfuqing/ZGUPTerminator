using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;
using ZG;
using NetworkPipelineStage = ZG.NetworkPipelineStage;

public enum ClientChannel
{
    /// <summary>
    /// 世界
    /// </summary>
    Public = NetworkRelayType.All,
    /// <summary>
    /// 私聊
    /// </summary>
    Private = NetworkRelayType.Identity,  
    /// <summary>
    /// 队伍
    /// </summary>
    Squad = NetworkRelayType.Channel
}

[Flags]
public enum ClientRemotePlayerFlag
{
    Online = NetworkRelayChannelFlag.Online,
    
    Creator = NetworkRelayChannelFlag.Creator, 
    
    Shift = NetworkRelayChannelFlag.ShiftToStatus
}

public enum ClientMessageType
{
    None, 
    
    /// <summary>
    /// 好友或队友状态改变，接收消息为<see cref="ClientMessageRemotePlayerStatus"/>，无发送消息
    /// </summary>
    Status = NetworkRelayMessageType.Status, 
    
    //匹配中，无接收消息，无发送消息
    Matching = NetworkRelayMessageType.Matching, 
    
    /// <summary>
    /// 匹配成功，接收消息为<see cref="ClientMessageMatchToRead"/>，发送消息为<see cref="ClientMessageMatchToSend"/>
    /// </summary>
    Match = NetworkRelayMessageType.Match, 
    
    /// <summary>
    /// 匹配失败&取消匹配，无接收消息，发送消息为<see cref="ClientMessageMismatch"/>
    /// </summary>
    Mismatch = NetworkRelayMessageType.Mismatch, 
    
    /// <summary>
    /// 申请匹配，队长先发送ApplyMatch，队员接收后发送<see cref="ApplyMatch"/>同意、<see cref="RejectMatch"/>拒绝或者<see cref="ApplyMatchFail"/>门票不足，当队员发送同意时，匹配将自动开始。
    /// 无接收消息，发送消息为<see cref="ClientMessageApplyMatch"/>
    /// </summary>
    ApplyMatch = ApplyFriend + 1,

    /// <summary>
    /// 门票不足
    /// 无接收消息，发送消息为<see cref="ClientMessageApplyMatchFail"/>
    /// </summary>
    ApplyMatchFail, 

    /// <summary>
    /// 拒绝匹配
    /// 无接收消息，发送消息为<see cref="ClientMessageRejectMatch"/>
    /// </summary>
    RejectMatch = ApplyMatchFail + 1, 

    /// <summary>
    /// 聊天，接收消息为<see cref="ClientMessageChatToRead"/>，发送消息为<see cref="ClientMessageChatToSend"/>
    /// </summary>
    Chat = ReplyMessageType.Chat, 

    /// <summary>
    /// 队伍已满，加入失败，无接收消息，无发送消息
    /// </summary>
    SquadJoinFail = NetworkRelayMessageType.JoinFailed, 

    /// <summary>
    /// 加入队伍（先发送申请，要等待Read到之后才正式加入），接收消息为<see cref="ClientMessageSquadJoinToRead"/>，发送消息为<see cref="ClientMessageSquadJoinToSend"/>
    /// </summary>
    SquadJoin = NetworkRelayMessageType.Join, 
    /// <summary>
    /// 离开队伍（不管是主动还是被踢，ReadMessageType之后才生效），无接收消息，发送消息为<see cref="ClientMessageSquadLeave"/>
    /// </summary>
    SquadLeave = NetworkRelayMessageType.Leave, 
    
    /// <summary>
    /// 踢人&被踢，无接收消息，发送消息为<see cref="ClientMessageSquadDrop"/>
    /// </summary>
    SquadDrop = NetworkRelayMessageType.Drop, 
    
    /// <summary>
    /// 组队邀请，接收消息为<see cref="ClientMessageSquadInviteToRead"/>，发送消息为<see cref="ClientMessageSquadInviteToSend"/>
    /// </summary>
    SquadInvite = NetworkRelayMessageType.Query + 1, 
    
    /// <summary>
    /// 申请好友，接收消息为<see cref="ClientMessageApplyFriendToRead"/>，发送消息为<see cref="ClientMessageApplyFriendToSend"/>
    /// </summary>
    ApplyFriend, 
    
    /// <summary>
    /// 添加好友，先通过<see cref="ApplyFriend"/>接收到好友申请，同意添加时发送添加。添加成功后会收到接收消息。注意：这个消息可能会重复接收到，根据ID判断是不是同一个好友，并以短链接服务器为主。
    /// 接收消息为<see cref="ClientMessageRemotePlayerStatus"/>，发送消息为<see cref="ClientMessageAddFriend"/>
    /// </summary>
    AddFriend = NetworkRelayMessageType.Add, 
    
    /// <summary>
    /// 删除好友，无接收消息，发送消息为<see cref="ClientMessageRemoveFriend"/>
    /// </summary>
    RemoveFriend = NetworkRelayMessageType.Remove, 

    /// <summary>
    /// 切换到对应页面，用来同步页面切换（排位赛或者战斗等）
    /// 接收消息，接收和发送消息为<see cref="ClientMessagePage"/>
    /// </summary>
    Page = RejectMatch + 1, 
    
    ChapterStage, 
    Play, 
    Cancel, 
    Error
}

public interface IClientMessageToRead
{
    
}

public interface IClientMessageToSend
{
    ClientMessageType messageType { get; }
}

public struct ClientHeader : IEquatable<ClientHeader>
{
    public uint userID;
    public int power;
    public FixedString32Bytes userName;
    public FixedString32Bytes userAvatar;

    public ClientHeader(ref DataStreamReader reader, StreamCompressionModel streamCompressionModel)
    {
        userID = reader.ReadPackedUInt(streamCompressionModel);
        var bytes = new FixedBytes80(ref reader);
        var header = new LevelPlayerHeader(bytes);
        power = header.power;
        userName = header.name;
        userAvatar = header.avatar;
    }
    
    public void Write(ref DataStreamWriter writer, StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedUInt(userID, streamCompressionModel);
        LevelPlayerHeader header;
        header.power = power;
        header.name = userName;
        header.avatar = userAvatar;
        header.Write(ref writer);
    }
    
    public void Write(
        ref DataStreamWriter writer, 
        StreamCompressionModel streamCompressionModel, 
        int messageType, 
        NetworkRelayType relayType)
    {
        writer.WriteReplyHeader(messageType, relayType);
        Write(ref writer, streamCompressionModel);
    }

    public bool Equals(ClientHeader other)
    {
        return userID == other.userID && userName == other.userName && userAvatar == other.userAvatar;
    }
}

public struct ClientMessageRemotePlayerStatus : IClientMessageToRead
{
    public ClientRemotePlayerFlag flag;

    /// <summary>
    /// 在线
    /// </summary>
    public bool isOnline => (flag & ClientRemotePlayerFlag.Online) == ClientRemotePlayerFlag.Online;
    
    /// <summary>
    /// 队长
    /// </summary>
    public bool isCreator => (flag & ClientRemotePlayerFlag.Creator) == ClientRemotePlayerFlag.Creator;

    /// <summary>
    /// 在游戏中
    /// </summary>
    public bool isInGame => ((int)flag >> (int)ClientRemotePlayerFlag.Shift) != 0;
}

public struct ClientMessageAddFriend : IClientMessageToSend
{
    public uint userID;

    public ClientMessageType messageType => ClientMessageType.AddFriend;
}

public struct ClientMessageRemoveFriend : IClientMessageToSend
{
    public uint userID;
    
    public ClientMessageType messageType => ClientMessageType.RemoveFriend;
}

public struct ClientMessageApplyFriendToRead : IClientMessageToRead
{
    /// <summary>
    /// 申请说明
    /// </summary>
    public FixedString512Bytes text;
}

public struct ClientMessageApplyFriendToSend : IClientMessageToSend
{
    public uint userID;
    
    /// <summary>
    /// 申请说明
    /// </summary>
    public FixedString512Bytes text;

    public ClientMessageType messageType => ClientMessageType.ApplyFriend;
}

public struct ClientMessageSquadJoinToRead : IClientMessageToRead
{
    public ClientMessageRemotePlayerStatus playerStatus;
    
    public uint squadInviteID;
}

public struct ClientMessageSquadJoinToSend : IClientMessageToSend
{
    public uint squadInviteID;
    
    public ClientMessageType messageType => ClientMessageType.SquadJoin;
    
    public override string ToString()
    {
        return $"ClientMessageSquadJoin({squadInviteID})";
    }
}

public struct ClientMessageSquadLeave : IClientMessageToSend
{
    public ClientMessageType messageType => ClientMessageType.SquadLeave;
}

public struct ClientMessageSquadDrop : IClientMessageToSend
{
    public uint userID;
    
    public ClientMessageType messageType => ClientMessageType.SquadDrop;
}

public struct ClientMessageSquadInviteToRead : IClientMessageToRead
{
    public ClientChannel channel;
    public uint squadInviteID;
    public uint levelID;
    public int stage;
    
    /// <summary>
    /// 邀请描述
    /// </summary>
    public FixedString512Bytes text;

    public override string ToString()
    {
        return $"ClientMessageSquadInviteToRead({squadInviteID}:{levelID}:{stage}:{text})";
    }
}

public struct ClientMessageSquadInviteToSend : IClientMessageToSend
{
    /// <summary>
    /// 当userID为0时发送到世界，否则发给对应用户
    /// </summary>
    public uint userID;
    public uint levelID;
    public int stage;
    
    /// <summary>
    /// 邀请描述
    /// </summary>
    public FixedString512Bytes text;
    
    public ClientMessageType messageType => ClientMessageType.SquadInvite;
}

public struct ClientMessageMatchToRead : IClientMessageToRead
{
    /// <summary>
    /// 对应<see cref="IUserData.LevelTicket.levelNames"/>的索引
    /// </summary>
    public int level;
    
    /// <summary>
    /// 匹配ID， 用这个来做随机种子进入相同的场景
    /// </summary>
    public int matchID;

    public int GetSceneIndex(int sceneCount)
    {
        return matchID % sceneCount;
        /*UnityEngine.Random.InitState(matchID);

        return UnityEngine.Random.Range(0, sceneCount);*/
    }
}

public struct ClientMessageMatchToSend : IClientMessageToSend
{
    /// <summary>
    /// 段位
    /// </summary>
    public int level;

    public ClientMessageType messageType => ClientMessageType.Match;
}

public struct ClientMessageMismatch : IClientMessageToSend
{
    public ClientMessageType messageType => ClientMessageType.Mismatch;
}

public struct ClientMessageApplyMatch : IClientMessageToSend
{
    public ClientMessageType messageType => ClientMessageType.ApplyMatch;
}

public struct ClientMessageApplyMatchFail : IClientMessageToSend
{
    public ClientMessageType messageType => ClientMessageType.ApplyMatchFail;
}

public struct ClientMessageRejectMatch : IClientMessageToSend
{
    public ClientMessageType messageType => ClientMessageType.RejectMatch;
}

public struct ClientMessageChatToRead : IClientMessageToRead
{
    public ClientChannel channel;
    public FixedString512Bytes value;
    
    public override string ToString()
    {
        return $"ClientMessageChatToRead({channel}:{value})";
    }
}

public struct ClientMessageChatToSend : IClientMessageToSend
{
    public ClientChannel channel;
    public uint userID;
    public FixedString512Bytes value;
    
    public ClientMessageType messageType => ClientMessageType.Chat;
}

public struct ClientMessagePage : IClientMessageToRead, IClientMessageToSend
{
    public int value;
    
    public ClientMessageType messageType => ClientMessageType.Page;
}

public struct ClientMessageChapterStage : IClientMessageToSend
{
    public uint userStageID;

    public static int capacity => UnsafeUtility.SizeOf<ClientMessageChapterStage>();

    public ClientMessageType messageType => ClientMessageType.ChapterStage;
    
    public ClientMessageChapterStage(ref DataStreamReader reader, StreamCompressionModel streamCompressionModel)
    {
        userStageID = reader.ReadPackedUInt(streamCompressionModel);
    }

    public void Write(ref DataStreamWriter writer, StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedUInt(userStageID, streamCompressionModel);
    }
}

public struct ClientMessagePlayerProperty
{
    public LevelPlayerProperty value;
    
    public static ClientMessageType messageType => (ClientMessageType)ReplyMessageType.PlayerProperty;
    
    public static int capacity => UnsafeUtility.SizeOf<ClientMessagePlayerProperty>();

    public ClientMessagePlayerProperty(ref DataStreamReader reader)
    {
        value = new LevelPlayerProperty(ref reader, StreamCompressionModel.Default);
    }

    public void Write(ref DataStreamWriter writer)
    {
        value.Write(ref writer, StreamCompressionModel.Default);
    }
}

public struct ClientMessagePlay
{
    public bool isRestart;
    public uint levelID;
    public int stage;
    public FixedString32Bytes levelName;
    public FixedString32Bytes sceneName;
    
    public static ClientMessageType messageType => ClientMessageType.Play;

    public static int capacity => UnsafeUtility.SizeOf<ClientMessagePlay>();

    public ClientMessagePlay(ref DataStreamReader reader)
    {
        var streamCompressionModel = StreamCompressionModel.Default;
        isRestart = reader.ReadRawBits(1) == 1;
        levelID = reader.ReadPackedUInt(streamCompressionModel);
        stage = reader.ReadPackedInt(streamCompressionModel);
        levelName = reader.ReadFixedString32();
        sceneName = reader.ReadFixedString32();
    }

    public void Write(ref DataStreamWriter writer)
    {
        var streamCompressionModel = StreamCompressionModel.Default;
        writer.WriteRawBits(isRestart ? 1u : 0, 1);
        writer.WritePackedUInt(levelID, streamCompressionModel);
        writer.WritePackedInt(stage, streamCompressionModel);
        writer.WriteFixedString32(levelName);
        writer.WriteFixedString32(sceneName);
    }

    public void Apply()
    {
        if (LevelShared.match != 0)
            return;
        
        LoginManager.instance.ApplyStart(isRestart, levelID, stage, levelName.ToString(), sceneName.ToString());
    }
}

public interface IClientData
{
    public static IClientData instance;
    
    ClientHeader header { get; }

    void SetHeaderOverride(in FixedString32Bytes userName, in FixedString32Bytes userAvatar, int power = 0);
    
    void Connect(in ClientHeader header, string address, ushort port);
    
    void SetStatus(int value);
    
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
    
    DataStreamWriter BeginSend(ClientMessageType type, int capacity);
    
    void EndSend(in DataStreamWriter writer);
}

public class ClientData : MonoBehaviour, IClientData
{
    /*public enum SquadInviteStatus
    {
        None,
        SquadCreating, 
        SquadInviting, 
        SquadInvited
    }*/

    private enum InitStatus
    {
        None, 
        Remote, 
        Local, 
        Friends,
        Complete
    }

    [SerializeField]
    internal int _connectTimeoutMS = 1000;
    [SerializeField]
    internal int _maxConnectAttempts = 60;
    [SerializeField]
    internal int _disconnectTimeoutMS = 30 * 1000;
    [SerializeField]
    internal int _heartbeatTimeoutMS = 500;
    [SerializeField]
    internal int _reconnectionTimeoutMS = 2000;
    //[SerializeField]
    //internal int _maxFrameTimeMS = 0;
    [SerializeField]
    internal int _fixedFrameTimeMS = 0;
    [SerializeField]
    internal int _receiveQueueCapacity = 4096;//ReceiveQueueCapacity;
    [SerializeField]
    internal int _sendQueueCapacity = 4096;// SendQueueCapacity;
    [SerializeField]
    internal string _address = "127.0.0.1";
    [SerializeField]
    internal ushort _port = 1386;
    [SerializeField] 
    internal NetworkPipelineStage[] _stages = new NetworkPipelineStage[]
    {
        NetworkPipelineStage.Fragmentation,
        NetworkPipelineStage.ReliableSequenced,
    };

    private InitStatus __initStatus;
    private bool __waitingForSend;
    private int __frameCount;
    private int __pipelineIndex;
    private int __messageIndex;
    private ClientHeader __remoteHeader;
    private ClientMessageSquadInviteToSend __squadInviteMessage;
    private NativeList<NetworkClient.Message> __messages;
    private NativeList<byte> __bytes;
    
    private static NativeHashMap<uint, ClientRemotePlayerFlag> __friends;
    //private static NativeHashSet<uint> __friendIDs;
    private static Entity __entity;
    private static string __address;
    private static ushort __port;

    public bool isMatching
    {
        get;

        private set;
    }

    public NetworkClientDriver driver
    {
        get
        {
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            if (__entity == Entity.Null)
            {
                __entity = entityManager.CreateSingleton<NetworkClientDriver>();

                var driver = new NetworkClientDriver(
                    Allocator.Persistent, 
                    _connectTimeoutMS,
                    _maxConnectAttempts,
                    _disconnectTimeoutMS,
                    _heartbeatTimeoutMS, 
                    _reconnectionTimeoutMS,
                    Mathf.CeilToInt(Time.maximumDeltaTime * 1000), //_maxFrameTimeMS, 
                    _fixedFrameTimeMS, 
                    _receiveQueueCapacity, 
                    _sendQueueCapacity);

                using (var stages = new NativeArray<NetworkPipelineStage>(_stages, Allocator.Temp))
                    __pipelineIndex = driver.CreatePipeline(stages);

                entityManager.SetComponentData(__entity, driver);

                return driver;
            }

            return entityManager.GetComponentData<NetworkClientDriver>(__entity);
        }
    }

    public ClientHeader header
    {
        get
        {
            ClientHeader header;
            header.userID = LevelPlayerShared<LocalPlayer>.id;
            
            var levelPlayerHeader = LevelPlayerShared<LocalPlayer>.header;
            header.power = levelPlayerHeader.power;
            header.userName = levelPlayerHeader.name;
            header.userAvatar = levelPlayerHeader.avatar;

            return header;
        }
    }
    
    public void Connect(in ClientHeader header, string address, ushort port)
    {
        //address = "192.168.1.168";
        //port = 1386;
        
        LevelPlayerShared<LocalPlayer>.id = header.userID;

        LevelPlayerHeader levelPlayerHeader;
        levelPlayerHeader.power = header.power;
        levelPlayerHeader.name = header.userName;
        levelPlayerHeader.avatar = header.userAvatar;
        LevelPlayerShared<LocalPlayer>.header = levelPlayerHeader;

        __initStatus = InitStatus.None;
        
        //var driver = this.driver.instance;
        bool isChanged = false;

        __address ??= _address;
        
        if(__port == 0)
            __port = _port;
        
        if (!string.IsNullOrEmpty(address) && address != __address)
        {
            __address = address;
            
            isChanged = true;
        }
        
        if (port != 0 && port != __port)
        {
            __port = port;
            
            isChanged = true;
        }
        
        if (!__isConnected || isChanged)
            __Connect();
        
        SetStatus(0);
    }

    public void SetHeaderOverride(in FixedString32Bytes userName, in FixedString32Bytes userAvatar, int power = 0)
    {
        ref var levelPlayerHeader = ref LevelPlayerShared<LocalPlayer>.header;
        
        levelPlayerHeader.name = userName;
        levelPlayerHeader.avatar = userAvatar;
        levelPlayerHeader.power = power;
    }

    public void SetStatus(int value)
    {
        if (!ReplyMessageShared.SetChannelStatus(value))
            return;

        if (value == 0)
            LevelShared.match = 0;

        __SendStatus();
    }

    public int ReadMessageType(out ClientHeader header)
    {
        switch (__initStatus++)
        {
            case InitStatus.None:
                if (ReplyMessageShared.remotePlayerCount > 0)
                {
                    header.userID = LevelPlayerShared<RemotePlayer>.id;

                    var levelPlayerHeader = LevelPlayerShared<RemotePlayer>.header;

                    header.power = levelPlayerHeader.power;
                    header.userName = levelPlayerHeader.name;
                    header.userAvatar = levelPlayerHeader.avatar;

                    ClientMessageSquadJoinToRead message;
                    message.playerStatus.flag = (ClientRemotePlayerFlag)LevelPlayerShared<RemotePlayer>.channelFlag;
                    message.squadInviteID = (uint)ReplyMessageShared.channel;

                    __Save(message);

                    return (int)ClientMessageType.SquadJoin;
                }

                break;
            case InitStatus.Remote:
                if (ReplyMessageShared.CHANNEL_NULL != ReplyMessageShared.channel)
                {
                    header = this.header;
                    
                    ClientMessageSquadJoinToRead message;
                    message.playerStatus.flag = ClientRemotePlayerFlag.Online;
                    if(ReplyMessageShared.isHost)
                        message.playerStatus.flag |= ClientRemotePlayerFlag.Creator;
                    
                    message.squadInviteID = (uint)ReplyMessageShared.channel;

                    __Save(message);

                    return (int)ClientMessageType.SquadJoin;
                }

                break;
            case InitStatus.Friends:
                int friendIndex = __initStatus - InitStatus.Friends, friendCount = __friends.IsCreated ? __friends.Count : 0;
                if (friendCount > friendIndex)
                {
                    foreach (var friend in __friends)
                    {
                        if (--friendIndex < 0)
                        {
                            header.userID = friend.Key;
                            header.userName = default;
                            header.userAvatar = default;
                            header.power = 0;

                            ClientMessageRemotePlayerStatus message;
                            message.flag = friend.Value;

                            __Save(message);

                            return (int)ClientMessageType.AddFriend;
                        }
                    }
                }
                else
                    __initStatus = friendCount + InitStatus.Friends;
                
                break;
        }

        var driver = this.driver;
        var instance = driver.instance;
        int frameCount = Time.frameCount;
        if (frameCount != __frameCount)
        {
            __frameCount = frameCount;
            
            //__messageIterator.Dispose();
            __messageIndex = 0;

            if(__messages.IsCreated)
                __messages.Clear();
            else
                __messages = new NativeList<NetworkClient.Message>(Allocator.Persistent);
            
            foreach (var message in instance.AsMessages())
                __messages.Add(message.Message);
            
            __messages.Sort();
        }
        
        int numMessages = __messages.IsCreated ? __messages.Length : 0;

        NetworkClient.MessageElement messageElement;
        while(numMessages > __messageIndex)
        {
            messageElement = new NetworkClient.MessageElement(__messages[__messageIndex++], instance.buffer.AsArray());
            switch (messageElement.Message.type)
            {
                case NetworkClientMessageType.Data:
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    var reader = messageElement.reader;
                    int type = reader.ReadPackedInt(streamCompressionModel);
                    print((NetworkRelayMessageType)type);
                    switch ((NetworkRelayMessageType)type)
                    {
                        case NetworkRelayMessageType.Disconnect:
                        case NetworkRelayMessageType.Connect:
                        case NetworkRelayMessageType.Status:
                        case NetworkRelayMessageType.Add:
                        case NetworkRelayMessageType.Remove:
                        {
                            int channelFlag;
                            switch (type)
                            {
                                case (int)NetworkRelayMessageType.Disconnect:
                                case (int)NetworkRelayMessageType.Remove:
                                    channelFlag = 0;
                                    break;
                                default:
                                    channelFlag = reader.ReadPackedInt(streamCompressionModel);
                                    break;
                            }
                            
                            header.userID = reader.ReadPackedUInt(streamCompressionModel);
                            if (header.userID == LevelPlayerShared<RemotePlayer>.id)
                            {
                                var levelPlayerHeader = LevelPlayerShared<RemotePlayer>.header;

                                header.power = levelPlayerHeader.power;
                                header.userName = levelPlayerHeader.name;
                                header.userAvatar = levelPlayerHeader.avatar;

                                switch (type)
                                {
                                    case (int)NetworkRelayMessageType.Connect:
                                        if(ReplyMessageShared.isHost)
                                            LoginManager.instance?.SendChapterStageMessage();
                                        break;
                                    case (int)NetworkRelayMessageType.Disconnect:
                                    case (int)NetworkRelayMessageType.Remove:
                                        channelFlag = LevelPlayerShared<RemotePlayer>.channelFlag &
                                                      (int)~NetworkRelayChannelFlag.Online;
                                        break;
                                }
                            }
                            else
                            {
                                header.power = 0;
                                header.userName = default;
                                header.userAvatar = default;
                            }

                            ClientMessageRemotePlayerStatus message;
                            message.flag = (ClientRemotePlayerFlag)channelFlag;

                            __Save(message);

                            switch ((NetworkRelayMessageType)type)
                            {
                                case NetworkRelayMessageType.Add:
                                    if (!__friends.IsCreated)
                                        __friends = new NativeHashMap<uint, ClientRemotePlayerFlag>(1,
                                            Allocator.Persistent);

                                    __friends.Add(header.userID, message.flag);
                                    return type;
                                case NetworkRelayMessageType.Remove:
                                    if(__friends.IsCreated)
                                        __friends.Remove(header.userID);
                                    
                                    return type;
                                default:
                                    if (__friends.IsCreated && __friends.ContainsKey(header.userID))
                                        __friends[header.userID] = message.flag;
                                    
                                    return (int)ClientMessageType.Status;
                            }
                        }
                        case NetworkRelayMessageType.Matching:
                        case NetworkRelayMessageType.Mismatch:
                            isMatching = type == (int)NetworkRelayMessageType.Matching;

                            LevelShared.match = 0;

                            header = this.header;
                            return type;
                        case NetworkRelayMessageType.Match:
                        {
                            header = this.header;
                            int match = reader.ReadPackedInt(streamCompressionModel), distance = reader.ReadPackedInt(streamCompressionModel);
                            ClientMessageMatchToRead message;
                            message.matchID = match;
                            message.level = distance;
                            __Save(message);

                            LevelShared.match = match;
                            
                            print($"[ClientData]Match {match}, distance {distance}");
                        }
                            return (int)ClientMessageType.Match;
                        case NetworkRelayMessageType.Create:
                        case NetworkRelayMessageType.Join:
                        case NetworkRelayMessageType.Leave:
                        case NetworkRelayMessageType.Drop:
                        {
                            int channel = reader.ReadPackedInt(streamCompressionModel);
                            var channelFlag = reader.ReadPackedInt(streamCompressionModel);

                            if (reader.GetBytesRead() < reader.Length)
                            {
                                reader.Flush();
                                header = new ClientHeader(ref reader, streamCompressionModel);
                                
                                UnityEngine.Assertions.Assert.AreNotEqual(this.header.userID, header.userID);

                                switch (type)
                                {
                                    case (int)NetworkRelayMessageType.Leave:
                                    case (int)NetworkRelayMessageType.Drop:
                                        if (header.userID == __remoteHeader.userID)
                                        {
                                            var loginManager = LoginManager.instance;
                                            if(loginManager != null && loginManager.status == LoginManager.Status.None)
                                                LevelShared.match = 0;

                                            __remoteHeader = default;
                                        }

                                        break;
                                    default:
                                        __remoteHeader = header;
                                    
                                        if (ReplyMessageShared.isHost)
                                            LoginManager.instance?.SendChapterStageMessage();
                                        break;
                                }
                            }
                            else
                            {
                                header = this.header;

                                var sendBuffer = driver.sendBuffer;
                                switch ((NetworkRelayMessageType)type)
                                {
                                    case NetworkRelayMessageType.Create:
                                        if (__waitingForSend && sendBuffer.BeginWrite(__pipelineIndex, out var writer))
                                        {
                                            __WriteSquadInvite(ref writer, streamCompressionModel, channel);
                                            
                                            sendBuffer.EndWrite(writer);

                                            __waitingForSend = false;
                                        }

                                        if ((channelFlag >> (int)NetworkRelayChannelFlag.ShiftToStatus) != 0)
                                        {
                                            if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                                            {
                                                writer.WritePackedInt((int)NetworkRelayMessageType.Status, streamCompressionModel);
                                                writer.WritePackedInt(0, streamCompressionModel);
                                                sendBuffer.EndWrite(writer);
                                            }
                                        }
                                        break;
                                    case NetworkRelayMessageType.Join:
                                        if ((channelFlag >> (int)NetworkRelayChannelFlag.ShiftToStatus) != 0)
                                        {
                                            if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                                            {
                                                writer.WritePackedInt((int)NetworkRelayMessageType.Status, streamCompressionModel);
                                                writer.WritePackedInt(0, streamCompressionModel);
                                                sendBuffer.EndWrite(writer);
                                            }
                                        }
                                        break;
                                }
                            }

                            switch ((NetworkRelayMessageType)type)
                            {
                                case NetworkRelayMessageType.Leave:
                                    return (int)ClientMessageType.SquadLeave;
                                case NetworkRelayMessageType.Drop:
                                    return (int)ClientMessageType.SquadDrop;
                            }
                            
                            ClientMessageSquadJoinToRead message;
                            message.playerStatus.flag = (ClientRemotePlayerFlag)channelFlag;
                            message.squadInviteID = (uint)channel;

                            __Save(message);
                            
                            return (int)ClientMessageType.SquadJoin;
                        }
                        case NetworkRelayMessageType.JoinFailed:
                            header = this.header;
                            return (int)ClientMessageType.SquadJoinFail;
                        case NetworkRelayMessageType.Query:
                            break;
                        default:
                        {
                            ClientChannel channel = ClientChannel.Private;
                            reader.ReadReplyHeader(out NetworkRelayType relayType, out uint id);
                            switch (relayType)
                            {
                                case NetworkRelayType.All:
                                    channel = ClientChannel.Public;
                                    break;
                                case NetworkRelayType.Channel:
                                    channel = ClientChannel.Squad;
                                    break;
                                default:
                                    //UnityEngine.Assertions.Assert.AreEqual(LevelPlayerShared<LocalPlayer>.id, relayType.RelayID());
                                    break;
                            }
                            
                            switch ((ClientMessageType)type)
                            {
                                case ClientMessageType.SquadInvite:
                                {
                                    //UnityEngine.Assertions.Assert.AreEqual(ClientChannel.Public, channel);
                                    
                                    header = new ClientHeader(ref reader, streamCompressionModel);

                                    ClientMessageSquadInviteToRead message;
                                    message.channel = channel;
                                    message.squadInviteID = (uint)reader.ReadPackedInt(streamCompressionModel);
                                    message.levelID = reader.ReadPackedUInt(streamCompressionModel);
                                    message.stage = reader.ReadPackedInt(streamCompressionModel);
                                    message.text = reader.ReadFixedString512();
                                    __Save(message);
                                    return (int)ClientMessageType.SquadInvite;
                                }
                                case ClientMessageType.Chat:
                                {
                                    header = new ClientHeader(ref reader, streamCompressionModel);

                                    ClientMessageChatToRead message;
                                    message.channel = channel;
                                    message.value = reader.ReadFixedString512();
                                    __Save(message);
                                    return (int)ClientMessageType.Chat;
                                }
                                case ClientMessageType.ApplyFriend:
                                {
                                    header = new ClientHeader(ref reader, streamCompressionModel);

                                    ClientMessageApplyFriendToRead message;
                                    message.text = reader.ReadFixedString512();
                                    __Save(message);
                                    return (int)ClientMessageType.ApplyFriend;
                                }
                                case ClientMessageType.ApplyMatch:
                                    header = this.header;
                                    return (int)ClientMessageType.ApplyMatch;
                                case ClientMessageType.ApplyMatchFail:
                                    header = this.header;
                                    return (int)ClientMessageType.ApplyMatchFail;
                                case ClientMessageType.RejectMatch:
                                    header = this.header;
                                    
                                    return (int)ClientMessageType.RejectMatch;
                                case ClientMessageType.Page:
                                    header = this.header;
                                    
                                    ClientMessagePage page;
                                    page.value = reader.ReadPackedInt(streamCompressionModel);
                                    __Save(page);
                                    return (int)ClientMessageType.Page;
                                case ClientMessageType.ChapterStage:
                                    LoginManager.instance?.MoveTo(new ClientMessageChapterStage(ref reader, streamCompressionModel).userStageID);
                                    break;
                                case ClientMessageType.Play:
                                    new ClientMessagePlay(ref reader).Apply();
                                    break;
                                case ClientMessageType.Cancel:
                                    //RemotePlayer.status = RemotePlayer.Status.Error;
                                    LoginManager.instance?.CancelRemotePlayer();
                                    break;
                                case ClientMessageType.Error:
                                    //RemotePlayer.status = RemotePlayer.Status.Error;
                                    LoginManager.instance?._onError?.Invoke();
                                    break;
                            }

                            break;
                        }
                    }
                }
                    break;
                case NetworkClientMessageType.Connect:
                {
                    __SendStatus();
                    /*if (SquadInviteStatus.None != squadInviteStatus)
                    {
                        var sendBuffer = driver.sendBuffer;
                        if (sendBuffer.BeginWrite(__pipelineIndex, out var writer))
                        {
                            var streamCompressionModel = StreamCompressionModel.Default;
                            writer.WritePackedInt((int)NetworkRelayMessageType.Create, streamCompressionModel);
                            sendBuffer.EndWrite(writer);
                        }
                    }*/

                    break;
                }
                case NetworkClientMessageType.Disconnect:
                    if(__friends.IsCreated)
                        __friends.Clear();
                    
                    if (__remoteHeader.userID != 0)
                    {
                        var loginManager = LoginManager.instance;
                        if(loginManager != null && loginManager.status == LoginManager.Status.None)
                            LevelShared.match = 0;

                        header = __remoteHeader;

                        __remoteHeader = default;

                        return (int)ClientMessageType.SquadLeave;
                    }

                    if (isMatching)
                    {
                        isMatching = false;

                        header = this.header;

                        return (int)ClientMessageType.Mismatch;
                    }
                    
                    break;
            }
        }

        header = default;
        return (int)ClientMessageType.None;
    }

    public T ReadMessage<T>() where T : unmanaged, IClientMessageToRead
    {
        return __bytes.AsArray().Reinterpret<T>(1)[0];
    }

    public void SendMessage<T>(in T message) where T : unmanaged, IClientMessageToSend
    {
        __Save(message);

        __Send(message.messageType);
    }

    public DataStreamWriter BeginSend(ClientMessageType type, int capacity)
    {
        if(!__bytes.IsCreated)
            __bytes = new NativeList<byte>(Allocator.Persistent);

        __bytes.ResizeUninitialized(capacity);

        var writer = new DataStreamWriter(__bytes.AsArray());
        writer.m_SendHandleData = (IntPtr)type;
        
        return writer;
    }

    public void EndSend(in DataStreamWriter writer)
    {
        __bytes.ResizeUninitialized(writer.Length);

        __Send((ClientMessageType)writer.m_SendHandleData);
    }

    private void __Send(ClientMessageType type)
    {
        DataStreamWriter writer;
        var sendBuffer = this.driver.sendBuffer;
        switch (type)
        {
            case (ClientMessageType)NetworkRelayMessageType.Status:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)type, streamCompressionModel);
                    writer.WritePackedInt(new DataStreamReader(__bytes.AsArray()).ReadPackedInt(streamCompressionModel), streamCompressionModel);

                    sendBuffer.EndWrite(writer);
                }
                break;
            case ClientMessageType.ApplyFriend:
            {
                var temp = __Load<ClientMessageApplyFriendToSend>();
                if ((!__friends.IsCreated || !__friends.ContainsKey(temp.userID)) &&
                    sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    this.header.Write(
                        ref writer,
                        StreamCompressionModel.Default,
                        (int)ClientMessageType.ApplyFriend, 
                        temp.userID.RelayType());

                    writer.WriteFixedString512(temp.text);

                    sendBuffer.EndWrite(writer);
                }

                break;
            }
            case ClientMessageType.AddFriend:
            {
                var temp = __Load<ClientMessageAddFriend>();
                if ((!__friends.IsCreated || !__friends.ContainsKey(temp.userID)) && 
                    sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Add, streamCompressionModel);
                    writer.WritePackedUInt(temp.userID, streamCompressionModel);
                    sendBuffer.EndWrite(writer);
                }

                break;
            }
            case ClientMessageType.RemoveFriend:
            {
                var temp = __Load<ClientMessageRemoveFriend>();
                if (__friends.IsCreated && __friends.ContainsKey(temp.userID) && 
                    sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Remove, streamCompressionModel);
                    writer.WritePackedUInt(temp.userID, streamCompressionModel);
                    sendBuffer.EndWrite(writer);
                }

                break;
            }
            case ClientMessageType.SquadJoin:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Join, streamCompressionModel);
                    writer.WritePackedInt((int)__Load<ClientMessageSquadJoinToSend>().squadInviteID, streamCompressionModel);
                    sendBuffer.EndWrite(writer);
                }
                break;
            case ClientMessageType.SquadLeave:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Leave, streamCompressionModel);
                    sendBuffer.EndWrite(writer);
                }
                break;
            case ClientMessageType.SquadDrop:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Drop, streamCompressionModel);
                    writer.WritePackedUInt(__Load<ClientMessageSquadDrop>().userID, streamCompressionModel);
                    sendBuffer.EndWrite(writer);
                }
                break;
            case ClientMessageType.SquadInvite:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    __squadInviteMessage = __Load<ClientMessageSquadInviteToSend>();

                    var streamCompressionModel = StreamCompressionModel.Default;
                    if (ReplyMessageShared.isHost)
                        __WriteSquadInvite(ref writer, streamCompressionModel, ReplyMessageShared.channel);
                    else
                    {
                        __waitingForSend = true;
                        
                        writer.WritePackedInt((int)NetworkRelayMessageType.Create, streamCompressionModel);
                        writer.WritePackedInt(2, streamCompressionModel);
                    }

                    //squadInviteStatus = SquadInviteStatus.SquadCreating;
                    sendBuffer.EndWrite(writer);
                }
                break;
            case ClientMessageType.Match:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var message = __Load<ClientMessageMatchToSend>();
                    NetworkRelayMatch match;
                    match.playerCount = 2;
                    match.distance = message.level;
                    match.distanceTime = 3.0f;

                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Match, streamCompressionModel);
                    match.Write(ref writer, streamCompressionModel);

                    //squadInviteStatus = SquadInviteStatus.SquadCreating;
                    sendBuffer.EndWrite(writer);
                }
                break;
            case ClientMessageType.Mismatch:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Mismatch, streamCompressionModel);
                    sendBuffer.EndWrite(writer);
                }

                break;
            case ClientMessageType.Chat:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var temp = __Load<ClientMessageChatToSend>();
                    this.header.Write(
                        ref writer, 
                        StreamCompressionModel.Default, 
                        (int)ClientMessageType.Chat, ClientChannel.Private == temp.channel ? temp.userID.RelayType() : (NetworkRelayType)temp.channel);

                    writer.WriteFixedString512(temp.value);
                    
                    sendBuffer.EndWrite(writer);
                }
                break;
            case ClientMessageType.Page:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    writer.WriteReplyHeader((int)type, NetworkRelayType.Channel);
                    
                    var temp = __Load<ClientMessagePage>();
                    writer.WritePackedInt(temp.value, StreamCompressionModel.Default);
                    
                    sendBuffer.EndWrite(writer);
                }
                break;
            default:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    writer.WriteReplyHeader((int)type, NetworkRelayType.Channel);
                    
                    writer.WriteBytes(__bytes.AsArray());
                    
                    sendBuffer.EndWrite(writer);
                }
                break;
            /*case ClientMessageType.Play:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    writer.WriteReplyHeader((int)ClientMessageType.Play, NetworkRelayType.Channel);
                    
                    var reader = new DataStreamReader(__bytes.AsArray());
                    var temp = new ClientMessagePlay(ref reader);
                    temp.Write(ref writer);
                    
                    sendBuffer.EndWrite(writer);
                }
                break;
            case (ClientMessageType)ReplyMessageType.PlayerProperty:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    writer.WriteReplyHeader((int)ReplyMessageType.PlayerProperty, NetworkRelayType.Channel);
                    
                    var reader = new DataStreamReader(__bytes.AsArray());
                    var temp = new ClientMessagePlayerProperty(ref reader);
                    temp.Write(ref writer);
                    
                    sendBuffer.EndWrite(writer);
                }
                break;*/
        }
    }
    
    private void __Save<T>(in T message) where T : unmanaged
    {
        if(!__bytes.IsCreated)
            __bytes = new NativeList<byte>(Allocator.Persistent);

        __bytes.ResizeUninitialized(UnsafeUtility.SizeOf<T>());
        
        var messages = __bytes.AsArray().Reinterpret<T>(1);
        messages[0] = message;
    }

    private T __Load<T>() where T : unmanaged
    {
        return __bytes.AsArray().Reinterpret<T>(1)[0];
    }

    private void __WriteSquadInvite(ref DataStreamWriter writer, in StreamCompressionModel streamCompressionModel, int channel)
    {
        header.Write(ref writer, streamCompressionModel, (int)ClientMessageType.SquadInvite,
            __squadInviteMessage.userID == 0 ? NetworkRelayType.All : __squadInviteMessage.userID.RelayType());
        writer.WritePackedInt(channel, streamCompressionModel);
        writer.WritePackedUInt(__squadInviteMessage.levelID, streamCompressionModel);
        writer.WritePackedInt(__squadInviteMessage.stage, streamCompressionModel);
        writer.WriteFixedString512(__squadInviteMessage.text);
    }

    private void __SendStatus()
    {
        print($"Sending status {ReplyMessageShared.channelStatus}");
        
        var writer = BeginSend((ClientMessageType)NetworkRelayMessageType.Status, 4);
        writer.WritePackedInt(ReplyMessageShared.channelStatus, StreamCompressionModel.Default);
        EndSend(writer);
    }

    private bool __isConnected;

    private void __Connect()
    {
        if (string.IsNullOrEmpty(__address))
            return;
        
        var client = driver.instance;
        if(__isConnected)
            client.Shutdown();
        
        using (var bytes = new NativeArray<byte>(1024, Allocator.Temp))
        {
            var writer = new DataStreamWriter(bytes);
            header.Write(ref writer, StreamCompressionModel.Default);
            client.Connect(__address, __port, bytes.GetSubArray(0, writer.Length));
            
            print($"{this} is connecting to {__address}:{__port}");
        }

        __isConnected = true;
    }

    private void __Disconnect()
    {
        if (!__isConnected)
            return;
        
        __isConnected = false;
        
        driver.instance.Shutdown();
        
        print($"{this} has been disconnected");
    }
    
    void Start()
    {
        IClientData.instance = this;

        /*ClientMessageSquadInviteToSend squadInviteToSend;
        squadInviteToSend.levelID = 1;
        squadInviteToSend.stage = 0;
        squadInviteToSend.text = "hehe";
        
        SendMessage(squadInviteToSend);*/
    }

    /*void Update()
    {
        ClientMessageType type;
        while ((type = (ClientMessageType)ReadMessageType(out var clientHeader)) != ClientMessageType.None)
        {
            switch (type)
            {
                case ClientMessageType.SquadJoin:
                    print(ReadMessage<ClientMessageSquadJoin>());
                    break;
                case ClientMessageType.SquadLeave:
                    //ReadMessage<ClientMessageSquadLeave>();
                    print("ClientMessageSquadLeave");
                    break;
                case ClientMessageType.SquadInvite:
                    print(ReadMessage<ClientMessageSquadInviteToRead>());
                    break;
                case ClientMessageType.Chat:
                    print(ReadMessage<ClientMessageChatToRead>());
                    break;
            }
        }
    }*/

    void OnDestroy()
    {
        if(__messages.IsCreated)
            __messages.Dispose();
        
        if(__bytes.IsCreated)
            __bytes.Dispose();

        if(__friends.IsCreated)
            __friends.Dispose();
    }

    /*void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            Invoke(nameof(__Disconnect), _disconnectTimeoutMS * 0.001f);
        else
        {
            CancelInvoke(nameof(__Disconnect));
            
            if(!__isConnected)
                __Connect();
        }
    }

    void OnApplicationQuit()
    {
        __Disconnect();
    }*/
}

