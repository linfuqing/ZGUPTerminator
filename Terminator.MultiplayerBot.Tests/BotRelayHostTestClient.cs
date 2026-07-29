using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;
using ZG;

/// <summary>
/// Editor PlayMode host using real <see cref="ClientData"/> + <see cref="NetworkClientDriver"/>
/// (Manual Play path on ReplyServer port). Not part of bot relay release hot path.
/// </summary>
internal static class BotRelayHostTestClient
{
    public const int SquadCapacity = 2;

    private static GameObject __hostRoot;
    private static ClientData __clientData;
    private static int __cachedSquadChannel = ReplyMessageShared.CHANNEL_NULL;

    public readonly struct SharedStateScope : IDisposable
    {
        private readonly bool __savedIsHost;
        private readonly int __savedChannel;
        private readonly int __savedChannelStatus;
        private readonly int __savedRemotePlayerCount;
        private readonly uint __savedRemoteId;
        private readonly int __savedRemoteChannelFlag;
        private readonly LevelPlayerHeader __savedRemoteHeader;
        private readonly RemotePlayer.Status __savedRemoteStatus;

        internal SharedStateScope(
            bool savedIsHost,
            int savedChannel,
            int savedChannelStatus,
            int savedRemotePlayerCount,
            uint savedRemoteId,
            int savedRemoteChannelFlag,
            LevelPlayerHeader savedRemoteHeader,
            RemotePlayer.Status savedRemoteStatus)
        {
            __savedIsHost = savedIsHost;
            __savedChannel = savedChannel;
            __savedChannelStatus = savedChannelStatus;
            __savedRemotePlayerCount = savedRemotePlayerCount;
            __savedRemoteId = savedRemoteId;
            __savedRemoteChannelFlag = savedRemoteChannelFlag;
            __savedRemoteHeader = savedRemoteHeader;
            __savedRemoteStatus = savedRemoteStatus;
        }

        public void Dispose()
        {
            ReplyMessageShared.isHost = __savedIsHost;
            ReplyMessageShared.channel = __savedChannel;
            ReplyMessageShared.channelStatus = __savedChannelStatus;
            ReplyMessageShared.remotePlayerCount = __savedRemotePlayerCount;
            LevelPlayerShared<RemotePlayer>.id = __savedRemoteId;
            LevelPlayerShared<RemotePlayer>.channelFlag = __savedRemoteChannelFlag;
            LevelPlayerShared<RemotePlayer>.header = __savedRemoteHeader;
            RemotePlayer.SetStatus(__savedRemoteStatus);
        }
    }

    public static SharedStateScope BeginScope() =>
        new SharedStateScope(
            ReplyMessageShared.isHost,
            ReplyMessageShared.channel,
            ReplyMessageShared.channelStatus,
            ReplyMessageShared.remotePlayerCount,
            LevelPlayerShared<RemotePlayer>.id,
            LevelPlayerShared<RemotePlayer>.channelFlag,
            LevelPlayerShared<RemotePlayer>.header,
            RemotePlayer.status);

    public static void ResetSessionState()
    {
        __cachedSquadChannel = ReplyMessageShared.CHANNEL_NULL;
        __ResetClientDataDriverCacheIfStale();
        if (__clientData == null)
            return;

        __TryLeaveSquadQuietly();
        PumpMessages();
        __ResetReadMessageCursor();
    }

    public static void TeardownHostClient()
    {
        __cachedSquadChannel = ReplyMessageShared.CHANNEL_NULL;
        if (__clientData != null)
        {
            __TryLeaveSquadQuietly();
            PumpMessages();
        }

        if (__hostRoot != null)
        {
            UnityEngine.Object.Destroy(__hostRoot);
            __hostRoot = null;
            __clientData = null;
        }
    }

    // ClientData creates its squad through the first SquadInvite, then automatically sends that
    // stored Invite after Create succeeds. Keep that bootstrap Invite directed to a sink identity:
    // it establishes the host without broadcasting to every bot before the acceptance test chooses
    // its explicit recipient.
    private const uint HostCreateSinkUserId = 1u;

    /// <summary>Connect → Status → Create via a sink-target Invite → wait for server Create echo.</summary>
    public static IEnumerator EstablishAsSquadHost(float settleSeconds = 1.5f)
    {
        TeardownHostClient();
        __EnsureHostClient();
        __ResetClientDataDriverCacheIfStale();
        yield return null;

        var config = Resources.Load<BotConfig>("Bot/BotConfig");
        var header = BotRelayWireTestFixtures.BuildSenderHeader(BotRelayWireTestFixtures.RealPlayerUserId);
        string address = config != null && !string.IsNullOrEmpty(config.replyServerAddress)
            ? config.replyServerAddress
            : "127.0.0.1";
        ushort port = config != null && config.replyServerPort != 0
            ? config.replyServerPort
            : (ushort)1386;

        __clientData.Connect(header, address, port);
        __BootstrapHostSession();

        yield return BotRelayPlayModeHarness.WaitUntil(
            __TryIsConnected,
            Mathf.Max(settleSeconds, 20f),
            "HostTestClient: ClientData did not connect to ReplyServer.");

        __clientData.SetStatus(0);
        yield return __SettleAndPump(0.75f);

        var createInvite = new ClientMessageSquadInviteToSend
        {
            userID = HostCreateSinkUserId,
            levelID = 1,
            stage = 0,
            text = new FixedString512Bytes("PlayModeHost")
        };
        __clientData.SendMessage(createInvite);

        yield return BotRelayPlayModeHarness.WaitUntil(
            () =>
            {
                PumpMessages();
                SyncChannelFromServer();
                return ReplyMessageShared.isHost &&
                       ReplyMessageShared.channel != ReplyMessageShared.CHANNEL_NULL;
            },
            Mathf.Max(settleSeconds, 25f),
            "HostTestClient: server did not echo Create after host SquadInvite.");
        PumpMessages();
        SyncChannelFromServer();
        if (!TryResolveHostSquadChannel(out int establishedChannel))
        {
            throw new InvalidOperationException(
                "HostTestClient: server did not register host squad channel after Create.");
        }

        if (BotRelayPlayModeServerProbe.TryFindSquadChannelForUser(
                BotRelayWireTestFixtures.RealPlayerUserId,
                out int serverChannel))
        {
            establishedChannel = serverChannel;
            ReplyMessageShared.channel = serverChannel;
            ReplyMessageShared.isHost = true;
        }

        __cachedSquadChannel = establishedChannel;
    }

    public static bool TryGetEstablishedSquadChannel(out int channel)
    {
        if (__cachedSquadChannel != ReplyMessageShared.CHANNEL_NULL)
        {
            channel = __cachedSquadChannel;
            return true;
        }

        if (TryResolveHostSquadChannel(out channel))
        {
            __cachedSquadChannel = channel;
            return true;
        }

        channel = ReplyMessageShared.CHANNEL_NULL;
        return false;
    }

    public static IEnumerator WaitForBotJoinedSquad(uint expectedBotUserId, float timeoutSeconds = 12f)
    {
        TryGetEstablishedSquadChannel(out _);
        yield return BotRelayPlayModeHarness.WaitUntil(
            () =>
            {
                __RestoreHostSquadChannelIfCollectWipedConnect();
                PumpMessages();
                if (__TryHostObservedBotJoin(expectedBotUserId))
                    return true;

                if (BotRelayPlayModeHarness.TryBotHasJoinFailEvent(0))
                    throw new InvalidOperationException(
                        $"HostTestClient: server rejected bot Join (JoinFailed on bot inbox) for {expectedBotUserId}.");

                return __TryHostObservedBotJoin(expectedBotUserId);
            },
            timeoutSeconds,
            () =>
            {
                PumpMessages();
                var probe = BotRelayPlayModeServerProbe.TryGetIdentity(expectedBotUserId, out var match, out var botChannel)
                    ? BotRelayPlayModeServerProbe.DescribeJoinGate(expectedBotUserId, ReplyMessageShared.channel)
                    : $"bot identity missing (channel={ReplyMessageShared.channel})";
                var joinFail = BotRelayPlayModeHarness.TryBotHasJoinFailEvent(0) ? " JoinFailed=yes" : string.Empty;
                return
                    $"HostTestClient: server did not relay Join for bot {expectedBotUserId} " +
                    $"(remote={LevelPlayerShared<RemotePlayer>.id}, count={ReplyMessageShared.remotePlayerCount}, " +
                    $"channel={ReplyMessageShared.channel}, isHost={ReplyMessageShared.isHost}, " +
                    $"remoteStatus={RemotePlayer.status}). {probe}{joinFail}";
            });
    }

    public static void AssertHostReceivedBotJoin(uint expectedBotUserId, string context = null)
    {
        PumpMessages();
        if (__TryHostRelaySharedJoin(expectedBotUserId))
            return;

        if (__TryScanHostJoinRelayMessage(expectedBotUserId))
        {
            if (LevelPlayerShared<RemotePlayer>.id == 0)
                LevelPlayerShared<RemotePlayer>.id = expectedBotUserId;
            if (ReplyMessageShared.remotePlayerCount <= 0)
                ReplyMessageShared.remotePlayerCount = 1;
            return;
        }

        var prefix = string.IsNullOrEmpty(context) ? string.Empty : context + ": ";
        Assert.AreEqual(expectedBotUserId, LevelPlayerShared<RemotePlayer>.id,
            $"{prefix}Host RemotePlayer.id must match joined bot.");
        Assert.Greater(ReplyMessageShared.remotePlayerCount, 0,
            $"{prefix}Host ReplyMessageShared.remotePlayerCount must increase after Join relay.");
        Assert.AreNotEqual(ReplyMessageShared.CHANNEL_NULL, ReplyMessageShared.channel,
            $"{prefix}Host squad channel must be active after Join.");
        Assert.IsTrue(ReplyMessageShared.isHost,
            $"{prefix}Host ReplyMessageShared.isHost must stay true after teammate Join.");
    }

    /// <summary>Drain ClientData read queue only. Never blocks ReceiveJob / NetworkClientDriver.</summary>
    public static int PumpMessages() => __DrainClientReadQueue();

    static int __DrainClientReadQueue()
    {
        if (__clientData == null)
            return 0;

        int pumped = 0;
        ClientHeader header;
        int messageType;
        while ((messageType = __clientData.ReadMessageType(out header)) != (int)ClientMessageType.None)
        {
            ++pumped;
            __DrainReadMessage(messageType, in header);
        }

        __RestoreHostSquadChannelIfCollectWipedConnect();
        if (ReplyMessageShared.channel != ReplyMessageShared.CHANNEL_NULL)
            __cachedSquadChannel = ReplyMessageShared.channel;

        return pumped;
    }

    /// <summary>Manual Play: host sends a SquadInvite; a non-zero target selects one bot.</summary>
public static void SendWorldSquadInvite(
        uint levelId = 1,
        int stage = 0,
        string text = "PlayModeHostInvite",
        uint targetUserID = 0)
    {
        if (__clientData == null)
            throw new InvalidOperationException("HostTestClient not connected.");

        __RestoreHostSquadChannelIfCollectWipedConnect();
        if (!TryGetEstablishedSquadChannel(out int squadChannel) ||
            squadChannel == ReplyMessageShared.CHANNEL_NULL)
            throw new InvalidOperationException(
                "HostTestClient: squad channel required before SquadInvite (Collect may have cleared ReplyMessageShared).");

        ReplyMessageShared.channel = squadChannel;
        ReplyMessageShared.isHost = true;

        // Match MiniChatFullManager.SendInviteToServerPrivate exactly: ClientMessageSquadInviteToSend
        // receives the actual target user id. Transforming it here routes the invitation to another bot.
        var invite = new ClientMessageSquadInviteToSend
        {
            userID = targetUserID,
            levelID = levelId,
            stage = stage,
            text = new FixedString512Bytes(text ?? string.Empty)
        };
        __clientData.SendMessage(invite);
        PumpMessages();
    }

    public static IEnumerator SendWorldSquadInviteAndWaitForBotPending(
        int agentIndex,
        float timeoutSeconds = 8f)
    {
        if (!BotRelayPlayModeHarness.TryGetBotUserId(agentIndex, out var botUserId))
            throw new InvalidOperationException($"Bot user id missing for target agent {agentIndex}.");

        SendWorldSquadInvite(targetUserID: botUserId);
        yield return BotRelayPlayModeHarness.WaitForAgentState(
            agentIndex,
            BotState.PendingInvite,
            timeoutSeconds);
    }

    public static IEnumerator SendWorldSquadInviteAndWaitForBotPending(
        int agentIndex,
        float timeoutSeconds,
        float routeSettleSeconds)
    {
        if (!BotRelayPlayModeHarness.TryGetBotUserId(agentIndex, out var botUserId))
            throw new InvalidOperationException($"Bot user id missing for target agent {agentIndex}.");

        SendWorldSquadInvite(targetUserID: botUserId);
        // The configured bot delay can be shorter than the route-settle window. Observe the
        // transient acceptance state first; otherwise a valid fast Mismatch→Join round trip has
        // already reached InSquad before the assertion starts.
        yield return BotRelayPlayModeHarness.WaitForAgentState(
            agentIndex,
            BotState.PendingInvite,
            timeoutSeconds);
        yield return BotRelayPlayModeHarness.WaitForRouteSendSettled(
            stableSeconds: routeSettleSeconds,
            timeoutSeconds: Mathf.Max(timeoutSeconds, routeSettleSeconds + 2f));
    }

    public static uint HostSquadChannel
    {
        get
        {
            SyncChannelFromServer();
            return ReplyMessageShared.channel != ReplyMessageShared.CHANNEL_NULL
                ? (uint)ReplyMessageShared.channel
                : 0;
        }
    }

    /// <summary>Mirror server squad channel onto host ReplyMessageShared (Collect may lag in PlayMode).</summary>
    public static void SyncChannelFromServer()
    {
        if (!BotRelayPlayModeServerProbe.TryGetIdentity(
                BotRelayWireTestFixtures.RealPlayerUserId,
                out _,
                out int hostChannel) ||
            hostChannel == NetworkRelayServerIdentity.CHANNEL_NULL)
            return;

        ReplyMessageShared.channel = hostChannel;
        ReplyMessageShared.isHost = true;
        __cachedSquadChannel = hostChannel;
    }

    public static bool TryApplySquadChannelFromInvite(int squadChannel)
    {
        if (squadChannel < 0)
            return false;

        ReplyMessageShared.channel = squadChannel;
        ReplyMessageShared.isHost = true;
        __cachedSquadChannel = squadChannel;
        return true;
    }

    public static bool TryResolveHostSquadChannel(out int channel)
    {
        SyncChannelFromServer();
        if (ReplyMessageShared.channel != ReplyMessageShared.CHANNEL_NULL)
        {
            channel = ReplyMessageShared.channel;
            __cachedSquadChannel = channel;
            return true;
        }

        PumpMessages();
        __TryApplyHostRelayFromDriver();
        __RestoreHostSquadChannelIfCollectWipedConnect();
        if (ReplyMessageShared.channel != ReplyMessageShared.CHANNEL_NULL)
        {
            channel = ReplyMessageShared.channel;
            __cachedSquadChannel = channel;
            return true;
        }

        if (__cachedSquadChannel != ReplyMessageShared.CHANNEL_NULL &&
            BotRelayPlayModeServerProbe.ChannelContainsUser(
                __cachedSquadChannel,
                BotRelayWireTestFixtures.RealPlayerUserId))
        {
            ReplyMessageShared.channel = __cachedSquadChannel;
            ReplyMessageShared.isHost = true;
            channel = __cachedSquadChannel;
            return true;
        }

        channel = NetworkRelayServerIdentity.CHANNEL_NULL;
        return false;
    }

    private static void __EnsureHostClient()
    {
        if (__clientData != null)
            return;

        __hostRoot = new GameObject(nameof(BotRelayHostTestClient));
        UnityEngine.Object.DontDestroyOnLoad(__hostRoot);
        __clientData = __hostRoot.AddComponent<ClientData>();
        IClientData.instance = __clientData;
    }

    private static void __BootstrapHostSession()
    {
        IClientData.instance = __clientData;
        __cachedSquadChannel = ReplyMessageShared.CHANNEL_NULL;
        RemotePlayer.SetStatus(RemotePlayer.Status.Canceled);
        LevelPlayerShared<RemotePlayer>.id = 0;
        LevelPlayerShared<RemotePlayer>.channelFlag = 0;
        LevelPlayerShared<RemotePlayer>.header = default;
        __ResetReadMessageCursor();
    }

    private static bool __TryHostObservedBotJoin(uint expectedBotUserId) =>
        __TryHostRelaySharedJoin(expectedBotUserId) || __TryScanHostJoinRelayMessage(expectedBotUserId);

    private static bool __TryHostRelaySharedJoin(uint expectedBotUserId) =>
        LevelPlayerShared<RemotePlayer>.id == expectedBotUserId &&
        ReplyMessageShared.remotePlayerCount > 0;

    private static bool __TryScanHostJoinRelayMessage(uint expectedBotUserId)
    {
        if (__clientData == null)
            return false;

        try
        {
            var driver = __clientData.driver;
            if (!driver.isCreated)
                return false;

            var instance = driver.instance;
            var buffer = instance.buffer.AsArray();
            var model = StreamCompressionModel.Default;
            int squadChannel = ReplyMessageShared.channel != ReplyMessageShared.CHANNEL_NULL
                ? ReplyMessageShared.channel
                : __cachedSquadChannel;

            foreach (var message in instance.AsMessages())
            {
                if (message.Message.type != NetworkClientMessageType.Data)
                    continue;

                var reader = new NetworkClient.MessageElement(message.Message, buffer).reader;
                if (reader.Length < 1)
                    continue;

                int type = reader.ReadPackedInt(model);
                if ((NetworkRelayMessageType)type != NetworkRelayMessageType.Join)
                    continue;

                if (reader.Length - reader.GetBytesRead() < 2)
                    continue;

                int channel = reader.ReadPackedInt(model);
                reader.ReadPackedInt(model);
                if (channel != squadChannel || reader.GetBytesRead() >= reader.Length)
                    continue;

                reader.Flush();
                return reader.ReadPackedUInt(model) == expectedBotUserId;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BotRelay.Test] Host join scan failed: {ex.Message}");
        }

        return false;
    }

    private static void __PumpNetworkClientDriver()
    {
        // No-op: driver.Schedule().Complete() deadlocks BotRelay ReceiveJob during PlayMode tests.
    }

    private static void __TryApplyHostRelayFromDriver()
    {
        if (__clientData == null)
            return;

        try
        {
            var driver = __clientData.driver;
            if (!driver.isCreated)
                return;

            var instance = driver.instance;
            var buffer = instance.buffer.AsArray();

            foreach (var message in instance.AsMessages())
            {
                if (message.Message.type != NetworkClientMessageType.Data)
                    continue;

                var reader = new NetworkClient.MessageElement(message.Message, buffer).reader;
                while (reader.GetBytesRead() + sizeof(ushort) <= reader.Length)
                {
                    int size = reader.ReadUShort();
                    if (size <= 0 || reader.Length - reader.GetBytesRead() < size)
                        break;

                    using var appBytes = new NativeArray<byte>(size, Allocator.Temp);
                    reader.ReadBytes(appBytes);

                    var app = default(BotRelayPacket);
                    app.length = size;
                    for (int i = 0; i < size; ++i)
                        app.SetByte(i, appBytes[i]);

                    BotRelayHostMessageParser.TryApplyForHost(in app);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BotRelay.Test] Host relay apply failed: {ex.Message}");
        }
    }

    private static void __SyncCollectRelayMessages(EntityManager entityManager)
    {
        // No-op: Collect every pump replays Connect and stalls PlayMode tests.
    }

    /// <summary>
    /// ReplyMessages.Collect replays Connect and clears squad channel; restore when server still has the host squad.
    /// </summary>
    private static void __RestoreHostSquadChannelIfCollectWipedConnect()
    {
        if (ReplyMessageShared.channel != ReplyMessageShared.CHANNEL_NULL)
        {
            __cachedSquadChannel = ReplyMessageShared.channel;
            return;
        }

        SyncChannelFromServer();
        if (ReplyMessageShared.channel != ReplyMessageShared.CHANNEL_NULL)
        {
            __cachedSquadChannel = ReplyMessageShared.channel;
            return;
        }

        if (__cachedSquadChannel == ReplyMessageShared.CHANNEL_NULL)
            return;

        ReplyMessageShared.channel = __cachedSquadChannel;
        ReplyMessageShared.isHost = true;
    }

    private static void __ResetReadMessageCursor()
    {
        if (__clientData == null)
            return;

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var field = typeof(ClientData).GetField("__initStatus", flags);
        field?.SetValue(__clientData, 0);
    }

    private static void __TryLeaveSquadQuietly()
    {
        if (__clientData == null ||
            ReplyMessageShared.channel == ReplyMessageShared.CHANNEL_NULL)
            return;

        try
        {
            __clientData.SendMessage(new ClientMessageSquadLeave());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BotRelay.Test] HostTestClient leave squad: {ex.Message}");
        }
    }

    private static void __ResetClientDataDriverCacheIfStale()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var field = typeof(ClientData).GetField("__entity", flags);
        if (field == null)
            return;

        var entity = (Entity)field.GetValue(null);
        if (entity == Entity.Null)
            return;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || !world.EntityManager.Exists(entity))
            field.SetValue(null, Entity.Null);
    }

    private static bool __TryIsConnected()
    {
        __ResetClientDataDriverCacheIfStale();
        PumpMessages();
        if (__clientData == null)
            return false;

        try
        {
            var driver = __clientData.driver;
            if (!driver.isCreated)
                return false;

            return driver.instance.connectionState == NetworkConnection.State.Connected;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BotRelay.Test] HostTestClient connection probe failed: {ex.Message}");
            __ResetClientDataDriverCacheIfStale();
            return false;
        }
    }

    private static void __DrainReadMessage(int messageType, in ClientHeader header)
    {
        switch ((ClientMessageType)messageType)
        {
            case ClientMessageType.SquadJoin:
            {
                var join = __clientData.ReadMessage<ClientMessageSquadJoinToRead>();
                __ApplySquadJoinRelayToHostShared(in header, in join);
                break;
            }
            case ClientMessageType.SquadLeave:
            case ClientMessageType.SquadDrop:
                break;
            case ClientMessageType.Status:
                break;
            case ClientMessageType.Match:
                __clientData.ReadMessage<ClientMessageMatchToRead>();
                break;
            case (ClientMessageType)NetworkRelayMessageType.Mismatch:
                break;
            default:
                break;
        }
    }

    private static void __ApplySquadJoinRelayToHostShared(
        in ClientHeader header,
        in ClientMessageSquadJoinToRead join)
    {
        if (header.userID == BotRelayWireTestFixtures.RealPlayerUserId ||
            header.userID == 0)
            return;

        __RestoreHostSquadChannelIfCollectWipedConnect();
        if (ReplyMessageShared.channel == ReplyMessageShared.CHANNEL_NULL && join.squadInviteID > 0)
            ReplyMessageShared.channel = (int)join.squadInviteID;

        if ((int)join.squadInviteID != ReplyMessageShared.channel && !ReplyMessageShared.isHost)
            return;

        LevelPlayerShared<RemotePlayer>.id = header.userID;
        LevelPlayerShared<RemotePlayer>.channelFlag = (int)join.playerStatus.flag;
        ++ReplyMessageShared.remotePlayerCount;
        ReplyMessageShared.isHost = true;
    }

    private static IEnumerator __SettleAndPump(float settleSeconds)
    {
        float deadline = Time.realtimeSinceStartup + settleSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            PumpMessages();
            yield return null;
        }

        PumpMessages();
    }
}
