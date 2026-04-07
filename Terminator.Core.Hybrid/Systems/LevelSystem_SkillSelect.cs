using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities;
using ZG;

public partial class LevelSystemManaged
{
    public enum SkillSelectionStatus
    {
        None, 
        Begin, 
        End, 
        Finish
    }
    
    private struct SkillVersion : IEquatable<LevelSkillVersion>
    {
        public int value;

        public int index;

        public static implicit operator SkillVersion(LevelSkillVersion value)
        {
            SkillVersion result;
            result.value = value.value;
            result.index = value.index;

            return result;
        }

        public bool Equals(LevelSkillVersion other)
        {
            return value == other.value && index == other.index;
        }
    }

    /*[BurstCompile]
    private struct RestoreSkills : IJob
    {
        [ReadOnly] 
        public NetworkClient.Messages client;
        [ReadOnly] 
        public ReplyMessages messages;

        public NativeList<LevelSkill> skills;

        public void Execute()
        {
            int numSkills;
            DataStreamReader reader;
            var streamCompressionModel = StreamCompressionModel.Default;
            foreach (var message in messages.GetValues(ReplyMessageType.SelectSkill, LevelPlayerShared<RemotePlayer>.id))
            {
                reader = new NetworkClient.MessageElement(message, client).reader;
                numSkills = reader.ReadPackedInt(streamCompressionModel);
                for (int i = 0; i < numSkills; ++i)
                    skills.Add(new LevelSkill(ref reader, streamCompressionModel));
            }
        }
    }*/

    private struct SelectSkills
    {
        public BlobAssetReference<SkillDefinition> definition;
        [ReadOnly] 
        public ReplyMessages messages;
        [ReadOnly] 
        public NativeList<byte> clientBuffer;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<RemoteIdentity> remoteIdentities;
        
        public NativeParallelMultiHashMap<Entity, int> skillIndices;

        public BufferAccessor<SkillActiveIndex> activeIndices;
        
        public BufferAccessor<BulletStatus> bulletStates;
        
        public void Execute(int index)
        {
            NativeList<NetworkClient.MessageElement> messageElements = default;
            foreach (var message in messages.GetValues(ReplyMessageType.SelectSkill, remoteIdentities[index].id, clientBuffer))
            {
                if(!messageElements.IsCreated)
                    messageElements = new NativeList<NetworkClient.MessageElement>(Allocator.Temp);
                    
                messageElements.Add(message);
            }
            
            if (!messageElements.IsCreated)
                return;
            
            messageElements.Sort();
            
            Entity entity = entityArray[index];
            var bulletStates = this.bulletStates[index];
            //bulletStates.Clear();
            
            var activeIndices = this.activeIndices[index];

            /*foreach (var activeIndex in activeIndices)
                skillIndices.Add(entity, activeIndex.value);*/
            
            ref var definition = ref this.definition.Value;

            int i, numSkills;
            LevelSkill skill;
            DataStreamReader reader;
            StreamCompressionModel streamCompressionModel = StreamCompressionModel.Default;
            foreach(var messageElement in messageElements)
            {
                reader = messageElement.reader;
                numSkills = reader.ReadPackedInt(streamCompressionModel);
                for (i = 0; i < numSkills; ++i)
                {
                    skill = new LevelSkill(ref reader, streamCompressionModel);
                    if (skill.activeIndex != -1)
                        skillIndices.Add(entity, activeIndices[skill.activeIndex].value);

                    skill.Apply(ref activeIndices, ref bulletStates, ref definition);
                }
            }

            messageElements.Dispose();
        }
    }

#if !DEBUG
    [BurstCompile]
#endif
    private struct RemotePlayerSelectSkillsEx : IJobChunk
    {
        public BlobAssetReference<SkillDefinition> definition;
        
        [ReadOnly] 
        public NativeList<byte> clientBuffer;
        
        [ReadOnly] 
        public ReplyMessages messages;
        
        [ReadOnly]
        public EntityTypeHandle entityType;
        
        [ReadOnly]
        public ComponentTypeHandle<RemoteIdentity> remoteIdentityType;
        
        public NativeParallelMultiHashMap<Entity, int> skillIndices;

        public BufferTypeHandle<SkillActiveIndex> activeIndexType;
        
        public BufferTypeHandle<BulletStatus> bulletStatusType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            SelectSkills selectSkills;
            selectSkills.definition = definition;
            selectSkills.clientBuffer = clientBuffer;
            selectSkills.messages = messages;
            selectSkills.entityArray = chunk.GetNativeArray(entityType);
            selectSkills.remoteIdentities = chunk.GetNativeArray(ref remoteIdentityType);
            selectSkills.skillIndices = skillIndices;
            selectSkills.activeIndices = chunk.GetBufferAccessor(ref activeIndexType);
            selectSkills.bulletStates = chunk.GetBufferAccessor(ref bulletStatusType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                selectSkills.Execute(i);
        }
    }

    private struct SkillSelectionRemotePlayer
    {
        private EntityQuery __removePlayerGroup;

        private ComponentTypeHandle<RemoteIdentity> __remoteIdentityType;

        private BufferTypeHandle<BulletStatus> __bulletStatusType;
        private BufferTypeHandle<SkillActiveIndex> __activeIndexType;

        private NativeParallelMultiHashMap<Entity, int> __skillIndices;

        public SkillSelectionRemotePlayer(SystemBase system)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __removePlayerGroup = builder
                    .WithAll<RemoteIdentity>()
                    .WithAllRW<SkillActiveIndex, BulletStatus>()
                    .Build(system);

            system.RequireForUpdate<ReplyMessages>();
            //system.RequireForUpdate<NetworkClientDriver>();

            __remoteIdentityType = system.GetComponentTypeHandle<RemoteIdentity>(true);
            __bulletStatusType = system.GetBufferTypeHandle<BulletStatus>();
            __activeIndexType = system.GetBufferTypeHandle<SkillActiveIndex>();

            __skillIndices = new NativeParallelMultiHashMap<Entity, int>(1, Allocator.Persistent);
        }

        public void Dispose()
        {
            __skillIndices.Dispose();
        }

        public void Apply(
            in BlobAssetReference<SkillDefinition> definition, 
            in NetworkClient networkClient, 
            in ReplyMessages clientMessages, 
            LevelSystemManaged system)
        {
            if (networkClient.isCreated && !__removePlayerGroup.IsEmpty)
            {
                __skillIndices.Clear();

                system.__entityType.Update(system);
                __remoteIdentityType.Update(system);
                __bulletStatusType.Update(system);
                __activeIndexType.Update(system);

                RemotePlayerSelectSkillsEx remotePlayerSelectSkills;
                remotePlayerSelectSkills.definition = definition;
                remotePlayerSelectSkills.clientBuffer = networkClient.buffer;
                remotePlayerSelectSkills.messages = clientMessages;
                remotePlayerSelectSkills.entityType = system.__entityType;
                remotePlayerSelectSkills.remoteIdentityType = __remoteIdentityType;
                remotePlayerSelectSkills.skillIndices = __skillIndices;
                remotePlayerSelectSkills.bulletStatusType = __bulletStatusType;
                remotePlayerSelectSkills.activeIndexType = __activeIndexType;

                remotePlayerSelectSkills.RunByRef(__removePlayerGroup);

                if (!__skillIndices.IsEmpty)
                {
                    var skillIndices = new NativeList<int>(Allocator.TempJob);
                    var remotePlayers = __skillIndices.GetKeyArray(Allocator.Temp);
                    remotePlayers.Sort();

                    int numRemotePlayers = remotePlayers.Unique();
                    Entity remotePlayer;
                    for (int i = 0; i < numRemotePlayers; ++i)
                    {
                        remotePlayer = remotePlayers[i];

                        skillIndices.Clear();
                        foreach (var skillIndex in __skillIndices.GetValuesForKey(remotePlayer))
                            skillIndices.Add(skillIndex);

                        system.__DestroyBullets(
                            remotePlayer,
                            skillIndices.AsArray());
                    }

                    skillIndices.Dispose();
                    remotePlayers.Dispose();
                }
            }
        }
    }

    private struct SkillSelection
    {
        public SkillSelectionStatus status;
        
        public SkillVersion version;

        public SkillSelectionRemotePlayer remotePlayer;
        //private int __stage;
    
        public SkillSelection(SystemBase system)
        {
            status = SkillSelectionStatus.None;
            version = default;
            remotePlayer = new SkillSelectionRemotePlayer(system);
        }

        public void Dispose()
        {
            remotePlayer.Dispose();
            //__spriteRefCounts.Dispose();
        }

        public void Reset()
        {
            version = default;
        }

        public void Release()
        {
            status = SkillSelectionStatus.None;
        }
    }

    private SkillSelection __skillSelection;

    private void __UpdateSkillSelection(
        ref DynamicBuffer<SkillActiveIndex> activeIndices, 
        in DynamicBuffer<SkillStatus> states, 
        //in DynamicBuffer<LevelSkillDesc> descs, 
        in BlobAssetReference<SkillDefinition> definition, 
        in BlobAssetReference<LevelSkillNameDefinition> nameDefinition, 
        in Entity player, 
        //int stage, 
        LevelManager manager)
    {
        //__skillSelection.SetStage(stage, this);

        if (manager.isRestart || !SystemAPI.Exists(player))
        {
            __skillSelection.Release();
            __skillSelection.Reset();

            return;
        }

        if(SystemAPI.TryGetSingleton<NetworkClientDriver>(out var networkClientDriver))
            __skillSelection.remotePlayer.Apply(
                definition,
                networkClientDriver.instance,
                SystemAPI.GetSingleton<ReplyMessages>(), this);
        
        if (SystemAPI.HasComponent<LevelSkillVersion>(player))
        {
            var skillVersion = SystemAPI.GetComponent<LevelSkillVersion>(player);

            bool isWaiting;
            switch (__skillSelection.status)
            {
                case SkillSelectionStatus.End:
                case SkillSelectionStatus.Finish:
                    isWaiting = true;
                    break;
                default:
                    isWaiting = __skillSelection.version.Equals(skillVersion);
                    break;
            }
            
            if (isWaiting)
            {
                if (SystemAPI.IsBufferEnabled<LevelSkill>(player))
                {
                    var skills = SystemAPI.GetBuffer<LevelSkill>(player);
                    var selectedSkillIndices = manager.CollectSelectedSkillIndices();
                    if (skills.IsEmpty || selectedSkillIndices != null)
                    {
                        if (__skillSelection.status == SkillSelectionStatus.End)
                            __skillSelection.status = SkillSelectionStatus.Finish;

                        if (definition.IsCreated)
                        {
                            int numSelectedSkillIndices = selectedSkillIndices == null ? 0 : selectedSkillIndices.Length;
                            if (numSelectedSkillIndices > 0)
                            {
                                var skillIndices = new NativeList<int>(
                                    numSelectedSkillIndices * (activeIndices.Length + 1),
                                    Allocator.TempJob);
                                skillIndices.CopyFromNBC(selectedSkillIndices);

                                var bulletStates = SystemAPI.GetBuffer<BulletStatus>(player);
                                LevelSkill.Apply(
                                    skillIndices,
                                    skills,
                                    ref activeIndices,
                                    ref bulletStates,
                                    //ref SystemAPI.GetComponent<BulletDefinitionData>(player).definition.Value,
                                    ref definition.Value,
                                    ref skillIndices);

                                int numActiveSkillIndices = skillIndices.Length;
                                if (numActiveSkillIndices > numSelectedSkillIndices)
                                    __DestroyBullets(
                                        player,
                                        skillIndices.AsArray()
                                            .GetSubArray(numSelectedSkillIndices,
                                                numActiveSkillIndices - numSelectedSkillIndices));

                                var sendBuffer = networkClientDriver.sendBuffer;
                                if (sendBuffer.isCreated && sendBuffer.BeginWrite(0, out var writer))
                                {
                                    var streamCompressionModel = StreamCompressionModel.Default;
                                    writer.WriteReplyHeader((int)ReplyMessageType.SelectSkill, NetworkRelayType.Channel);
                                    //writer.WriteInt(skillVersion.value);
                                    writer.WritePackedInt(numSelectedSkillIndices, streamCompressionModel);
                                    for(int i = 0; i < numSelectedSkillIndices; ++i)
                                        skills[skillIndices[i]].Write(ref writer, streamCompressionModel);
                                    
                                    sendBuffer.EndWrite(writer);
                                }
                                
                                skillIndices.Dispose();
                            }
                        }

                        SystemAPI.SetBufferEnabled<LevelSkill>(player, false);
                    }
                }
            }
            else if(nameDefinition.IsCreated)
            {
                ref var skillAssetNames = ref nameDefinition.Value.skills;
                var skills = SystemAPI.GetBuffer<LevelSkill>(player);
                //LevelSkillDesc desc, activeDesc;
                //SkillAsset asset;
                int numSkills = skills.Length;
                bool isAllDone = true;
                for (int i = 0; i < numSkills; ++i)
                {
                    ref var skill = ref skills.ElementAt(i);
                    if (skill.originIndex != -1)
                    {
                        //activeDesc = descs[skill.originIndex];
                        //if (!activeDesc.WaitForCompletion())
                        if(!SkillManager.TryGetAsset(skillAssetNames[skill.originIndex].ToString(), out _))
                        {
                            isAllDone = false;

                            break;
                            //__skillSelection.Retain(activeDesc);
                        }
                    }

                    //desc = descs[skill.index];
                    //if (!desc.WaitForCompletion())
                    if(!SkillManager.TryGetAsset(skillAssetNames[skill.index].ToString(), out _))
                    {
                        isAllDone = false;

                        break;
                        //__skillSelection.Retain(desc);
                    }
                }

                if (isAllDone)
                {
                    if (skillVersion.index == 0)
                    {
                        __skillSelection.status = SkillSelectionStatus.Begin;

                        manager.SelectSkillBegin(skillVersion.selection, skillVersion.timeScale);
                    }

                    if (numSkills > 0)
                    {
                        LevelSkillData result;
                        var results = new List<LevelSkillData>(numSkills);
                        var skillNames = new Dictionary<int, string>(numSkills);
                        for (int i = 0; i < numSkills; ++i)
                        {
                            ref var skill = ref skills.ElementAt(i);
                            result.parentName = null;
                            if (skill.originIndex != -1)
                            {
                                if (!skillNames.TryGetValue(skill.originIndex, out result. /*value.*/name))
                                {
                                    result.selectIndex = -1;
                                    result.name = skillAssetNames[skill.originIndex].ToString();
                                    //SkillManager.TryGetAsset(result.name, out result.value);

                                    results.Add(result);

                                    skillNames.Add(skill.originIndex, result. /*value.*/name);
                                }

                                result.parentName = result. /*value.*/name;
                            }

                            result.selectIndex = i;
                            result.name = skillAssetNames[skill.index].ToString();
                            //SkillManager.TryGetAsset(result.name, out result.value);

                            results.Add(result);

                            skillNames.Add(skill.index, result. /*value.*/name);
                        }
                        
                        manager.SelectSkills(skillVersion.priority, results.ToArray());
                    }
                    
                    if (skillVersion.index + 1 == skillVersion.count)
                    {
                        manager.SelectSkillEnd();

                        __skillSelection.status = SkillSelectionStatus.End;
                    }

                    __skillSelection.version = skillVersion;
                }
            }
        }

        if (manager.isClear && 
            manager.selectedSkillSelectionIndex == -1 && 
            SkillSelectionStatus.Finish == __skillSelection.status)
            __skillSelection.Release();
    }
}
