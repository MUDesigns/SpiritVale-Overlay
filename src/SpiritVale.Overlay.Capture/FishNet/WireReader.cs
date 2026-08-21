namespace SpiritVale.Overlay.Capture.FishNet;

public sealed class FishNetProtocolException : Exception
{
    public FishNetProtocolException(string message) : base(message) { }
}

public static class WireReader
{
    public readonly record struct PackedInt(int Value, int NextOffset);
    public readonly record struct NetworkObjectReference(int ObjectId, bool Spawned, int NextOffset);
    public readonly record struct NetworkBehaviourHeader(int ObjectId, int ComponentIndex, int NextOffset);

    public static void RequireBytes(ReadOnlySpan<byte> buffer, int offset, int count, string description)
    {
        if (buffer.Length - offset < count)
            throw new FishNetProtocolException($"{description} needs {count} bytes at byte {offset}; {buffer.Length - offset} remain");
    }

    public static int CheckedEnd(ReadOnlySpan<byte> buffer, int start, int length)
    {
        if (length < 0 || length > buffer.Length - start)
            throw new FishNetProtocolException($"length {length} exceeds {buffer.Length - start} remaining bytes");
        return start + length;
    }

    public static PackedInt ReadUnsignedPackedWholeAsInt(ReadOnlySpan<byte> buffer, int start)
    {
        ulong value = 0;
        var shift = 0;
        var offset = start;
        while (offset < buffer.Length && offset - start < 10)
        {
            var b = buffer[offset++];
            value |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                if (value > int.MaxValue)
                    throw new FishNetProtocolException("packed integer exceeds Int32 range");
                return new PackedInt((int)value, offset);
            }
            shift += 7;
        }
        throw new FishNetProtocolException($"unterminated packed integer at byte {start}");
    }

    public static PackedInt ReadSignedPackedWhole(ReadOnlySpan<byte> buffer, int start)
    {
        ulong u = 0;
        var shift = 0;
        var offset = start;
        while (offset < buffer.Length && offset - start < 10)
        {
            var b = buffer[offset++];
            u |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                var signed = (long)(u >> 1) ^ -(long)(u & 1);
                if (signed is < int.MinValue or > int.MaxValue)
                    throw new FishNetProtocolException("packed integer exceeds Int32 range");
                return new PackedInt((int)signed, offset);
            }
            shift += 7;
        }
        throw new FishNetProtocolException($"unterminated packed integer at byte {start}");
    }

    public static NetworkObjectReference ReadNetworkObjectReference(ReadOnlySpan<byte> buffer, int start)
    {
        var obj = ReadSignedPackedWhole(buffer, start);
        if (obj.Value == -1)
            return new NetworkObjectReference(obj.Value, false, obj.NextOffset);
        RequireBytes(buffer, obj.NextOffset, 1, "network object spawned flag");
        var flag = buffer[obj.NextOffset];
        if (flag is not (0 or 1))
            throw new FishNetProtocolException("invalid network object spawned flag");
        return new NetworkObjectReference(obj.Value, flag == 1, obj.NextOffset + 1);
    }

    public static NetworkBehaviourHeader ReadNetworkBehaviourHeader(ReadOnlySpan<byte> buffer, int start)
    {
        var reference = ReadNetworkObjectReference(buffer, start);
        if (!reference.Spawned)
            throw new FishNetProtocolException("RPC object is not spawned");
        RequireBytes(buffer, reference.NextOffset, 1, "network behaviour component");
        return new NetworkBehaviourHeader(reference.ObjectId, buffer[reference.NextOffset], reference.NextOffset + 1);
    }
}
