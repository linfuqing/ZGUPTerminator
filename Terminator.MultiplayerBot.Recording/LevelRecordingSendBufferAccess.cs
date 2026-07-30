using System;
using ZG;

/// <summary>
/// Owns the recording-only EndWrite journal lifecycle on the same sendBuffer used by ClientData.
/// The journal never reads or advances the transport send/retry cursor.
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

    public static bool TryBeginCapture(
        in NetworkClientSendBuffer.EndWriteCaptureStamp initialStamp,
        out uint generation,
        out string error)
    {
        generation = 0;
        error = null;
        if (!TryGetSendBuffer(out var sendBuffer))
        {
            error = "ClientData.sendBuffer is unavailable.";
            return false;
        }

        try
        {
            generation = sendBuffer.BeginEndWriteCapture(in initialStamp);
            return generation != 0;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static bool TrySetCaptureStamp(
        uint generation,
        in NetworkClientSendBuffer.EndWriteCaptureStamp stamp,
        out string error)
    {
        error = null;
        if (!TryGetSendBuffer(out var sendBuffer))
        {
            error = "ClientData.sendBuffer is unavailable while advancing the capture clock.";
            return false;
        }

        if (sendBuffer.SetEndWriteCaptureStamp(generation, in stamp))
        {
            return true;
        }

        error = $"EndWrite capture generation {generation} is no longer active.";
        return false;
    }

    public static bool TryEndCapture(
        uint generation,
        bool discardUnread,
        out string error)
    {
        error = null;
        if (!TryGetSendBuffer(out var sendBuffer))
        {
            error = "ClientData.sendBuffer is unavailable while ending capture.";
            return false;
        }

        if (sendBuffer.EndWriteCapture(generation, discardUnread))
        {
            return true;
        }

        error =
            $"EndWrite capture generation {generation} could not end cleanly " +
            $"(outstanding={sendBuffer.capturedEndWriteCount}, fault={sendBuffer.endWriteCaptureFault}, " +
            $"discardUnread={discardUnread}).";
        return false;
    }

    public static string DescribeCaptureJournal()
    {
        if (!TryGetSendBuffer(out var sendBuffer))
        {
            return "sendBuffer=unavailable";
        }

        return
            $"captureOutstanding={sendBuffer.capturedEndWriteCount}, " +
            $"captureFault={sendBuffer.endWriteCaptureFault}, " +
            $"sendBufferSlots={sendBuffer.GetPendingSendSlotCount()}";
    }

    public static int GetCapturedEndWriteCount()
    {
        return TryGetSendBuffer(out var sendBuffer)
            ? sendBuffer.capturedEndWriteCount
            : 0;
    }
}
