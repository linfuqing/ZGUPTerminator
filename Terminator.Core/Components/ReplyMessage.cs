using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ZG;

public enum ReplyMessageType
{
    Move = 100, 
    Damage, 
    HP, 
        
    SelectSkill, 
    PlayerProperty
}

public struct ReplyMessages : IComponentData
{
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
        
        int bufferLength = __buffer.Length;
        if (!isBuffer)
        {
            bool isClear = false;
            NetworkClient.Message message;
            foreach (var pair in __values)
            {
                message = pair.Value;
                if (message.offset + message.size > bufferLength)
                {
                    isClear = true;
                    
                    break;
                }
            }

            if (isClear)
            {
                __buffer.Clear();
                __values.Clear();

                bufferLength = 0;
            }
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

        int channel, size;
        NetworkRelayChannelFlag channelFlag;
        MessageKey key;
        NetworkClient.Message value, temp;
        DataStreamReader reader;
        var streamCompressionModel = StreamCompressionModel.Default;
        foreach (var messageElement in messageElements)
        {
            switch (messageElement.Message.type)
            {
                case NetworkClientMessageType.Connect:
                    //LevelPlayerShared<LocalPlayer>.id = 0;
                    
                    ReplyMessageShared.isHost = false;
                    ReplyMessageShared.remotePlayerCount = 0;
                    break;
                case NetworkClientMessageType.Data:
                    reader = messageElement.reader;

                    key.type = (ReplyMessageType)reader.ReadPackedInt(streamCompressionModel);
                    switch ((NetworkRelayMessageType)key.type)
                    {
                        case NetworkRelayMessageType.Connect:
                            key.id = reader.ReadPackedUInt(streamCompressionModel);
                            if (key.id == LevelPlayerShared<RemotePlayer>.id)
                                RemotePlayer.isOnline = true;

                            break;
                        case NetworkRelayMessageType.Disconnect:
                            key.id = reader.ReadPackedUInt(streamCompressionModel);
                            if (key.id == LevelPlayerShared<RemotePlayer>.id)
                                RemotePlayer.isOnline = false;

                            break;
                        case NetworkRelayMessageType.Create:
                            ReplyMessageShared.isHost = true;
                            ReplyMessageShared.channel = reader.ReadPackedInt(streamCompressionModel);

                            ReplyMessageShared.remotePlayerCount = 0;
                            
                            UnityEngine.Debug.Log($"{(NetworkRelayMessageType)key.type}");
                            break;
                        case NetworkRelayMessageType.Join:
                            channel = reader.ReadPackedInt(streamCompressionModel);
                            if (reader.GetBytesRead() < reader.Length)
                            {
                                if (channel == ReplyMessageShared.channel)
                                {
                                    channelFlag = (NetworkRelayChannelFlag)reader.ReadPackedInt(streamCompressionModel);

                                    reader.Flush();
                                    key.id = reader.ReadPackedUInt(streamCompressionModel);
                                    if ((++ReplyMessageShared.remotePlayerCount > 1 ||
                                         RemotePlayer.status >= RemotePlayer.Status.Joined &&
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

                                    RemotePlayer.isOnline = (channelFlag & NetworkRelayChannelFlag.Online) ==
                                                            NetworkRelayChannelFlag.Online;

                                    reader.ReadBytes(ReplyMessageShared.remotePlayerHeader.AsArray());

                                    UnityEngine.Debug.Log($"{(NetworkRelayMessageType)key.type}:{key.id}");
                                }
                                else
                                    UnityEngine.Debug.LogError($"WTF Channel {channel} For Join!");
                            }
                            else
                            {
                                ReplyMessageShared.isHost = false;
                                ReplyMessageShared.channel = channel;
                                
                                UnityEngine.Debug.Log($"{(NetworkRelayMessageType)key.type}");
                            }

                            break;
                        case NetworkRelayMessageType.Leave:
                        case NetworkRelayMessageType.Drop:
                            channel = reader.ReadPackedInt(streamCompressionModel);
                            if (channel == ReplyMessageShared.channel)
                            {
                                if (reader.GetBytesRead() < reader.Length)
                                {
                                    channelFlag = (NetworkRelayChannelFlag)reader.ReadPackedInt(streamCompressionModel);
                                    if ((channelFlag & NetworkRelayChannelFlag.Creator) ==
                                        NetworkRelayChannelFlag.Creator)
                                    {
                                        if (sendBuffer.BeginWrite(0, out var writer))
                                        {
                                            writer.WritePackedInt((int)NetworkRelayMessageType.Leave,
                                                streamCompressionModel);

                                            sendBuffer.EndWrite(writer);
                                        }
                                    }
                                    
                                    reader.Flush();
                                    
                                    key.id = reader.ReadPackedUInt(streamCompressionModel);
                                    if (key.id == LevelPlayerShared<RemotePlayer>.id)
                                    {
                                        LevelPlayerShared<RemotePlayer>.id = 0;

                                        RemotePlayer.isOnline = false;
                                    }

                                    ReplyMessageShared.remotePlayerCount =
                                        math.max(ReplyMessageShared.remotePlayerCount - 1, 0);
                                    
                                    UnityEngine.Debug.Log($"{(NetworkRelayMessageType)key.type}:{key.id}");
                                }
                                else
                                {
                                    ReplyMessageShared.isHost = false;

                                    ReplyMessageShared.remotePlayerCount = 0;
                                    
                                    RemotePlayer.isOnline = false;

                                    LevelPlayerShared<RemotePlayer>.id = 0;
                                    
                                    UnityEngine.Debug.Log($"{(NetworkRelayMessageType)key.type}");
                                }
                            }
                            else
                                UnityEngine.Debug.LogError($"WTF Channel {channel} For Leave!");

                            break;
                        default:
                            if (key.type < ReplyMessageType.Move || key.type > ReplyMessageType.PlayerProperty)
                                continue;

                            channel = reader.ReadPackedInt(streamCompressionModel);
                            UnityEngine.Assertions.Assert.AreEqual((int)NetworkRelayType.Channel, channel);

                            key.id = reader.ReadPackedUInt(streamCompressionModel);

                            size = reader.GetBytesRead();

                            value = messageElement.Message;
                            value.offset += size;
                            value.size -= size;

                            if (ReplyMessageType.PlayerProperty == key.type)
                            {
                                reader = new NetworkClient.MessageElement(value, messages).reader;

                                LevelPlayerShared<RemotePlayer>.property =
                                    new LevelPlayerProperty(ref reader, streamCompressionModel);

                                if(RemotePlayer.Status.StandBy != RemotePlayer.status)
                                    RemotePlayer.status = RemotePlayer.Status.Joined;
                            }
                            else
                            {
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
                            }

                            break;
                    }

                    break;
            }
        }

        messageElements.Dispose();
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

    private struct RemotePlayerCount
    {
        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<RemotePlayerCount>();
    }

    private struct RemotePlayerHeader
    {
        public static readonly SharedStatic<FixedBytes64> Value = SharedStatic<FixedBytes64>.GetOrCreate<RemotePlayerHeader>();
    }

    public static ref bool isHost => ref IsHost.Value.Data;
    
    public static ref int channel => ref Channel.Value.Data;
    
    public static ref int remotePlayerCount => ref RemotePlayerCount.Value.Data;
    
    public static ref FixedBytes64 remotePlayerHeader => ref RemotePlayerHeader.Value.Data;
}