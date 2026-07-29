using System;
using NUnit.Framework;
using UnityEngine;
using ZG;

/// <summary>
/// Shared assertions for bot relay EditMode tests — catches stream over-read error spam seen in Play.
/// </summary>
internal static class BotRelayWireTestAsserts
{
    public const string StreamOverreadErrorPrefix = "Trying to read";

    public sealed class LogScope : IDisposable
    {
        public int StreamOverreadErrorCount { get; private set; }
        public int ErrorCount { get; private set; }

        public static LogScope Begin() => new LogScope();

        private LogScope()
        {
            Application.logMessageReceived += __OnLog;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= __OnLog;
        }

        private void __OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error)
                return;

            ErrorCount++;
            if (condition != null && condition.Contains(StreamOverreadErrorPrefix))
                StreamOverreadErrorCount++;
        }
    }

    public static bool TryExtractLive117Wire(out BotRelayPacket app, out string failure)
    {
        app = default;
        failure = null;
        var wire = BotRelayWireTestFixtures.FromBytes(BotRelayWireTestFixtures.CapturedLiveInviteWire117);

        using var logs = LogScope.Begin();
        if (!BotRelayWireTestFixtures.TryExtractLiveInviteWire(in wire, out app) || app.IsEmpty)
        {
            failure = "TryExtractLiveInviteWire returned false for CapturedLiveInviteWire117.";
            return false;
        }

        if (logs.StreamOverreadErrorCount > 0)
        {
            failure = $"stream over-read errors={logs.StreamOverreadErrorCount}";
            return false;
        }

        return true;
    }

    public static void AssertNoStreamOverreadErrors(LogScope scope, string context)
    {
        if (scope.StreamOverreadErrorCount > 0)
            throw new AssertionException(
                $"{context}: expected no '{StreamOverreadErrorPrefix}' errors, got {scope.StreamOverreadErrorCount} (total errors={scope.ErrorCount}).");
    }
}
