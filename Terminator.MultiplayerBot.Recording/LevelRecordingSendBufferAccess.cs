using System.Collections.Generic;
using Unity.Collections;
using ZG;

/// <summary>
/// Recording capture reads the same sendBuffer as <see cref="ClientData.__Send"/> / ApplyStage PlayerProperty.
/// Do not gate on ECS <see cref="NetworkClientDriver"/> singleton (replay inject uses ClientData for the same reason).
/// </summary>
internal static class LevelRecordingSendBufferAccess
{
    public static bool TryGetSendBuffer(out NetworkClientSendBuffer sendBuffer)
    {
        sendBuffer = default;
        if (IClientData.instance is not ClientData clientData)
        {
            return false;
        }

        sendBuffer = clientData.driver.sendBuffer;
        return sendBuffer.isCreated;
    }

    public static void EnsureSlotCursors(ref int[] slotReadCursors, int slotCount)
    {
        if (slotCount <= 0)
        {
            return;
        }

        if (slotReadCursors != null && slotReadCursors.Length >= slotCount)
        {
            return;
        }

        var next = new int[slotCount];
        if (slotReadCursors != null)
        {
            for (int i = 0; i < slotReadCursors.Length && i < slotCount; ++i)
            {
                next[i] = slotReadCursors[i];
            }
        }

        slotReadCursors = next;
    }

    /// <summary>
    /// Advance capture cursors past EndWrites that existed before this session.
    /// Never silently drop <see cref="ReplyMessageType.PlayerProperty"/> — ApplyStage can EndWrite before
    /// <see cref="ReplyMessageRecorder.BeginRecording"/> if Login starts before the first harness Update.
    /// </summary>
    public static void EstablishRecordingBaseline(
        ref int[] slotReadCursors,
        List<LevelRecordingFrame> preservePlayerPropertyFrames,
        float timestamp)
    {
        if (!TryGetSendBuffer(out var sendBuffer))
        {
            return;
        }

        int slotCount = sendBuffer.GetPendingSendSlotCount();
        EnsureSlotCursors(ref slotReadCursors, slotCount);

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
                if (!LevelRecordingPayloadUtility.TryGetReplyMessageType(copy, out var messageType) ||
                    messageType != ReplyMessageType.PlayerProperty ||
                    preservePlayerPropertyFrames == null)
                {
                    continue;
                }

                preservePlayerPropertyFrames.Add(new LevelRecordingFrame
                {
                    timestamp = timestamp,
                    payload = copy
                });
                BotReplayLog.Diag(
                    $"Preserved pre-baseline PlayerProperty EndWrite ({copy.Length} bytes).");
            }

            slotReadCursors[slot] = probeIndex;
        }
    }

    /// <summary>Slots with unread pending EndWrites at current per-slot cursors.</summary>
    public static int CountUncapturedPendingSlots(int[] slotReadCursors)
    {
        if (!TryGetSendBuffer(out var sendBuffer))
        {
            return 0;
        }

        int slotCount = sendBuffer.GetPendingSendSlotCount();
        EnsureSlotCursors(ref slotReadCursors, slotCount);

        int uncapturedSlots = 0;
        NativeArray<byte> bytes;
        for (int slot = 0; slot < slotCount; ++slot)
        {
            int probeIndex = slotReadCursors[slot];
            if (sendBuffer.TryReadPendingSend(slot, ref probeIndex, out bytes))
            {
                uncapturedSlots++;
            }
        }

        return uncapturedSlots;
    }

    public static string DescribeSlotCursors(int[] slotReadCursors)
    {
        if (slotReadCursors == null || slotReadCursors.Length == 0)
        {
            return "slotCursors=empty";
        }

        int max = slotReadCursors[0];
        for (int i = 1; i < slotReadCursors.Length; ++i)
        {
            if (slotReadCursors[i] > max)
            {
                max = slotReadCursors[i];
            }
        }

        return $"slotCount={slotReadCursors.Length}, maxSlotCursor={max}";
    }
}
