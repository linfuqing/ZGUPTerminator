using System;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;

public static class CollectionUtility
{
    public static unsafe void FixedListInterlockedAdd<T, U>(ref U list, in T item) 
        where T : unmanaged
        where U : unmanaged, INativeList<T>
    {
        __CheckResize<T, U>(list, list.Length + 1);
        
        fixed(void* ptr = &list)
            list[Interlocked.Increment(ref *((int*)ptr)) - 1] = item;
    }
    
    [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(int), typeof(int) })]
    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
    private static void __CheckResize<T, U>(in U list, int newLength) 
        where T : unmanaged
        where U : unmanaged, INativeList<T> 
    {
        int capacity = list.Capacity;
        if (newLength < 0 || newLength > capacity)
            throw new IndexOutOfRangeException($"NewLength {newLength} is out of range of '{capacity}' Capacity.");
    }
}
