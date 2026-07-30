using Unity.Collections;
public unsafe struct BotRelayPacket
{
    public const int MaxPayloadSize = 1472;

    public int length;
    public fixed byte data[MaxPayloadSize];

    public bool IsEmpty => length <= 0;

    public byte GetByte(int index) => data[index];

    public void SetByte(int index, byte value) => data[index] = value;

    public bool TryCopyBytesTo(NativeArray<byte> destination, int offset)
    {
        if (length <= 0 || !destination.IsCreated || offset < 0 || offset + length > destination.Length)
            return false;

        for (int i = 0; i < length; ++i)
            destination[offset + i] = data[i];

        return true;
    }

    public bool TryCopyTo(NativeArray<byte> destination)
    {
        if (!destination.IsCreated || length <= 0 || destination.Length < length)
            return false;

        for (int i = 0; i < length; ++i)
            destination[i] = data[i];

        return true;
    }

    public void CopyFromPtr(byte* source, int count)
    {
        if (source == null || count <= 0)
        {
            length = 0;
            return;
        }

        if (count > MaxPayloadSize)
            count = MaxPayloadSize;

        for (int i = 0; i < count; ++i)
            data[i] = source[i];

        length = count;
    }
}
