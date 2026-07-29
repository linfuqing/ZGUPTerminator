using Unity.Entities;
using ZG;

/// <summary>
/// Server readiness checks for co-deployed bot farm. Uses ECS singleton via
/// <see cref="NetworkRelayServerManager.server"/> (no reflection).
/// </summary>
internal static class BotRelayServerBridge
{
    public static bool IsServerReady() => NetworkRelayServerManager.server.isCreated;
}
