using System;
using System.Runtime.InteropServices;
using Unity.Collections;

[StructLayout(LayoutKind.Explicit, Size=64)]
[GenerateTestsForBurstCompatibility]
public struct FixedBytes80
{
    [FieldOffset(0)]
    public FixedBytes16 offset00;
    [FieldOffset(16)]
    public FixedBytes16 offset16;
    [FieldOffset(32)]
    public FixedBytes16 offset32;
    [FieldOffset(48)]
    public FixedBytes16 offset48;
    [FieldOffset(64)]
    public FixedBytes16 offset64;

    public FixedBytes80(ref DataStreamReader reader)
    {
        this = default;
        reader.ReadBytes(AsArray());
    }

    public void Write(ref DataStreamWriter writer)
    {
        writer.WriteBytes(AsArray());
    }

    public unsafe NativeArray<byte> AsArray()
    {
        fixed (void* ptr = &this)
            return CollectionHelper.ConvertExistingDataToNativeArray<byte>(ptr,
                80, Allocator.Temp, true);
    }
}
