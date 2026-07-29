using Unity.Burst;
using Unity.Entities;

internal struct BotRelayInviteShellCache
{
    public BotRelayPacket wire;
    public ushort virtualPort;
}

/// <summary>Isolated SharedStatic so BotRelayManager struct size stays stable across domain reloads.</summary>
[BurstCompile]
internal static class BotRelayInviteShellCacheStore
{
    private struct CacheKey
    {
    }

    private static readonly SharedStatic<BotRelayInviteShellCache> s_Cache =
        SharedStatic<BotRelayInviteShellCache>.GetOrCreate<CacheKey>();

    public static void Store(in BotRelayPacket wire, ushort virtualPort)
    {
        var cache = s_Cache.Data;
        cache.wire = wire;
        cache.virtualPort = virtualPort;
        s_Cache.Data = cache;
    }

    public static bool TryGet(ushort virtualPort, out BotRelayPacket wire)
    {
        wire = default;
        var cache = s_Cache.Data;
        if (virtualPort == 0 ||
            virtualPort != cache.virtualPort ||
            cache.wire.IsEmpty)
        {
            return false;
        }

        wire = cache.wire;
        return true;
    }

#if UNITY_EDITOR
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        s_Cache.Data = default;
    }
#endif
}
