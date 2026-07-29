using Unity.Collections;
using Unity.Entities;

public struct BotReplayFrameMeta
{
    public float timestamp;
    public int payloadOffset;
    public ushort payloadLength;
}

public struct BotRecordingBlob
{
    public BlobArray<BotReplayFrameMeta> frames;
    public BlobArray<byte> payloadBytes;
}

public struct BotReplayCatalogEntry
{
    // Replay identity is derived exclusively from the parent directory name:
    // Recordings/{userStageID}/, Recordings/{levelID}_{stageIndex}/, or Recordings/{sceneName}/.
    // TRBT Header IDs and Header sceneName belong to the recording client and must never
    // influence live replay lookup.
    public uint userStageID;
    public uint levelID;
    public int stageIndex;
    public FixedString32Bytes sceneName;
    public BlobAssetReference<BotRecordingBlob> recording;
}

public struct BotReplayCatalog : IComponentData, System.IDisposable
{
    public Unity.Collections.NativeArray<BotReplayCatalogEntry> entries;

    public bool IsCreated => entries.IsCreated;

    public int EntryCount => entries.IsCreated ? entries.Length : 0;

    public void Dispose()
    {
        if (entries.IsCreated)
            entries.Dispose();
    }
}

[System.Flags]
public enum BotReplayRuntimeFlags : byte
{
    None = 0,
    Loaded = 1 << 0,
    Playing = 1 << 1,
}

public struct BotReplayRuntimeState
{
    public const int InvalidCatalogIndex = -1;

    public int catalogIndex;
    public int nextFrameIndex;

    // Accumulated playback time since Begin(), advanced by the per-tick delta. Pacing is derived
    // from this monotonic accumulator rather than (worldElapsedTime - origin) so it can never
    // desync from the world clock (a stale origin used to dump the whole recording in one tick →
    // NetworkSendMessage -5 + flap). A frame is emitted once playbackTime reaches its timestamp.
    public double playbackTime;
    public double timestampBase;
    public BotReplayRuntimeFlags flags;
}
