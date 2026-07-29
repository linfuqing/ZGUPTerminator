using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

/// <summary>
/// Snapshots pending sendBuffer payloads for level recording.
/// Uses per-slot <see cref="NetworkClientSendBuffer.TryReadPendingSend"/> so capture survives
/// global EndWrite index resets from <see cref="NetworkClientSendBuffer.Apply"/>.
/// SelectSkill upgrade chains rely on frame timestamps and replay validation, not message-type ordering.
/// </summary>
internal static class NetworkSendBufferCapture
{
    private const float DefaultBatchFrameSpacingSeconds = 1f / 30f;

    public static bool TryCapture(
        in NetworkClientSendBuffer sendBuffer,
        ref int[] slotReadCursors,
        List<LevelRecordingFrame> output,
        float timestamp)
    {
        if (!sendBuffer.isCreated)
        {
            return false;
        }

        int slotCount = sendBuffer.GetPendingSendSlotCount();
        LevelRecordingSendBufferAccess.EnsureSlotCursors(ref slotReadCursors, slotCount);

        int batchStart = output.Count;
        bool captured = false;
        NativeArray<byte> bytes;
        for (int slot = 0; slot < slotCount; ++slot)
        {
            int probeIndex = slotReadCursors[slot];
            while (sendBuffer.TryReadPendingSend(slot, ref probeIndex, out bytes))
            {
                if (!bytes.IsCreated || bytes.Length == 0)
                {
                    continue;
                }

                var copy = new byte[bytes.Length];
                bytes.CopyTo(copy);
                output.Add(new LevelRecordingFrame
                {
                    timestamp = timestamp,
                    payload = copy
                });
                captured = true;
            }

            slotReadCursors[slot] = probeIndex;
        }

        int batchCount = output.Count - batchStart;
        if (batchCount > 1)
        {
            __SpreadBatchCaptureTimestamps(output, batchStart, batchCount, timestamp);
        }

        return captured;
    }

    private static void __SpreadBatchCaptureTimestamps(
        List<LevelRecordingFrame> output,
        int batchStart,
        int batchCount,
        float fallbackTimestamp)
    {
        float anchor = batchStart > 0
            ? output[batchStart - 1].timestamp
            : math.max(0f, fallbackTimestamp - (batchCount - 1) * DefaultBatchFrameSpacingSeconds);

        for (int i = 0; i < batchCount; ++i)
        {
            var frame = output[batchStart + i];
            frame.timestamp = anchor + (i + 1) * DefaultBatchFrameSpacingSeconds;
            output[batchStart + i] = frame;
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
