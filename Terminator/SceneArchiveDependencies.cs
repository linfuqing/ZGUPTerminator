using System;
using System.Collections.Generic;

/// <summary>
/// Build-time / runtime shared format: parent Unity scene → files to materialize.
/// Global string pools + CSR dependency edges; scenes store root indices only.
/// </summary>
public static class SceneArchiveDependencies
{
    public const string FileName = "scene_archive_dependencies";

    public const string Extension = "json";

    public static string RelativePathWithoutExtension => $"ContentArchives/{FileName}";

    /// <summary>Path relative to content pack root (same folder as archive_dependencies.bin).</summary>
    public static string RelativePath => $"{RelativePathWithoutExtension}.{Extension}";

    public static string DiskFileName => $"{FileName}.{Extension}";

    [Serializable]
    public class File
    {
        public string catalog;

        /// <summary>Deduped ContentArchives ids (Hash128 strings).</summary>
        public string[] archives;

        /// <summary>
        /// CSR: length == archives.Length + 1.
        /// Node i's direct deps are archiveDepIndices[archiveDepOffsets[i] .. archiveDepOffsets[i+1]).
        /// </summary>
        public int[] archiveDepOffsets;

        public int[] archiveDepIndices;

        /// <summary>
        /// Deduped PathRemap-relative EntityScenes paths
        /// (e.g. EntityScenes/{guid}.entityheader).
        /// </summary>
        public string[] entityScenes;

        /// <summary>
        /// CSR: length == entityScenes.Length + 1.
        /// Header → same-GUID .entities and nested EntityPrefab/EntityScene headers.
        /// </summary>
        public int[] entitySceneDepOffsets;

        public int[] entitySceneDepIndices;

        /// <summary>
        /// CSR: ContentArchives roots per entityScenes node (typically on .entityheader).
        /// </summary>
        public int[] entitySceneArchiveOffsets;

        public int[] entitySceneArchiveIndices;

        public SceneEntry[] scenes;
    }

    [Serializable]
    public class SceneEntry
    {
        /// <summary>Parent Unity scene asset name (matches LoadScene / GameSceneActivation).</summary>
        public string sceneName;

        /// <summary>Parent scene asset GUID (optional, for debugging).</summary>
        public string sceneGuid;

        /// <summary>Root indices into File.archives (closure via archiveDep*).</summary>
        public int[] archiveIndices;

        /// <summary>
        /// Root indices into File.entityScenes (typically .entityheader only;
        /// closure via entitySceneDep* / entitySceneArchive*).
        /// </summary>
        public int[] entitySceneIndices;
    }

    /// <summary>
    /// How far to follow <see cref="File.entitySceneDepIndices"/> from scene roots.
    /// </summary>
    public enum EntitySceneExpandMode
    {
        /// <summary>Full nested EntityPrefab/EntityScene closure (Player hub included).</summary>
        Full = 0,

        /// <summary>
        /// Critical for scene enter: SubScene roots + same-GUID <c>.entities</c>,
        /// plus <b>one hop</b> of nested <c>.entityheader</c> (e.g. baked
        /// <c>RequestEntityPrefabLoaded</c> → Player) and those headers' local sections.
        /// Does not follow Player → weapons (depth ≥ 2 headers).
        /// </summary>
        SubSceneLocal = 1,
    }

    /// <summary>
    /// Expand scene roots through CSR entity/archive dependency edges (full Entity* closure).
    /// </summary>
    public static void ExpandSceneRoots(
        File file,
        SceneEntry entry,
        List<int> archiveIndicesOut,
        List<int> entitySceneIndicesOut)
    {
        ExpandSceneRoots(file, entry, archiveIndicesOut, entitySceneIndicesOut, EntitySceneExpandMode.Full);
    }

    /// <summary>
    /// Expand critical EntityScenes for scene enter: SubScene roots, one-hop nested
    /// EntityPrefab headers (e.g. Player via <c>RequestEntityPrefabLoaded</c>), and same-GUID sections.
    /// Follow with <see cref="ExpandSceneRoots"/> for deferred Player→weapons closure.
    /// </summary>
    public static void ExpandSceneRootsLocal(
        File file,
        SceneEntry entry,
        List<int> archiveIndicesOut,
        List<int> entitySceneIndicesOut)
    {
        ExpandSceneRoots(file, entry, archiveIndicesOut, entitySceneIndicesOut, EntitySceneExpandMode.SubSceneLocal);
    }

    /// <summary>
    /// Expand scene roots through CSR entity/archive dependency edges.
    /// </summary>
    public static void ExpandSceneRoots(
        File file,
        SceneEntry entry,
        List<int> archiveIndicesOut,
        List<int> entitySceneIndicesOut,
        EntitySceneExpandMode entityMode)
    {
        archiveIndicesOut.Clear();
        entitySceneIndicesOut.Clear();
        if (file == null || entry == null)
        {
            return;
        }

        var entityPool = file.entityScenes;
        var archivePool = file.archives;
        if (entityPool == null && archivePool == null)
        {
            return;
        }

        var entityVisited = new HashSet<int>();
        var archiveRoots = new HashSet<int>();

        if (entry.entitySceneIndices != null && entityPool != null)
        {
            // index → nested header hop count from scene roots (0 = root).
            var queue = new Queue<(int index, int headerDepth)>();
            for (int i = 0; i < entry.entitySceneIndices.Length; i++)
            {
                var root = entry.entitySceneIndices[i];
                if (root < 0 || root >= entityPool.Length || !entityVisited.Add(root))
                {
                    continue;
                }

                queue.Enqueue((root, 0));
            }

            while (queue.Count > 0)
            {
                var (index, headerDepth) = queue.Dequeue();
                entitySceneIndicesOut.Add(index);
                ForEachCsrEdge(
                    file.entitySceneArchiveOffsets,
                    file.entitySceneArchiveIndices,
                    index,
                    archivePool != null ? archivePool.Length : 0,
                    dep => archiveRoots.Add(dep));

                var fromPath = entityPool[index];
                ForEachCsrEdge(
                    file.entitySceneDepOffsets,
                    file.entitySceneDepIndices,
                    index,
                    entityPool.Length,
                    dep =>
                    {
                        var toPath = entityPool[dep];
                        if (entityMode == EntitySceneExpandMode.SubSceneLocal)
                        {
                            if (IsSameGuidEntitySectionEdge(fromPath, toPath))
                            {
                                if (entityVisited.Add(dep))
                                {
                                    // Sections keep the parent's headerDepth.
                                    queue.Enqueue((dep, headerDepth));
                                }

                                return;
                            }

                            // Nested Entity* header: only from roots (depth 0) → depth 1 (Player etc.).
                            if (!IsEntityHeaderRelativePath(toPath) || headerDepth > 0)
                            {
                                return;
                            }

                            if (entityVisited.Add(dep))
                            {
                                queue.Enqueue((dep, 1));
                            }

                            return;
                        }

                        if (entityVisited.Add(dep))
                        {
                            queue.Enqueue((dep, 0));
                        }
                    });
            }
        }

        if (entry.archiveIndices != null && archivePool != null)
        {
            for (int i = 0; i < entry.archiveIndices.Length; i++)
            {
                var root = entry.archiveIndices[i];
                if (root >= 0 && root < archivePool.Length)
                {
                    archiveRoots.Add(root);
                }
            }
        }

        ExpandArchiveClosure(
            file.archiveDepOffsets,
            file.archiveDepIndices,
            archivePool,
            archiveRoots,
            archiveIndicesOut);
        archiveIndicesOut.Sort();
        entitySceneIndicesOut.Sort();
    }

    /// <summary>
    /// Local section edge: <c>EntityScenes/{guid}.entityheader</c> → <c>EntityScenes/{guid}.{n}.entities</c>.
    /// </summary>
    public static bool IsSameGuidEntitySectionEdge(string fromPath, string toPath)
    {
        if (!TryParseEntitySceneGuid(fromPath, out var fromGuid) ||
            !TryParseEntitySceneGuid(toPath, out var toGuid))
        {
            return false;
        }

        if (!string.Equals(fromGuid, toGuid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsEntitySectionRelativePath(toPath);
    }

    public static bool TryParseEntitySceneGuid(string relativePath, out string guid)
    {
        guid = null;
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var path = relativePath.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        var fileName = slash >= 0 ? path.Substring(slash + 1) : path;
        if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName.Substring(0, fileName.Length - ".bytes".Length);
        }

        var dot = fileName.IndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        guid = fileName.Substring(0, dot);
        return guid.Length > 0;
    }

    public static bool IsEntitySectionRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var path = relativePath.Replace('\\', '/');
        if (path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(0, path.Length - ".bytes".Length);
        }

        return path.EndsWith(".entities", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEntityHeaderRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var path = relativePath.Replace('\\', '/');
        if (path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(0, path.Length - ".bytes".Length);
        }

        return path.EndsWith(".entityheader", StringComparison.OrdinalIgnoreCase);
    }

    static void EnqueueIndex(int index, int poolLength, HashSet<int> visited, Queue<int> queue)
    {
        if (index < 0 || index >= poolLength || !visited.Add(index))
        {
            return;
        }

        queue.Enqueue(index);
    }

    static void ForEachCsrEdge(
        int[] offsets,
        int[] indices,
        int nodeIndex,
        int targetPoolLength,
        Action<int> visit)
    {
        if (offsets == null ||
            indices == null ||
            nodeIndex < 0 ||
            nodeIndex + 1 >= offsets.Length)
        {
            return;
        }

        var begin = offsets[nodeIndex];
        var end = offsets[nodeIndex + 1];
        if (begin < 0 || end < begin || end > indices.Length)
        {
            return;
        }

        for (int i = begin; i < end; i++)
        {
            var dep = indices[i];
            if (dep >= 0 && dep < targetPoolLength)
            {
                visit(dep);
            }
        }
    }

    static void ExpandArchiveClosure(
        int[] offsets,
        int[] indices,
        string[] archivePool,
        HashSet<int> roots,
        List<int> archiveIndicesOut)
    {
        if (archivePool == null || roots == null || roots.Count == 0)
        {
            return;
        }

        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        foreach (var root in roots)
        {
            if (root >= 0 && root < archivePool.Length && visited.Add(root))
            {
                queue.Enqueue(root);
            }
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            archiveIndicesOut.Add(index);
            ForEachCsrEdge(
                offsets,
                indices,
                index,
                archivePool.Length,
                dep =>
                {
                    if (visited.Add(dep))
                    {
                        queue.Enqueue(dep);
                    }
                });
        }
    }

    /// <summary>
    /// Build CSR arrays from per-node sorted dependency index lists (length == nodeCount).
    /// </summary>
    public static void BuildCsr(IReadOnlyList<int[]> perNodeDeps, out int[] offsets, out int[] indices)
    {
        var nodeCount = perNodeDeps != null ? perNodeDeps.Count : 0;
        offsets = new int[nodeCount + 1];
        var flat = new List<int>(nodeCount * 2);
        for (int i = 0; i < nodeCount; i++)
        {
            offsets[i] = flat.Count;
            var deps = perNodeDeps[i];
            if (deps == null || deps.Length == 0)
            {
                continue;
            }

            for (int j = 0; j < deps.Length; j++)
            {
                flat.Add(deps[j]);
            }
        }

        offsets[nodeCount] = flat.Count;
        indices = flat.ToArray();
    }
}
