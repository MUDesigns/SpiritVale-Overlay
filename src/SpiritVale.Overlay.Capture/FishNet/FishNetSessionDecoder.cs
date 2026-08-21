namespace SpiritVale.Overlay.Capture.FishNet;

/// <summary>Stateful decoder for FishNet session payloads (RPC links, splits, multi-message bundles).</summary>
public sealed class FishNetSessionDecoder
{
    private readonly Dictionary<string, ConnectionState> _connections = new(StringComparer.Ordinal);
    private readonly FishNetRpcMap? _rpcMap;

    public FishNetSessionDecoder(FishNetRpcMap? rpcMap = null) => _rpcMap = rpcMap;

    public IReadOnlyList<DecodedFishNetPacket> Decode(ReadOnlySpan<byte> payload, FishNetDecodeOptions? options = null)
    {
        options ??= new FishNetDecodeOptions();
        if (payload.Length < 6)
            throw new FishNetProtocolException($"FishNet payload needs a 4-byte tick and 2-byte packet id; received {payload.Length} bytes");

        var tick = BitConverter.ToUInt32(payload.Slice(0, 4));
        var connectionKey = options.ConnectionId ?? "default";
        var state = GetConnection(connectionKey);
        var map = options.RpcMap ?? _rpcMap;
        var effective = new FishNetDecodeOptions
        {
            Reliable = options.Reliable,
            ConnectionId = connectionKey,
            Direction = options.Direction,
            Channel = options.Channel,
            Sequence = options.Sequence,
            RpcMap = map,
        };

        // Split reassembly is best-effort; for v1 treat split packets as opaque markers.
        if (BitConverter.ToUInt16(payload.Slice(4, 2)) == 2)
        {
            return new[]
            {
                new DecodedFishNetPacket
                {
                    Tick = tick,
                    PacketId = 2,
                    PacketName = FishNetPacketNames.Split,
                    Raw = payload.ToArray(),
                    Payload = payload.Slice(6).ToArray(),
                },
            };
        }

        return DecodeMessages(payload, 4, tick, state, effective);
    }

    public void Reset(string? connectionId = null)
    {
        if (connectionId is null)
        {
            _connections.Clear();
            return;
        }
        _connections.Remove(connectionId);
    }

    private ConnectionState GetConnection(string key)
    {
        if (!_connections.TryGetValue(key, out var state))
        {
            state = new ConnectionState();
            _connections[key] = state;
        }
        return state;
    }

    private static List<DecodedFishNetPacket> DecodeMessages(
        ReadOnlySpan<byte> buffer,
        int start,
        uint tick,
        ConnectionState state,
        FishNetDecodeOptions options)
    {
        var packets = new List<DecodedFishNetPacket>();
        var offset = start;
        while (buffer.Length - offset >= 2)
        {
            var parsed = MessageParser.Parse(buffer, offset, tick, packets.Count, state, options);
            packets.Add(parsed.Packet);

            // Keep RpcLinks across re-auth (mid-session); only wipe on disconnect.
            if (parsed.Packet.PacketName == FishNetPacketNames.Disconnect)
            {
                state.Links.Clear();
                state.Components.Clear();
            }

            if (parsed.Stop || parsed.End <= offset)
                break;
            offset = parsed.End;
        }
        return packets;
    }
}
