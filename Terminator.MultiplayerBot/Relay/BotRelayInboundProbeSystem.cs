using Unity.Entities;
using UnityEngine;

namespace ZG
{
    /// <summary>
    /// TEMP DIAGNOSTIC — managed reader for <see cref="BotRelayInboundProbe"/>. Logs a snapshot
    /// whenever any probe counter changes, so the Invite→Play deadlock can be localized to
    /// "host didn't send" vs "bot didn't receive" vs "bot received but stage unresolved".
    /// Reads SharedStatic ints (benign race, diagnostic only) — no job Complete on the hot path.
    /// Remove together with the probe once resolved.
    /// </summary>
    [UpdateAfter(typeof(BotRelayReplayLoadSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Default)]
    public partial struct BotRelayInboundProbeSystem : ISystem
    {
        private int __inbound;
        private int __lastRelayType;
        private int __lastClientMsg;
        private int __levelStartHandled;
        private int __stageWritten;
        private int __wirePackets;
        private int __channelJoin;
        private int __channelLeave;
        private int __levelStartCleared;
        private int __lastClearSite;
        private int __botState;
        private int __appInjected;
        private int __replayNextFrame;
        private int __replayFrameCount;
        private int __inLevelExitCount;
        private int __routeEnq;
        private int __routeDrop;
        private int __sendPkts;
        private int __inDeq;
        private int __drainExh;
        private int __inboundDrops;
        private int __eventDrops;
        private int __remotePresence;
        private byte __initialized;

        public void OnUpdate(ref SystemState state)
        {
            if (!BotRelayDefines.VerboseTelemetry)
                return;

            int inbound = BotRelayInboundProbe.InboundCount;
            int lastRelayType = BotRelayInboundProbe.LastRelayType;
            int lastClientMsg = BotRelayInboundProbe.LastClientMsgType;
            int levelStartHandled = BotRelayInboundProbe.LevelStartHandled;
            int stageWritten = BotRelayInboundProbe.LastStageWritten;
            int wirePackets = BotRelayManager.Instance.diagDrainPacketsBudget;
            int channelJoin = BotRelayInboundProbe.ChannelJoinSeen;
            int channelLeave = BotRelayInboundProbe.ChannelLeaveSeen;
            int levelStartCleared = BotRelayInboundProbe.LevelStartCleared;
            int lastClearSite = BotRelayInboundProbe.LastClearSite;
            int botState = BotRelayInboundProbe.BotStateValue;
            int appInjected = BotRelayInboundProbe.AppInjected;
            int replayNextFrame = BotRelayInboundProbe.ReplayNextFrame;
            int replayFrameCount = BotRelayInboundProbe.ReplayFrameCount;
            int inLevelExitCount = BotRelayInboundProbe.InLevelExitCount;
            int routeEnq = BotRelayManager.Instance.diagRouteSendEnqueued;
            int routeDrop = BotRelayManager.Instance.diagRouteSendDropped;
            int sendPkts = BotRelayManager.Instance.diagSendJobPackets;
            int inDeq = BotRelayManager.Instance.diagInboundDequeued;
            int drainExh = BotRelayManager.Instance.diagDrainBudgetExhausted;
            int inboundDrops = BotRelayInboundProbe.InboundDrops;
            int eventDrops = BotRelayInboundProbe.EventDrops;
            int remotePresence = BotRelayInboundProbe.RemotePresence;

            bool changed =
                __initialized == 0 ||
                inbound != __inbound ||
                lastRelayType != __lastRelayType ||
                lastClientMsg != __lastClientMsg ||
                levelStartHandled != __levelStartHandled ||
                stageWritten != __stageWritten ||
                wirePackets != __wirePackets ||
                channelJoin != __channelJoin ||
                channelLeave != __channelLeave ||
                levelStartCleared != __levelStartCleared ||
                lastClearSite != __lastClearSite ||
                botState != __botState ||
                appInjected != __appInjected ||
                replayNextFrame != __replayNextFrame ||
                replayFrameCount != __replayFrameCount ||
                inLevelExitCount != __inLevelExitCount ||
                routeEnq != __routeEnq ||
                routeDrop != __routeDrop ||
                sendPkts != __sendPkts ||
                inDeq != __inDeq ||
                drainExh != __drainExh ||
                inboundDrops != __inboundDrops ||
                eventDrops != __eventDrops ||
                remotePresence != __remotePresence;

            if (!changed)
                return;

            __initialized = 1;
            __inbound = inbound;
            __lastRelayType = lastRelayType;
            __lastClientMsg = lastClientMsg;
            __levelStartHandled = levelStartHandled;
            __stageWritten = stageWritten;
            __wirePackets = wirePackets;
            __channelJoin = channelJoin;
            __channelLeave = channelLeave;
            __levelStartCleared = levelStartCleared;
            __lastClearSite = lastClearSite;
            __botState = botState;
            __appInjected = appInjected;
            __replayNextFrame = replayNextFrame;
            __replayFrameCount = replayFrameCount;
            __inLevelExitCount = inLevelExitCount;
            __routeEnq = routeEnq;
            __routeDrop = routeDrop;
            __sendPkts = sendPkts;
            __inDeq = inDeq;
            __drainExh = drainExh;
            __inboundDrops = inboundDrops;
            __eventDrops = eventDrops;
            __remotePresence = remotePresence;

            string line =
                $"[BotProbe] wirePkts={wirePackets} appParsed={inbound} lastRelayType={lastRelayType} " +
                $"lastClientMsg={lastClientMsg} " +
                $"levelStartHandled={levelStartHandled} stageWritten={stageWritten} " +
                $"chJoin={channelJoin} chLeave={channelLeave} levelStartCleared={levelStartCleared} clearSite={lastClearSite} " +
                $"botState={botState} appInj={appInjected} " +
                $"replay={replayNextFrame}/{replayFrameCount} " +
                $"exit={BotRelayInboundProbe.InLevelExitDest}@{BotRelayInboundProbe.InLevelExitTimeMs}ms#{inLevelExitCount} " +
                $"routeEnq={routeEnq} routeDrop={routeDrop} sendPkts={sendPkts} inDeq={inDeq} drainExh={drainExh} " +
                $"inDrops={inboundDrops} evDrops={eventDrops} remotePresence={remotePresence}";

            Debug.Log(line);

            try
            {
                var path = System.IO.Path.Combine(Application.dataPath, "..", "BotProbe.log");
                System.IO.File.AppendAllText(
                    path, $"{System.DateTime.Now:HH:mm:ss.fff} {line}\n");
            }
            catch
            {
                // Diagnostic-only; ignore file I/O failures.
            }
        }
    }
}
