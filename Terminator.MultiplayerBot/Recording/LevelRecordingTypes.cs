using System;
using System.Collections.Generic;
using Unity.Collections;

public static class LevelRecordingConstants
{
    public const string Magic = "TRBT";
    public const ushort Version = 2;
    public const string RecordingFolderName = "Recordings";
}

[Serializable]
public struct LevelRecordingFileHeader
{
    public uint userStageID;
    public uint levelID;
    public int stageIndex;
    public FixedString32Bytes sceneName;
    public float duration;
    public int frameCount;
}

[Serializable]
public struct LevelRecordingFrame
{
    public float timestamp;
    public byte[] payload;
}

public sealed class LevelRecordingSession
{
    public LevelRecordingFileHeader header;
    public readonly List<LevelRecordingFrame> frames = new List<LevelRecordingFrame>();
    public double startTime;
    public string captureError;
    public bool isActive;

    public float ElapsedTime => isActive
        ? (float)(UnityEngine.Time.unscaledTimeAsDouble - startTime)
        : header.duration;
}
