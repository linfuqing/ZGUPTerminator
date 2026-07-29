using Unity.Collections;
using ZG;

/// <summary>
/// Post-Play squad handshake. PlayerProperty promotes the host's RemotePlayer to Joined and the
/// non-zero Status publishes the live stage. A trailing Status(0) is forbidden because the host
/// interprets a non-zero to zero transition after Joined as cancellation.
/// </summary>
internal static class BotRelaySquadHandshakeOps
{
    internal static void TryInjectStatus(
        int index,
        ref BotRelayFarmNative farm,
        uint userStageID,
        ref NativeQueue<BotRelayInject>.ParallelWriter injectWriter,
        byte injectEnabled)
    {
        if (userStageID == 0 || injectEnabled == 0)
        {
            return;
        }

        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        farm.lastSeenChapterStage[index] = userStageID;
        state.targetUserStageID = userStageID;
        session.channelStatus = (int)userStageID;
        BotRelayInjectOps.InjectControlMessage(
            ref injectWriter,
            state.userID,
            (int)NetworkRelayMessageType.Status,
            (int)userStageID,
            true);
        BotRelayAgentDiagnostics.LogSquadHandshakeStatus(state.userID, userStageID);
    }

}
