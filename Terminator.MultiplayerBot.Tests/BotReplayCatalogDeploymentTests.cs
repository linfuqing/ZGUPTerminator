using NUnit.Framework;
using Unity.Collections;
using ZG;

/// <summary>
/// Explicit deployment preflight for the ignored StreamingAssets recording payload.
/// This validates the real files copied alongside the server, rather than an in-memory fixture.
/// </summary>
[Category("Deployment")]
public sealed class BotReplayCatalogDeploymentTests
{
    [Test]
    public void DeploymentCatalog_LoadsAndCanInjectAMoveFrame()
    {
        var catalog = BotReplayCatalogBuilder.Build(Allocator.TempJob);
        var farm = BotRelayFlowTestFixtures.CreateFarm();
        try
        {
            Assert.Greater(catalog.EntryCount, 0,
                "No recordings were deployed to StreamingAssets/Recordings; Bot matchmaking must stay disabled.");

            int catalogIndex = -1;
            int moveFrameIndex = -1;
            for (int i = 0; i < catalog.EntryCount && catalogIndex < 0; ++i)
            {
                var recording = catalog.entries[i].recording;
                for (int frameIndex = 0; recording.IsCreated && frameIndex < recording.Value.frames.Length; ++frameIndex)
                {
                    if (BotReplayPayloadUtility.TryGetReplyMessageType(recording, frameIndex, out var type) &&
                        type == ReplyMessageType.Move)
                    {
                        catalogIndex = i;
                        moveFrameIndex = frameIndex;
                        break;
                    }
                }
            }

            Assert.GreaterOrEqual(catalogIndex, 0, "The deployed catalog has no replayable Move frame.");

            var recordingToReplay = catalog.entries[catalogIndex].recording;
            var runtime = farm.replayRuntime[0];
            runtime.catalogIndex = catalogIndex;
            runtime.nextFrameIndex = moveFrameIndex;
            runtime.timestampBase = recordingToReplay.Value.frames[moveFrameIndex].timestamp;
            runtime.playbackTime = 0.0;
            runtime.flags = BotReplayRuntimeFlags.Playing;
            farm.replayRuntime[0] = runtime;

            var noInject = default(NativeQueue<BotRelayInject>.ParallelWriter);
            BotReplayLogic.Tick(0, ref farm, catalog.entries, 0.0, ref noInject, 0);

            int outboundCount = BotRelayFlowTestFixtures.GetOutboundCount(ref farm, 0);
            Assert.Greater(outboundCount, 0);
            bool injectedMove = false;
            for (int i = 0; i < outboundCount; ++i)
            {
                var packet = BotRelayFlowTestFixtures.GetOutboundPacket(ref farm, 0, i);
                if (BotRelayFlowTestFixtures.TryReadFirstMessageType(in packet, out int messageType) &&
                    messageType == (int)ReplyMessageType.Move)
                {
                    injectedMove = true;
                    break;
                }
            }

            Assert.IsTrue(injectedMove, "The real recording's selected Move frame was not emitted by replay tick.");
        }
        finally
        {
            for (int i = 0; i < catalog.EntryCount; ++i)
            {
                var recording = catalog.entries[i].recording;
                if (recording.IsCreated)
                    recording.Dispose();
            }

            catalog.Dispose();
            farm.Dispose();
        }
    }
}
