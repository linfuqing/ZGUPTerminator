using ZG;

/// <summary>
/// Server readiness checks for co-deployed bot farm. The query remains non-throwing while the
/// ECS server component is still being created (no reflection and no large struct copy).
/// </summary>
internal static class BotRelayServerBridge
{
    public static bool IsServerReady() => NetworkRelayServerManager.IsServerReady();
}
