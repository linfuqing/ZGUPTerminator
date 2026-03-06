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

    public ClientHeader(ref DataStreamReader reader, StreamCompressionModel streamCompressionModel)
    {
        userID = reader.ReadPackedUInt(streamCompressionModel);
        userName = reader.ReadFixedString32();
        userIcon = reader.ReadFixedString32();
    }
    
    public void Write(ref DataStreamWriter writer, StreamCompressionModel streamCompressionModel)
    {
        writer.WritePackedUInt(userID, streamCompressionModel);
        writer.WriteFixedString32(userName);
        writer.WriteFixedString32(userIcon);
    }
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

public struct ClientMessageSquadInviteToRead : IClientMessageToRead
{
    public uint squadInviteID;
    public uint levelID;
    public int stage;
    
    /// <summary>
    /// 邀请描述
    /// </summary>
    public FixedString512Bytes text;
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
}

public struct ClientMessageChatToSend : IClientMessageToSend
{
    public ClientChannel channel;
    public uint userID;
    public FixedString512Bytes value;
    
    public ClientMessageType messageType => ClientMessageType.Chat;
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

    private int __identityIndex;
    private int __frameCount;
    private int __pipelineIndex;
    private NetworkClient.MessageIterator __messageIterator;
    private NativeList<byte> __bytes;

    private static Entity __entity;
    
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
                    _reconnectionTimeoutMS,
                    _maxFrameTimeMS, 
                    _fixedFrameTimeMS, 
                    _receiveQueueCapacity, 
                    _sendQueueCapacity);

                using (var stages = new NativeArray<NetworkPipelineStage>(_stages, Allocator.Temp))
                    __pipelineIndex = driver.CreatePipeline(stages);
                
                driver.Connect(_address, _port);
                
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
            header.userID = GameMain.userID;
            header.userName = GameUser.Shared.channelUsername ?? string.Empty;
            header.userIcon = default;

            return header;
        }
    }
    
    public int ReadMessageType(out ClientHeader header)
    {
        int frameCount = Time.frameCount;
        if (frameCount != __frameCount)
        {
            __frameCount = frameCount;
            
            //__messageIterator.Dispose();

            __messageIterator = driver.AsMessages().CreateIterator(Allocator.Temp);
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
                                __channel = channel;

                                header = this.header;
                            }
                            else
                                header = new ClientHeader(ref reader, streamCompressionModel);

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
                            }

                            break;
                        }
                    }
                }
                    break;
                case NetworkClientMessageType.Connect:
                    header = this.header;
                    var driver = this.driver;
                    if (driver.BeginWrite(__pipelineIndex, out var writer))
                    {
                        var streamCompressionModel = StreamCompressionModel.Default;
                        writer.WritePackedInt((int)NetworkRelayMessageType.Init, streamCompressionModel);
                        header.Write(ref writer, streamCompressionModel);
                        driver.EndWrite(writer);
                    }
                    break;
                case NetworkClientMessageType.Disconnect:
                    break;
            }
        }

        header = default;
        return (int)ClientMessageType.None;
    }

    public T ReadMessage<T>() where T : unmanaged, IClientMessageToRead
    {
        return __bytes.AsArray().Reinterpret<T>()[0];
    }

    public void SendMessage<T>(in T message) where T : unmanaged, IClientMessageToSend
    {
        __Save(message);

        DataStreamWriter writer;
        var driver = this.driver;
        switch (message.messageType)
        {
            case ClientMessageType.SquadJoin:
                if (driver.BeginWrite(__pipelineIndex, out writer))
                {
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)NetworkRelayMessageType.Join, streamCompressionModel);
                    writer.WritePackedInt((int)__bytes.AsArray().Reinterpret<ClientMessageSquadJoin>()[0].squadInviteID, streamCompressionModel);
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

                    if (driver.BeginWrite(__pipelineIndex, out writer))
                    {
                        var temp = __bytes.AsArray().Reinterpret<ClientMessageSquadInviteToSend>()[0];
                        writer.WritePackedInt((int)ClientMessageType.SquadInvite, streamCompressionModel);
                        writer.WritePackedInt((int)NetworkRelayType.All, streamCompressionModel);
                        header.Write(ref writer, streamCompressionModel);
                        //writer.WritePackedInt(__channel, streamCompressionModel);
                        writer.WritePackedUInt(temp.levelID, streamCompressionModel);
                        writer.WritePackedInt(temp.stage, streamCompressionModel);
                        writer.WriteFixedString512(temp.text);

                        driver.EndWrite(writer);
                    }
                }
                break;
            case ClientMessageType.Chat:
                if (driver.BeginWrite(__pipelineIndex, out writer))
                {
                    var temp = __bytes.AsArray().Reinterpret<ClientMessageChatToSend>()[0];
                    var streamCompressionModel = StreamCompressionModel.Default;
                    writer.WritePackedInt((int)ClientMessageType.Chat, streamCompressionModel);
                    writer.WritePackedInt((int)temp.channel, streamCompressionModel);
                    header.Write(ref writer, streamCompressionModel);
                    writer.WriteFixedString512(temp.value);
                    
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
        
        var messages = __bytes.AsArray().Reinterpret<T>();
        messages[0] = message;
    }

    void Update()
    {
        ClientMessageType type;
        while ((type = (ClientMessageType)ReadMessageType(out var clientHeader)) != ClientMessageType.None)
        {
            
        }
    }

    void OnDestroy()
    {
        __bytes.Dispose();
    }
}

