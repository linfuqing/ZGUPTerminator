using Unity.Collections;
using ZG;

/// <summary>
/// Post-MatchStart squad handshake. PlayerProperty promotes the host's RemotePlayer to Joined and
/// the descriptor's non-zero Status publishes the live stage. A trailing Status(0) is forbidden
/// because the host interprets a non-zero to zero transition after Joined as cancellation.
/// </summary>
internal static class BotRelaySquadHandshakeOps
{
    internal static bool TryInjectStatus(
        int index,
        ref BotRelayFarmNative farm,
        uint userStageID,
        ref NativeQueue<BotRelayInject>.ParallelWriter injectWriter,
        byte injectEnabled)
    {
        if (userStageID == 0 || injectEnabled == 0)
        {
            return false;
        }

        ref var state = ref farm.agentStates.ElementAt(index);
        ref var session = ref farm.sessions.ElementAt(index);
        ref readonly var descriptor = ref farm.levelStartMessages.ElementAt(index);
        if (descriptor.userStageID == 0 || descriptor.userStageID != userStageID)
            return false;

        session.channelStatus = (int)userStageID;
        BotRelayInjectOps.InjectControlMessage(
            ref injectWriter,
            state.userID,
            (int)NetworkRelayMessageType.Status,
            (int)userStageID,
            true);
        BotRelayAgentDiagnostics.LogSquadHandshakeStatus(state.userID, userStageID);
        return true;
    }

}
