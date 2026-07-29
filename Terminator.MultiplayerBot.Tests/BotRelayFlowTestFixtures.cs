using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using ZG;

internal static class BotRelayFlowTestFixtures
{
    public const uint RealPlayerUserId = BotRelayWireTestFixtures.RealPlayerUserId;
    public const uint BotUserId = BotRelayWireTestFixtures.BotUserId;
    public const uint SquadInviteId = 42u;
    public const int MatchLevel = 0;

    public static BotRelayFarmTickConfig CreateTickConfig(double elapsedTime = 0, float inviteDelayMax = 0f)
    {
        return new BotRelayFarmTickConfig
        {
            inviteTimeoutMin = 0f,
            inviteTimeoutMax = inviteDelayMax,
            remoteOfflineLeaveTimeout = (float)BotAgentLogic.DefaultRemoteOfflineLeaveTimeoutSeconds,
            elapsedTime = elapsedTime,
            frameSeed = 1u
        };
    }

    public static BotRelayFarmNative CreateFarm(uint botUserId = BotUserId)
    {
        var farm = BotRelayFarmNative.Create(1, Allocator.TempJob);
        farm.agentStates[0] = new BotAgentState
        {
            state = BotState.Idle,
            userID = botUserId,
            stateEnterTime = 0,
            matchLevel = MatchLevel,
            remoteOfflineSince = -1.0
        };
        farm.transport[0] = new BotRelayTransportState
        {
            header = BotRelayWireTestFixtures.BuildBotHeader()
        };
        farm.inboxSentInitialStatus[0] = 1;
        farm.nextIdleMatchTime[0] = 0;
        return farm;
    }

    public static void EnqueueEvent(int agentIndex, ref BotRelayFarmNative farm, in BotRelayEvent evt)
    {
        int count = farm.eventCount[agentIndex];
        if (count >= BotRelayFarmNative.MaxEventsPerAgent)
            throw new InvalidOperationException("event queue full");

        farm.events[agentIndex * BotRelayFarmNative.MaxEventsPerAgent + count] = evt;
        farm.eventCount[agentIndex] = count + 1;
    }

    public static ClientMessageSquadInviteToRead BuildPublicInvite(uint squadInviteId = SquadInviteId)
    {
        return new ClientMessageSquadInviteToRead
        {
            channel = ClientChannel.Public,
            squadInviteID = squadInviteId,
            levelID = 1,
            stage = 0
        };
    }

    public static BotRelayPacket BuildMatchApp(int matchId = 7, int level = MatchLevel)
    {
        if (!BotRelayWireTestFixtures.TryBuildInboundMatchApp(matchId, level, out var app))
            throw new InvalidOperationException("Failed to build inbound match app.");
        return app;
    }

    public static BotRelayPacket BuildMismatchApp()
    {
        if (!BotRelayWireTestFixtures.TryBuildInboundMismatchApp(out var app))
            throw new InvalidOperationException("Failed to build inbound mismatch app.");
        return app;
    }

    public static BotRelayPacket BuildApplyMatchApp(uint senderId = RealPlayerUserId) =>
        __BuildClientHeaderApp(ClientMessageType.ApplyMatch, senderId);

    public static BotRelayPacket BuildPlayApp(uint levelId = 1, int stage = 0, uint senderId = RealPlayerUserId)
    {
        if (!BotRelayWireTestFixtures.TryBuildInboundPlayApp(levelId, stage, senderId, out var app))
            throw new InvalidOperationException("Failed to build inbound play app.");
        return app;
    }

    public static BotRelayPacket BuildJoinChannelApp(uint squadInviteId = SquadInviteId, uint remoteUserId = RealPlayerUserId)
    {
        if (!BotRelayWireTestFixtures.TryBuildInboundJoinApp(squadInviteId, remoteUserId, out var app))
            throw new InvalidOperationException("Failed to build inbound join app.");
        return app;
    }

    public static bool TryReadInjectMessageType(in BotRelayInject inject, out int type)
    {
        type = 0;
        int len = inject.packet.length;
        if (len <= 0)
            return false;

        var bytes = new NativeArray<byte>(len, Allocator.Temp);
        if (!inject.packet.TryCopyTo(bytes))
            return false;

        var reader = new DataStreamReader(bytes);
        type = reader.ReadPackedInt(StreamCompressionModel.Default);
        bytes.Dispose();
        return true;
    }

    public static bool TryReadFirstMessageType(in BotRelayPacket packet, out int type)
    {
        type = 0;
        if (packet.IsEmpty)
            return false;

        using var bytes = new NativeArray<byte>(packet.length, Allocator.Temp);
        packet.TryCopyTo(bytes);
        var reader = new DataStreamReader(bytes);
        type = reader.ReadPackedInt(StreamCompressionModel.Default);
        return true;
    }

    public static bool TryReadMatchDistance(in BotRelayPacket packet, out int distance)
    {
        distance = 0;
        if (packet.IsEmpty)
            return false;

        using var bytes = new NativeArray<byte>(packet.length, Allocator.Temp);
        packet.TryCopyTo(bytes);
        var reader = new DataStreamReader(bytes);
        var model = StreamCompressionModel.Default;
        if (reader.ReadPackedInt(model) != (int)NetworkRelayMessageType.Match)
            return false;

        var match = new NetworkRelayMatch(ref reader, in model);
        distance = match.distance;
        return true;
    }

    public static int GetOutboundCount(ref BotRelayFarmNative farm, int agentIndex) =>
        farm.outboundCount[agentIndex];

    public static BotRelayPacket GetOutboundPacket(ref BotRelayFarmNative farm, int agentIndex, int index)
    {
        int baseIndex = agentIndex * BotRelayFarmNative.MaxOutboundPerAgent;
        return farm.outbound[baseIndex + index];
    }

    public static BlobAssetReference<BotRecordingBlob> CreateSyntheticRecording(out int damageFrameCount)
    {
        damageFrameCount = 2;
        var propertyPayload = __BuildRecordedPayload(ReplyMessageType.PlayerProperty, new byte[] { 0x01 });
        var damagePayload = __BuildRecordedPayload(ReplyMessageType.Damage, new byte[] { 0x02, 0x03 });

        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<BotRecordingBlob>();
        var frames = builder.Allocate(ref root.frames, 3);
        var payloadBytes = builder.Allocate(ref root.payloadBytes, propertyPayload.Length + damagePayload.Length * 2);

        int offset = 0;
        __WriteFrame(ref frames[0], payloadBytes, ref offset, 0f, propertyPayload);
        __WriteFrame(ref frames[1], payloadBytes, ref offset, 0.5f, damagePayload);
        __WriteFrame(ref frames[2], payloadBytes, ref offset, 1f, damagePayload);

        return builder.CreateBlobAssetReference<BotRecordingBlob>(Allocator.Persistent);
    }

    /// <summary>
    /// Builds a recording that mirrors real in-level traffic: a startup PlayerProperty at t=0, then
    /// <paramref name="moveFrameCount"/> Move frames evenly spaced at <paramref name="frameSpacing"/>
    /// seconds, with a single SelectSkill substituted near the middle. Used to verify replay pacing
    /// (frames must emit over the recording's real duration, not dumped) and that a rare frame like
    /// SelectSkill is emitted exactly once per playthrough.
    /// </summary>
    public static BlobAssetReference<BotRecordingBlob> CreateInLevelReplayRecording(
        int moveFrameCount,
        float frameSpacing,
        out int whitelistedFrameCount,
        out float duration)
    {
        var propertyPayload = __BuildRecordedPayload(ReplyMessageType.PlayerProperty, new byte[] { 0x01 });
        var movePayload = __BuildRecordedPayload(ReplyMessageType.Move, new byte[] { 0x10, 0x11, 0x12, 0x13 });
        var skillPayload = __BuildRecordedPayload(ReplyMessageType.SelectSkill, new byte[] { 0x20 });

        int totalFrames = moveFrameCount + 1;
        int skillFrame = 1 + moveFrameCount / 2;

        int payloadTotal = propertyPayload.Length;
        for (int i = 1; i < totalFrames; ++i)
            payloadTotal += (i == skillFrame ? skillPayload.Length : movePayload.Length);

        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<BotRecordingBlob>();
        var frames = builder.Allocate(ref root.frames, totalFrames);
        var payloadBytes = builder.Allocate(ref root.payloadBytes, payloadTotal);

        int offset = 0;
        __WriteFrame(ref frames[0], payloadBytes, ref offset, 0f, propertyPayload);
        for (int i = 1; i < totalFrames; ++i)
        {
            var payload = i == skillFrame ? skillPayload : movePayload;
            __WriteFrame(ref frames[i], payloadBytes, ref offset, i * frameSpacing, payload);
        }

        whitelistedFrameCount = moveFrameCount; // Move frames + the single SelectSkill substitute.
        duration = moveFrameCount * frameSpacing;
        return builder.CreateBlobAssetReference<BotRecordingBlob>(Allocator.Persistent);
    }

    public static NativeArray<BotReplayCatalogEntry> CreateSyntheticCatalog(
        BlobAssetReference<BotRecordingBlob> recording,
        Allocator allocator)
    {
        var entries = new NativeArray<BotReplayCatalogEntry>(1, allocator);
        entries[0] = new BotReplayCatalogEntry
        {
            userStageID = 10,
            levelID = 1,
            stageIndex = 0,
            recording = recording
        };
        return entries;
    }

    public static bool TrySimulateReplayLoad(
        int index,
        ref BotRelayFarmNative farm,
        in NativeArray<BotReplayCatalogEntry> catalogEntries,
        double elapsedTime)
    {
        if ((farm.agentFlags[index] & BotAgentRuntimeFlags.PendingPlay) == 0)
            return false;

        var catalog = new BotReplayCatalog { entries = catalogEntries };
        var injectWriter = default(NativeQueue<BotRelayInject>.ParallelWriter);
        BotRelayReplayLoadOps.HandlePendingPlay(index, ref farm, in catalog, elapsedTime, ref injectWriter, 0);
        return (farm.agentFlags[index] & BotAgentRuntimeFlags.PendingPlay) == 0;
    }

    private static BotRelayPacket __BuildClientHeaderApp(ClientMessageType messageType, uint senderId) =>
        __BuildRelayAppPacket(writer =>
        {
            var model = StreamCompressionModel.Default;
            writer.WritePackedInt((int)messageType, model);
            writer.WritePackedInt((int)NetworkRelayType.Channel, model);
            writer.WritePackedUInt(senderId, model);
        });

    private static BotRelayPacket __BuildRelayAppPacket(Action<DataStreamWriter> write)
    {
        using var scratch = new NativeArray<byte>(BotRelayPacket.MaxPayloadSize, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        write(writer);

        if (writer.Length <= 0 || writer.Length > BotRelayPacket.MaxPayloadSize)
            return default;

        var packet = default(BotRelayPacket);
        packet.length = writer.Length;
        for (int i = 0; i < writer.Length; ++i)
            packet.SetByte(i, scratch[i]);
        return packet;
    }

    private static byte[] __BuildRecordedPayload(ReplyMessageType type, byte[] body)
    {
        using var scratch = new NativeArray<byte>(64 + body.Length, Allocator.Temp);
        var writer = new DataStreamWriter(scratch);
        var model = StreamCompressionModel.Default;
        writer.WritePackedInt((int)type, model);
        writer.WritePackedInt((int)NetworkRelayType.Channel, model);
        writer.WritePackedUInt(BotUserId, model);
        for (int i = 0; i < body.Length; ++i)
            writer.WriteByte(body[i]);

        var payload = new byte[writer.Length];
        for (int i = 0; i < writer.Length; ++i)
            payload[i] = scratch[i];
        return payload;
    }

    private static void __WriteFrame(
        ref BotReplayFrameMeta frame,
        Unity.Entities.BlobBuilderArray<byte> payloadBytes,
        ref int offset,
        float timestamp,
        byte[] payload)
    {
        frame.timestamp = timestamp;
        frame.payloadOffset = offset;
        frame.payloadLength = (ushort)payload.Length;
        for (int i = 0; i < payload.Length; ++i)
            payloadBytes[offset + i] = payload[i];
        offset += payload.Length;
    }
}
