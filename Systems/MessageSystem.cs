using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Jobs;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup)), UpdateBefore(typeof(InstanceSystem))]
public partial class MessageSystem : SystemBase
{
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
    
    private BufferLookup<Message> __messages;
    
    private EntityQuery __group;

    private NativeList<Entity> __entities;

    protected override void OnCreate()
    {
        base.OnCreate();

        __messages = GetBufferLookup<Message>();

        __entities = new NativeList<Entity>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        __entities.Dispose();
    }

    protected override void OnUpdate()
    {
        __entities.Clear();
        
        CompleteDependency();
        
        Entities.ForEach((
                Entity entity, 
                ref DynamicBuffer<Message> messages, 
                ref DynamicBuffer<MessageParameter> parameters, 
                in CopyMatrixToTransformInstanceID instanceID) =>
            {
                int numMessages = messages.Length;
                if (numMessages > 0)
                {
                    Object messageValue;
                    var transform = Resources.InstanceIDToObject(instanceID.value) as Transform;
                    if (transform != null)
                    {
                        for (int i = 0; i < numMessages; ++i)
                        {
                            ref var message = ref messages.ElementAt(i);
                            switch (message.value.LoadingStatus)
                            {
                                case ObjectLoadingStatus.None:
                                    if (message.value.IsReferenceValid)
                                        message.value.LoadAsync();
                                    else
                                    {
                                        __InvokeParameter(message.key, null, ref parameters);

                                        transform.BroadcastMessage(message.name.ToString(), null);

                                        messages.RemoveAtSwapBack(i--);

                                        --numMessages;
                                    }

                                    break;
                                case ObjectLoadingStatus.Completed:
                                    messageValue = message.value.Result;

                                    __InvokeParameter(message.key, messageValue as IMessage, ref parameters);

                                    //Debug.LogError($"Send message {message.name} : {messageValue} to {transform}", transform);
                                    
                                    transform.BroadcastMessage(message.name.ToString(), messageValue);

                                    //message.value.Release();

                                    messages.RemoveAtSwapBack(i--);

                                    --numMessages;
                                    break;
                                case ObjectLoadingStatus.Error:

                                    Debug.LogError($"Message {message.name} Error!");

                                    __InvokeParameter(message.key, null, ref parameters);

                                    messages.RemoveAtSwapBack(i--);

                                    --numMessages;
                                    break;
                            }
                        }
                    }
                }

                if(numMessages < 1)
                    __entities.Add(entity);
            })
            .WithAll<Message, CopyMatrixToTransformInstanceID>()
            .WithoutBurst()
            .WithStoreEntityQueryInField(ref __group)
            .Run();
        
        __messages.Update(this);

        Disable disable;
        disable.entities = __entities.AsArray();
        disable.messages = __messages;
        Dependency = disable.ScheduleByRef(__entities.Length, 4, Dependency);
    }

    private static void __InvokeParameter(int messageKey, IMessage message, ref DynamicBuffer<MessageParameter> parameters)
    {
        if (message == null)
            return;

        message.Clear();

        int numParameters = parameters.IsCreated ? parameters.Length : 0;
        for (int i = 0; i < numParameters; ++i)
        {
            ref var parameter = ref parameters.ElementAt(i);
            if(parameter.messageKey != messageKey)
                continue;
            
            message.Set(parameter.id, parameter.value);
            
            parameters.RemoveAt(i--);

            --numParameters;
        }
    }
}
