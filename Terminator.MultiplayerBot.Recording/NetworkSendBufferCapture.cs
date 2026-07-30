using System;
using System.Collections.Generic;
using Unity.Collections;
using ZG;

/// <summary>
/// Drains the opt-in EndWrite journal without touching the transport send/retry cursor.
/// Payload, global EndWrite sequence and producer timestamp are committed together by the common package.
/// </summary>
internal static class NetworkSendBufferCapture
{
    internal struct Cursor
    {
        public bool hasValue;
        public uint sequence;
        public uint epoch;
        public double timestamp;
    }

    public static bool TryCapture(
        in NetworkClientSendBuffer sendBuffer,
        ref Cursor cursor,
        List<LevelRecordingFrame> output,
        out int capturedCount,
        out string error)
    {
        capturedCount = 0;
        error = null;
        if (!sendBuffer.isCreated)
        {
            error = "NetworkClientSendBuffer is not created.";
            return false;
        }

        if (output == null)
        {
            error = "Recording frame output is null.";
            return false;
        }

        while (true)
        {
            var status = sendBuffer.TryPeekCapturedEndWrite(
                out var token,
                out NativeArray<byte> bytes,
                out var stamp);
            switch (status)
            {
                case NetworkClientSendBuffer.EndWriteCaptureReadStatus.Empty:
                    return true;
                case NetworkClientSendBuffer.EndWriteCaptureReadStatus.NotActive:
                    error = "EndWrite capture journal is not active.";
                    return false;
                case NetworkClientSendBuffer.EndWriteCaptureReadStatus.Faulted:
                    error = $"EndWrite capture journal fault: {sendBuffer.endWriteCaptureFault}.";
                    return false;
                case NetworkClientSendBuffer.EndWriteCaptureReadStatus.Success:
                    break;
                default:
                    error = $"Unexpected EndWrite capture read status: {status}.";
                    return false;
            }

            if (!bytes.IsCreated || bytes.Length < 1)
            {
                error = $"EndWrite capture sequence {token.sequence} has an empty payload.";
                return false;
            }

            if (double.IsNaN(stamp.timestamp) ||
                double.IsInfinity(stamp.timestamp) ||
                stamp.timestamp < 0.0)
            {
                error =
                    $"EndWrite capture sequence {token.sequence} has invalid timestamp {stamp.timestamp}.";
                return false;
            }

            if (cursor.hasValue)
            {
                int sequenceDelta = unchecked((int)(token.sequence - cursor.sequence));
                if (sequenceDelta <= 0)
                {
                    error =
                        $"EndWrite capture sequence is not increasing: {cursor.sequence} -> {token.sequence}.";
                    return false;
                }

                int epochDelta = unchecked((int)(stamp.epoch - cursor.epoch));
                if (epochDelta < 0 || stamp.timestamp < cursor.timestamp)
                {
                    error =
                        "EndWrite producer timestamp regressed at sequence " +
                        $"{token.sequence}: epoch {cursor.epoch}->{stamp.epoch}, " +
                        $"time {cursor.timestamp:R}->{stamp.timestamp:R}.";
                    return false;
                }
            }

            float timestamp = (float)stamp.timestamp;
            if (float.IsNaN(timestamp) || float.IsInfinity(timestamp))
            {
                error =
                    $"EndWrite capture sequence {token.sequence} timestamp exceeds TRBT v2 float range.";
                return false;
            }

            var copy = new byte[bytes.Length];
            bytes.CopyTo(copy);
            output.Add(new LevelRecordingFrame
            {
                timestamp = timestamp,
                payload = copy
            });

            if (!sendBuffer.ConsumeCapturedEndWrite(in token))
            {
                output.RemoveAt(output.Count - 1);
                error = $"Failed to consume EndWrite capture sequence {token.sequence}.";
                return false;
            }

            cursor.hasValue = true;
            cursor.sequence = token.sequence;
            cursor.epoch = stamp.epoch;
            cursor.timestamp = stamp.timestamp;
            ++capturedCount;
        }
    }
}

internal static class ClientDataRecordingUtility
{
    public struct ConnectionSnapshot
    {
        public bool wasConnected;
        public string address;
        public ushort port;
    }

    public static ConnectionSnapshot SuppressNetwork()
    {
        var snapshot = default(ConnectionSnapshot);
        if (IClientData.instance is not ClientData clientData)
        {
            return snapshot;
        }

        ResolveReplyServerEndpoint(out snapshot.address, out snapshot.port);

        var driver = clientData.driver;
        snapshot.wasConnected = driver.isCreated &&
            driver.instance.connectionState == Unity.Networking.Transport.NetworkConnection.State.Connected;

        if (driver.isCreated)
        {
            driver.instance.Shutdown();
        }

        return snapshot;
    }

    public static void RestoreNetwork(in ConnectionSnapshot snapshot)
    {
        if (!snapshot.wasConnected || IClientData.instance is not ClientData clientData)
        {
            return;
        }

        if (string.IsNullOrEmpty(snapshot.address))
        {
            return;
        }

        clientData.Connect(clientData.header, snapshot.address, snapshot.port);
    }

    /// <summary>
    /// Recording harness shuts down the live socket but replay injects via
    /// <see cref="NetworkClient.TryEnqueueSyntheticData"/>. Ensure the ECS driver singleton exists.
    /// </summary>
    public static bool EnsureDriverForReplayInjection()
    {
        if (IClientData.instance is not ClientData clientData)
        {
            return false;
        }

        var driver = clientData.driver;
        return driver.isCreated;
    }

    private static void ResolveReplyServerEndpoint(out string address, out ushort port)
    {
        address = "127.0.0.1";
        port = 1386;

        var botConfig = UnityEngine.Resources.Load<BotConfig>("Bot/BotConfig");
        if (botConfig == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(botConfig.replyServerAddress))
        {
            address = botConfig.replyServerAddress;
        }

        if (botConfig.replyServerPort != 0)
        {
            port = botConfig.replyServerPort;
        }
    }
}
