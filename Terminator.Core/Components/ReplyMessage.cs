using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ZG;

public enum ReplyMessageType
{
    Chat = 100, 
    Camera, 
    Move, 
    Damage, 
    HP, 
        
    SelectSkill, 
    PlayerProperty
}

public struct ReplyMessages : IComponentData
{
    /*private enum Status : byte
    {
        Buffer, 
        Clear
    }*/
    
    public struct MessageKey : IEquatable<MessageKey>
    {
        public ReplyMessageType type;

        public uint id;

        public bool Equals(MessageKey other)
        {
            return type == other.type && id == other.id;
        }

        public override int GetHashCode()
        {
            return id.GetHashCode();
        }
    }

    public struct Enumerator
    {
        private NativeList<byte> __clientBuffer;
        private NativeList<byte> __buffer;
        private NativeParallelMultiHashMap<MessageKey, NetworkClient.Message>.Enumerator __instance;

        public NetworkClient.MessageElement Current
        {
            get
            {
                int bufferLength = __buffer.Length;
                var message = __instance.Current;
                if(message.offset < bufferLength)
                    return new NetworkClient.MessageElement(message, __buffer.AsArray());
                        
                message.offset -= bufferLength;
                
                return new NetworkClient.MessageElement(message, __clientBuffer.AsArray());
            }
        }

        internal Enumerator(
            ReplyMessageType type, 
            uint id, 
            in ReplyMessages message, 
            in NativeList<byte> clientBuffer)
        {
            __clientBuffer = clientBuffer;
            __buffer = message.__buffer;
            
            MessageKey key;
            key.type = type;
            key.id = id;

            __instance = message.__values.GetValuesForKey(key);
        }

        public bool MoveNext() => __instance.MoveNext();

        public Enumerator GetEnumerator()
        {
            return this;
        }
    }

    private NativeList<byte> __buffer;
    private NativeParallelMultiHashMap<MessageKey, NetworkClient.Message> __values;

    public static void WriteHeader(ref DataStreamWriter writer, ReplyMessageType messageType)
    {
        writer.WriteReplyHeader((int)messageType, NetworkRelayType.Channel);
    }

    public ReplyMessages(in AllocatorManager.AllocatorHandle allocator)
    {
        __buffer = new NativeList<byte>(allocator);
        __values = new NativeParallelMultiHashMap<MessageKey, NetworkClient.Message>(1, allocator);
    }

    public void Dispose()
    {
        __buffer.Dispose();

        __values.Dispose();
    }

    public Enumerator GetValues(ReplyMessageType type, uint id, in NativeList<byte> clientBuffer)
    {
        return new Enumerator(type, id, this, clientBuffer);
    }

    public void Collect(in NetworkClient.Messages messages, ref NetworkClientSendBuffer sendBuffer)
    {
        bool isBuffer;
        switch (RemotePlayer.status)
        {
            case RemotePlayer.Status.Disabled:
            case RemotePlayer.Status.StandBy:
                isBuffer = false;
                break;
            default:
                isBuffer = true;
                break;
        }
        
        if (!isBuffer)
        {
            __buffer.Clear();
            __values.Clear();
        }

        NativeList<NetworkClient.MessageElement> messageElements = default;
        foreach (var message in messages)
        {
            if (!messageElements.IsCreated)
                messageElements = new NativeList<NetworkClient.MessageElement>(Allocator.Temp);
            
            messageElements.Add(message);
        }

        if (!messageElements.IsCreated)
            return;
        
        messageElements.Sort();

        int bufferLength = __buffer.Length, channel, size, channelFlag;
        MessageKey key;
        NetworkClient.Message value, temp;
        DataStreamReader reader;
        var streamCompressionModel = StreamCompressionModel.Default;
        foreach (var messageElement in messageElements)
        {
            switch (messageElement.Message.type)
            {
                case NetworkClientMessageType.Connect:
                    __Log("Reply Message Connect");
                    //LevelPlayerShared<LocalPlayer>.id = 0;
                    
                    ReplyMessageShared.isHost = false;
                    ReplyMessageShared.channel = ReplyMessageShared.CHANNEL_NULL;
                    ReplyMessageShared.remotePlayerCount = 0;
                    break;
                case NetworkClientMessageType.Data:
                    reader = messageElement.reader;

                    key.type = (ReplyMessageType)reader.ReadPackedInt(streamCompressionModel);
                    switch ((NetworkRelayMessageType)key.type)
                    {
                        case NetworkRelayMessageType.Connect:
                            channelFlag = reader.ReadPackedInt(streamCompressionModel);
                            key.id = reader.ReadPackedUInt(streamCompressionModel);
                            if (key.id == LevelPlayerShared<RemotePlayer>.id)
                                LevelPlayerShared<RemotePlayer>.channelFlag = channelFlag;

                            __Log($"Reply Message Connect {key.id}");
                            break;
                        case NetworkRelayMessageType.Disconnect:
                            key.id = reader.ReadPackedUInt(streamCompressionModel);
                            if (key.id == LevelPlayerShared<RemotePlayer>.id)
                                LevelPlayerShared<RemotePlayer>.channelFlag &= (int)~NetworkRelayChannelFlag.Online;

                            __Log($"Reply Message Disconnect {key.id}");
                            break;
                        case NetworkRelayMessageType.Status:
                            channelFlag = reader.ReadPackedInt(streamCompressionModel);
                            //reader.Flush();
                            key.id = reader.ReadPackedUInt(streamCompressionModel);
                            if (key.id == LevelPlayerShared<RemotePlayer>.id)
                            {
                                LevelPlayerShared<RemotePlayer>.channelFlag = channelFlag;

                                if (LevelPlayerShared<RemotePlayer>.channelStatus == 0)
                                    RemotePlayer.SetStatus(RemotePlayer.Status.None,
                                        (1 << (int)RemotePlayer.Status.Joined) |
                                        (1 << (int)RemotePlayer.Status.StandBy));
                            }

                            break;
                        case NetworkRelayMessageType.Create:
                            ReplyMessageShared.isHost = true;
                            ReplyMessageShared.remotePlayerCount = 0;
                            ReplyMessageShared.channel = reader.ReadPackedInt(streamCompressionModel);
                            
                            ReplyMessageShared.SetChannelFlag(reader.ReadPackedInt(streamCompressionModel));
                            //LevelPlayerShared<LocalPlayer>.channelFlag = reader.ReadPackedInt(streamCompressionModel);

                            __Log("Reply Message Create");
                            break;
                        case NetworkRelayMessageType.Join:
                            channel = reader.ReadPackedInt(streamCompressionModel);
                            channelFlag = reader.ReadPackedInt(streamCompressionModel);
                            if (reader.GetBytesRead() < reader.Length)
                            {
                                if (channel == ReplyMessageShared.channel)
                                {
                                    reader.Flush();
                                    key.id = reader.ReadPackedUInt(streamCompressionModel);
                                    
                                    __Log($"Reply Message {(NetworkRelayMessageType)key.type} {channel}:{channelFlag}:{key.id}");
                                    //++ReplyMessageShared.remotePlayerCount;
                                    if ((++ReplyMessageShared.remotePlayerCount > 1 ||
                                         RemotePlayer.status >= RemotePlayer.Status.Joined &&
                                         LevelPlayerShared<RemotePlayer>.id != 0 && 
                                         LevelPlayerShared<RemotePlayer>.id != key.id) &&
                                        ReplyMessageShared.isHost)
                                    {
                                        if (sendBuffer.BeginWrite(0, out var writer))
                                        {
                                            writer.WritePackedInt((int)NetworkRelayMessageType.Drop,
                                                streamCompressionModel);
                                            writer.WritePackedUInt(key.id, streamCompressionModel);

                                            sendBuffer.EndWrite(writer);
                                        }

                                        continue;
                                    }

                                    LevelPlayerShared<RemotePlayer>.id = key.id;

                                    LevelPlayerShared<RemotePlayer>.channelFlag = channelFlag;

                                    RemotePlayer.SetStatus(RemotePlayer.Status.None,
                                        (1 << (int)RemotePlayer.Status.Joined) |
                                        (1 << (int)RemotePlayer.Status.StandBy));
                                    
                                    FixedBytes80 bytes = default;
                                    reader.ReadBytes(bytes.AsArray());

                                    LevelPlayerShared<RemotePlayer>.header = new LevelPlayerHeader(bytes);
                                    //UnityEngine.Debug.Log($"Reply Message {(NetworkRelayMessageType)key.type} {channel}:{channelFlag}:{key.id}");
                                }
                                else
                                    UnityEngine.Debug.LogError($"WTF Channel {channel} For Join!");
                            }
                            else
                            {
                                ReplyMessageShared.isHost = false;
                                ReplyMessageShared.channel = channel;
                                ReplyMessageShared.SetChannelFlag(channelFlag);
                                //LevelPlayerShared<LocalPlayer>.channelFlag = channelFlag;
                                
                                __Log($"Reply Message {(NetworkRelayMessageType)key.type} {channel}:{channelFlag}");
                            }

                            break;
                        case NetworkRelayMessageType.Leave:
                        case NetworkRelayMessageType.Drop:
                            channel = reader.ReadPackedInt(streamCompressionModel);
                            if (channel == ReplyMessageShared.channel)
                            {
                                channelFlag = reader.ReadPackedInt(streamCompressionModel);
                                if (reader.GetBytesRead() < reader.Length)
                                {
                                    /*if (((NetworkRelayChannelFlag)channelFlag & NetworkRelayChannelFlag.Creator) ==
                                        NetworkRelayChannelFlag.Creator)
                                    {
                                        if (sendBuffer.BeginWrite(0, out var writer))
                                        {
                                            writer.WritePackedInt((int)NetworkRelayMessageType.Leave,
                                                streamCompressionModel);

                                            sendBuffer.EndWrite(writer);
                                        }
                                    }*/
                                    
                                    reader.Flush();
                                    
                                    key.id = reader.ReadPackedUInt(streamCompressionModel);
                                    if (key.id == LevelPlayerShared<RemotePlayer>.id)
                                    {
                                        LevelPlayerShared<RemotePlayer>.id = 0;

                                        LevelPlayerShared<RemotePlayer>.channelFlag = 0;
                                    }

                                    ReplyMessageShared.remotePlayerCount =
                                        math.max(ReplyMessageShared.remotePlayerCount - 1, 0);
                                    
                                    __Log($"Reply Message {(NetworkRelayMessageType)key.type} {channel}:{key.id}");
                                }
                                else
                                {
                                    ReplyMessageShared.isHost = false;

                                    ReplyMessageShared.remotePlayerCount = 0;

                                    ReplyMessageShared.channel = ReplyMessageShared.CHANNEL_NULL;
                                    
                                    ReplyMessageShared.SetChannelFlag(channelFlag);
                                    
                                    //LevelPlayerShared<LocalPlayer>.channelFlag = channelFlag;
                                    
                                    LevelPlayerShared<RemotePlayer>.channelFlag = 0;

                                    LevelPlayerShared<RemotePlayer>.id = 0;
                                    
                                    __Log($"{(NetworkRelayMessageType)key.type}");
                                }
                            }
                            else
                                UnityEngine.Debug.LogError($"WTF Channel {channel} For Leave!");

                            break;
                        default:
                            if (key.type < ReplyMessageType.Chat || key.type > ReplyMessageType.PlayerProperty)
                                continue;

                            channel = reader.ReadPackedInt(streamCompressionModel);
                            if(NetworkRelayType.Channel != (NetworkRelayType)channel)
                                continue;

                            key.id = reader.ReadPackedUInt(streamCompressionModel);

                            __Log($"Reply Message {key.type} {key.id}");
                            
                            size = reader.GetBytesRead();

                            value = messageElement.Message;
                            value.offset += size;
                            value.size -= size;

                            switch (key.type)
                            {
                                case ReplyMessageType.Chat:
                                    reader = new NetworkClient.MessageElement(value, messages).reader;
                                    ReplyMessageChatShared.output = reader.ReadFixedString512();
                                    break;
                                case ReplyMessageType.PlayerProperty:
                                    reader = new NetworkClient.MessageElement(value, messages).reader;

                                    LevelPlayerShared<RemotePlayer>.property =
                                        new LevelPlayerProperty(ref reader, streamCompressionModel);

                                    RemotePlayer.SetStatus(RemotePlayer.Status.Joined,
                                        1 << (int)RemotePlayer.Status.Waiting);
                                    break;
                                default:
                                    if (isBuffer)
                                    {
                                        temp.type = value.type;
                                        temp.size = value.size;
                                        temp.offset = __buffer.Length;

                                        __buffer.AddRange(new NetworkClient.MessageElement(value, messages).AsArray());

                                        value = temp;
                                    }
                                    else
                                        value.offset += bufferLength;

                                    __values.Add(key, value);
                                    break;
                            }
                            break;
                    }

                    break;
            }
        }

        messageElements.Dispose();
    }

    private static void __Log(string message)
    {
        UnityEngine.Debug.Log(message);
    }
}

public static class ReplyMessageShared
{
    private struct IsHost
    {
        public static readonly SharedStatic<bool> Value = SharedStatic<bool>.GetOrCreate<IsHost>();
    }

    private struct Channel
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<Channel>();
    }

    private struct ChannelStatus
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<ChannelStatus>();
    }

    private struct RemotePlayerCount
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<RemotePlayerCount>();
    }

    public const int CHANNEL_NULL = -1;

    public static ref bool isHost => ref IsHost.Value.Data;
    
    public static ref int channel => ref Channel.Value.Data;
    
    public static ref int channelStatus => ref ChannelStatus.Value.Data;

    public static ref int remotePlayerCount => ref RemotePlayerCount.Value.Data;

    public static int GetChannelFlag(int value)
    {
        return (value & (int)NetworkRelayChannelFlag.All) | (channelStatus << (int)NetworkRelayChannelFlag.ShiftToStatus);
    }

    public static void SetChannelFlag(int value)
    {
        LevelPlayerShared<LocalPlayer>.channelFlag = GetChannelFlag(value);
    }

    public static bool SetChannelStatus(int value)
    {
        if (value == channelStatus)
            return false;
        
        channelStatus = value;
        
        LevelPlayerShared<LocalPlayer>.channelFlag = GetChannelFlag(LevelPlayerShared<LocalPlayer>.channelFlag);

        return true;
    }
    
    static ReplyMessageShared()
    {
        channel = CHANNEL_NULL;
    }
}

public static class ReplyMessageChatShared
{
    private struct InputVersionOverride
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<InputVersionOverride>();
    }

    private struct InputVersion
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<InputVersion>();
    }

    private struct Input
    {
        public static readonly SharedStatic<FixedString512Bytes> Value = SharedStatic<FixedString512Bytes>.GetOrCreate<Input>();
    }

    private struct Output
    {
        public static readonly SharedStatic<FixedString512Bytes> Value = SharedStatic<FixedString512Bytes>.GetOrCreate<Output>();
    }

    private struct OutputVersion
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<OutputVersion>();
    }
    
    private struct OutputVersionOverride
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<OutputVersionOverride>();
    }
    
    public static FixedString512Bytes input
    {
        get
        {
            int version = InputVersion.Value.Data;
            if (version == InputVersionOverride.Value.Data)
                return default;
            
            InputVersionOverride.Value.Data = version;

            return Input.Value.Data;
        }

        set
        {
            Input.Value.Data = value;

            ++InputVersion.Value.Data;
        }
    }
    
    public static FixedString512Bytes output
    {
        get
        {
            int version = OutputVersion.Value.Data;
            if (version == OutputVersionOverride.Value.Data)
                return default;
            
            OutputVersionOverride.Value.Data = version;

            return Output.Value.Data;
        }

        set
        {
            Output.Value.Data = value;

            ++OutputVersion.Value.Data;
        }
    }
}