    /// <summary>
    /// Resolves the live userStageID used for Status inject and replay catalog pick on Play.
    /// The value must originate from the current relay session (ChapterStage or remote Status) or
    /// the authoritative MatchStart descriptor produced by LoginManager.ApplyStart.
    /// ClientMessagePlay.stage is a stage index, not a live userStageID, and recording metadata
    /// must never invent a player stage.
    /// </summary>
internal static class BotRelayPlayStageOps
{
    internal static uint ResolveUserStageIDForPlay(
        int index,
        ref BotRelayFarmNative farm,
        in ClientMessagePlay play)
    {
        ref var state = ref farm.agentStates.ElementAt(index);
        ref readonly var session = ref farm.sessions.ElementAt(index);

        // Match: pendingPlayMessage is synthesized from MatchStart, while Play.stage remains only
        // a stage index. targetUserStageID is the backend ID captured by ApplyStart after Register.
        if (state.matchPaired)
        {
            // This target is set by MatchStart (or fresh live relay state for diagnostics); it is
            // never populated from Play/recording metadata.
            if (state.targetUserStageID != 0)
            {
                return state.targetUserStageID;
            }

            if (session.remoteChannelStatus > 0)
            {
                return (uint)session.remoteChannelStatus;
            }

            uint matchChapterStage = farm.lastSeenChapterStage[index];
            if (matchChapterStage != 0)
            {
                return matchChapterStage;
            }
        }
        else
        {
            uint chapterStage = farm.lastSeenChapterStage[index];
            if (chapterStage != 0)
            {
                return chapterStage;
            }

            if (session.remoteChannelStatus > 0)
            {
                return (uint)session.remoteChannelStatus;
            }

            if (state.targetUserStageID != 0)
            {
                return state.targetUserStageID;
            }
        }

        return 0;
    }

    internal static void ClearPlayStageState(int index, ref BotRelayFarmNative farm)
    {
        ref var runtime = ref farm.replayRuntime.ElementAt(index);
        BotReplayLogic.Stop(ref runtime);

        ref var state = ref farm.agentStates.ElementAt(index);
        state.targetUserStageID = 0;
        state.playLevelID = 0;
        state.playStageIndex = 0;
        state.squadHostPlaySeen = false;
        farm.lastSeenChapterStage[index] = 0;
    }
}
