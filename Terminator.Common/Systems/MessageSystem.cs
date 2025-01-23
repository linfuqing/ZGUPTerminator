using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Jobs;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true), UpdateBefore(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial class MessageSystem : SystemBase
{
    private struct Collect
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        
        public BufferAccessor<MessageParameter> inputParameters;
        
        public BufferAccessor<Message> inputMessages;
        
        public NativeParallelMultiHashMap<Entity, Message> outputMessages;
        
        public NativeParallelMultiHashMap<int, MessageParameter> outputParameters;

        public NativeList<Entity> entitiesToDisable;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var parameters = index < inputParameters.Length ? inputParameters[index] : default;
            var messages = inputMessages[index];
            int numMessages = messages.Length;
            if (numMessages > 0)
            {
                int numParameters, i, j;
                for (i = 0; i < numMessages; ++i)
                {
                    ref var message = ref messages.ElementAt(i);
                    switch (message.value.LoadingStatus)
                    {
                        case ObjectLoadingStatus.None:
                            if (message.value.GetHashCode() == 0)
                            {
                                __Collect(entity, message, ref parameters);

                                messages.RemoveAtSwapBack(i--);

                                --numMessages;
                            }
                            else
                                message.value.LoadAsync();

                            break;
                        case ObjectLoadingStatus.Completed:
                            __Collect(entity, message, ref parameters);

                            messages.RemoveAtSwapBack(i--);

                            --numMessages;
                            break;
                        case ObjectLoadingStatus.Error:
                            Debug.LogError($"Message {message.name} Error!");

                            if (message.key != 0)
                            {
                                numParameters = parameters.IsCreated ? parameters.Length : 0;
                                for (j = 0; j < numParameters; ++j)
                                {
                                    ref var parameter = ref parameters.ElementAt(i);
                                    if (parameter.messageKey != message.key)
                                        continue;

                                    parameters.RemoveAt(i--);

                                    --numParameters;
                                }
                            }
                            
                            messages.RemoveAtSwapBack(i--);

                            --numMessages;
                            break;
                    }
                }
            }

            if(numMessages < 1)
                entitiesToDisable.Add(entity);
        }

        private void __Collect(in Entity entity, in Message message, ref DynamicBuffer<MessageParameter> parameters)
        {
            outputMessages.Add(entity, message);

            if (message.key != 0)
            {
                int numParameters = parameters.IsCreated ? parameters.Length : 0;
                for (int i = 0; i < numParameters; ++i)
                {
                    ref var parameter = ref parameters.ElementAt(i);
                    if (parameter.messageKey != message.key)
                        continue;

                    outputParameters.Add(parameter.messageKey, parameter);

                    parameters.RemoveAt(i--);

                    --numParameters;
                }
            }
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;
        
        public BufferTypeHandle<MessageParameter> inputParameterType;
        
        public BufferTypeHandle<Message> inputMessageType;
        
        public NativeParallelMultiHashMap<Entity, Message> outputMessages;
        
        public NativeParallelMultiHashMap<int, MessageParameter> outputParameters;

        public NativeList<Entity> entitiesToDisable;

        public void Execute(
            in ArchetypeChunk chunk, 
            int unfilteredChunkIndex, 
            bool useEnabledMask, 
            in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.inputParameters = chunk.GetBufferAccessor(ref inputParameterType);
            collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
            collect.outputMessages = outputMessages;
            collect.outputParameters = outputParameters;
            collect.entitiesToDisable = entitiesToDisable;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }
    
    [BurstCompile]
    private struct Disable : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<Entity> entities;
        
        [NativeDisableParallelForRestriction]
        public BufferLookup<Message> messages;

        public void Execute(int index)
        {
            messages.SetBufferEnabled(entities[index], false);
        }
    }
    
    private EntityTypeHandle __entityType;
    
    private ComponentLookup<CopyMatrixToTransformInstanceID> __instanceIDs;

    private ComponentLookup<MessageParent> __parents;

    private BufferLookup<Message> __messages;
    
    private BufferTypeHandle<Message> __instanceType;

    private BufferTypeHandle<MessageParameter> __parameterType;
        
    private EntityQuery __group;

    private NativeList<Entity> __entitiesToDisable;

    private NativeParallelMultiHashMap<Entity, Message> __instances;
        
    private NativeParallelMultiHashMap<int, MessageParameter> __parameters;

    protected override void OnCreate()
    {
        base.OnCreate();

        __instanceIDs = GetComponentLookup<CopyMatrixToTransformInstanceID>(true);
        __parents = GetComponentLookup<MessageParent>(true);
        __messages = GetBufferLookup<Message>();
        __instanceType = GetBufferTypeHandle<Message>();
        __parameterType = GetBufferTypeHandle<MessageParameter>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<Message>()
                .Build(this);
        
        //RequireForUpdate(__group);

        __entitiesToDisable = new NativeList<Entity>(Allocator.Persistent);
        __instances = new NativeParallelMultiHashMap<Entity, Message>(1, Allocator.Persistent);
        __parameters = new NativeParallelMultiHashMap<int, MessageParameter>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        __entitiesToDisable.Dispose();
        __instances.Dispose();
        __parameters.Dispose();
    }

    protected override void OnUpdate()
    {
        __entitiesToDisable.Clear();
        //__instances.Clear();
        //__parameters.Clear();
        
        CompleteDependency();

        __entityType.Update(this);
        __instanceType.Update(this);
        __parameterType.Update(this);
        
        CollectEx collect;
        collect.entityType = __entityType;
        collect.inputMessageType = __instanceType;
        collect.inputParameterType = __parameterType;
        collect.outputMessages = __instances;
        collect.outputParameters = __parameters;
        collect.entitiesToDisable = __entitiesToDisable;
        collect.RunByRef(__group);

        if (!__instances.IsEmpty)
        {
            __parents.Update(this);
            
            Entities.ForEach((
                Entity entity, 
                in CopyMatrixToTransformInstanceID instanceID) =>
                {
                    MessageParent parent = default;
                    if (__instances.TryGetFirstValue(entity, out var message, out var iterator) ||
                        __parents.TryGetComponent(entity, out parent) &&
                        __instances.TryGetFirstValue(parent.entity, out message, out iterator))
                    {
                        var transform = Resources.InstanceIDToObject(instanceID.value) as Transform;
                        do
                        {
                            __Send(message, transform);
                        } while (__instances.TryGetNextValue(out message, ref iterator));

                        __instances.Remove(parent.entity == Entity.Null ? entity : parent.entity);
                    }
                })
            .WithAll<CopyMatrixToTransformInstanceID>()
            .WithAny<Message, MessageParent>()
            .WithoutBurst()
            .Run();

            if (!__instances.IsEmpty)
            {
                using (var keys = __instances.GetKeyArray(Allocator.Temp))
                {
                    Transform transform;
                    CopyMatrixToTransformInstanceID instanceID;
                    __instanceIDs.Update(this);
                    foreach (var key in keys)
                    {
                        if(!__instanceIDs.TryGetComponent(key, out instanceID))
                            continue;

                        transform = Resources.InstanceIDToObject(instanceID.value) as Transform;
 
                        foreach (var message in __instances.GetValuesForKey(key))
                            __Send(message, transform);
                        
                        __instances.Remove(key);
                    }
                }
            }
        }
        
        __messages.Update(this);

        Disable disable;
        disable.entities = __entitiesToDisable.AsArray();
        disable.messages = __messages;
        Dependency = disable.ScheduleByRef(__entitiesToDisable.Length, 4, Dependency);
    }

    private void __Send(in Message message, Transform transform)
    {
        var messageValue = message.value.IsReferenceValid ? message.value.Result : null;
        if (message.key != 0)
        {
            if (messageValue is IMessage temp)
            {
                temp.Clear();

                foreach (var parameter in __parameters.GetValuesForKey(message.key))
                    temp.Set(parameter.id, parameter.value);
            }

            __parameters.Remove(message.key);
        }

        if(transform != null)
            transform.BroadcastMessage(message.name.ToString(), messageValue);
    }
}
