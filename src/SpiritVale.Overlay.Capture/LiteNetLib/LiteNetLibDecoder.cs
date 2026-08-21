namespace SpiritVale.Overlay.Capture.LiteNetLib;

public enum LiteNetLibProperty
{
    Unreliable = 0,
    Channeled = 1,
    Ack = 2,
    Ping = 3,
    Pong = 4,
    ConnectRequest = 5,
    ConnectAccept = 6,
    Disconnect = 7,
    UnconnectedMessage = 8,
    MtuCheck = 9,
    MtuOk = 10,
    Broadcast = 11,
    Merged = 12,
    ShutdownOk = 13,
    PeerNotFound = 14,
    InvalidProtocol = 15,
    NatMessage = 16,
    Empty = 17,
}

public sealed class LiteNetLibProtocolException : Exception
{
    public int Offset { get; }
    public LiteNetLibProtocolException(string message, int offset) : base($"{message} at byte {offset}")
        => Offset = offset;
}

public sealed record LiteNetLibFragment(ushort Id, ushort Part, ushort Total);

public sealed class DecodedLiteNetLibPacket
{
    public required LiteNetLibProperty Property { get; init; }
    public int PropertyId { get; init; }
    public int ConnectionNumber { get; init; }
    public bool Fragmented { get; init; }
    public ushort? Sequence { get; init; }
    public byte? Channel { get; init; }
    public LiteNetLibFragment? Fragment { get; init; }
    public required ReadOnlyMemory<byte> Payload { get; init; }
    public required ReadOnlyMemory<byte> Raw { get; init; }
    public IReadOnlyList<int> MergePath { get; init; } = Array.Empty<int>();
}

public static class LiteNetLibDecoder
{
    private const byte PropertyMask = 0x1f;
    private const byte ConnectionMask = 0x60;
    private const byte FragmentedMask = 0x80;
    private const int MergedProperty = 12;
    private const int MaxMergeDepth = 32;

    public static IReadOnlyList<DecodedLiteNetLibPacket> Decode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new LiteNetLibProtocolException("LiteNetLib datagram is empty", 0);

        var output = new List<DecodedLiteNetLibPacket>();
        DecodePacket(data, 0, Array.Empty<int>(), 0, output);
        return output;
    }

    private static void DecodePacket(
        ReadOnlySpan<byte> bytes,
        int absoluteOffset,
        int[] mergePath,
        int depth,
        List<DecodedLiteNetLibPacket> output)
    {
        if (bytes.IsEmpty)
            throw new LiteNetLibProtocolException("LiteNetLib packet is empty", absoluteOffset);

        var first = bytes[0];
        var propertyId = first & PropertyMask;
        if (propertyId > (int)LiteNetLibProperty.Empty)
            throw new LiteNetLibProtocolException($"unknown LiteNetLib 1.x property {propertyId}", absoluteOffset);
        if ((first & FragmentedMask) != 0 && propertyId != 1)
            throw new LiteNetLibProtocolException("fragmentation flag is valid only on channeled packets", absoluteOffset);

        if (propertyId == MergedProperty)
        {
            DecodeMerged(bytes, absoluteOffset, mergePath, depth, output);
            return;
        }

        output.Add(DecodeLeaf(bytes, propertyId, mergePath));
    }

    private static void DecodeMerged(
        ReadOnlySpan<byte> bytes,
        int absoluteOffset,
        int[] mergePath,
        int depth,
        List<DecodedLiteNetLibPacket> output)
    {
        if (depth >= MaxMergeDepth)
            throw new LiteNetLibProtocolException($"LiteNetLib merged nesting exceeds {MaxMergeDepth}", absoluteOffset);

        var offset = 1;
        var childIndex = 0;
        while (offset < bytes.Length)
        {
            RequireLength(bytes, offset, 2, absoluteOffset, "merged child length");
            var childLength = BitConverter.ToUInt16(bytes.Slice(offset, 2));
            offset += 2;
            if (childLength == 0)
                throw new LiteNetLibProtocolException("merged child length is zero", absoluteOffset + offset - 2);
            RequireLength(bytes, offset, childLength, absoluteOffset, "merged child");
            var childPath = new int[mergePath.Length + 1];
            mergePath.CopyTo(childPath, 0);
            childPath[^1] = childIndex;
            DecodePacket(bytes.Slice(offset, childLength), absoluteOffset + offset, childPath, depth + 1, output);
            offset += childLength;
            childIndex++;
        }

        if (childIndex == 0)
            throw new LiteNetLibProtocolException("merged packet has no children", absoluteOffset);
    }

    private static DecodedLiteNetLibPacket DecodeLeaf(ReadOnlySpan<byte> bytes, int propertyId, int[] mergePath)
    {
        var first = bytes[0];
        var fragmented = (first & FragmentedMask) != 0;
        var connectionNumber = (first & ConnectionMask) >> 5;
        var property = (LiteNetLibProperty)propertyId;
        var raw = bytes.ToArray();

        switch (propertyId)
        {
            case 0:
                return new DecodedLiteNetLibPacket
                {
                    Property = property,
                    PropertyId = propertyId,
                    ConnectionNumber = connectionNumber,
                    Fragmented = fragmented,
                    Payload = raw.AsMemory(1),
                    Raw = raw,
                    MergePath = mergePath,
                };
            case 1:
            {
                var headerLength = fragmented ? 10 : 4;
                RequireLength(bytes, 0, headerLength, 0, "channeled header");
                LiteNetLibFragment? fragment = fragmented
                    ? new LiteNetLibFragment(
                        BitConverter.ToUInt16(bytes.Slice(4, 2)),
                        BitConverter.ToUInt16(bytes.Slice(6, 2)),
                        BitConverter.ToUInt16(bytes.Slice(8, 2)))
                    : null;
                return new DecodedLiteNetLibPacket
                {
                    Property = property,
                    PropertyId = propertyId,
                    ConnectionNumber = connectionNumber,
                    Fragmented = fragmented,
                    Sequence = BitConverter.ToUInt16(bytes.Slice(1, 2)),
                    Channel = bytes[3],
                    Fragment = fragment,
                    Payload = raw.AsMemory(headerLength),
                    Raw = raw,
                    MergePath = mergePath,
                };
            }
            case 2:
                RequireLength(bytes, 0, 4, 0, "ack header");
                return new DecodedLiteNetLibPacket
                {
                    Property = property,
                    PropertyId = propertyId,
                    ConnectionNumber = connectionNumber,
                    Fragmented = fragmented,
                    Sequence = BitConverter.ToUInt16(bytes.Slice(1, 2)),
                    Channel = bytes[3],
                    Payload = raw.AsMemory(4),
                    Raw = raw,
                    MergePath = mergePath,
                };
            case 3:
                RequireLength(bytes, 0, 3, 0, "ping header");
                return new DecodedLiteNetLibPacket
                {
                    Property = property,
                    PropertyId = propertyId,
                    ConnectionNumber = connectionNumber,
                    Fragmented = fragmented,
                    Sequence = BitConverter.ToUInt16(bytes.Slice(1, 2)),
                    Payload = raw.AsMemory(3),
                    Raw = raw,
                    MergePath = mergePath,
                };
            case 4:
                RequireLength(bytes, 0, 11, 0, "pong header");
                return new DecodedLiteNetLibPacket
                {
                    Property = property,
                    PropertyId = propertyId,
                    ConnectionNumber = connectionNumber,
                    Fragmented = fragmented,
                    Sequence = BitConverter.ToUInt16(bytes.Slice(1, 2)),
                    Payload = raw.AsMemory(11),
                    Raw = raw,
                    MergePath = mergePath,
                };
            default:
                return new DecodedLiteNetLibPacket
                {
                    Property = property,
                    PropertyId = propertyId,
                    ConnectionNumber = connectionNumber,
                    Fragmented = fragmented,
                    Payload = raw.AsMemory(1),
                    Raw = raw,
                    MergePath = mergePath,
                };
        }
    }

    private static void RequireLength(ReadOnlySpan<byte> bytes, int offset, int length, int absoluteOffset, string field)
    {
        if (offset + length > bytes.Length)
            throw new LiteNetLibProtocolException($"{field} is truncated", absoluteOffset + bytes.Length);
    }
}
