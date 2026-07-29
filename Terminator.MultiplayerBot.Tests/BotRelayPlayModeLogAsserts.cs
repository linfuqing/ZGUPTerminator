using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode acceptance: fail when Server PopEvents / Handler hits DataStreamReader overread.
/// </summary>
internal static class BotRelayPlayModeLogAsserts
{
    public sealed class StreamOverreadScope : System.IDisposable
    {
        readonly List<string> _violations = new();
        bool _disposed;

        public StreamOverreadScope() =>
            Application.logMessageReceived += __OnLog;

        void __OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception)
                return;

            if (condition.Contains("Trying to read") && condition.Contains("stream where only"))
                _violations.Add(condition);
        }

        public void AssertNoViolations(string context)
        {
            Assert.IsEmpty(
                _violations,
                $"{context} Detected DataStreamReader stream overread (NetworkServer PopEvents / Handler):\n" +
                string.Join("\n", _violations));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Application.logMessageReceived -= __OnLog;
        }
    }

    /// <summary>Captures <c>[CreateOrJoin]Join</c> / <c>[Relay]Join</c> from NetworkRelayServerIdentity.</summary>
    public sealed class ServerJoinRelayLogScope : System.IDisposable
    {
        readonly uint _botUserId;
        readonly List<string> _joinLines = new();
        bool _disposed;

        public ServerJoinRelayLogScope(uint botUserId)
        {
            _botUserId = botUserId;
            Application.logMessageReceived += __OnLog;
        }

        void __OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Log && type != LogType.Warning)
                return;

            if (condition.Contains("[CreateOrJoin]") ||
                condition.Contains("JoinFailed") ||
                condition.Contains("NetworkRelayServerRead") ||
                (condition.Contains("[Relay]") && condition.Contains(" to ")))
                _joinLines.Add(condition);
        }

        public string DescribeCaptured() =>
            _joinLines.Count > 0 ? string.Join(" || ", _joinLines) : "(none)";

        public bool TryHasNetworkRelayServerReadJoinType6()
        {
            string botId = _botUserId.ToString();
            foreach (var line in _joinLines)
            {
                if (!line.Contains("NetworkRelayServerRead") || !line.Contains(botId))
                    continue;

                if (line.Contains("type=6"))
                    return true;
            }

            return false;
        }

        public void AssertNetworkRelayServerReadJoinType6(string context)
        {
            Assert.IsTrue(
                TryHasNetworkRelayServerReadJoinType6(),
                $"{context} Expected NetworkRelayServerRead type=6 for bot {_botUserId}. " +
                $"Captured:\n{(_joinLines.Count > 0 ? string.Join("\n", _joinLines) : "(none)")}");
        }

        public bool TryHasBotJoinProcessed()
        {
            string botId = _botUserId.ToString();
            foreach (var line in _joinLines)
            {
                if (line.Contains("[CreateOrJoin]") && line.Contains("Join") && line.Contains(botId))
                    return true;

                if (line.Contains("JoinFailed") && line.Contains(botId))
                    return true;

                if (line.Contains("[Relay]") && line.Contains($"to {botId}"))
                    return true;

                if (line.Contains("NetworkRelayServerRead") && line.Contains("type=6") && line.Contains(botId))
                    return true;
            }

            return false;
        }

        public void AssertBotJoinProcessed(string context)
        {
            Assert.IsTrue(
                TryHasBotJoinProcessed(),
                $"{context} Expected server log [CreateOrJoin]Join or [Relay]…to {_botUserId}. " +
                $"Captured:\n{( _joinLines.Count > 0 ? string.Join("\n", _joinLines) : "(none)")}");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Application.logMessageReceived -= __OnLog;
        }
    }

    public static StreamOverreadScope BeginStreamOverreadScope() => new();

    /// <summary>
    /// Host ClientData may receive ClosedByRemote during PlayMode Server.unity relay (ReplyServer 1386).
    /// Suppresses that LogError so acceptance can assert Join relay on ReplyMessageShared.
    /// </summary>
    public sealed class HostRelayDisconnectScope : System.IDisposable
    {
        readonly bool _previousIgnore;
        bool _disposed;

        public HostRelayDisconnectScope()
        {
            _previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            LogAssert.ignoreFailingMessages = _previousIgnore;
        }
    }

    public static HostRelayDisconnectScope BeginHostRelayDisconnectScope() => new();

    public static ServerJoinRelayLogScope BeginServerJoinRelayLogScope(uint botUserId) =>
        new(botUserId);
}
