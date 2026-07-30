using Unity.Collections;
using Unity.Entities;
public struct BotRelayWireBlob
{
    public int length;
    public BlobArray<byte> bytes;
}

public struct BotRelayConnectWireBlobEntry
{
    public int wireIndex;
}

public struct BotRelayAgentConnectWireRange
{
    public int offset;
    public int count;
}

public struct BotRelayCatalogBlob
{
    public int inboundAppPayloadOffset;
    public int serverHelloWireIndex;
    public BlobArray<BotRelayConnectWireBlobEntry> connectClientWires;
    /// <summary>Per-agent UTP Connect handshake wires. Business frames are not stored here.</summary>
    public BlobArray<BotRelayAgentConnectWireRange> agentConnectClientWireRanges;
    public BlobArray<BotRelayConnectWireBlobEntry> agentConnectClientWires;
    public BlobArray<BotRelayConnectWireBlobEntry> connectServerWires;
    public BlobArray<int> linkAckWireIndices;
    public BlobArray<BotRelayWireBlob> wires;

    public bool IsValid => connectClientWires.Length > 0 && wires.Length > 0;
}

public struct BotRelayWireCatalogBlobRef : IComponentData
{
    public BlobAssetReference<BotRelayCatalogBlob> blob;
}

internal static class BotRelayCatalogBlobBuilder
{
    public static BlobAssetReference<BotRelayCatalogBlob> Create(
        in BotRelayWireCatalogState catalog,
        BotRelayConnectWireState[] agentConnectWires = null)
    {
        if (!catalog.IsValid || !__HasRequiredAgentConnectWires(agentConnectWires))
            return default;

        var uniquePackets = new NativeList<BotRelayPacket>(32, Allocator.Temp);
        uniquePackets.Add(default); // index 0 = empty wire sentinel (Burst-safe GetWire fallback)

        int AddWire(in BotRelayPacket packet) => __FindOrAddWire(in packet, ref uniquePackets);

        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<BotRelayCatalogBlob>();

        root.inboundAppPayloadOffset = catalog.inboundAppPayloadOffset;
        root.serverHelloWireIndex = AddWire(in catalog.serverHelloWire);

        __CopyConnectWires(ref builder, ref root.connectClientWires, catalog.connectWire.clientPackets, ref uniquePackets);
        __CopyPerAgentConnectWires(
            ref builder,
            ref root.agentConnectClientWireRanges,
            ref root.agentConnectClientWires,
            in catalog,
            agentConnectWires,
            ref uniquePackets);
        __CopyConnectWires(ref builder, ref root.connectServerWires, catalog.connectWire.serverPackets, ref uniquePackets);

        int ackCount = catalog.linkAckTemplates.IsCreated ? catalog.linkAckTemplates.Length : 0;
        var ackArray = builder.Allocate(ref root.linkAckWireIndices, ackCount);
        for (int i = 0; i < ackCount; ++i)
        {
            var ackTemplate = catalog.linkAckTemplates[i];
            ackArray[i] = AddWire(in ackTemplate);
        }

        var wiresArray = builder.Allocate(ref root.wires, uniquePackets.Length);
        for (int i = 0; i < uniquePackets.Length; ++i)
        {
            var packet = uniquePackets[i];
            var bytes = builder.Allocate(ref wiresArray[i].bytes, packet.length);
            for (int j = 0; j < packet.length; ++j)
                bytes[j] = packet.GetByte(j);
            wiresArray[i].length = packet.length;
        }

        uniquePackets.Dispose();
        var blob = builder.CreateBlobAssetReference<BotRelayCatalogBlob>(Allocator.Persistent);
        builder.Dispose();
        return blob;
    }

    private static void __CopyPerAgentConnectWires(
        ref BlobBuilder builder,
        ref BlobArray<BotRelayAgentConnectWireRange> ranges,
        ref BlobArray<BotRelayConnectWireBlobEntry> destination,
        in BotRelayWireCatalogState catalog,
        BotRelayConnectWireState[] agentConnectWires,
        ref NativeList<BotRelayPacket> uniquePackets)
    {
        int agentCount = agentConnectWires != null && agentConnectWires.Length > 0
            ? agentConnectWires.Length
            : 1;
        int totalWireCount = 0;

        for (int agentIndex = 0; agentIndex < agentCount; ++agentIndex)
        {
            bool hasDedicatedWire = agentIndex > 0;
            int wireCount = hasDedicatedWire
                ? agentConnectWires[agentIndex].clientPackets.Length
                : catalog.connectClientWireCount;
            int sourceCount = hasDedicatedWire
                ? agentConnectWires[agentIndex].clientPackets.Length
                : catalog.connectWire.clientPackets.Length;
            if (wireCount > sourceCount)
                wireCount = sourceCount;
            if (wireCount > 0)
                totalWireCount += wireCount;
        }

        var rangeArray = builder.Allocate(ref ranges, agentCount);
        var wireArray = builder.Allocate(ref destination, totalWireCount);
        int writeIndex = 0;

        for (int agentIndex = 0; agentIndex < agentCount; ++agentIndex)
        {
            bool hasDedicatedWire = agentIndex > 0;
            int wireCount = hasDedicatedWire
                ? agentConnectWires[agentIndex].clientPackets.Length
                : catalog.connectClientWireCount;
            int sourceCount = hasDedicatedWire
                ? agentConnectWires[agentIndex].clientPackets.Length
                : catalog.connectWire.clientPackets.Length;
            if (wireCount > sourceCount)
                wireCount = sourceCount;

            rangeArray[agentIndex] = new BotRelayAgentConnectWireRange
            {
                offset = writeIndex,
                count = wireCount
            };

            for (int wireIndex = 0; wireIndex < wireCount; ++wireIndex)
            {
                var entry = hasDedicatedWire
                    ? agentConnectWires[agentIndex].clientPackets[wireIndex]
                    : catalog.connectWire.clientPackets[wireIndex];
                wireArray[writeIndex++] = new BotRelayConnectWireBlobEntry
                {
                    wireIndex = __FindOrAddWire(in entry.wire, ref uniquePackets)
                };
            }
        }
    }

    private static bool __HasRequiredAgentConnectWires(
        BotRelayConnectWireState[] agentConnectWires)
    {
        if (agentConnectWires == null)
            return true;

        for (int i = 1; i < agentConnectWires.Length; ++i)
        {
            if (!agentConnectWires[i].IsValid)
                return false;
        }

        return true;
    }

    private static void __CopyConnectWires(
        ref BlobBuilder builder,
        ref BlobArray<BotRelayConnectWireBlobEntry> destination,
        NativeList<BotRelayConnectWirePacket> source,
        ref NativeList<BotRelayPacket> uniquePackets)
    {
        int count = source.IsCreated ? source.Length : 0;
        var array = builder.Allocate(ref destination, count);
        for (int i = 0; i < count; ++i)
        {
            var entry = source[i];
            array[i] = new BotRelayConnectWireBlobEntry
            {
                wireIndex = __FindOrAddWire(in entry.wire, ref uniquePackets)
            };
        }
    }

    private static int __FindOrAddWire(in BotRelayPacket packet, ref NativeList<BotRelayPacket> uniquePackets)
    {
        if (packet.IsEmpty)
            return BotRelayWireBytes.EmptyWireIndex;

        for (int i = 0; i < uniquePackets.Length; ++i)
        {
            var existing = uniquePackets[i];
            if (BotRelayWireBytes.Equals(in existing, in packet))
                return i;
        }

        uniquePackets.Add(packet);
        return uniquePackets.Length - 1;
    }
}
