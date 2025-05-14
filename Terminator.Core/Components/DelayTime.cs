using Unity.Entities;

public struct DelayTime : IBufferElementData
{
    public double start;
    public float value;
    
    public double end => start + value;

    public static void Append(ref DynamicBuffer<DelayTime> delayTimes, double time, float value)
    {
        if (!delayTimes.IsCreated)
            return;

        DelayTime delayTime;
        delayTime.start = time;
        delayTime.value = value;
        delayTimes.Add(delayTime);
    }

    public static bool IsDelay(ref DynamicBuffer<DelayTime> delayTimes, double time, out float value)
    {
        if (delayTimes.IsCreated)
        {
            double end;
            int numDelayTimes = delayTimes.Length;
            for (int i = 0; i < numDelayTimes; ++i)
            {
                ref var delayTime = ref delayTimes.ElementAt(i);
                if (delayTime.start > time)
                    continue;

                end = delayTime.end;
                if (end < time)
                {
                    delayTimes.RemoveAtSwapBack(i--);

                    --numDelayTimes;

                    continue;
                }
                
                value = (float)(end - time);

                return true;
            }
        }

        value = 0.0f;
        
        return false;
    }
}
