using System;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Session-scoped single bookkeeper for materialized content files, shared by
/// GameSceneActivation (critical set) and SceneArchiveDependencySystem
/// (on-demand prefab plans).
///
/// Invariants:
/// - Entry lifetime == file-on-disk lifetime. An entry is added when the file
///   is successfully materialized and removed only when it is successfully
///   deleted. While an entry exists the on-disk copy is authoritative for this
///   session, so Acquire never re-materializes it (IAssetFileManager.Materialize
///   is a destructive replace and must not run against an open archive).
/// - Sharing is refcounted. The critical set and every ready prefab GUID each
///   hold one reference per path; a file is deleted only after all owners
///   released it.
/// - Deletion is deferred and budgeted (Tick), so the caller's unload fence and
///   frame budget keep their guarantees. Flush is the teardown path and deletes
///   everything unconditionally.
///
/// GC discipline: all bookkeeping lives in native containers keyed by
/// FixedString128Bytes; refcount-only Acquire/Release and dedup hits allocate
/// nothing. Managed strings are produced only at the IO boundary (one per real
/// copy/delete, bounded by the caller's per-frame budget), because file APIs
/// and the remap delegate require them.
/// </summary>
public sealed class SceneArchiveContentProvisioner : IDisposable
{
    private struct Entry
    {
        public int refCount;
        public int releaseFrame;
        public bool isPendingRelease;
    }

    private readonly Func<string, bool> __materialize;
    private readonly Func<string, bool> __dematerialize;
    private NativeParallelHashMap<FixedString128Bytes, Entry> __entries;
    private NativeList<FixedString128Bytes> __pendingReleases;

    public int trackedCount => __entries.IsCreated ? __entries.Count() : 0;

    /// <param name="materialize">
    /// Raw copy operation for one relative path (resolve + replace-write).
    /// No dedup or tracking; that is this class's job.
    /// </param>
    /// <param name="dematerialize">
    /// Raw delete operation for one relative path. Returns false when the file
    /// is still busy (e.g. archive unmounting asynchronously) to request a retry.
    /// </param>
    public SceneArchiveContentProvisioner(
        Func<string, bool> materialize,
        Func<string, bool> dematerialize)
    {
        __materialize = materialize ?? throw new ArgumentNullException(nameof(materialize));
        __dematerialize = dematerialize ?? throw new ArgumentNullException(nameof(dematerialize));
        __entries = new NativeParallelHashMap<FixedString128Bytes, Entry>(64, Allocator.Persistent);
        __pendingReleases = new NativeList<FixedString128Bytes>(16, Allocator.Persistent);
    }

    /// <summary>
    /// Adds one reference to the path, materializing it on first use.
    /// <paramref name="performedIO"/> reports whether file IO actually happened
    /// so callers charge their per-frame streaming budget only for real work.
    /// Refcount-only hits are allocation-free.
    /// </summary>
    public bool Acquire(in FixedString128Bytes relativePath, out bool performedIO)
    {
        performedIO = false;

        var key = __Normalize(relativePath);
        if (key.IsEmpty)
        {
            return false;
        }

        if (__entries.TryGetValue(key, out var entry))
        {
            // Still on disk (alive or awaiting deferred release): adopt without
            // IO. This also cancels a pending release without touching a file
            // whose archive may still be unmounting.
            entry.refCount++;
            entry.isPendingRelease = false;
            __entries[key] = entry;

            return true;
        }

        performedIO = true;
        if (!__materialize(key.ToString()))
        {
            return false;
        }

        __entries.TryAdd(key, new Entry { refCount = 1 });

        return true;
    }

    public bool Acquire(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        return Acquire(new FixedString128Bytes(relativePath), out _);
    }

    /// <summary>
    /// Removes one reference. At zero the file is queued for deferred deletion,
    /// executed by a later Tick, so unload fences keep their timing guarantees.
    /// Allocation-free.
    /// </summary>
    public void Release(in FixedString128Bytes relativePath)
    {
        var key = __Normalize(relativePath);
        if (key.IsEmpty || !__entries.TryGetValue(key, out var entry))
        {
            return;
        }

        if (entry.refCount > 1)
        {
            entry.refCount--;
            __entries[key] = entry;

            return;
        }

        entry.refCount = 0;
        if (!entry.isPendingRelease)
        {
            entry.isPendingRelease = true;
            __pendingReleases.Add(key);
        }

        entry.releaseFrame = Time.frameCount;
        __entries[key] = entry;
    }

    /// <summary>
    /// Executes queued deletions with a per-call IO budget. A failed delete
    /// (archive still unmounting) retries on a later Tick.
    /// </summary>
    public void Tick(int maxDematerializeCount)
    {
        if (!__pendingReleases.IsCreated || __pendingReleases.IsEmpty)
        {
            return;
        }

        var frame = Time.frameCount;
        var remaining = Math.Max(1, maxDematerializeCount);
        for (int i = __pendingReleases.Length - 1; i >= 0 && remaining > 0; i--)
        {
            var key = __pendingReleases[i];
            if (!__entries.TryGetValue(key, out var entry) ||
                !entry.isPendingRelease ||
                entry.refCount > 0)
            {
                // Re-acquired (or already removed) since it was queued.
                __pendingReleases.RemoveAtSwapBack(i);

                continue;
            }

            if (frame < entry.releaseFrame)
            {
                continue;
            }

            remaining--;
            if (__dematerialize(key.ToString()))
            {
                __entries.Remove(key);
                __pendingReleases.RemoveAtSwapBack(i);
            }
            else
            {
                entry.releaseFrame = frame + 1;
                __entries[key] = entry;
            }
        }
    }

    /// <summary>
    /// Teardown: best-effort deletes every tracked file regardless of refCount,
    /// without budget. Only call after the runtime no longer reads these files.
    /// </summary>
    public void Flush()
    {
        if (!__entries.IsCreated)
        {
            return;
        }

        using (var keys = __entries.GetKeyArray(Allocator.Temp))
        {
            foreach (var key in keys)
            {
                __dematerialize(key.ToString());
            }
        }

        __entries.Clear();
        __pendingReleases.Clear();
    }

    public void Dispose()
    {
        if (__entries.IsCreated)
        {
            __entries.Dispose();
        }

        if (__pendingReleases.IsCreated)
        {
            __pendingReleases.Dispose();
        }
    }

    // Byte-wise, allocation-free normalization: '\' -> '/' and ASCII lowercase,
    // matching the (lowercased) remap destination and the previous
    // OrdinalIgnoreCase managed dictionary semantics for these ASCII-only paths.
    private static FixedString128Bytes __Normalize(in FixedString128Bytes relativePath)
    {
        var result = relativePath;
        var length = result.Length;
        for (int i = 0; i < length; i++)
        {
            ref var value = ref result.ElementAt(i);
            if (value == (byte)'\\')
            {
                value = (byte)'/';
            }
            else if (value >= (byte)'A' && value <= (byte)'Z')
            {
                value = (byte)(value + 32);
            }
        }

        return result;
    }
}
