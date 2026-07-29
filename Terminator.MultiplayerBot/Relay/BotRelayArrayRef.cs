using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

internal static class BotRelayArrayRef
{
    public static ref T ElementAt<T>(this NativeArray<T> array, int index) where T : struct
    {
        unsafe
        {
            return ref UnsafeUtility.ArrayElementAsRef<T>(array.GetUnsafePtr(), index);
        }
    }
}
