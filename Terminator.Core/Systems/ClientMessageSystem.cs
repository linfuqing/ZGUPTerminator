using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

public enum ClientMessageType
{
    SelectSkill = 100
}

public struct ClientMessages : IComponentData
{
    public NativeParallelMultiHashMap<int, NetworkClient.Message> values;
}

[BurstCompile, UpdateInGroup(typeof(PresentationSystemGroup)), UpdateAfter(typeof(NetworkClientSystem))]
public partial struct ClientMessageSystem : ISystem
{
    [BurstCompile]
    private struct Collect : IJob
    {
        [ReadOnly]
        public NetworkClient.Messages inputs;
        public NativeParallelMultiHashMap<int, NetworkClient.Message> outputs;

        public void Execute()
        {
            outputs.Clear();
            
            var streamCompressionModel = StreamCompressionModel.Default;
            foreach (var element in inputs)
            {
                if(NetworkClientMessageType.Data != element.Message.type)
                    continue;

                outputs.Add(element.reader.ReadPackedInt(streamCompressionModel), element.Message);
            }
        }
    }

    private NativeParallelMultiHashMap<int, NetworkClient.Message> __messages;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __messages = new NativeParallelMultiHashMap<int, NetworkClient.Message>(1, Allocator.Persistent);
        
        state.RequireForUpdate<NetworkClientDriver>();

        ClientMessages clientMessages;
        clientMessages.values = __messages;
        state.EntityManager.CreateSingleton(clientMessages);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __messages.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Collect collect;
        collect.inputs = SystemAPI.GetSingleton<NetworkClientDriver>().instance.AsMessages();
        collect.outputs = __messages;
        state.Dependency = collect.ScheduleByRef(state.Dependency);
        
        ClientMessages clientMessages;
        clientMessages.values = __messages;
        SystemAPI.SetSingleton(clientMessages);
    }
}
