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
    /// Expand scene roots through CSR entity/archive dependency edges.
    /// </summary>
    public static void ExpandSceneRoots(
        File file,
        SceneEntry entry,
        List<int> archiveIndicesOut,
        List<int> entitySceneIndicesOut)
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
            var queue = new Queue<int>();
            for (int i = 0; i < entry.entitySceneIndices.Length; i++)
            {
                EnqueueIndex(entry.entitySceneIndices[i], entityPool.Length, entityVisited, queue);
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                entitySceneIndicesOut.Add(index);
                ForEachCsrEdge(
                    file.entitySceneArchiveOffsets,
                    file.entitySceneArchiveIndices,
                    index,
                    archivePool != null ? archivePool.Length : 0,
                    dep => archiveRoots.Add(dep));
                ForEachCsrEdge(
                    file.entitySceneDepOffsets,
                    file.entitySceneDepIndices,
                    index,
                    entityPool.Length,
                    dep => EnqueueIndex(dep, entityPool.Length, entityVisited, queue));
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
