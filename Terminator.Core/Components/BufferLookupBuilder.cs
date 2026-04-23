using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

public enum BufferLookupBufferOpcode
{
    None = 0,
    Enabled,
    Disabled
}

[BurstCompile]
public struct BufferLookupBufferJob<T> : IJob where T : unmanaged, IBufferElementData
{
    public BufferLookupBuffer<T> buffer;

    public void Execute()
    {
        buffer.Playback();
    }
}

public struct BufferLookupBuffer<T> where T : unmanaged, IBufferElementData
{
    private struct Element
    {
        public BufferLookupBufferOpcode opcode;
        
        public Entity entity;
        
        public T value;
    }

    public struct ParallelWriter
    {
        private NativeQueue<Element>.ParallelWriter __elements;

        public ParallelWriter(ref BufferLookupBuffer<T> buffer)
        {
            __elements = buffer.__elements.AsParallelWriter();
        }

        public void Enqueue(in Entity entity, in T value, BufferLookupBufferOpcode opcode)
        {
            Element element;
            element.opcode = opcode;
            element.entity = entity;
            element.value = value;
            __elements.Enqueue(element);
        }
    }

    private NativeQueue<Element> __elements;
    private BufferLookup<T> __results;

    public BufferLookup<T> results => __results;

    public BufferLookupBuffer(ref SystemState systemState, in AllocatorManager.AllocatorHandle allocator)
    {
        __results = systemState.GetBufferLookup<T>();
        __elements = new NativeQueue<Element>(allocator);
    }

    public void Dispose()
    {
        __elements.Dispose();
    }

    public ParallelWriter AsParallelWriter()
    {
        return new ParallelWriter(ref this);
    }

    public void Playback()
    {
        while (__elements.TryDequeue(out var element))
        {
            results[element.entity].Add(element.value);

            switch (element.value)
            {
                case BufferLookupBufferOpcode.Enabled:
                    results.SetBufferEnabled(element.entity, true);
                    break;
                case BufferLookupBufferOpcode.Disabled:
                    results.SetBufferEnabled(element.entity, false);
                    break;
            }
        }
    }

    public JobHandle Schedule(ref SystemState systemState, in JobHandle dependsOn)
    {
        __results.Update(ref systemState);
        
        BufferLookupBufferJob<T> job;
        job.buffer = this;
        
        return job.Schedule(dependsOn);
    }
}
