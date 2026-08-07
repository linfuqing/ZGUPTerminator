using System;
using System.Collections.Generic;
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
/// </summary>
public sealed class SceneArchiveContentProvisioner
{
    private sealed class Entry
    {
        public int refCount;
        public bool isPendingRelease;
        public int releaseFrame;
    }

    private readonly Func<string, bool> __materialize;
    private readonly Func<string, bool> __dematerialize;
    private readonly Dictionary<string, Entry> __entries =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> __pendingReleases = new List<string>();

    public int trackedCount => __entries.Count;

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
    }

    /// <summary>
    /// Adds one reference to the path, materializing it on first use.
    /// <paramref name="performedIO"/> reports whether file IO actually happened
    /// so callers charge their per-frame streaming budget only for real work.
    /// </summary>
    public bool Acquire(string relativePath, out bool performedIO)
    {
        performedIO = false;

        var key = __Normalize(relativePath);
        if (string.IsNullOrEmpty(key))
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
            return true;
        }

        performedIO = true;
        if (!__materialize(key))
        {
            return false;
        }

        entry = new Entry { refCount = 1 };
        __entries.Add(key, entry);

        return true;
    }

    public bool Acquire(string relativePath)
    {
        return Acquire(relativePath, out _);
    }

    /// <summary>
    /// Removes one reference. At zero the file is queued for deferred deletion,
    /// executed by a later Tick, so unload fences keep their timing guarantees.
    /// </summary>
    public void Release(string relativePath)
    {
        var key = __Normalize(relativePath);
        if (string.IsNullOrEmpty(key) || !__entries.TryGetValue(key, out var entry))
        {
            return;
        }

        if (entry.refCount > 1)
        {
            entry.refCount--;

            return;
        }

        entry.refCount = 0;
        if (!entry.isPendingRelease)
        {
            entry.isPendingRelease = true;
            __pendingReleases.Add(key);
        }

        entry.releaseFrame = Time.frameCount;
    }

    /// <summary>
    /// Executes queued deletions with a per-call IO budget. A failed delete
    /// (archive still unmounting) retries on a later Tick.
    /// </summary>
    public void Tick(int maxDematerializeCount)
    {
        if (__pendingReleases.Count == 0)
        {
            return;
        }

        var frame = Time.frameCount;
        var remaining = Math.Max(1, maxDematerializeCount);
        for (int i = __pendingReleases.Count - 1; i >= 0 && remaining > 0; i--)
        {
            var key = __pendingReleases[i];
            if (!__entries.TryGetValue(key, out var entry) ||
                !entry.isPendingRelease ||
                entry.refCount > 0)
            {
                // Re-acquired (or already removed) since it was queued.
                __pendingReleases.RemoveAt(i);

                continue;
            }

            if (frame < entry.releaseFrame)
            {
                continue;
            }

            remaining--;
            if (__dematerialize(key))
            {
                __entries.Remove(key);
                __pendingReleases.RemoveAt(i);
            }
            else
            {
                entry.releaseFrame = frame + 1;
            }
        }
    }

    /// <summary>
    /// Teardown: best-effort deletes every tracked file regardless of refCount,
    /// without budget. Only call after the runtime no longer reads these files.
    /// </summary>
    public void Flush()
    {
        foreach (var pair in __entries)
        {
            __dematerialize(pair.Key);
        }

        __entries.Clear();
        __pendingReleases.Clear();
    }

    private static string __Normalize(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }

        return relativePath.Replace('\\', '/');
    }
}
