using System;
using System.Threading;
using Unity.Burst;
using Unity.Collections;

/// <summary>
/// FIFO multi-bucket queue (mirrors UTP internal NativeMultiQueue for use outside the transport assembly).
/// Per-bucket spinlocks allow parallel Enqueue/Dequeue on distinct buckets during farm tick jobs.
/// </summary>
[BurstCompile]
public struct BotRelayMultiQueue<T> : IDisposable where T : unmanaged
{
    private NativeList<T> m_Queue;
    private NativeList<int> m_QueueHeadTail;
    private NativeList<int> m_BucketLocks;
    private NativeArray<int> m_MaxItems;

    public bool IsCreated => m_Queue.IsCreated;

    public BotRelayMultiQueue(int initialMessageCapacity)
    {
        m_MaxItems = new NativeArray<int>(1, Allocator.Persistent);
        m_MaxItems[0] = initialMessageCapacity;
        m_Queue = new NativeList<T>(initialMessageCapacity, Allocator.Persistent);
        m_QueueHeadTail = new NativeList<int>(2, Allocator.Persistent);
        m_BucketLocks = new NativeList<int>(2, Allocator.Persistent);
    }

    public void Dispose()
    {
        if (m_MaxItems.IsCreated)
            m_MaxItems.Dispose();
        if (m_Queue.IsCreated)
            m_Queue.Dispose();
        if (m_QueueHeadTail.IsCreated)
            m_QueueHeadTail.Dispose();
        if (m_BucketLocks.IsCreated)
            m_BucketLocks.Dispose();
    }

    public void Enqueue(int bucket, T value)
    {
        __AcquireBucketLock(bucket);
        try
        {
            __EnqueueUnlocked(bucket, value);
        }
        finally
        {
            __ReleaseBucketLock(bucket);
        }
    }

    public bool Dequeue(int bucket, out T value)
    {
        if (!__TryAcquireExistingBucketLock(bucket))
        {
            value = default;
            return false;
        }

        try
        {
            return __DequeueUnlocked(bucket, out value);
        }
        finally
        {
            __ReleaseBucketLock(bucket);
        }
    }

    public bool Peek(int bucket, out T value)
    {
        if (!__TryAcquireExistingBucketLock(bucket))
        {
            value = default;
            return false;
        }

        try
        {
            return __PeekUnlocked(bucket, out value);
        }
        finally
        {
            __ReleaseBucketLock(bucket);
        }
    }

    public void Clear(int bucket)
    {
        if (!__TryAcquireExistingBucketLock(bucket))
            return;

        try
        {
            m_QueueHeadTail[bucket * 2] = 0;
            m_QueueHeadTail[bucket * 2 + 1] = 0;
        }
        finally
        {
            __ReleaseBucketLock(bucket);
        }
    }

    public void ClearAll()
    {
        if (!m_QueueHeadTail.IsCreated)
            return;

        for (int bucket = 0; bucket < m_QueueHeadTail.Length / 2; ++bucket)
        {
            __AcquireBucketLock(bucket);
            try
            {
                m_QueueHeadTail[bucket * 2] = 0;
                m_QueueHeadTail[bucket * 2 + 1] = 0;
            }
            finally
            {
                __ReleaseBucketLock(bucket);
            }
        }
    }

    private void __EnqueueUnlocked(int bucket, T value)
    {
        if (bucket >= m_QueueHeadTail.Length / 2)
        {
            int oldSize = m_QueueHeadTail.Length;
            m_QueueHeadTail.ResizeUninitialized((bucket + 1) * 2);
            for (; oldSize < m_QueueHeadTail.Length; ++oldSize)
                m_QueueHeadTail[oldSize] = 0;
            m_Queue.ResizeUninitialized(m_QueueHeadTail.Length / 2 * m_MaxItems[0]);
            __EnsureBucketLockCount(bucket);
        }

        int idx = m_QueueHeadTail[bucket * 2 + 1];
        if (idx >= m_MaxItems[0])
        {
            int oldMax = m_MaxItems[0];
            while (idx >= m_MaxItems[0])
                m_MaxItems[0] *= 2;

            int maxBuckets = m_QueueHeadTail.Length / 2;
            m_Queue.ResizeUninitialized(maxBuckets * m_MaxItems[0]);
            for (int b = maxBuckets - 1; b >= 0; --b)
            {
                for (int i = m_QueueHeadTail[b * 2 + 1] - 1; i >= m_QueueHeadTail[b * 2]; --i)
                    m_Queue[b * m_MaxItems[0] + i] = m_Queue[b * oldMax + i];
            }
        }

        m_Queue[m_MaxItems[0] * bucket + idx] = value;
        m_QueueHeadTail[bucket * 2 + 1] = idx + 1;
    }

    private bool __DequeueUnlocked(int bucket, out T value)
    {
        if (bucket < 0 || bucket >= m_QueueHeadTail.Length / 2)
        {
            value = default;
            return false;
        }

        int idx = m_QueueHeadTail[bucket * 2];
        if (idx >= m_QueueHeadTail[bucket * 2 + 1])
        {
            m_QueueHeadTail[bucket * 2] = 0;
            m_QueueHeadTail[bucket * 2 + 1] = 0;
            value = default;
            return false;
        }

        if (idx + 1 == m_QueueHeadTail[bucket * 2 + 1])
            m_QueueHeadTail[bucket * 2] = m_QueueHeadTail[bucket * 2 + 1] = 0;
        else
            m_QueueHeadTail[bucket * 2] = idx + 1;

        value = m_Queue[m_MaxItems[0] * bucket + idx];
        return true;
    }

    private bool __PeekUnlocked(int bucket, out T value)
    {
        if (bucket < 0 || bucket >= m_QueueHeadTail.Length / 2)
        {
            value = default;
            return false;
        }

        int idx = m_QueueHeadTail[bucket * 2];
        if (idx >= m_QueueHeadTail[bucket * 2 + 1])
        {
            value = default;
            return false;
        }

        value = m_Queue[m_MaxItems[0] * bucket + idx];
        return true;
    }

    private void __EnsureBucketLockCount(int bucket)
    {
        while (m_BucketLocks.Length <= bucket)
            m_BucketLocks.Add(0);
    }

    // Parallel drain jobs must never resize NativeList metadata. A virtual port can be
    // registered before RouteSend has created its queue bucket; that is simply an empty queue.
    private bool __TryAcquireExistingBucketLock(int bucket)
    {
        if (bucket < 0 ||
            bucket >= m_QueueHeadTail.Length / 2 ||
            bucket >= m_BucketLocks.Length)
        {
            return false;
        }

        ref int lockState = ref m_BucketLocks.ElementAt(bucket);
        while (Interlocked.CompareExchange(ref lockState, 1, 0) != 0)
            Unity.Burst.Intrinsics.Common.Pause();

        return true;
    }

    private void __AcquireBucketLock(int bucket)
    {
        __EnsureBucketLockCount(bucket);
        ref int lockState = ref m_BucketLocks.ElementAt(bucket);
        while (Interlocked.CompareExchange(ref lockState, 1, 0) != 0)
            Unity.Burst.Intrinsics.Common.Pause();
    }

    private void __ReleaseBucketLock(int bucket)
    {
        if (bucket < 0 || bucket >= m_BucketLocks.Length)
            return;

        Interlocked.Exchange(ref m_BucketLocks.ElementAt(bucket), 0);
    }
}
