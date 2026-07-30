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
            for (int i = 0; i < farm.agentCount; ++i)
            {
                if ((farm.agentFlags[i] & BotAgentRuntimeFlags.PendingLevelStart) == 0)
                    continue;

                if (!catalog.IsCreated || catalog.EntryCount == 0)
                {
                    BotRelayReplayRuntimeGate.RemovePendingLevelStart();
                    farm.agentFlags[i] &= ~BotAgentRuntimeFlags.PendingLevelStart;
                    Debug.LogWarning(
                        $"[BotAgent:{farm.agentStates[i].userID}] Replay catalog is unavailable; " +
                        "keeping Status=0 and failing closed.");
                    BotAgentLogic.ApplyReplayLoadFailure(i, ref farm, elapsedTime);
                    continue;
                }

                BotRelayReplayLoadOps.HandlePendingLevelStart(
                    i,
                    ref farm,
                    in catalog,
                    elapsedTime,
                    ref injectWriter,
                    injectEnabled);
            }
        }

        public JobHandle ScheduleAfter(JobHandle dependency) => IJobExtensions.Schedule(this, dependency);
    }

    internal static class BotRelayReplayLoadOps
    {
        internal static void HandlePendingLevelStart(
            int index,
            ref BotRelayFarmNative farm,
            in BotReplayCatalog catalog,
            double elapsedTime,
            ref NativeQueue<BotRelayInject>.ParallelWriter injectWriter,
            byte injectEnabled)
        {
            ref var state = ref farm.agentStates.ElementAt(index);
            BotRelayReplayRuntimeGate.RemovePendingLevelStart();
            farm.agentFlags[index] &= ~BotAgentRuntimeFlags.PendingLevelStart;

            var levelStart = farm.levelStartMessages[index];
            uint stageID = levelStart.userStageID;
            if (stageID == 0 ||
                levelStart.levelID == 0 ||
                levelStart.stage < 0 ||
                levelStart.sceneName.IsEmpty)
            {
                Debug.LogWarning(
                    $"[BotAgent:{state.userID}] Invalid level-start descriptor; " +
                    "keeping Status=0 and failing closed.");
                BotAgentLogic.ApplyReplayLoadFailure(index, ref farm, elapsedTime);
                return;
            }

            BotRelayInboundProbe.RecordStageWritten((int)stageID);

            var random = Unity.Mathematics.Random.CreateFromIndex(
                state.userID ^ levelStart.levelID ^ (uint)levelStart.stage);
            if (!BotReplayCatalogBuilder.TryPickIndex(
                    catalog.entries,
                    stageID,
                    levelStart.levelID,
                    levelStart.stage,
                    levelStart.sceneName,
                    ref random,
                    out var catalogIndex))
            {
                Debug.LogWarning(
                    $"[BotAgent:{state.userID}] No catalog recording for " +
                    $"{stageID}/{levelStart.levelID}_{levelStart.stage}/{levelStart.sceneName}. " +
                    "Lookup is StageID -> LevelID_stage -> Path.GetFileName(sceneName); no cross-key fallback.");
                if (state.levelLoginPhase == BotLevelLoginPhase.PendingPlayerProperty)
                {
                    Debug.LogWarning(
                        $"[BotAgent:{state.userID}] Level login PlayerProperty unavailable; " +
                        "keeping Status=0 and failing closed before level entry.");
                }
                // A live Status without a replay (and therefore without the startup
                // PlayerProperty) makes the Host believe the Bot entered while the level
                // handshake can never finish. Keep Status=0, leave the invalid squad, and free
                // the Bot for a later invitation.
                BotAgentLogic.ApplyReplayLoadFailure(index, ref farm, elapsedTime);
                return;
            }

            ref readonly var picked = ref catalog.entries.ElementAt(catalogIndex);
            var recording = picked.recording;

            if (state.levelLoginPhase == BotLevelLoginPhase.PendingPlayerProperty)
            {
                bool sent = BotReplayLoadUtility.TrySendFirstPlayerProperty(
                    ref farm, index, recording, ref injectWriter, injectEnabled);
                state.levelLoginPhase = sent
                    ? BotLevelLoginPhase.PlayerPropertySent
                    : BotLevelLoginPhase.PlayerPropertyFailed;
                if (sent)
                {
                    Debug.Log(
                        $"[BotAgent:{state.userID}] Level login PlayerProperty injected; " +
                        "Status remains 0 until the next level-entry tick.");
                }
                else
                {
                    Debug.LogWarning(
                        $"[BotAgent:{state.userID}] Level login PlayerProperty send failed; " +
                        "Status remains 0 and level entry is fail-closed.");
                    BotAgentLogic.ApplyReplayLoadFailure(index, ref farm, elapsedTime);
                }

                return;
            }

            if (state.levelLoginPhase != BotLevelLoginPhase.PlayerPropertySent)
            {
                Debug.LogWarning(
                    $"[BotAgent:{state.userID}] Level login phase is not armed; " +
                    "keeping Status=0 and failing closed.");
                BotAgentLogic.ApplyReplayLoadFailure(index, ref farm, elapsedTime);
                return;
            }

            ref var runtime = ref farm.replayRuntime.ElementAt(index);
            BotReplayLogic.Stop(ref runtime);
            runtime.catalogIndex = catalogIndex;
            BotReplayLogic.PrepareRuntime(recording, ref runtime);

            if (!BotReplayLoadUtility.TrySendFirstPlayerProperty(ref farm, index, recording, ref injectWriter, injectEnabled))
            {
                Debug.LogWarning(
                    $"[BotAgent:{state.userID}] PlayerProperty send failed; " +
                    "keeping Status=0 and failing closed before level entry.");
                BotAgentLogic.ApplyReplayLoadFailure(index, ref farm, elapsedTime);
                return;
            }

            // MatchStart schedules PlayerProperty and then publishes the live stage on a later
            // tick. Do not inject a subsequent Status(0): on the host, ReplyMessages
            // treats a non-zero -> zero transition after Joined as an explicit cancellation.
            if (!BotRelaySquadHandshakeOps.TryInjectStatus(
                    index,
                    ref farm,
                    stageID,
                    ref injectWriter,
                    injectEnabled))
            {
                Debug.LogWarning(
                    $"[BotAgent:{state.userID}] Status({stageID}) injection failed; " +
                    "failing closed before replay start.");
                BotAgentLogic.ApplyReplayLoadFailure(index, ref farm, elapsedTime);
                return;
            }

            BotReplayLogic.Begin(ref runtime);
            BotAgentLogic.ApplyInLevelEntryFromReplayLoad(index, ref farm, elapsedTime);
            Debug.Log(
                $"[BotAgent:{state.userID}] InLevel replay catalog={catalogIndex} folder={picked.levelID}_{picked.stageIndex}");
        }
    }
}
