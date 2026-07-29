using Unity.Jobs;
using Unity.Networking.Transport;

/// <summary>
/// Init-only driver stepping for WireCatalog / Connect capture.
/// Chains ScheduleUpdate/ScheduleFlushSend per driver; PopEvent only after Complete.
/// </summary>
internal static class BotRelayDriverScheduleUtil
{
    private struct CaptureSession
    {
        public JobHandle listenDep;
        public JobHandle clientDep;
    }

    private static CaptureSession s_capture;

    internal static void BeginCaptureSession()
    {
        CompleteCaptureSession();
    }

    internal static void CompleteCaptureSession()
    {
        s_capture.listenDep.Complete();
        s_capture.clientDep.Complete();
        BotRelayManager.ManagerAccessHandle.Complete();
        s_capture = default;
    }

    internal static void EnsureCaptureIdle()
    {
        s_capture.listenDep.Complete();
        s_capture.clientDep.Complete();
        BotRelayManager.ManagerAccessHandle.Complete();
    }

    internal static void StepDriver(ref NetworkDriver driver, ref JobHandle dep)
    {
        if (!driver.IsCreated)
            return;

        dep.Complete();
        BotRelayManager.ManagerAccessHandle.Complete();

        dep = driver.ScheduleUpdate(dep);
        dep.Complete();
        BotRelayManager.ManagerAccessHandle.Complete();

        dep = driver.ScheduleFlushSend(dep);
        dep.Complete();
        BotRelayManager.ManagerAccessHandle.Complete();
    }

    internal static void StepCapturePair(ref NetworkDriver listenDriver, ref NetworkDriver clientDriver)
    {
        // Client update first so connect outbound is queued before listen receive in the same tick.
        StepDriver(ref clientDriver, ref s_capture.clientDep);
        StepDriver(ref listenDriver, ref s_capture.listenDep);
    }
}
