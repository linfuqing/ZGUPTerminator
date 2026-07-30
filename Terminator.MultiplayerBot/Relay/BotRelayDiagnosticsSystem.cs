using Unity.Entities;
using UnityEngine;
using ZG;

/// <summary>
/// Managed relay telemetry (Burst jobs cannot emit Debug.Log reliably).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
[UpdateAfter(typeof(BotRelayPostTickSystem))]
public partial struct BotRelayDiagnosticsSystem : ISystem
{
    private int __prevRouteEnqueued;
    private int __prevInboundDequeued;
    private int __prevRouteZero;
    private int __prevSendPkts;
    private int __prevInboundApps;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BotRelayWorldTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!BotRelayDefines.VerboseTelemetry)
            return;

        // Never Complete ManagerAccessHandle here — ReceiveJob/SendJob and farm burst jobs share it;
        // blocking the main thread deadlocks when ReceiveJob is still draining the listen queue.
        ref var manager = ref BotRelayManager.Instance;
        if (!manager.IsCreated)
            return;

        bool changed =
            manager.diagRouteSendEnqueued != __prevRouteEnqueued ||
            manager.diagInboundDequeued != __prevInboundDequeued ||
            manager.diagRouteSendZeroPort != __prevRouteZero ||
            manager.diagSendJobPackets != __prevSendPkts ||
            manager.diagInboundAppsEnqueued != __prevInboundApps;

        if (!changed)
            return;

        Debug.Log($"[BotRelay] Telemetry {manager.BuildDiagnosticsSnapshotNoSync()}");

        __prevRouteEnqueued = manager.diagRouteSendEnqueued;
        __prevInboundDequeued = manager.diagInboundDequeued;
        __prevRouteZero = manager.diagRouteSendZeroPort;
        __prevSendPkts = manager.diagSendJobPackets;
        __prevInboundApps = manager.diagInboundAppsEnqueued;
    }
}
