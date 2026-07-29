using System;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using ZG;

/// <summary>PlayMode diagnostics: read server identity state without modifying ZGUPEntitiesNetworkingCommon.</summary>
internal static class BotRelayPlayModeServerProbe
{
    static readonly FieldInfo s_identitiesField =
        typeof(NetworkRelayServer).GetField("__identities", BindingFlags.Instance | BindingFlags.NonPublic);

    static readonly FieldInfo s_channelIdsField =
        typeof(NetworkRelayServer).GetField("__channelIDs", BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool TryGetIdentity(uint userId, out int match, out int channel)
    {
        match = 0;
        channel = NetworkRelayServerIdentity.CHANNEL_NULL;

        if (s_identitiesField == null)
            return false;

        ref var server = ref NetworkRelayServerManager.server;
        if (!server.isCreated)
            return false;

        if (!__TryGetIdentities(ref server, out var identities))
            return false;

        for (int i = 0; i < identities.Length; ++i)
        {
            var identity = identities[i];
            if (identity.ID != userId)
                continue;

            match = identity.match;
            channel = identity.channel;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Diagnostics: enqueue one Bot→Server relay control through the same game producer queue used
    /// by Farm jobs. The flatten system is the sole writer of the package inject list, so tests
    /// must not write that list directly and then mistake its per-frame clear for Handler.Read.
    /// </summary>
    public static bool TryEnqueueInjectControl(uint id, NetworkRelayMessageType type, int arg, bool hasArg)
    {
        if (!__TryGetInjectQueue(out var queue))
            return false;

        var writer = queue.AsParallelWriter();
        BotRelayInjectOps.InjectControlMessage(ref writer, id, (int)type, arg, hasArg);
        return true;
    }

    public static bool TryEnqueueInjectJoin(uint id, int targetChannel) =>
        TryEnqueueInjectControl(id, NetworkRelayMessageType.Join, targetChannel, hasArg: true);

    /// <summary>Diagnostics: current depth of the game inject producer queue (how many injects are pending flatten/drain).</summary>
    public static int GetInjectCount()
    {
        if (!__TryGetInjectQueue(out var queue))
            return -1;

        return queue.Count;
    }

    static bool __TryGetInjectQueue(out NativeQueue<BotRelayInject> queue)
    {
        queue = default;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        var entityManager = world.EntityManager;
        using var query = entityManager.CreateEntityQuery(typeof(BotRelayInjectQueueSingleton));
        if (query.CalculateEntityCount() != 1)
            return false;

        // Editor diagnostic only: ensure no flatten/producer job is mid-write before touching the
        // queue from the main thread.
        entityManager.CompleteAllTrackedJobs();

        queue = query.GetSingleton<BotRelayInjectQueueSingleton>().queue;
        return queue.IsCreated;
    }

    public static bool ChannelContainsUser(int channelIndex, uint userId)
    {
        if (channelIndex < 0 || s_channelIdsField == null)
            return false;

        ref var server = ref NetworkRelayServerManager.server;
        if (!server.isCreated)
            return false;

        if (!__TryGetChannelIds(ref server, out var channelIds) ||
            !channelIds.ContainsKey(channelIndex))
            return false;

        foreach (var id in channelIds.GetValuesForKey(channelIndex))
        {
            if (id == userId)
                return true;
        }

        return false;
    }

    public static string DescribeJoinGate(uint botUserId, int targetChannel)
    {
        if (!TryGetIdentity(botUserId, out var match, out var channel))
            return $"bot {botUserId} identity not found";

        bool hostChannelReady = targetChannel >= 0 && ChannelContainsUser(targetChannel, botUserId) == false &&
                                ChannelHasAnyMember(targetChannel);

        return
            $"bot match={match}, botChannel={channel}, targetChannel={targetChannel}, hostChannelReady={hostChannelReady}";
    }

/// <summary>Editor-only failure context: compact snapshot of relay identities currently known to Server.</summary>
public static string DescribeIdentities()
{
    ref var server = ref NetworkRelayServerManager.server;
    if (!server.isCreated)
        return "server=not-created";

    if (!__TryGetIdentities(ref server, out var identities))
        return "identities=unavailable";

    string result = "identities=[";
    for (int i = 0; i < identities.Length; ++i)
    {
        if (i > 0)
            result += ", ";

        var identity = identities[i];
        result += $"{identity.ID}(match={identity.match},channel={identity.channel})";
    }

    return result + "]";
}


    public static bool ChannelHasAnyMember(int channelIndex)
    {
        if (channelIndex < 0 || s_channelIdsField == null)
            return false;

        ref var server = ref NetworkRelayServerManager.server;
        if (!server.isCreated)
            return false;

        if (!__TryGetChannelIds(ref server, out var channelIds))
            return false;

        return channelIds.ContainsKey(channelIndex);
    }

    public static bool TryFindSquadChannelForUser(uint userId, out int channel)
    {
        channel = NetworkRelayServerIdentity.CHANNEL_NULL;
        if (s_channelIdsField == null)
            return false;

        ref var server = ref NetworkRelayServerManager.server;
        if (!server.isCreated)
            return false;

        if (!__TryGetChannelIds(ref server, out var channelIds))
            return false;

        foreach (var pair in channelIds)
        {
            if (pair.Value != userId)
                continue;

            channel = pair.Key;
            return true;
        }

        return false;
    }

    static bool __TryGetIdentities(
        ref NetworkRelayServer server,
        out NativeList<NetworkRelayServerIdentity> identities)
    {
        identities = default;
        if (s_identitiesField == null)
            return false;

        TypedReference serverRef = __makeref(server);
        if (s_identitiesField.GetValueDirect(serverRef) is not NativeList<NetworkRelayServerIdentity> list ||
            !list.IsCreated)
            return false;

        identities = list;
        return true;
    }

    static bool __TryGetChannelIds(
        ref NetworkRelayServer server,
        out NativeParallelMultiHashMap<int, uint> channelIds)
    {
        channelIds = default;
        if (s_channelIdsField == null)
            return false;

        TypedReference serverRef = __makeref(server);
        if (s_channelIdsField.GetValueDirect(serverRef) is not NativeParallelMultiHashMap<int, uint> map ||
            !map.IsCreated)
            return false;

        channelIds = map;
        return true;
    }
}
