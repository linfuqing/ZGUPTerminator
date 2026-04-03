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

[BurstCompile, UpdateInGroup(typeof(PresentationSystemGroup)), UpdateAfter(typeof(NetworkClientSystem))]
public partial struct ReplyMessageSystem : ISystem
{
    [Flags]
    private enum ReadEffectTargetFlag
    {
        Damage = 0x01, 
        HP = 0x02
    }
    
    [BurstCompile]
    private struct Collect : IJob
    {
        [ReadOnly]
        public NetworkClient.Messages inputs;
        public NetworkClientSendBuffer sendBuffer;
        public ReplyMessages outputs;

        public void Execute()
        {
            outputs.Collect(inputs, ref sendBuffer);
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

        public ReadEffectTargetFlag Execute(int index)
        {
            ReadEffectTargetFlag result = 0;
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
                    result |= ReadEffectTargetFlag.Damage;
                    
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

                foreach (var message in messages.GetValues(ReplyMessageType.HP, id, clientBuffer))
                {
                    if(!messageElements.IsCreated)
                        messageElements = new NativeList<NetworkClient.MessageElement>(Allocator.Temp);
                    
                    messageElements.Add(message);
                }

                if (messageElements.IsCreated && messageElements.Length > 0)
                {
                    result |= ReadEffectTargetFlag.HP;

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

            return result;
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

        public ComponentTypeHandle<EffectTargetDamage> effectTargetDamageType;

        public ComponentTypeHandle<EffectTargetHP> effectTargetHPType;

        public BufferTypeHandle<RemoteEffectTargetDamage> remoteEffectTargetDamageType;

        public BufferTypeHandle<RemoteEffectTargetHP> remoteEffectTargetHPType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            if (chunk.Has(ref remoteIdentityType))
            {
                ReadEffectTargets readEffectTargets;
                readEffectTargets.messages = messages;
                readEffectTargets.clientBuffer = clientBuffer;
                readEffectTargets.remoteIdentities = chunk.GetNativeArray(ref remoteIdentityType);
                readEffectTargets.effectTargetDamages = chunk.GetBufferAccessor(ref remoteEffectTargetDamageType);
                readEffectTargets.effectTargetHPs = chunk.GetBufferAccessor(ref remoteEffectTargetHPType);

                ReadEffectTargetFlag flag;
                while (iterator.NextEntityIndex(out int i))
                {
                    flag = readEffectTargets.Execute(i);
                    if((flag & ReadEffectTargetFlag.Damage) == ReadEffectTargetFlag.Damage)
                        chunk.SetComponentEnabled(ref effectTargetDamageType, i, true);
                    
                    if((flag & ReadEffectTargetFlag.HP) == ReadEffectTargetFlag.HP)
                        chunk.SetComponentEnabled(ref effectTargetHPType, i, true);
                }
            }
            else
            {
                WriteEffectTargets writeEffectTargets;
                writeEffectTargets.sendBuffer = sendBuffer;
                writeEffectTargets.effectTargetDamages = chunk.GetBufferAccessor(ref remoteEffectTargetDamageType);
                writeEffectTargets.effectTargetHPs = chunk.GetBufferAccessor(ref remoteEffectTargetHPType);

                while (iterator.NextEntityIndex(out int i))
                    writeEffectTargets.Execute(i);
            }
        }
    }

    private struct ReadPositions
    {
        public const float MAX_DISTANCE_SQ = 10.0F;
        
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

            int numRemotePositions;
            RemotePosition remotePosition;
            var remotePositions = this.remotePositions[index];
            if (messageElements.IsCreated)
            {
                messageElements.Sort();

                DataStreamReader reader;
                var streamCompressionModel = StreamCompressionModel.Default;
                int maxRemotePositionIndex = remotePositions.Length - 1, i;
                foreach (var messageElement in messageElements)
                {
                    reader = messageElement.reader;

                    numRemotePositions = reader.ReadPackedInt(streamCompressionModel);
                    for (i = 0; i < numRemotePositions; ++i)
                    {
                        remotePosition = new RemotePosition(ref reader, streamCompressionModel);
                        if (maxRemotePositionIndex < 0 ||
                            remotePositions[maxRemotePositionIndex].type == RemotePosition.Type.Key)
                        {
                            remotePositions.Add(remotePosition);

                            ++maxRemotePositionIndex;
                        }
                        else
                            remotePositions[maxRemotePositionIndex] = remotePosition;
                    }
                }

                messageElements.Dispose();
            }

            numRemotePositions = remotePositions.Length;
            if (numRemotePositions > 0)
            {
                var localTransform = localTransforms[index];

                float3 position = remotePositions[numRemotePositions - 1].value;
                if (math.distancesq(localTransform.Position, position) >
                    MAX_DISTANCE_SQ)
                {
                    localTransform.Position = position;
                    localTransforms[index] = localTransform;

                    thirdPersonCharacterControls[index] = default;
                    
                    remotePositions.Clear();
                }
                else
                {
                    remotePosition = remotePositions[0];
                    if (RemotePosition.Type.Warp == remotePosition.type)
                    {
                        localTransform.Position = remotePosition.value;
                        localTransforms[index] = localTransform;

                        thirdPersonCharacterControls[index] = default;
                        
                        remotePositions.RemoveAt(0);
                    }
                    else
                    {
                        var thirdPersonCharacterControl = thirdPersonCharacterControls[index];

                        thirdPersonCharacterControl.MoveVector =
                            remotePosition.value -
                            localTransform.Position;
                        if (!__ClampToMaxLength(ref thirdPersonCharacterControl.MoveVector) &&
                            numRemotePositions > 1)
                            remotePositions.RemoveAt(0);

                        thirdPersonCharacterControls[index] = thirdPersonCharacterControl;
                    }
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
            remotePosition.value = localTransform.Position;
            
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

    private ComponentTypeHandle<EffectTargetDamage> __effectTargetDamageType;

    private ComponentTypeHandle<EffectTargetHP> __effectTargetHPType;

    private BufferTypeHandle<RemoteEffectTargetDamage> __remoteEffectTargetDamageType;

    private BufferTypeHandle<RemoteEffectTargetHP> __remoteEffectTargetHPType;

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
        __effectTargetDamageType = state.GetComponentTypeHandle<EffectTargetDamage>();
        __effectTargetHPType = state.GetComponentTypeHandle<EffectTargetHP>();
        __remoteEffectTargetDamageType = state.GetBufferTypeHandle<RemoteEffectTargetDamage>();
        __remoteEffectTargetHPType = state.GetBufferTypeHandle<RemoteEffectTargetHP>();
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
        collect.sendBuffer = driver.sendBuffer;
        collect.outputs = messages;
        var jobHandle = collect.ScheduleByRef(state.Dependency);

        if (LevelPlayerShared<RemotePlayer>.isOnline)
        {
            var sendBuffer = driver.sendBuffer.AsParallelWriter();

            __remoteIdentityType.Update(ref state);
            __effectTargetDamageType.Update(ref state);
            __effectTargetHPType.Update(ref state);
            __remoteEffectTargetDamageType.Update(ref state);
            __remoteEffectTargetHPType.Update(ref state);

            ReplyEffectTargets replyEffectTargets;
            replyEffectTargets.sendBuffer = sendBuffer;
            replyEffectTargets.messages = messages;
            replyEffectTargets.clientBuffer = clientBuffer;
            replyEffectTargets.remoteIdentityType = __remoteIdentityType;
            replyEffectTargets.effectTargetDamageType = __effectTargetDamageType;
            replyEffectTargets.effectTargetHPType = __effectTargetHPType;
            replyEffectTargets.remoteEffectTargetDamageType = __remoteEffectTargetDamageType;
            replyEffectTargets.remoteEffectTargetHPType = __remoteEffectTargetHPType;
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
        }

        state.Dependency = jobHandle;
        SystemAPI.SetSingleton(messages);
    }
}
