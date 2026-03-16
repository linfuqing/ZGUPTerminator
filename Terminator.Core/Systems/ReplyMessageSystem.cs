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

    private NativeParallelMultiHashMap<MessageKey, NetworkClient.Message> __values;

    public static void WriteHeader(ref DataStreamWriter writer, ReplyMessageType messageType)
    {
        writer.WriteReplyHeader((int)messageType, NetworkRelayType.Channel);
    }

    public ReplyMessages(in AllocatorManager.AllocatorHandle allocator)
    {
        __values = new NativeParallelMultiHashMap<MessageKey, NetworkClient.Message>(1, allocator);
    }

    public void Dispose()
    {
        __values.Dispose();
    }

    public NativeParallelMultiHashMap<MessageKey, NetworkClient.Message>.Enumerator GetValues(ReplyMessageType type, uint id)
    {
        MessageKey key;
        key.type = type;
        key.id = id;
        return __values.GetValuesForKey(key);
    }

    public void Collect(in NetworkClient.Messages messages)
    {
        __values.Clear();

        int replayType, size;
        MessageKey key;
        NetworkClient.Message value;
        DataStreamReader reader;
        var streamCompressionModel = StreamCompressionModel.Default;
        foreach (var element in messages)
        {
            if(NetworkClientMessageType.Data != element.Message.type)
                continue;

            reader = element.reader;

            key.type = (ReplyMessageType)reader.ReadPackedInt(streamCompressionModel);
            if(key.type < ReplyMessageType.Move || key.type >= ReplyMessageType.PlayerProperty)
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
                __values.Add(key, value);
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
            outputs.Collect(inputs);
        }
    }

    private struct ReadEffectTargets
    {
        [ReadOnly]
        public NetworkClient.Messages client;
        
        [ReadOnly] 
        public ReplyMessages messages;
        
        [ReadOnly] 
        public NativeArray<RemoteIdentity> remoteIdentities;

        public NativeArray<RemoteEffectTargetDamage> effectTargetDamages;

        public NativeArray<RemoteEffectTargetHP> effectTargetHPs;

        public void Execute(int index)
        {
            uint id = remoteIdentities[index].id;

            StreamCompressionModel streamCompressionModel = StreamCompressionModel.Default;
            if (index < effectTargetDamages.Length)
            {
                RemoteEffectTargetDamage effectTargetDamage = default;

                int offset = 0;
                DataStreamReader reader;
                foreach (var message in messages.GetValues(ReplyMessageType.Damage, id))
                {
                    if (message.offset > offset)
                    {
                        offset = message.offset;

                        reader = new NetworkClient.MessageElement(message, client).reader;

                        effectTargetDamage = new RemoteEffectTargetDamage(ref reader, streamCompressionModel);
                    }
                }
                
                effectTargetDamages[index] = effectTargetDamage;
            }
            
            if (index < effectTargetHPs.Length)
            {
                RemoteEffectTargetHP effectTargetHP = default;

                int offset = 0;
                DataStreamReader reader;
                foreach (var message in messages.GetValues(ReplyMessageType.Damage, id))
                {
                    if (message.offset > offset)
                    {
                        offset = message.offset;

                        reader = new NetworkClient.MessageElement(message, client).reader;

                        effectTargetHP = new RemoteEffectTargetHP(ref reader, streamCompressionModel);
                    }
                }
                
                effectTargetHPs[index] = effectTargetHP;
            }
        }
    }

    private struct WriteEffectTargets
    {
        public NetworkClientSendBuffer.ParallelWriter sendBuffer;

        [ReadOnly]
        public NativeArray<RemoteEffectTargetDamage> effectTargetDamages;

        [ReadOnly]
        public NativeArray<RemoteEffectTargetHP> effectTargetHPs;

        public void Execute(int index)
        {
            var effectTargetDamage = effectTargetDamages[index];
            if (!effectTargetDamage.value.isEmpty)
            {
                if (sendBuffer.BeginWrite(0, out var writer))
                {
                    writer.WriteReplyHeader((int)ReplyMessageType.Damage, NetworkRelayType.Channel);
                    effectTargetDamage.Write(ref writer, StreamCompressionModel.Default);
                    
                    sendBuffer.EndWrite(writer);
                }
            }
            
            var effectTargetHP = effectTargetHPs[index];
            if (!effectTargetHP.value.isEmpty)
            {
                if (sendBuffer.BeginWrite(0, out var writer))
                {
                    writer.WriteReplyHeader((int)ReplyMessageType.HP, NetworkRelayType.Channel);
                    effectTargetHP.Write(ref writer, StreamCompressionModel.Default);
                    
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
        public NetworkClient.Messages client;
        
        [ReadOnly] 
        public ReplyMessages messages;
        
        [ReadOnly] 
        public ComponentTypeHandle<RemoteIdentity> remoteIdentityType;

        public ComponentTypeHandle<RemoteEffectTargetDamage> effectTargetDamageType;

        public ComponentTypeHandle<RemoteEffectTargetHP> effectTargetHPType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            if (chunk.Has(ref remoteIdentityType))
            {
                ReadEffectTargets readEffectTargets;
                readEffectTargets.client = client;
                readEffectTargets.messages = messages;
                readEffectTargets.remoteIdentities = chunk.GetNativeArray(ref remoteIdentityType);
                readEffectTargets.effectTargetDamages = chunk.GetNativeArray(ref effectTargetDamageType);
                readEffectTargets.effectTargetHPs = chunk.GetNativeArray(ref effectTargetHPType);

                while (iterator.NextEntityIndex(out int i))
                    readEffectTargets.Execute(i);
            }
            else
            {
                WriteEffectTargets writeEffectTargets;
                writeEffectTargets.sendBuffer = sendBuffer;
                writeEffectTargets.effectTargetDamages = chunk.GetNativeArray(ref effectTargetDamageType);
                writeEffectTargets.effectTargetHPs = chunk.GetNativeArray(ref effectTargetHPType);

                while (iterator.NextEntityIndex(out int i))
                    writeEffectTargets.Execute(i);
            }
        }
    }

    private struct ReadPositions
    {
        [ReadOnly]
        public NetworkClient.Messages client;
        
        [ReadOnly] 
        public ReplyMessages messages;

        [ReadOnly] 
        public NativeArray<RemoteIdentity> remoteIdentities;

        public NativeArray<ThirdPersonCharacterControl> thirdPersonCharacterControls;

        public NativeArray<LocalTransform> localTransforms;

        public BufferAccessor<RemotePosition> remotePositions;

        public void Execute(int index)
        {
            var remotePositions = this.remotePositions[index];
            RemotePosition remotePosition;
            DataStreamReader reader;
            var streamCompressionModel = StreamCompressionModel.Default;
            int maxRemotePositionIndex = remotePositions.Length - 1, numRemotePositions, i;
            foreach (var message in messages.GetValues(ReplyMessageType.Move, remoteIdentities[index].id))
            {
                reader = new NetworkClient.MessageElement(message, client).reader;

                numRemotePositions = reader.ReadPackedInt(streamCompressionModel);
                for (i = 0; i < numRemotePositions; ++i)
                {
                    remotePosition = new RemotePosition(ref reader, streamCompressionModel);
                    if (maxRemotePositionIndex >= 0 &&
                        remotePositions[maxRemotePositionIndex].type == RemotePosition.Type.Key)
                    {
                        remotePositions.Add(remotePosition);

                        ++maxRemotePositionIndex;
                    }
                    else
                        remotePositions[maxRemotePositionIndex] = remotePosition;
                }
            }

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
            if (sendBuffer.BeginWrite(0, out var writer))
            {
                writer.WriteReplyHeader((int)ReplyMessageType.Move, NetworkRelayType.Channel);
                var streamCompressionModel = StreamCompressionModel.Default;

                var remotePositions = this.remotePositions[index];
                int numRemotePositions = remotePositions.Length;
                writer.WritePackedInt(numRemotePositions + 1, streamCompressionModel);
                for(int i = 0; i < numRemotePositions; ++i)
                    remotePositions[i].Write(ref writer, streamCompressionModel);
                remotePositions.Clear();
                
                RemotePosition remotePosition;
                var characterBody = index < characterBodies.Length ? characterBodies[index] : default;
                remotePosition.type =
                    math.dot(characterBody.GroundingUp, characterBody.RelativeVelocity) > math.FLT_MIN_NORMAL
                        ? RemotePosition.Type.Key
                        : RemotePosition.Type.Normal;
                var localTransform = localTransforms[index];
                remotePosition.value = math.float2(localTransform.Position.x, localTransform.Position.z);
                remotePosition.Write(ref writer, streamCompressionModel);
                    
                sendBuffer.EndWrite(writer);
            }
        }
    }

    [BurstCompile]
    private struct ReplyPositions : IJobChunk
    {
        public NetworkClientSendBuffer.ParallelWriter sendBuffer;
        
        [ReadOnly]
        public NetworkClient.Messages client;
        
        [ReadOnly] 
        public ReplyMessages messages;

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
                readPositions.client = client;
                readPositions.messages = messages;
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

    private ComponentTypeHandle<RemoteEffectTargetDamage> __effectTargetDamageType;

    private ComponentTypeHandle<RemoteEffectTargetHP> __effectTargetHPType;

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
        __effectTargetDamageType = state.GetComponentTypeHandle<RemoteEffectTargetDamage>();
        __effectTargetHPType = state.GetComponentTypeHandle<RemoteEffectTargetHP>();
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
        replyEffectTargets.client = client;
        replyEffectTargets.messages = messages;
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
        replyPositions.client = client;
        replyPositions.messages = messages;
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
