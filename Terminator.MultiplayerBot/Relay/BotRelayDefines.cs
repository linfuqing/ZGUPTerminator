using Unity.Burst;

public static class BotRelayDefines
{
    private struct VerboseTelemetryKey { }

    private static readonly SharedStatic<bool> s_verboseTelemetry =
        SharedStatic<bool>.GetOrCreate<VerboseTelemetryKey>();

    /// <summary>
    /// Runtime gate for the high-frequency relay diagnostics that flood the console during a normal
    /// Play session: per-send <c>[BotRelay] RouteSend / RouteWirePreview</c>, per-inbound
    /// <c>extracted / dequeued</c>, and the aggregate <c>[BotRelay] Telemetry</c> /
    /// <c>[BotProbe]</c> snapshots (incl. the BotProbe.log file write).
    ///
    /// Off by default so gameplay is quiet; set to <c>true</c> (from the console, a test, or a
    /// debug toggle) when diagnosing relay wire/routing issues. Low-frequency lifecycle logs
    /// (connected / joined / left squad / match / invite / warnings) are always emitted.
    /// SharedStatic so Burst jobs can read the gate; resets to <c>false</c> on domain reload.
    /// </summary>
    public static bool VerboseTelemetry
    {
        get => s_verboseTelemetry.Data;
        set => s_verboseTelemetry.Data = value;
    }
}
