using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

public struct RemotePosition : IBufferElementData
{
    public uint id;
}

public struct ClientMessages : IComponentData
{
    public enum MessageType
    {
        Move = 100, 
        Damage, 
        HP, 
        
        SelectSkill, 
        Total
    }

    public struct MessageKey : IEquatable<MessageKey>
    {
        public MessageType type;

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

    public ClientMessages(in AllocatorManager.AllocatorHandle allocator)
    {
        __values = new NativeParallelMultiHashMap<MessageKey, NetworkClient.Message>(1, allocator);
    }

    public void Dispose()
    {
        __values.Dispose();
    }

    public NativeParallelMultiHashMap<MessageKey, NetworkClient.Message>.Enumerator GetValues(MessageType type, uint id)
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

            key.type = (MessageType)reader.ReadPackedInt(streamCompressionModel);
            if(key.type < MessageType.Move || key.type >= MessageType.Total)
                continue;
                
            replayType = reader.ReadPackedInt(streamCompressionModel);
            UnityEngine.Assertions.Assert.AreEqual((int)NetworkRelayType.Channel, replayType);

            key.id = reader.ReadPackedUInt(streamCompressionModel);

            size = reader.GetBytesRead();

            value = element.Message;
            value.offset += size;
            value.size -= size;
            __values.Add(key, value);
        }
    }
}

[BurstCompile, UpdateInGroup(typeof(PresentationSystemGroup)), UpdateAfter(typeof(NetworkClientSystem))]
public partial struct ClientMessageSystem : ISystem
{
    [BurstCompile]
    private struct Collect : IJob
    {
        [ReadOnly]
        public NetworkClient.Messages inputs;
        public ClientMessages outputs;

        public void Execute()
        {
            outputs.Collect(inputs);
        }
    }

    [BurstCompile]
    private struct ReadEffectTargets
    {
        [ReadOnly]
        public NetworkClient.Messages client;
        
        [ReadOnly] 
        public ClientMessages messages;
        
        [ReadOnly] 
        public NativeArray<EffectTargetRemote> effectTargets;

        public NativeArray<EffectTargetDamageRemote> effectTargetDamages;

        public NativeArray<EffectTargetHPRemote> effectTargetHPs;

        public void Execute(int index)
        {
            uint id = effectTargets[index].id;

            StreamCompressionModel streamCompressionModel = StreamCompressionModel.Default;
            if (index < effectTargetDamages.Length)
            {
                EffectTargetDamageRemote effectTargetDamage = default;

                int offset = 0;
                DataStreamReader reader;
                foreach (var message in messages.GetValues(ClientMessages.MessageType.Damage, id))
                {
                    if (message.offset > offset)
                    {
                        offset = message.offset;

                        reader = new NetworkClient.MessageElement(message, client).reader;

                        effectTargetDamage = new EffectTargetDamageRemote(ref reader, streamCompressionModel);
                    }
                }
                
                effectTargetDamages[index] = effectTargetDamage;
            }
            
            if (index < effectTargetHPs.Length)
            {
                EffectTargetHPRemote effectTargetHP = default;

                int offset = 0;
                DataStreamReader reader;
                foreach (var message in messages.GetValues(ClientMessages.MessageType.Damage, id))
                {
                    if (message.offset > offset)
                    {
                        offset = message.offset;

                        reader = new NetworkClient.MessageElement(message, client).reader;

                        effectTargetHP = new EffectTargetHPRemote(ref reader, streamCompressionModel);
                    }
                }
                
                effectTargetHPs[index] = effectTargetHP;
            }
        }
    }

    [BurstCompile]
    private struct ReadEffectTargetsEx : IJobChunk
    {
        [ReadOnly]
        public NetworkClient.Messages client;
        
        [ReadOnly] 
        public ClientMessages messages;
        
        [ReadOnly] 
        public ComponentTypeHandle<EffectTargetRemote> effectTargetType;

        public ComponentTypeHandle<EffectTargetDamageRemote> effectTargetDamageType;

        public ComponentTypeHandle<EffectTargetHPRemote> effectTargetHPType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ReadEffectTargets readEffectTargets;
            readEffectTargets.client = client;
            readEffectTargets.messages = messages;
            readEffectTargets.effectTargets = chunk.GetNativeArray(ref effectTargetType);
            readEffectTargets.effectTargetDamages = chunk.GetNativeArray(ref effectTargetDamageType);
            readEffectTargets.effectTargetHPs = chunk.GetNativeArray(ref effectTargetHPType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                readEffectTargets.Execute(i);
        }
    }

    private struct ReadPlayers
    {
        [ReadOnly]
        public NetworkClient.Messages client;
        
        [ReadOnly] 
        public ClientMessages messages;

        [ReadOnly] 
        public NativeArray<EffectTargetRemote> effectTargets;

        public NativeArray<RemotePlayer> remotePlayers;

        public NativeArray<ThirdPersonPlayerInputs> thirdPersonPlayerInputs;

        public void Execute(int index)
        {
            DataStreamReader reader;
            foreach (var message in messages.GetValues(ClientMessages.MessageType.Move, effectTargets[index].id))
            {
                reader = new NetworkClient.MessageElement(message, client).reader;
                
                
            }
        }
    }

    private struct SubmitPlayer
    {
        [ReadOnly]
        public NativeArray<LocalPlayer> localPlayers;
        
        public NativeArray<EffectTargetDamageRemote> damageTargets;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkClientDriver>();
        state.RequireForUpdate<ClientMessages>();

        var messages = new ClientMessages(Allocator.Persistent);

        state.EntityManager.CreateSingleton(messages);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        SystemAPI.GetSingleton<ClientMessages>().Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var messages = SystemAPI.GetSingleton<ClientMessages>();
        
        Collect collect;
        collect.inputs = SystemAPI.GetSingleton<NetworkClientDriver>().instance.AsMessages();
        collect.outputs = messages;
        state.Dependency = collect.ScheduleByRef(state.Dependency);
        
        SystemAPI.SetSingleton(messages);
    }
}
