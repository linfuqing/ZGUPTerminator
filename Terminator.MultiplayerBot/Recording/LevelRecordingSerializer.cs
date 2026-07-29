using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

public static class LevelRecordingSerializer
{
    public static string GetRecordingRootDirectory()
    {
        return Path.Combine(Application.streamingAssetsPath, LevelRecordingConstants.RecordingFolderName);
    }

    public static string BuildDefaultFilePath(in LevelRecordingFileHeader header)
    {
        // Header IDs are captured from the recording client and can be remapped by ReplyServer.
        // The recording window requires the operator to choose a canonical {userStageID},
        // {levelID}_{stageIndex}, or {sceneName} parent before it writes the file.
        var directory = Path.Combine(GetRecordingRootDirectory(), "UNASSIGNED_LEVEL_STAGE");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.bin");
    }

    public static void Write(string path, in LevelRecordingFileHeader header, IReadOnlyList<LevelRecordingFrame> frames)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, false);

        var magic = Encoding.ASCII.GetBytes(LevelRecordingConstants.Magic);
        writer.Write(magic);
        writer.Write(LevelRecordingConstants.Version);
        __WriteHeader(writer, in header, frames.Count);

        for (int i = 0; i < frames.Count; ++i)
        {
            var frame = frames[i];
            writer.Write(frame.timestamp);
            var payload = frame.payload ?? Array.Empty<byte>();
            if (payload.Length > ushort.MaxValue)
                throw new InvalidOperationException($"Frame payload too large: {payload.Length}");

            writer.Write((ushort)payload.Length);
            writer.Write(payload);
        }
    }

    public static LevelRecordingFileHeader ReadHeader(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, false);
        __ValidateMagic(reader);
        var version = reader.ReadUInt16();
        return __ReadHeader(reader, version);
    }

    public static LevelRecordingSession Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, false);

        __ValidateMagic(reader);
        var version = reader.ReadUInt16();
        if (version != LevelRecordingConstants.LegacyVersion && version != LevelRecordingConstants.Version)
        {
            throw new InvalidDataException($"Unsupported recording version: {version}");
        }

        var session = new LevelRecordingSession
        {
            header = __ReadHeader(reader, version)
        };

        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();

        long payloadRegionStart = stream.Position;
        long payloadRegionLength = stream.Length - payloadRegionStart;
        for (int i = 0; i < session.header.frameCount; ++i)
        {
            long frameHeaderRemaining = stream.Length - stream.Position;
            if (frameHeaderRemaining < 6)
            {
                throw new InvalidDataException(
                    $"Frame {i}/{session.header.frameCount}: truncated frame header near file end.");
            }

            float timestamp = reader.ReadSingle();
            int payloadLength = reader.ReadUInt16();
            long payloadRemaining = stream.Length - stream.Position;
            if (payloadLength < 0 || payloadLength > payloadRemaining)
            {
                throw new InvalidDataException(
                    $"Frame {i}/{session.header.frameCount}: payload length {payloadLength} exceeds " +
                    $"remaining bytes {payloadRemaining}.");
            }

            session.frames.Add(new LevelRecordingFrame
            {
                timestamp = timestamp,
                payload = reader.ReadBytes(payloadLength)
            });
        }

        if (payloadRegionLength > 0 && session.frames.Count == 0 && session.header.frameCount > 0)
        {
            throw new InvalidDataException("Header frameCount > 0 but no frames could be read.");
        }

        return session;
    }

    private static void __ValidateMagic(BinaryReader reader)
    {
        var magic = reader.ReadBytes(4);
        if (Encoding.ASCII.GetString(magic) != LevelRecordingConstants.Magic)
            throw new InvalidDataException("Invalid recording file magic.");
    }

    private static void __WriteHeader(BinaryWriter writer, in LevelRecordingFileHeader header, int frameCount)
    {
        writer.Write(header.userStageID);
        writer.Write(header.levelID);
        writer.Write(header.stageIndex);
        __WriteFixedString32(writer, header.sceneName);
        writer.Write(header.duration);
        writer.Write(frameCount);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
    }

    private static LevelRecordingFileHeader __ReadHeader(BinaryReader reader, ushort version)
    {
        var header = new LevelRecordingFileHeader
        {
            userStageID = reader.ReadUInt32(),
            levelID = reader.ReadUInt32(),
            stageIndex = reader.ReadInt32(),
            sceneName = version >= LevelRecordingConstants.Version
                ? __ReadFixedString32(reader)
                : default,
            duration = reader.ReadSingle(),
            frameCount = reader.ReadInt32()
        };
        return header;
    }

    private static void __WriteFixedString32(BinaryWriter writer, FixedString32Bytes value)
    {
        var text = value.ToString();
        if (text.Length > 31)
            throw new InvalidOperationException($"sceneName too long: {text.Length}");

        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }

    private static FixedString32Bytes __ReadFixedString32(BinaryReader reader)
    {
        var length = reader.ReadByte();
        if (length == 0)
            return default;

        var bytes = reader.ReadBytes(length);
        return new FixedString32Bytes(Encoding.UTF8.GetString(bytes));
    }
}
