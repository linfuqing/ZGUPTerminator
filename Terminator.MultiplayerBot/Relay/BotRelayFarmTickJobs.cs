using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ZG
{
    [BurstCompile]
    internal struct BotRelayPreTickBurstJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public BotRelayFarmNative farm;
        [ReadOnly] public BlobAssetReference<BotRelayCatalogBlob> catalogBlob;

        public void Execute(int index)
        {
            if (!catalogBlob.IsCreated)
                return;

            ref var manager = ref BotRelayManager.Instance;
            ref var catalog = ref catalogBlob.Value;
            BotRelayBurstWireOps.PreTickAgent(index, ref farm, ref manager, ref catalog);
        }
    }

    [BurstCompile]
    internal struct BotRelayPostDrainBurstJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public BotRelayFarmNative farm;
        [ReadOnly] public BlobAssetReference<BotRelayCatalogBlob> catalogBlob;

        public void Execute(int index)
        {
            if (!catalogBlob.IsCreated)
                return;

            ref var manager = ref BotRelayManager.Instance;
            ref var catalog = ref catalogBlob.Value;
            BotRelayBurstWireOps.DrainInbound(index, ref farm, ref manager, ref catalog);
        }
    }

    [BurstCompile]
    internal struct BotRelayPostAgentJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public BotRelayFarmNative farm;
        public BotRelayFarmTickConfig tickConfig;

        public void Execute(int index)
        {
            BotRelaySlotOps.PumpInboundBurst(index, ref farm);
            BotAgentLogic.Execute(index, ref farm, in tickConfig);
        }
    }

    [BurstCompile]
    internal struct BotRelayMatchDispatchJob : IJob
    {
        public BotRelayFarmNative farm;
        public BotRelayFarmTickConfig tickConfig;
        public NetworkRelayServer.ReadOnly server;

        public void Execute()
        {
            BotAgentLogic.TickMatchDispatch(ref farm, in tickConfig, in server);
        }
    }

    [BurstCompile]
    internal struct BotRelayPostFlushBurstJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public BotRelayFarmNative farm;
        [ReadOnly] public BlobAssetReference<BotRelayCatalogBlob> catalogBlob;

        public void Execute(int index)
        {
            if (!catalogBlob.IsCreated)
                return;

            ref var manager = ref BotRelayManager.Instance;
            ref var catalog = ref catalogBlob.Value;
            BotRelayBurstWireOps.PumpLinkLayerAcks(index, ref farm, ref manager, ref catalog);
        }
    }
}
