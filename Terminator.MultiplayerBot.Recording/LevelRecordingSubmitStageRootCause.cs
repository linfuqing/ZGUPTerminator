using ZG;

/// <summary>
/// Tracks why native sendBuffer PlayerProperty EndWrite was missing (handshake diagnostics).
/// </summary>
internal static class LevelRecordingSubmitStageRootCause
{
    private static bool s_loggedDisabledPromotion;
    private static bool s_loggedCountRepair;
    private static bool s_nativePlayerPropertyCaptured;
    private static int s_disabledPromotionCount;
    private static int s_countRepairCount;

    public static void Reset()
    {
        s_loggedDisabledPromotion = false;
        s_loggedCountRepair = false;
        s_nativePlayerPropertyCaptured = false;
        s_disabledPromotionCount = 0;
        s_countRepairCount = 0;
    }

    public static void NotifyNativePlayerPropertyCaptured()
    {
        s_nativePlayerPropertyCaptured = true;
    }

    public static bool HasNativePlayerPropertyCapture => s_nativePlayerPropertyCaptured;

    public static void NotifyDisabledPromotedToWaiting()
    {
        s_disabledPromotionCount++;
        if (s_loggedDisabledPromotion)
        {
            return;
        }

        s_loggedDisabledPromotion = true;
        BotReplayLog.Diag(
            "Promoted Remote Disabled→Waiting before handshake completed. " +
            "If native PlayerProperty is still missing, __SubmitStage likely ran while Remote was Disabled " +
            "(LoginManager skips sendBuffer EndWrite when Disabled; property.Apply still runs).");
    }

    public static void NotifyRemotePlayerCountRepaired()
    {
        s_countRepairCount++;
        if (s_loggedCountRepair)
        {
            return;
        }

        s_loggedCountRepair = true;
        BotReplayLog.Diag(
            "Repaired remotePlayerCount to 1 after Reply Connect reset. " +
            "If __Start began with count=0, SinglePlay would set Remote Disabled before __SubmitStage.");
    }

    public static string DescribeForHandshake(bool localPropertyEmpty, int largePendingPayloads)
    {
        if (s_nativePlayerPropertyCaptured)
        {
            return "rootCause=nativeEndWriteOk";
        }

        if (localPropertyEmpty)
        {
            return "rootCause=ApplyStageNotFinishedOrPropertyInvalid";
        }

        if (s_disabledPromotionCount > 0 || s_countRepairCount > 0)
        {
            return
                "rootCause=SubmitStageSendSkippedRemoteDisabledOrSinglePlay " +
                $"(disabledPromotions={s_disabledPromotionCount}, countRepairs={s_countRepairCount}, " +
                $"largePendingPayloads={largePendingPayloads})";
        }

        if (largePendingPayloads > 0)
        {
            return "rootCause=sendBufferHasLargePendingButCaptureMissed (investigate capture cursor)";
        }

        return "rootCause=sendBufferBeginWriteFailedOrEndWriteNeverQueued (coop gate was open at probe time)";
    }
}
