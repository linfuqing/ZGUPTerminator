using System;
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

public enum ClientMessageType
{
    None, 
    
    /// <summary>
    /// 加入队伍（先发送申请，要等待Read到之后才正式加入）
    /// </summary>
    SquadJoin = NetworkRelayMessageType.Join, 
    /// <summary>
    /// 离开队伍（不管是主动还是被踢，ReadMessageType之后才生效，本类型没有Message结构体，不需要ReadMessage）
    /// </summary>
    SquadLeave = NetworkRelayMessageType.Leave, 
    
    /// <summary>
    /// 组队邀请
    /// </summary>
    SquadInvite = NetworkRelayMessageType.Query + 1, 
    
    /// <summary>
    /// 聊天
    /// </summary>
    Chat, 
    
    ChapterStage, 
    Play
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
    public FixedString32Bytes userName;
    public FixedString32Bytes userAvatar;

    public ClientHeader(ref DataStreamReader reader, StreamCompressionModel streamCompressionModel)
    {
        userID = reader.ReadPackedUInt(streamCompressionModel);
        int position = reader.GetBytesRead();
        userName = reader.ReadFixedString32();
        reader.SeekSet(position + 32);
        userAvatar = reader.ReadFixedString32();
        reader.SeekSet(position + 64);
    }
    
    public void Write(ref DataStreamWriter writer, StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedUInt(userID, streamCompressionModel);
        
        int position = writer.Length + 32;
        writer.WriteFixedString32(userName);
        while (writer.Length < position)
            writer.WriteByte(0);
        
        position += 32;
        writer.WriteFixedString32(userAvatar);
        while (writer.Length < position)
            writer.WriteByte(0);
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

public struct ClientMessageSquadJoin : IClientMessageToRead, IClientMessageToSend
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

public struct ClientMessageSquadInviteToRead : IClientMessageToRead
{
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
    public uint levelID;
    public int stage;
    
    /// <summary>
    /// 邀请描述
    /// </summary>
    public FixedString512Bytes text;
    
    public ClientMessageType messageType => ClientMessageType.SquadInvite;
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
        LoginManager.instance.ApplyStart(isRestart, levelID, stage, levelName.ToString(), sceneName.ToString());
    }
}

public interface IClientData
{
    public static IClientData instance;

    ClientHeader header { get; set; }
    
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
    public enum SquadInviteStatus
    {
        None,
        SquadCreating, 
        SquadInviting, 
        SquadInvited
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
        NetworkPipelineStage.UnreliableSequenced,
    };

    private int __frameCount;
    private int __pipelineIndex;
    private int __messageIndex;
    private NativeList<NetworkClient.Message> __messages;
    private NativeList<byte> __bytes;
    private ClientMessageSquadInviteToSend __squadInviteMessage;

    private static ClientHeader __header;
    private static Entity __entity;

    public SquadInviteStatus squadInviteStatus
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
        get => __header;

        set
        {
            if (__header.Equals(value))
                return;

            LevelPlayerShared<LocalPlayer>.id = value.userID;

            if (NetworkConnection.State.Disconnected == driver.instance.connectionState)
            {
                using (var bytes = new NativeArray<byte>(1024, Allocator.Temp))
                {
                    var writer = new DataStreamWriter(bytes);
                    value.Write(ref writer, StreamCompressionModel.Default);
                    driver.Connect(_address, _port, bytes.GetSubArray(0, writer.Length));
                }
            }
            
            __header = value;
        }
    }
    
    public int ReadMessageType(out ClientHeader header)
    {
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
                        case NetworkRelayMessageType.Init:
                            
                            //reset
                            break;
                        case NetworkRelayMessageType.Create:
                        case NetworkRelayMessageType.Join:
                        case NetworkRelayMessageType.Leave:
                        {
                            int channel = reader.ReadPackedInt(streamCompressionModel);
                            if (reader.GetBytesRead() < reader.Length)
                            {
                                reader.Flush();
                                header = new ClientHeader(ref reader, streamCompressionModel);
                                
                                if ((int)NetworkRelayMessageType.Leave == type)
                                {
                                    //对面离开
                                    //--remotePlayerCount;

                                    ClientMessageSquadLeave temp;
                                    SendMessage(temp);
                                }
                                else
                                {
                                    if (ReplyMessageShared.isHost)
                                        LoginManager.instance?.SendChapterStageMessage();
                                }
                                /*else
                                {
                                    ++remotePlayerCount;
                                    
                                    LevelPlayerShared<RemotePlayer>.id = header.userID;
                                }*/
                            }
                            else
                            {
                                header = this.header;

                                switch ((NetworkRelayMessageType)type)
                                {
                                    case NetworkRelayMessageType.Create:
                                        var sendBuffer = driver.sendBuffer;
                                        if (sendBuffer.BeginWrite(__pipelineIndex, out var writer))
                                        {
                                            header.Write(ref writer, streamCompressionModel, (int)ClientMessageType.SquadInvite, NetworkRelayType.All);
                                            writer.WritePackedInt(channel, streamCompressionModel);
                                            writer.WritePackedUInt(__squadInviteMessage.levelID, streamCompressionModel);
                                            writer.WritePackedInt(__squadInviteMessage.stage, streamCompressionModel);
                                            writer.WriteFixedString512(__squadInviteMessage.text);

                                            sendBuffer.EndWrite(writer);
                                        }

                                        squadInviteStatus = SquadInviteStatus.SquadInviting;

                                        //isHost = true;

                                        break;
                                    case NetworkRelayMessageType.Join:
                                        squadInviteStatus = SquadInviteStatus.SquadInvited;
                                        
                                        //isHost = false;
                                        break;
                                    case NetworkRelayMessageType.Leave:
                                        squadInviteStatus = SquadInviteStatus.None;
                                        
                                        //isHost = false;
                                        break;
                                }
                            }

                            if ((int)NetworkRelayMessageType.Leave == type)
                                return (int)ClientMessageType.SquadLeave;

                            ClientMessageSquadJoin message;
                            message.squadInviteID = (uint)channel;

                            __Save(message);
                            
                            return (int)ClientMessageType.SquadJoin;
                        }
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
                                    UnityEngine.Assertions.Assert.AreEqual(ClientChannel.Public, channel);
                                    
                                    header = new ClientHeader(ref reader, streamCompressionModel);

                                    ClientMessageSquadInviteToRead message;
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
                                /*case ClientMessageType.PlayerProperty:
                                    var playerProperty = new ClientMessagePlayerProperty(ref reader);
                                    LevelPlayerShared<RemotePlayer>.property = playerProperty.value;

                                    RemotePlayer.status = RemotePlayer.Status.Joined;
                                    break;*/
                                case ClientMessageType.ChapterStage:
                                    LoginManager.instance?.MoveTo(new ClientMessageChapterStage(ref reader, streamCompressionModel).userStageID);
                                    break;
                                case ClientMessageType.Play:
                                    new ClientMessagePlay(ref reader).Apply();
                                    break;
                            }

                            break;
                        }
                    }
                }
                    break;
                case NetworkClientMessageType.Connect:
                {
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
                    /*if (SquadInviteStatus.None != squadInviteStatus)
                    {
                        header = default;
                        
                        return (int)ClientMessageType.SquadLeave;
                    }*/

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
            case ClientMessageType.SquadJoin:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Join, streamCompressionModel);
                    writer.WritePackedInt((int)__Load<ClientMessageSquadJoin>().squadInviteID, streamCompressionModel);
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
            case ClientMessageType.SquadInvite:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Create, streamCompressionModel);
                    sendBuffer.EndWrite(writer);

                    __squadInviteMessage = __Load<ClientMessageSquadInviteToSend>();
                    
                    squadInviteStatus = SquadInviteStatus.SquadCreating;
                }
                break;
            case ClientMessageType.Chat:
                if (sendBuffer.BeginWrite(__pipelineIndex, out writer))
                {
                    var temp = __Load<ClientMessageChatToSend>();
                    header.Write(
                        ref writer, 
                        StreamCompressionModel.Default, 
                        (int)ClientMessageType.Chat, ClientChannel.Squad == temp.channel ? temp.userID.RelayType() : (NetworkRelayType)temp.channel);

                    writer.WriteFixedString512(temp.value);
                    
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
        __bytes.Dispose();
    }
}

