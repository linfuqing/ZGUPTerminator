using Unity.Collections;
using Unity.Networking.Transport;

/// <summary>
/// Single source of truth for the pipeline every bot connection uses: the UTP null
/// pipeline (no pipeline stages).
///
/// Rationale (see Documentation~/MultiplayerBot/联机机器人-架构约束.md):
/// - server→bot datagrams carry only transport framing, so inbound decode strips one
///   fixed offset (catalog.inboundAppPayloadOffset) — no per-message byte scanning, and
///   nothing to re-derive if framing ever changes (re-capture re-measures that single
///   offset automatically).
/// - there is no ReliableSequenced send window to advance, so the single-driver bot
///   (which never emits link-layer ACKs) can no longer stall the server's send queue.
///   This removes the NetworkSendQueueFull (-5) flood at its root.
///
/// Bots publish <see cref="Null"/> into <c>NetworkRelayServerInjectSingleton.pipelines[userID]</c>;
/// the relay server's send path (<c>NetworkServerSendBuffer.Send</c>) then resolves
/// <c>pipelines.TryGetValue(userID) ? that : Pipeline</c>, so bot connections send on the null
/// pipeline while human clients keep the default reliable+fragmentation pipeline.
/// </summary>
internal static class BotRelayPipeline
{
    public static NetworkPipeline Null => default;

    /// <summary>
    /// The bot listen driver is added to the relay server's <c>MultiNetworkDriver</c>
    /// (<c>NetworkRelayServerManager.server.AddDriver</c>), which requires every member driver to
    /// expose the SAME pipeline count as the server's udp/ws drivers — otherwise AddDriver throws
    /// "driver must have 1 pipelines, but has 0". The server builds exactly one pipeline from
    /// <c>NetworkRelayServerManager._stages</c> = { Fragmentation, ReliableSequenced }, so the
    /// listen driver must create the matching single pipeline before it is added.
    ///
    /// This pipeline only satisfies the count/index invariant of the multi-driver; it is never the
    /// pipeline bots actually send on — bot traffic still resolves to <see cref="Null"/> through the
    /// per-userID <c>pipelines</c> map above.
    /// </summary>
    public static NetworkPipeline CreateServerMatchingPipeline(ref NetworkDriver driver)
    {
        var stages = new NativeArray<NetworkPipelineStageId>(2, Allocator.Temp);
        stages[0] = NetworkPipelineStageId.Get<FragmentationPipelineStage>();
        stages[1] = NetworkPipelineStageId.Get<ReliableSequencedPipelineStage>();
        var pipeline = driver.CreatePipeline(stages);
        stages.Dispose();
        return pipeline;
    }
}
