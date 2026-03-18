using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
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

    public void Collect(bool isBuffer, in NetworkClient.Messages messages)
    {
        int bufferLength = __buffer.Length;
        if (!isBuffer)
        {
            bool isClear = false;
            foreach (var element in messages)
            {
                if (element.Message.offset >= bufferLength)
                {
                    isClear = true;
                    
                    break;
                }
            }

            if (isClear)
            {
                __buffer.Clear();
                __values.Clear();
            }
        }

        int replayType, size;
        MessageKey key;
        NetworkClient.Message value, temp;
        DataStreamReader reader;
        var streamCompressionModel = StreamCompressionModel.Default;
        foreach (var element in messages)
        {
            if(NetworkClientMessageType.Data != element.Message.type)
                continue;

            reader = element.reader;

            key.type = (ReplyMessageType)reader.ReadPackedInt(streamCompressionModel);
            if(key.type < ReplyMessageType.Move || key.type > ReplyMessageType.PlayerProperty)
                continue;
                
            replayType = reader.ReadPackedInt(streamCompressionModel);
            UnityEngine.Assertions.Assert.AreEqual((int)NetworkRelayType.Channel, replayType);

            key.id = reader.ReadPackedUInt(streamCompressionModel);

            size = reader.GetBytesRead();

            value = element.Message;
            value.offset += size;
            value.size -= size;
            
            if (ReplyMessageType.PlayerProperty == key.type)
            {
                reader = new NetworkClient.MessageElement(value, messages).reader;

                LevelPlayerShared<RemotePlayer>.property = new LevelPlayerProperty(ref reader, streamCompressionModel);

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
        }
    }
}

[BurstCompile, UpdateInGroup(typeof(PresentationSystemGroup)), UpdateAfter(typeof(NetworkClientSystem))]
public partial struct ReplyMessageSystem : ISystem
{
    [BurstCompile]
    private struct Collect : IJob
    {
        [ReadOnly]
        public NetworkClient.Messages inputs;
        public ReplyMessages outputs;

        public void Execute()
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
            outputs.Collect(isBuffer, inputs);
        }
    }

    private struct ReadEffectTargets
    {
        [ReadOnly] 
        public ReplyMessages messages;
        
        [ReadOnly]
        public NativeList<byte> clientBuffer;

        [ReadOnly] 
        public NativeArray<RemoteIdentity> remoteIdentities;

        public BufferAccessor<RemoteEffectTargetDamage> effectTargetDamages;

        public BufferAccessor<RemoteEffectTargetHP> effectTargetHPs;

        public void Execute(int index)
        {
            uint id = remoteIdentities[index].id;

            NativeList<NetworkClient.MessageElement> messageElements = default;
            DataStreamReader reader;
            StreamCompressionModel streamCompressionModel = StreamCompressionModel.Default;
            if (index < effectTargetDamages.Length)
            {
                foreach (var message in messages.GetValues(ReplyMessageType.Damage, id, clientBuffer))
                {
                    if(!messageElements.IsCreated)
                        messageElements = new NativeList<NetworkClient.MessageElement>(Allocator.Temp);
                    
                    messageElements.Add(message);
                }

                if (messageElements.IsCreated)
                {
                    messageElements.Sort();
                    
                    var effectTargetDamages = this.effectTargetDamages[index];
                    foreach (var messageElement in messageElements)
                    {
                        reader = messageElement.reader;
                        do
                        {
                            effectTargetDamages.Add(new RemoteEffectTargetDamage(ref reader,
                                streamCompressionModel));
                        } while (reader.GetBytesRead() < reader.Length);
                    }
                }
            }
            
            if (index < effectTargetHPs.Length)
            {
                if(messageElements.IsCreated)
                    messageElements.Clear();

                foreach (var message in messages.GetValues(ReplyMessageType.Damage, id, clientBuffer))
                {
                    if(!messageElements.IsCreated)
                        messageElements = new NativeList<NetworkClient.MessageElement>(Allocator.Temp);
                    
                    messageElements.Add(message);
                }

                if (messageElements.IsCreated && messageElements.Length > 0)
                {
                    messageElements.Sort();
                    
                    var effectTargetHPs = this.effectTargetHPs[index];
                    foreach (var messageElement in messageElements)
                    {
                        reader = messageElement.reader;
                        do
                        {
                            effectTargetHPs.Add(new RemoteEffectTargetHP(ref reader,
                                streamCompressionModel));
                        } while (reader.GetBytesRead() < reader.Length);
                    }
                }
            }

            if(messageElements.IsCreated)
                messageElements.Dispose();
        }
    }

    private struct WriteEffectTargets
    {
        public NetworkClientSendBuffer.ParallelWriter sendBuffer;

        public BufferAccessor<RemoteEffectTargetDamage> effectTargetDamages;

        public BufferAccessor<RemoteEffectTargetHP> effectTargetHPs;

        public void Execute(int index)
        {
            var streamCompressionModel = StreamCompressionModel.Default;
            
            var effectTargetDamages = index < this.effectTargetDamages.Length ? this.effectTargetDamages[index] : default;
            if (effectTargetDamages.IsCreated && effectTargetDamages.Length > 0)
            {
                if (sendBuffer.BeginWrite(0, out var writer))
                {
                    writer.WriteReplyHeader((int)ReplyMessageType.Damage, NetworkRelayType.Channel);
                    
                    foreach (var effectTargetDamage in effectTargetDamages)
                        effectTargetDamage.Write(ref writer, streamCompressionModel);

                    effectTargetDamages.Clear();
                    
                    sendBuffer.EndWrite(writer);
                }
            }
            
            var effectTargetHPs = index < this.effectTargetHPs.Length ? this.effectTargetHPs[index] : default;
            if (effectTargetHPs.IsCreated && effectTargetHPs.Length > 0)
            {
                if (sendBuffer.BeginWrite(0, out var writer))
                {
                    writer.WriteReplyHeader((int)ReplyMessageType.HP, NetworkRelayType.Channel);
                    
                    foreach (var effectTargetHP in effectTargetHPs)
                        effectTargetHP.Write(ref writer, streamCompressionModel);

                    effectTargetHPs.Clear();

                    sendBuffer.EndWrite(writer);
                }
            }
        }
    }

    [BurstCompile]
    private struct ReplyEffectTargets : IJobChunk
    {
        public NetworkClientSendBuffer.ParallelWriter sendBuffer;

        [ReadOnly] 
        public ReplyMessages messages;
        
        [ReadOnly]
        public NativeList<byte> clientBuffer;

        [ReadOnly] 
        public ComponentTypeHandle<RemoteIdentity> remoteIdentityType;

        public BufferTypeHandle<RemoteEffectTargetDamage> effectTargetDamageType;

        public BufferTypeHandle<RemoteEffectTargetHP> effectTargetHPType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            if (chunk.Has(ref remoteIdentityType))
            {
                ReadEffectTargets readEffectTargets;
                readEffectTargets.messages = messages;
                readEffectTargets.clientBuffer = clientBuffer;
                readEffectTargets.remoteIdentities = chunk.GetNativeArray(ref remoteIdentityType);
                readEffectTargets.effectTargetDamages = chunk.GetBufferAccessor(ref effectTargetDamageType);
                readEffectTargets.effectTargetHPs = chunk.GetBufferAccessor(ref effectTargetHPType);

                while (iterator.NextEntityIndex(out int i))
                    readEffectTargets.Execute(i);
            }
            else
            {
                WriteEffectTargets writeEffectTargets;
                writeEffectTargets.sendBuffer = sendBuffer;
                writeEffectTargets.effectTargetDamages = chunk.GetBufferAccessor(ref effectTargetDamageType);
                writeEffectTargets.effectTargetHPs = chunk.GetBufferAccessor(ref effectTargetHPType);

                while (iterator.NextEntityIndex(out int i))
                    writeEffectTargets.Execute(i);
            }
        }
    }

    private struct ReadPositions
    {
        [ReadOnly] 
        public ReplyMessages messages;

        [ReadOnly]
        public NativeList<byte> clientBuffer;

        [ReadOnly] 
        public NativeArray<RemoteIdentity> remoteIdentities;

        public NativeArray<ThirdPersonCharacterControl> thirdPersonCharacterControls;

        public NativeArray<LocalTransform> localTransforms;

        public BufferAccessor<RemotePosition> remotePositions;

        public void Execute(int index)
        {
            NativeList<NetworkClient.MessageElement> messageElements = default;
            foreach (var messageElement in messages.GetValues(ReplyMessageType.Move, remoteIdentities[index].id, clientBuffer))
            {
                if(!messageElements.IsCreated)
                    messageElements = new NativeList<NetworkClient.MessageElement>(Allocator.Temp);
                    
                messageElements.Add(messageElement);
            }

            if (!messageElements.IsCreated)
                return;
            
            messageElements.Sort();
            
            var remotePositions = this.remotePositions[index];
            RemotePosition remotePosition;
            DataStreamReader reader;
            var streamCompressionModel = StreamCompressionModel.Default;
            int maxRemotePositionIndex = remotePositions.Length - 1, numRemotePositions, i;
            foreach (var messageElement in messageElements)
            {
                reader = messageElement.reader;

                numRemotePositions = reader.ReadPackedInt(streamCompressionModel);
                for (i = 0; i < numRemotePositions; ++i)
                {
                    remotePosition = new RemotePosition(ref reader, streamCompressionModel);
                    if (maxRemotePositionIndex < 0 || remotePositions[maxRemotePositionIndex].type == RemotePosition.Type.Key)
                    {
                        remotePositions.Add(remotePosition);

                        ++maxRemotePositionIndex;
                    }
                    else
                        remotePositions[maxRemotePositionIndex] = remotePosition;
                }
            }

            messageElements.Dispose();

            numRemotePositions = remotePositions.Length;
            if (numRemotePositions > 0)
            {
                var localTransform = localTransforms[index];
                
                remotePosition = remotePositions[0];
                if (RemotePosition.Type.Wrap == remotePosition.type)
                {
                    localTransform.Position = math.float3(remotePosition.value.x, localTransform.Position.y,
                        remotePosition.value.y);
                    localTransforms[index] = localTransform;
                    
                    thirdPersonCharacterControls[index] = default;
                }
                else
                {
                    var thirdPersonCharacterControl = thirdPersonCharacterControls[index];
                    
                    thirdPersonCharacterControl.MoveVector =
                        math.float3(remotePosition.value.x, localTransform.Position.y, remotePosition.value.y) -
                        localTransform.Position;
                    if (!__ClampToMaxLength(ref thirdPersonCharacterControl.MoveVector) &&
                        numRemotePositions > 1)
                        remotePositions.RemoveAt(0);

                    thirdPersonCharacterControls[index] = thirdPersonCharacterControl;
                }
            }
        }

        private static bool __ClampToMaxLength(ref float3 vector)
        {
            float sqrmag = math.lengthsq(vector);
            if (sqrmag > 1.0f)
            {
                float mag = math.sqrt(sqrmag);
                vector /= mag;

                return true;
            }

            return false;
        }
    }

    private struct WritePositions
    {
        public NetworkClientSendBuffer.ParallelWriter sendBuffer;

        [ReadOnly]
        public NativeArray<LocalTransform> localTransforms;

        [ReadOnly]
        public NativeArray<KinematicCharacterBody> characterBodies;

        public BufferAccessor<RemotePosition> remotePositions;

        public void Execute(int index)
        {
            RemotePosition remotePosition;
            var characterBody = index < characterBodies.Length ? characterBodies[index] : default;
            remotePosition.type =
                math.dot(characterBody.GroundingUp, characterBody.RelativeVelocity) > math.FLT_MIN_NORMAL
                    ? RemotePosition.Type.Key
                    : RemotePosition.Type.Normal;
            var localTransform = localTransforms[index];
            remotePosition.value = math.float2(localTransform.Position.x, localTransform.Position.z);
            
            var remotePositions = this.remotePositions[index];
            int numRemotePositions = remotePositions.Length;
            if (numRemotePositions < 1)
            {
                remotePositions.Add(remotePosition);
                
                ++numRemotePositions;
            }
            else
            {
                int endIndex = numRemotePositions - 1;
                var temp = remotePositions[endIndex];
                if (!ZG.Mathematics.Math.Approximately(temp.value, remotePosition.value))
                {
                    if (temp.type == remotePosition.type)
                        remotePositions[endIndex] = remotePosition;
                    else
                    {
                        remotePositions.Add(remotePosition);
                
                        ++numRemotePositions;
                    }
                }
                else if (RemotePosition.Type.Normal == temp.type)
                {
                    if (RemotePosition.Type.Normal == remotePosition.type)
                        return;
                    
                    remotePositions[endIndex] = remotePosition;
                }
            }
            
            if (sendBuffer.BeginWrite(0, out var writer))
            {
                writer.WriteReplyHeader((int)ReplyMessageType.Move, NetworkRelayType.Channel);
                var streamCompressionModel = StreamCompressionModel.Default;

                writer.WritePackedInt(numRemotePositions, streamCompressionModel);
                for(int i = 0; i < numRemotePositions; ++i)
                    remotePositions[i].Write(ref writer, streamCompressionModel);
                
                sendBuffer.EndWrite(writer);
                
                remotePositions.Clear();
                remotePositions.Add(remotePosition);
            }
        }
    }

    [BurstCompile]
    private struct ReplyPositions : IJobChunk
    {
        public NetworkClientSendBuffer.ParallelWriter sendBuffer;
        
        [ReadOnly] 
        public ReplyMessages messages;

        [ReadOnly]
        public NativeList<byte> clientBuffer;

        [ReadOnly] 
        public ComponentTypeHandle<RemoteIdentity> remoteIdentityType;

        [ReadOnly]
        public ComponentTypeHandle<KinematicCharacterBody> characterBodyType;

        public ComponentTypeHandle<ThirdPersonCharacterControl> thirdPersonCharacterControlType;

        public ComponentTypeHandle<LocalTransform> localTransformType;

        public BufferTypeHandle<RemotePosition> remotePositionType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            if (chunk.Has(ref remoteIdentityType))
            {
                ReadPositions readPositions;
                readPositions.messages = messages;
                readPositions.clientBuffer = clientBuffer;
                readPositions.remoteIdentities = chunk.GetNativeArray(ref remoteIdentityType);
                readPositions.thirdPersonCharacterControls = chunk.GetNativeArray(ref thirdPersonCharacterControlType);
                readPositions.localTransforms = chunk.GetNativeArray(ref localTransformType);
                readPositions.remotePositions = chunk.GetBufferAccessor(ref remotePositionType);
                while (iterator.NextEntityIndex(out int i))
                    readPositions.Execute(i);
            }
            else
            {
                WritePositions writePositions;
                writePositions.sendBuffer = sendBuffer;
                writePositions.localTransforms = chunk.GetNativeArray(ref localTransformType);
                writePositions.characterBodies = chunk.GetNativeArray(ref characterBodyType);
                writePositions.remotePositions = chunk.GetBufferAccessor(ref remotePositionType);
                while (iterator.NextEntityIndex(out int i))
                    writePositions.Execute(i);
            }
        }
    }

    private ComponentTypeHandle<RemoteIdentity> __remoteIdentityType;

    private ComponentTypeHandle<KinematicCharacterBody> __characterBodyType;

    private ComponentTypeHandle<ThirdPersonCharacterControl> __thirdPersonCharacterControlType;

    private ComponentTypeHandle<LocalTransform> __localTransformType;

    private BufferTypeHandle<RemoteEffectTargetDamage> __effectTargetDamageType;

    private BufferTypeHandle<RemoteEffectTargetHP> __effectTargetHPType;

    private BufferTypeHandle<RemotePosition> __remotePositionType;

    private EntityQuery __effectTargetGroup;
    private EntityQuery __positionGroup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __remoteIdentityType = state.GetComponentTypeHandle<RemoteIdentity>(true);
        __characterBodyType =  state.GetComponentTypeHandle<KinematicCharacterBody>(true);
        __thirdPersonCharacterControlType = state.GetComponentTypeHandle<ThirdPersonCharacterControl>();
        __localTransformType = state.GetComponentTypeHandle<LocalTransform>();
        __effectTargetDamageType = state.GetBufferTypeHandle<RemoteEffectTargetDamage>();
        __effectTargetHPType = state.GetBufferTypeHandle<RemoteEffectTargetHP>();
        __remotePositionType = state.GetBufferTypeHandle<RemotePosition>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __effectTargetGroup =
                builder.WithAny<RemoteEffectTargetDamage, RemoteEffectTargetHP>().Build(ref state);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __positionGroup =
                builder.WithAll<LocalTransform, RemotePosition>().Build(ref state);

        state.RequireForUpdate<NetworkClientDriver>();
        state.RequireForUpdate<ReplyMessages>();

        var messages = new ReplyMessages(Allocator.Persistent);

        state.EntityManager.CreateSingleton(messages);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        SystemAPI.GetSingleton<ReplyMessages>().Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var messages = SystemAPI.GetSingleton<ReplyMessages>();
        var driver = SystemAPI.GetSingleton<NetworkClientDriver>();
        var client = driver.instance.AsMessages();
        var clientBuffer = driver.instance.buffer;
        
        Collect collect;
        collect.inputs = client;
        collect.outputs = messages;
        var jobHandle = collect.ScheduleByRef(state.Dependency);
        
        var sendBuffer = driver.sendBuffer.AsParallelWriter();
        
        __remoteIdentityType.Update(ref state);
        __effectTargetDamageType.Update(ref state);
        __effectTargetHPType.Update(ref state);

        ReplyEffectTargets replyEffectTargets;
        replyEffectTargets.sendBuffer = sendBuffer;
        replyEffectTargets.messages = messages;
        replyEffectTargets.clientBuffer = clientBuffer;
        replyEffectTargets.remoteIdentityType = __remoteIdentityType;
        replyEffectTargets.effectTargetDamageType = __effectTargetDamageType;
        replyEffectTargets.effectTargetHPType = __effectTargetHPType;
        jobHandle = replyEffectTargets.ScheduleParallelByRef(__effectTargetGroup, jobHandle);

        __characterBodyType.Update(ref state);
        __thirdPersonCharacterControlType.Update(ref state);
        __localTransformType.Update(ref state);
        __remotePositionType.Update(ref state);

        ReplyPositions replyPositions;
        replyPositions.sendBuffer = sendBuffer;
        replyPositions.messages = messages;
        replyPositions.clientBuffer = clientBuffer;
        replyPositions.remoteIdentityType = __remoteIdentityType;
        replyPositions.characterBodyType = __characterBodyType;
        replyPositions.thirdPersonCharacterControlType = __thirdPersonCharacterControlType;
        replyPositions.localTransformType = __localTransformType;
        replyPositions.remotePositionType = __remotePositionType;
        jobHandle = replyPositions.ScheduleParallelByRef(__positionGroup, jobHandle);

        state.Dependency = jobHandle;
        SystemAPI.SetSingleton(messages);
    }
}
