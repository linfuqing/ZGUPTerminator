using UnityEngine;

/// <summary>
/// Unified console tag for editor replay-bot diagnostics. Filter Unity Console with: BotReplay
/// </summary>
internal static class BotReplayLog
{
    public const string Tag = "[BotReplay]";

    /// <summary>Opt-in verbose handshake / capture diagnostics ([RecordingDiag]).</summary>
    public static bool VerboseDiagnostics { get; set; }

    public static void Info(string message)
    {
        Debug.Log($"{Tag} {message}");
    }

    public static void Warn(string message)
    {
        Debug.LogWarning($"{Tag} {message}");
    }

    public static void Error(string message)
    {
        Debug.LogError($"{Tag} {message}");
    }

    public static void Diag(string message)
    {
        if (VerboseDiagnostics)
        {
            Info($"[RecordingDiag] {message}");
        }
    }
}
