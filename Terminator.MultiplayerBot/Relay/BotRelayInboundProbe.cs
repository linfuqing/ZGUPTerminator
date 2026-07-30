using Unity.Burst;

/// <summary>
/// TEMP DIAGNOSTIC — Burst-safe probe for the bot inbound / Play handshake path.
/// Counters are written from Burst jobs (inbound decode, agent logic, replay load) and
/// read from the managed <see cref="BotRelayInboundProbeSystem"/>. Aggregate (not per-agent);
/// with a single bot in the farm this is exact. Remove once the Invite→Play deadlock is resolved.
/// </summary>
internal static class BotRelayInboundProbe
{
    struct LastRelayTypeKey { }
    struct InboundCountKey { }
    struct LastClientMsgTypeKey { }
    struct LevelStartHandledKey { }
    struct LastStageWrittenKey { }
    struct ChannelJoinSeenKey { }
    struct ChannelLeaveSeenKey { }
    struct LevelStartClearedKey { }
    struct LastClearSiteKey { }
    struct BotStateKey { }
    struct AppInjectedKey { }
    struct ReplayFrameCountKey { }
    struct ReplayNextFrameKey { }
    struct InLevelExitDestKey { }
    struct InLevelExitTimeMsKey { }
    struct InLevelExitCountKey { }
    struct InboundDropsKey { }
    struct EventDropsKey { }
    struct RemotePresenceKey { }

    static readonly SharedStatic<int> s_LastRelayType =
        SharedStatic<int>.GetOrCreate<LastRelayTypeKey>();

    static readonly SharedStatic<int> s_InboundCount =
        SharedStatic<int>.GetOrCreate<InboundCountKey>();

    static readonly SharedStatic<int> s_LastClientMsgType =
        SharedStatic<int>.GetOrCreate<LastClientMsgTypeKey>();

    static readonly SharedStatic<int> s_LevelStartHandled =
        SharedStatic<int>.GetOrCreate<LevelStartHandledKey>();

    static readonly SharedStatic<int> s_LastStageWritten =
        SharedStatic<int>.GetOrCreate<LastStageWrittenKey>();

    static readonly SharedStatic<int> s_ChannelJoinSeen =
        SharedStatic<int>.GetOrCreate<ChannelJoinSeenKey>();

    static readonly SharedStatic<int> s_ChannelLeaveSeen =
        SharedStatic<int>.GetOrCreate<ChannelLeaveSeenKey>();

    static readonly SharedStatic<int> s_LevelStartCleared =
        SharedStatic<int>.GetOrCreate<LevelStartClearedKey>();

    static readonly SharedStatic<int> s_LastClearSite =
        SharedStatic<int>.GetOrCreate<LastClearSiteKey>();

    static readonly SharedStatic<int> s_BotState =
        SharedStatic<int>.GetOrCreate<BotStateKey>();

    // Count of in-level reply frames (Move/Camera/etc) the replay actually pushed into the
    // app-layer inject queue. Lets us see whether the bot is emitting gameplay traffic at all.
    static readonly SharedStatic<int> s_AppInjected =
        SharedStatic<int>.GetOrCreate<AppInjectedKey>();

    // Replay drain diagnostics: total frames in the active recording and the current playhead.
    // If nextFrame reaches frameCount within a handful of ticks the recording itself is tiny (or
    // pacing is dumping); if it climbs slowly the replay is playing over its real duration.
    static readonly SharedStatic<int> s_ReplayFrameCount =
        SharedStatic<int>.GetOrCreate<ReplayFrameCountKey>();

    static readonly SharedStatic<int> s_ReplayNextFrame =
        SharedStatic<int>.GetOrCreate<ReplayNextFrameKey>();

    // Captured the instant the agent leaves InLevel: the destination BotState, how long (ms) it
    // held InLevel, and how many times it has happened. This is the decisive read on the flap.
    static readonly SharedStatic<int> s_InLevelExitDest =
        SharedStatic<int>.GetOrCreate<InLevelExitDestKey>();

    static readonly SharedStatic<int> s_InLevelExitTimeMs =
        SharedStatic<int>.GetOrCreate<InLevelExitTimeMsKey>();

    static readonly SharedStatic<int> s_InLevelExitCount =
        SharedStatic<int>.GetOrCreate<InLevelExitCountKey>();

    // Bounded-queue overflow drops (suspected invite-starvation path). Non-zero means the per-agent
    // inbound (cap 64) or event (cap 32) FIFO discarded a message under a burst/catch-up drain.
    static readonly SharedStatic<int> s_InboundDrops =
        SharedStatic<int>.GetOrCreate<InboundDropsKey>();

    static readonly SharedStatic<int> s_EventDrops =
        SharedStatic<int>.GetOrCreate<EventDropsKey>();

    // Last observed InLevel remote presence classification: 0 = not-yet-confirmed (streaming by
    // default), 1 = present (streaming), 2 = absent-after-present (suppressed).
    static readonly SharedStatic<int> s_RemotePresence =
        SharedStatic<int>.GetOrCreate<RemotePresenceKey>();

    public static int LastRelayType => s_LastRelayType.Data;
    public static int InboundCount => s_InboundCount.Data;
    public static int LastClientMsgType => s_LastClientMsgType.Data;
    public static int LevelStartHandled => s_LevelStartHandled.Data;
    public static int LastStageWritten => s_LastStageWritten.Data;
    public static int ChannelJoinSeen => s_ChannelJoinSeen.Data;
    public static int ChannelLeaveSeen => s_ChannelLeaveSeen.Data;
    public static int LevelStartCleared => s_LevelStartCleared.Data;
    public static int LastClearSite => s_LastClearSite.Data;
    public static int BotStateValue => s_BotState.Data;
    public static int AppInjected => s_AppInjected.Data;
    public static int ReplayFrameCount => s_ReplayFrameCount.Data;
    public static int ReplayNextFrame => s_ReplayNextFrame.Data;
    public static int InLevelExitDest => s_InLevelExitDest.Data;
    public static int InLevelExitTimeMs => s_InLevelExitTimeMs.Data;
    public static int InLevelExitCount => s_InLevelExitCount.Data;
    public static int InboundDrops => s_InboundDrops.Data;
    public static int EventDrops => s_EventDrops.Data;
    public static int RemotePresence => s_RemotePresence.Data;

    public static void RecordInbound(int relayType)
    {
        s_LastRelayType.Data = relayType;
        ++s_InboundCount.Data;
    }

    public static void RecordClientMessage(int clientMsgType)
    {
        s_LastClientMsgType.Data = clientMsgType;
    }

    public static void RecordLevelStartHandled()
    {
        ++s_LevelStartHandled.Data;
    }

    public static void RecordStageWritten(int stageID)
    {
        s_LastStageWritten.Data = stageID;
    }

    public static void RecordChannelJoin()
    {
        ++s_ChannelJoinSeen.Data;
    }

    public static void RecordChannelLeave()
    {
        ++s_ChannelLeaveSeen.Data;
    }

    // site: 1 = Leaving state, 2 = OnSquadJoin, 3 = OnSquadLeave
    public static void RecordLevelStartCleared(int site)
    {
        ++s_LevelStartCleared.Data;
        s_LastClearSite.Data = site;
    }

    public static void RecordBotState(int state)
    {
        s_BotState.Data = state;
    }

    public static void RecordAppInjected()
    {
        ++s_AppInjected.Data;
    }

    public static void RecordReplayFrames(int nextFrame, int frameCount)
    {
        s_ReplayNextFrame.Data = nextFrame;
        s_ReplayFrameCount.Data = frameCount;
    }

    public static void RecordInLevelExit(int destState, int timeMs)
    {
        s_InLevelExitDest.Data = destState;
        s_InLevelExitTimeMs.Data = timeMs;
        ++s_InLevelExitCount.Data;
    }

    public static void RecordInboundDrop()
    {
        ++s_InboundDrops.Data;
    }

    public static void RecordEventDrop()
    {
        ++s_EventDrops.Data;
    }

    public static void RecordRemotePresence(int classification)
    {
        s_RemotePresence.Data = classification;
    }

    public static void Reset()
    {
        s_LastRelayType.Data = 0;
        s_InboundCount.Data = 0;
        s_LastClientMsgType.Data = 0;
        s_LevelStartHandled.Data = 0;
        s_LastStageWritten.Data = -1;
        s_ChannelJoinSeen.Data = 0;
        s_ChannelLeaveSeen.Data = 0;
        s_LevelStartCleared.Data = 0;
        s_LastClearSite.Data = 0;
        s_BotState.Data = 0;
        s_AppInjected.Data = 0;
        s_ReplayFrameCount.Data = 0;
        s_ReplayNextFrame.Data = 0;
        s_InLevelExitDest.Data = -1;
        s_InLevelExitTimeMs.Data = 0;
        s_InLevelExitCount.Data = 0;
        s_InboundDrops.Data = 0;
        s_EventDrops.Data = 0;
        s_RemotePresence.Data = 0;
    }
}
