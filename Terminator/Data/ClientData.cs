using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using ZG;

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
    
    LevelChapter, 
    PlayerProperty,
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
        userName = reader.ReadFixedString32();
        userAvatar = reader.ReadFixedString32();
    }
    
    public void Write(ref DataStreamWriter writer, StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedUInt(userID, streamCompressionModel);
        writer.WriteFixedString32(userName);
        writer.WriteFixedString32(userAvatar);
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

public struct ClientMessageLevelChapter : IClientMessageToSend
{
    public uint levelID;
    public int stage;

    public ClientMessageType messageType => ClientMessageType.LevelChapter;

    public ClientMessageLevelChapter(ref DataStreamReader reader, StreamCompressionModel streamCompressionModel)
    {
        levelID = reader.ReadPackedUInt(streamCompressionModel);
        stage = reader.ReadPackedInt(streamCompressionModel);
    }

    public void Write(ref DataStreamWriter writer, StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedUInt(levelID, streamCompressionModel);
        writer.WritePackedInt(stage, streamCompressionModel);
    }
}

public struct ClientMessagePlayerProperty
{
    public LevelPlayerProperty value;
    
    public static ClientMessageType messageType => ClientMessageType.PlayerProperty;
    
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

    bool isHost { get; }
    
    int remotePlayerCount { get; }

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
    [SerializeField]
    internal int _maxFrameTimeMS = 0;
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

    private int __identityIndex = -1;
    private int __frameCount;
    private int __pipelineIndex;
    private NetworkClient.MessageIterator __messageIterator;
    private NativeList<byte> __bytes;
    private ClientMessageSquadInviteToSend __squadInviteMessage;

    private static ClientHeader __header;
    private static Entity __entity;

    public bool isHost
    {
        get;

        private set;
    }

    public int remotePlayerCount
    {
        get;

        private set;
    }

    public NetworkClientDriver driver => __GetOrCreateDriver(out _);
    
    public ClientHeader header
    {
        get => __header;

        set
        {
            if (__header.Equals(value))
                return;

            __header = value;
        }
    }
    
    public int ReadMessageType(out ClientHeader header)
    {
        int frameCount = Time.frameCount;
        if (frameCount != __frameCount)
        {
            __frameCount = frameCount;
            
            //__messageIterator.Dispose();

            __messageIterator = __GetOrCreateDriver(out var entityManager).AsMessages().CreateIterator(entityManager.World.UpdateAllocator.ToAllocator);
        }

        NetworkClient.MessageIterator.Element element;
        while (__messageIterator.MoveNext())
        {
            element = __messageIterator.Current;
            switch (element.type)
            {
                case NetworkClientMessageType.Data:
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    var reader = element.reader;
                    int type = reader.ReadPackedInt(streamCompressionModel);
                    print((NetworkRelayMessageType)type);
                    switch ((NetworkRelayMessageType)type)
                    {
                        case NetworkRelayMessageType.Init:
                            __identityIndex = reader.ReadPackedInt(streamCompressionModel);
                            break;
                        case NetworkRelayMessageType.Create:
                        case NetworkRelayMessageType.Join:
                        case NetworkRelayMessageType.Leave:
                        {
                            int identityIndex = reader.ReadPackedInt(streamCompressionModel),
                                channel = reader.ReadPackedInt(streamCompressionModel);
                            if (identityIndex == __identityIndex)
                            {
                                header = this.header;

                                if ((int)NetworkRelayMessageType.Create == type)
                                {
                                    var driver = this.driver;
                                    if (driver.BeginWrite(__pipelineIndex, out var writer))
                                    {
                                        writer.WritePackedInt((int)ClientMessageType.SquadInvite,
                                            streamCompressionModel);
                                        writer.WritePackedInt((int)NetworkRelayType.All, streamCompressionModel);
                                        header.Write(ref writer, streamCompressionModel);
                                        writer.WritePackedInt(channel, streamCompressionModel);
                                        writer.WritePackedUInt(__squadInviteMessage.levelID, streamCompressionModel);
                                        writer.WritePackedInt(__squadInviteMessage.stage, streamCompressionModel);
                                        writer.WriteFixedString512(__squadInviteMessage.text);

                                        driver.EndWrite(writer);
                                    }

                                    isHost = true;

                                    break;
                                }
                            }
                            else
                            {
                                if ((int)NetworkRelayMessageType.Leave == type)
                                {
                                    //对面离开
                                    --remotePlayerCount;

                                    ClientMessageSquadLeave temp;
                                    SendMessage(temp);
                                }
                                else
                                    ++remotePlayerCount;
                                
                                header = new ClientHeader(ref reader, streamCompressionModel);
                            }

                            isHost = false;

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
                            int relayType = reader.ReadPackedInt(streamCompressionModel);
                            switch ((NetworkRelayType)relayType)
                            {
                                case NetworkRelayType.All:
                                    channel = ClientChannel.Public;
                                    break;
                                case NetworkRelayType.Channel:
                                    channel = ClientChannel.Squad;
                                    break;
                                default:
                                    
                                    UnityEngine.Assertions.Assert.AreEqual(__identityIndex, relayType);
                                    break;
                            }
                            int identityIndex = reader.ReadPackedInt(streamCompressionModel);
                            
                            header = new ClientHeader(ref reader, streamCompressionModel);

                            switch ((ClientMessageType)type)
                            {
                                case ClientMessageType.SquadInvite:
                                {
                                    UnityEngine.Assertions.Assert.AreEqual(ClientChannel.Public, channel);
                                    
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
                                    ClientMessageChatToRead message;
                                    message.channel = channel;
                                    message.value = reader.ReadFixedString512();
                                    __Save(message);
                                    return (int)ClientMessageType.Chat;
                                }
                                case ClientMessageType.PlayerProperty:
                                    var playerProperty = new ClientMessagePlayerProperty(ref reader);
                                    LevelPlayerShared<RemotePlayer>.property = playerProperty.value;

                                    RemotePlayer.status = RemotePlayer.Status.Joined;
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
                    /*header = this.header;
                    var driver = this.driver;
                    if (driver.BeginWrite(__pipelineIndex, out var writer))
                    {
                        var streamCompressionModel = StreamCompressionModel.Default;
                        writer.WritePackedInt((int)NetworkRelayMessageType.Init, streamCompressionModel);
                        header.Write(ref writer, streamCompressionModel);
                        driver.EndWrite(writer);
                    }*/

                    break;
                }
                case NetworkClientMessageType.Disconnect:
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
        var driver = this.driver;
        switch (type)
        {
            case ClientMessageType.SquadJoin:
                if (driver.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Join, streamCompressionModel);
                    writer.WritePackedInt((int)__Load<ClientMessageSquadJoin>().squadInviteID, streamCompressionModel);
                    driver.EndWrite(writer);
                }
                break;
            case ClientMessageType.SquadLeave:
                if (driver.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Leave, streamCompressionModel);
                    driver.EndWrite(writer);
                }
                break;
            case ClientMessageType.SquadInvite:
                if (driver.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Create, streamCompressionModel);
                    driver.EndWrite(writer);

                    __squadInviteMessage = __Load<ClientMessageSquadInviteToSend>();
                }
                break;
            case ClientMessageType.Chat:
                if (driver.BeginWrite(__pipelineIndex, out writer))
                {
                    var temp = __Load<ClientMessageChatToSend>();
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)ClientMessageType.Chat, streamCompressionModel);
                    writer.WritePackedInt((int)temp.channel, streamCompressionModel);
                    header.Write(ref writer, streamCompressionModel);
                    writer.WriteFixedString512(temp.value);
                    
                    driver.EndWrite(writer);
                }
                break;
            case ClientMessageType.PlayerProperty:
                if (driver.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)ClientMessageType.PlayerProperty, streamCompressionModel);
                    writer.WritePackedInt((int)NetworkRelayType.Channel, streamCompressionModel);
                    
                    var reader = new DataStreamReader(__bytes.AsArray());
                    var temp = new ClientMessagePlayerProperty(ref reader);
                    temp.Write(ref writer);
                    
                    driver.EndWrite(writer);
                }
                break;
            case ClientMessageType.Play:
                if (driver.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)ClientMessageType.PlayerProperty, streamCompressionModel);
                    writer.WritePackedInt((int)NetworkRelayType.Channel, streamCompressionModel);
                    
                    var reader = new DataStreamReader(__bytes.AsArray());
                    var temp = new ClientMessagePlay(ref reader);
                    temp.Write(ref writer);
                    
                    driver.EndWrite(writer);
                }
                break;
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

    private NetworkClientDriver __GetOrCreateDriver(out EntityManager entityManager)
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        if (__entity == Entity.Null)
        {
            __entity = entityManager.CreateSingleton<NetworkClientDriver>();

            var driver = new NetworkClientDriver(
                Allocator.Persistent, 
                _connectTimeoutMS,
                _maxConnectAttempts,
                _disconnectTimeoutMS,
                _reconnectionTimeoutMS,
                _maxFrameTimeMS, 
                _fixedFrameTimeMS, 
                _receiveQueueCapacity, 
                _sendQueueCapacity);

            using (var stages = new NativeArray<NetworkPipelineStage>(_stages, Allocator.Temp))
                __pipelineIndex = driver.CreatePipeline(stages);

            using (var bytes = new NativeArray<byte>(1024, Allocator.Temp))
            {
                var writer = new DataStreamWriter(bytes);
                var streamCompressionModel = StreamCompressionModel.Default;
                writer.WritePackedInt((int)NetworkRelayMessageType.Init, streamCompressionModel);
                header.Write(ref writer, streamCompressionModel);
                driver.Connect(_address, _port, bytes.GetSubArray(0, writer.Length));
            }

            entityManager.SetComponentData(__entity, driver);

            return driver;
        }

        return entityManager.GetComponentData<NetworkClientDriver>(__entity);
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

