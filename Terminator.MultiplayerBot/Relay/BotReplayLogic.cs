using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ZG;

[BurstCompile]
public static class BotReplayLogic
{
    public static void PrepareRuntime(
        BlobAssetReference<BotRecordingBlob> recording,
        ref BotReplayRuntimeState runtime)
    {
        runtime.timestampBase = __ComputeTimestampBase(recording);
        runtime.nextFrameIndex = __SkipStartupFrames(recording, runtime.timestampBase);
        runtime.playbackTime = 0;
        runtime.flags = BotReplayRuntimeFlags.Loaded;
    }

    public static void Begin(ref BotReplayRuntimeState runtime)
    {
        runtime.playbackTime = 0;
        runtime.flags |= BotReplayRuntimeFlags.Playing;
    }

    // Job-internal helper: intentionally no method-level [BurstCompile]. The enclosing Burst jobs
    // compile this call transitively. A standalone direct-call wrapper falls back through
    // Stop$BurstManaged on Linux/Mono and requires a MonoPInvokeCallback that Burst ILPP does not
    // emit for this signature, aborting SquadJoin before the InSquad state commit.
    public static void Stop(ref BotReplayRuntimeState runtime)
    {
        runtime.catalogIndex = BotReplayRuntimeState.InvalidCatalogIndex;
        runtime.nextFrameIndex = 0;
        runtime.timestampBase = 0;
        runtime.playbackTime = 0;
        runtime.flags = BotReplayRuntimeFlags.None;
    }

    // Called only from BotRelayReplayTickJob (or managed tests). Keep this as a normal static call
    // so Linux/Mono managed job fallback never enters a standalone $BurstDirectCall wrapper.
    public static void Tick(
        int agentIndex,
        ref BotRelayFarmNative farm,
        in NativeArray<BotReplayCatalogEntry> catalogEntries,
        double deltaTime,
        ref NativeQueue<BotRelayInject>.ParallelWriter injectWriter,
        byte injectEnabled)
    {
        ref var runtime = ref farm.replayRuntime.ElementAt(agentIndex);
        if ((runtime.flags & BotReplayRuntimeFlags.Playing) == 0)
            return;

        if (!catalogEntries.IsCreated ||
            runtime.catalogIndex < 0 ||
            runtime.catalogIndex >= catalogEntries.Length)
            return;

        var recording = catalogEntries[runtime.catalogIndex].recording;
        if (!recording.IsCreated)
            return;

        ref var blob = ref recording.Value;
        if (blob.frames.Length == 0)
            return;

        // Advance by the full server delta and consume every frame that is now eligible on the
        // recorded timeline. Runtime throttling must not split a same-timestamp recording batch or
        // carry eligible input into later ticks: malformed/collapsed timelines belong to the
        // RecordingAudit gate, while replay remains a faithful consumer of an audited recording.
        if (deltaTime < 0)
            deltaTime = 0;

        runtime.playbackTime += deltaTime;

        var playbackElapsed = runtime.playbackTime;
        while (runtime.nextFrameIndex < blob.frames.Length)
        {
            ref readonly var meta = ref blob.frames[runtime.nextFrameIndex];
            if (meta.timestamp - runtime.timestampBase > playbackElapsed)
                break;

            if (meta.payloadLength > 0 &&
                BotReplayPayloadUtility.TryCopyFrameToPacket(recording, runtime.nextFrameIndex, out var packet) &&
                BotReplayPayloadUtility.TryGetReplyMessageType(in packet, out var messageType) &&
                __IsInLevelReplayable(messageType))
            {
                // Only in-level gameplay reply messages are replayed. Lobby/control frames the
                // recording may contain (ChapterStage / Play / Status / Join / Invite) must NOT be
                // re-sent: once injected they reach the server, get relayed to the host and echoed
                // back, driving a feedback storm that saturates the send queue (NetworkSendQueueFull).
                // PlayerProperty is handled once via TrySendFirstPlayerProperty, not here.
                // The production path is app-layer injection. Keep the old outbound slot only for
                // the explicit no-injection test/fallback path; otherwise it fills with packets that
                // no longer have a UTP flush owner and can incorrectly make later legacy writes fail.
                if (injectEnabled != 0)
                {
                    if (BotRelayInjectOps.InjectAppPacket(
                            ref injectWriter,
                            farm.agentStates[agentIndex].userID,
                            in packet))
                    {
                        BotRelayInboundProbe.RecordAppInjected();
                    }
                }
                else
                {
                    BotRelaySlotOps.TryEnqueueOutbound(
                        agentIndex,
                        farm.outbound,
                        farm.outboundCount,
                        in packet);
                }
            }

            ++runtime.nextFrameIndex;
        }

        BotRelayInboundProbe.RecordReplayFrames(runtime.nextFrameIndex, blob.frames.Length);

        if (runtime.nextFrameIndex >= blob.frames.Length)
            runtime.flags &= ~BotReplayRuntimeFlags.Playing;
    }

    // In-level gameplay reply messages a co-op remote player emits during a stage. Everything
    // else in a recording (lobby navigation: ChapterStage/Play/Status/Join, plus Invite and the
    // once-only PlayerProperty) must never be replayed onto the live channel.
    private static bool __IsInLevelReplayable(ReplyMessageType messageType)
    {
        switch (messageType)
        {
            case ReplyMessageType.Chat:
            case ReplyMessageType.Camera:
            case ReplyMessageType.Move:
            case ReplyMessageType.Damage:
            case ReplyMessageType.SelectSkill:
                return true;
            default:
                return false;
        }
    }

    private static double __ComputeTimestampBase(BlobAssetReference<BotRecordingBlob> recording)
    {
        ref var blob = ref recording.Value;
        for (int i = 0; i < blob.frames.Length; ++i)
        {
            ref readonly var meta = ref blob.frames[i];
            if (meta.payloadLength == 0)
                continue;

            if (!BotReplayPayloadUtility.TryGetReplyMessageType(recording, i, out var messageType))
                continue;

            if (messageType != ReplyMessageType.PlayerProperty)
                return meta.timestamp;
        }

        return 0;
    }

    private static int __SkipStartupFrames(BlobAssetReference<BotRecordingBlob> recording, double timestampBase)
    {
        ref var blob = ref recording.Value;
        int index = 0;
        while (index < blob.frames.Length && blob.frames[index].timestamp < timestampBase)
            ++index;

        return index;
    }
}

public static class BotReplayLoadUtility
{
    public static bool TrySendFirstPlayerProperty(
        ref BotRelayFarmNative farm,
        int agentIndex,
        BlobAssetReference<BotRecordingBlob> recording,
        ref NativeQueue<BotRelayInject>.ParallelWriter injectWriter,
        byte injectEnabled)
    {
        if (!recording.IsCreated)
            return false;

        ref var blob = ref recording.Value;
        for (int i = 0; i < blob.frames.Length; ++i)
        {
            if (!BotReplayPayloadUtility.TryGetReplyMessageType(recording, i, out var messageType) ||
                messageType != ReplyMessageType.PlayerProperty)
                continue;

            if (!BotReplayPayloadUtility.TryCopyFrameToPacket(recording, i, out var packet))
                return false;

            // The host marks the bot RemotePlayer as Joined only when it receives a PlayerProperty.
            // Production must report the injection result, not whether an obsolete outbound UTP slot
            // had room; otherwise a successful injection is treated as a failed lobby handshake.
            if (injectEnabled != 0)
            {
                if (!BotRelayCodec.TryBuildPlayerPropertyPacket(in packet, out var injectPacket))
                    return false;

                return BotRelayInjectOps.InjectAppPacket(
                    ref injectWriter,
                    farm.agentStates[agentIndex].userID,
                    in injectPacket);
            }

            // Explicit no-injection fallback only. Production never allocates a managed payload in
            // ReplayLoadJob; this conversion remains for the retired outbound-wire test path.
            var payload = new byte[packet.length];
            using (var native = new NativeArray<byte>(packet.length, Allocator.Temp))
            {
                if (!packet.TryCopyTo(native))
                    return false;

                native.CopyTo(payload);
            }

            return BotRelaySlotOps.TryWritePlayerPropertyBody(ref farm, agentIndex, payload);
        }

        return false;
    }
}
