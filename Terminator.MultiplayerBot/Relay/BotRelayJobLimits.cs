/// <summary>Per-job packet budgets so burst workers cannot monopolize a thread (mirrors UTP ReceiveJob).</summary>
internal static class BotRelayJobLimits
{
    public const int MaxListenPacketsPerReceiveJob = 512;
    public const int MaxBotPacketsPerDrainJob = 512;
    public const int MaxLinkAckPacketsPerPump = 8;
}
