using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace ZG
{
    /// <summary>Runs after PostTick flush; never blocks main thread with Dependency.Complete().</summary>
    public struct BotRelayReplayLoadJob : IJob
    {
        public BotRelayFarmNative farm;
        public BotReplayCatalog catalog;
        public double elapsedTime;
        public byte injectEnabled;
        public NativeQueue<BotRelayInject>.ParallelWriter injectWriter;

        public void Execute()
        {
            if (!catalog.IsCreated || catalog.EntryCount == 0)
                return;

            for (int i = 0; i < farm.agentCount; ++i)
            {
                if ((farm.agentFlags[i] & BotAgentRuntimeFlags.PendingPlay) == 0)
                    continue;

                BotRelayReplayLoadOps.HandlePendingPlay(i, ref farm, in catalog, elapsedTime, ref injectWriter, injectEnabled);
            }
        }

        public JobHandle ScheduleAfter(JobHandle dependency) => IJobExtensions.Schedule(this, dependency);
    }

    internal static class BotRelayReplayLoadOps
    {
        internal static void HandlePendingPlay(
            int index,
            ref BotRelayFarmNative farm,
            in BotReplayCatalog catalog,
            double elapsedTime,
            ref NativeQueue<BotRelayInject>.ParallelWriter injectWriter,
            byte injectEnabled)
        {
            ref var state = ref farm.agentStates.ElementAt(index);
            BotRelayReplayRuntimeGate.RemovePendingPlay();
            farm.agentFlags[index] &= ~BotAgentRuntimeFlags.PendingPlay;
            var play = farm.pendingPlayMessage[index];
            state.playLevelID = play.levelID;
            state.playStageIndex = play.stage;

            var stageID = BotRelayPlayStageOps.ResolveUserStageIDForPlay(
                index,
                ref farm,
                in play);

            if (stageID == 0)
            {
                // Ordinary squad ChapterStage may arrive after Play. Matching gets this value from
                // MatchStart; in either case defer rather than publishing a zero-stage handshake.
                BotRelayReplayRuntimeGate.AddPendingPlay();
                farm.agentFlags[index] |= BotAgentRuntimeFlags.PendingPlay;
                return;
            }

            state.targetUserStageID = stageID;

            BotRelayInboundProbe.RecordStageWritten((int)stageID);

            var random = Unity.Mathematics.Random.CreateFromIndex(state.userID ^ play.levelID ^ (uint)play.stage);
            if (!BotReplayCatalogBuilder.TryPickIndex(
                    catalog.entries,
                    stageID,
                    play.levelID,
                    play.stage,
                    play.sceneName,
                    ref random,
                    out var catalogIndex))
            {
                Debug.LogWarning(
                    $"[BotAgent:{state.userID}] No catalog recording for " +
                    $"{stageID}/{play.levelID}_{play.stage}/{play.sceneName}. " +
                    "Lookup is StageID -> LevelID_stage -> Path.GetFileName(sceneName); no cross-key fallback.");
                if (state.matchLoginPhase == BotMatchLoginPhase.PendingPlayerProperty)
                {
                    state.matchLoginPhase = BotMatchLoginPhase.PlayerPropertyFailed;
                    Debug.LogWarning(
                        $"[BotAgent:{state.userID}] Match login PlayerProperty unavailable; " +
                        "keeping Status=0 and failing closed before level entry.");
                    return;
                }

                __TryInjectMatchStatusOnly(index, ref farm, stageID, ref injectWriter, injectEnabled);
                return;
            }

            ref readonly var picked = ref catalog.entries.ElementAt(catalogIndex);
            var recording = picked.recording;

            if (state.matchLoginPhase == BotMatchLoginPhase.PendingPlayerProperty)
            {
                bool sent = BotReplayLoadUtility.TrySendFirstPlayerProperty(
                    ref farm, index, recording, ref injectWriter, injectEnabled);
                state.matchLoginPhase = sent
                    ? BotMatchLoginPhase.PlayerPropertySent
                    : BotMatchLoginPhase.PlayerPropertyFailed;
                if (sent)
                {
                    Debug.Log(
                        $"[BotAgent:{state.userID}] Match login PlayerProperty injected; " +
                        "Status remains 0 until the next match-entry tick.");
                }
                else
                {
                    Debug.LogWarning(
                        $"[BotAgent:{state.userID}] Match login PlayerProperty send failed; " +
                        "Status remains 0 and level entry is fail-closed.");
                }

                return;
            }

            ref var runtime = ref farm.replayRuntime.ElementAt(index);
            BotReplayLogic.Stop(ref runtime);
            runtime.catalogIndex = catalogIndex;
            BotReplayLogic.PrepareRuntime(recording, ref runtime);

            if (!BotReplayLoadUtility.TrySendFirstPlayerProperty(ref farm, index, recording, ref injectWriter, injectEnabled))
            {
                Debug.LogWarning($"[BotAgent:{state.userID}] PlayerProperty send failed.");
            }

            // MatchStart schedules PlayerProperty and then publishes the live stage on a later
            // tick. Do not inject a subsequent Status(0): on the host, ReplyMessages
            // treats a non-zero -> zero transition after Joined as an explicit cancellation.
            if (stageID != 0)
            {
                BotRelaySquadHandshakeOps.TryInjectStatus(
                    index,
                    ref farm,
                    stageID,
                    ref injectWriter, injectEnabled);
            }

            BotReplayLogic.Begin(ref runtime);
            BotAgentLogic.ApplyInLevelEntryFromReplayLoad(index, ref farm, elapsedTime);
            Debug.Log(
                $"[BotAgent:{state.userID}] InLevel replay catalog={catalogIndex} folder={picked.levelID}_{picked.stageIndex}");
        }

        private static void __TryInjectMatchStatusOnly(
            int index,
            ref BotRelayFarmNative farm,
            uint stageID,
            ref NativeQueue<BotRelayInject>.ParallelWriter injectWriter,
            byte injectEnabled)
        {
            if (stageID == 0 || injectEnabled == 0)
                return;

            ref var state = ref farm.agentStates.ElementAt(index);
            ref var sessionState = ref farm.sessions.ElementAt(index);
            sessionState.channelStatus = (int)stageID;
            BotRelayInjectOps.InjectControlMessage(
                ref injectWriter, state.userID, (int)NetworkRelayMessageType.Status, (int)stageID, true);
        }

    }
}
