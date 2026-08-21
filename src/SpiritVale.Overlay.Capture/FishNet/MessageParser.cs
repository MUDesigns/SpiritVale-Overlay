namespace SpiritVale.Overlay.Capture.FishNet;

internal sealed class ConnectionState
{
    public Dictionary<int, RpcLinkRegistration> Links { get; } = new();
    public Dictionary<string, string> Components { get; } = new(StringComparer.Ordinal);
}

internal readonly record struct ParsedMessage(DecodedFishNetPacket Packet, int End, bool Stop);

internal static class MessageParser
{
    public static ParsedMessage Parse(
        ReadOnlySpan<byte> buffer,
        int start,
        uint tick,
        int bundleIndex,
        ConnectionState state,
        FishNetDecodeOptions options)
    {
        var packetId = BitConverter.ToUInt16(buffer.Slice(start, 2));
        var packetName = FishNetPacketNames.Classify(packetId);
        var dataStart = start + 2;

        if (packetName == FishNetPacketNames.RpcLink)
            return ParseRpcLink(buffer, start, dataStart, tick, bundleIndex, packetId, state, options);

        if (FishNetPacketNames.RpcPacketNames.Contains(packetName))
            return ParseFixedRpc(buffer, start, dataStart, tick, bundleIndex, packetId, packetName, state, options);

        try
        {
            int? end = null;
            int? objectId = null;

            switch (packetName)
            {
                case FishNetPacketNames.ObjectDespawn:
                {
                    var obj = WireReader.ReadSignedPackedWhole(buffer, dataStart);
                    WireReader.RequireBytes(buffer, obj.NextOffset, 1, "despawn type");
                    objectId = obj.Value;
                    end = obj.NextOffset + 1;
                    break;
                }
                case FishNetPacketNames.Authenticated:
                    end = WireReader.ReadSignedPackedWhole(buffer, dataStart).NextOffset;
                    break;
                case FishNetPacketNames.PredictedSpawnResult:
                {
                    WireReader.RequireBytes(buffer, dataStart, 1, "predicted spawn result");
                    var used = WireReader.ReadSignedPackedWhole(buffer, dataStart + 1);
                    end = WireReader.ReadSignedPackedWhole(buffer, used.NextOffset).NextOffset;
                    break;
                }
                case FishNetPacketNames.SyncType:
                {
                    var header = WireReader.ReadNetworkBehaviourHeader(buffer, dataStart);
                    WireReader.RequireBytes(buffer, header.NextOffset, 4, "SyncType length");
                    var length = BitConverter.ToInt32(buffer.Slice(header.NextOffset, 4));
                    end = WireReader.CheckedEnd(buffer, header.NextOffset + 4, length);
                    var syncPayload = buffer.Slice(header.NextOffset + 4, length).ToArray();
                    var packet = BasePacket(buffer, start, end.Value, tick, bundleIndex, packetId, packetName);
                    packet.ObjectId = header.ObjectId;
                    packet.NetworkBehaviourIndex = header.ComponentIndex;
                    packet.NetworkBehaviourType = state.Components.GetValueOrDefault($"{header.ObjectId}:{header.ComponentIndex}");
                    packet.SyncPayload = syncPayload;
                    packet.Payload = syncPayload;
                    if (syncPayload.Length > 0)
                        packet.SyncIndex = syncPayload[0];
                    return new ParsedMessage(packet, end.Value, false);
                }
                case FishNetPacketNames.Broadcast:
                {
                    WireReader.RequireBytes(buffer, dataStart, 2, "broadcast hash");
                    var length = WireReader.ReadSignedPackedWhole(buffer, dataStart + 2);
                    if (length.Value < 0) throw new FishNetProtocolException("negative broadcast length");
                    end = WireReader.CheckedEnd(buffer, length.NextOffset, length.Value);
                    var packet = BasePacket(buffer, start, end.Value, tick, bundleIndex, packetId, packetName);
                    packet.BroadcastHash = BitConverter.ToUInt16(buffer.Slice(dataStart, 2));
                    packet.Payload = buffer.Slice(length.NextOffset, length.Value).ToArray();
                    return new ParsedMessage(packet, end.Value, false);
                }
                case FishNetPacketNames.PingPong:
                case FishNetPacketNames.TimingUpdate:
                    end = WireReader.CheckedEnd(buffer, dataStart, 4);
                    break;
                case FishNetPacketNames.Version:
                    end = WireReader.CheckedEnd(buffer, dataStart, 1);
                    break;
                case FishNetPacketNames.OwnershipChange:
                {
                    var obj = WireReader.ReadNetworkObjectReference(buffer, dataStart);
                    var owner = WireReader.ReadSignedPackedWhole(buffer, obj.NextOffset);
                    end = owner.NextOffset;
                    var packet = BasePacket(buffer, start, end.Value, tick, bundleIndex, packetId, packetName);
                    packet.ObjectId = obj.ObjectId;
                    packet.OwnerConnectionId = owner.Value;
                    return new ParsedMessage(packet, end.Value, false);
                }
                case FishNetPacketNames.ObjectSpawn:
                {
                    // Best-effort: treat remaining bytes as opaque spawn when full spawn parser is unavailable.
                    var packet = Opaque(buffer, start, tick, bundleIndex, packetName);
                    TryReadSpawnObjectId(buffer, dataStart, packet);
                    return new ParsedMessage(packet, buffer.Length, true);
                }
                case FishNetPacketNames.Disconnect:
                    end = buffer.Length;
                    break;
            }

            if (end is int e)
            {
                var packet = BasePacket(buffer, start, e, tick, bundleIndex, packetId, packetName);
                if (objectId is int oid) packet.ObjectId = oid;
                return new ParsedMessage(packet, e, packetName == FishNetPacketNames.Disconnect);
            }
        }
        catch
        {
            // Fall through to opaque.
        }

        return new ParsedMessage(Opaque(buffer, start, tick, bundleIndex, packetName), buffer.Length, true);
    }

    private static void TryReadSpawnObjectId(ReadOnlySpan<byte> buffer, int dataStart, DecodedFishNetPacket packet)
    {
        try
        {
            var obj = WireReader.ReadSignedPackedWhole(buffer, dataStart);
            packet.ObjectId = obj.Value;
        }
        catch
        {
            // ignore
        }
    }

    private static ParsedMessage ParseRpcLink(
        ReadOnlySpan<byte> buffer,
        int start,
        int dataStart,
        uint tick,
        int bundleIndex,
        int packetId,
        ConnectionState state,
        FishNetDecodeOptions options)
    {
        var payloadStart = dataStart;
        var end = buffer.Length;
        var stop = !options.Reliable;
        if (options.Reliable)
        {
            try
            {
                var length = WireReader.ReadSignedPackedWhole(buffer, dataStart);
                if (length.Value < 0) throw new FishNetProtocolException("negative RPC Link length");
                payloadStart = length.NextOffset;
                end = WireReader.CheckedEnd(buffer, payloadStart, length.Value);
                stop = false;
            }
            catch
            {
                var unresolved = Opaque(buffer, start, tick, bundleIndex, FishNetPacketNames.RpcLink);
                unresolved.LinkId = packetId;
                unresolved.LinkResolved = false;
                return new ParsedMessage(unresolved, buffer.Length, true);
            }
        }

        var packet = BasePacket(buffer, start, end, tick, bundleIndex, packetId, FishNetPacketNames.RpcLink);
        packet.LinkId = packetId;
        packet.Payload = buffer.Slice(payloadStart, end - payloadStart).ToArray();
        if (state.Links.TryGetValue(packetId, out var registration))
        {
            packet.LinkResolved = true;
            packet.LinkedPacketName = registration.PacketName;
            packet.ObjectId = registration.ObjectId;
            packet.NetworkBehaviourIndex = registration.ComponentIndex;
            packet.RpcHash = registration.RpcHash;
            packet.NetworkBehaviourType = registration.NetworkBehaviourType
                ?? state.Components.GetValueOrDefault($"{registration.ObjectId}:{registration.ComponentIndex}");
            ApplyRpcLookup(packet, options.RpcMap, registration.PacketName, registration.RpcHash, null);
        }
        else
        {
            packet.LinkResolved = false;
        }
        return new ParsedMessage(packet, end, stop);
    }

    private static ParsedMessage ParseFixedRpc(
        ReadOnlySpan<byte> buffer,
        int start,
        int dataStart,
        uint tick,
        int bundleIndex,
        int packetId,
        string packetName,
        ConnectionState state,
        FishNetDecodeOptions options)
    {
        try
        {
            var header = WireReader.ReadNetworkBehaviourHeader(buffer, dataStart);
            var rpcStart = header.NextOffset;
            var end = buffer.Length;
            var stop = !options.Reliable;
            if (options.Reliable)
            {
                var length = WireReader.ReadSignedPackedWhole(buffer, rpcStart);
                if (length.Value < 1) throw new FishNetProtocolException("invalid RPC length");
                rpcStart = length.NextOffset;
                end = WireReader.CheckedEnd(buffer, rpcStart, length.Value);
                stop = false;
            }

            WireReader.RequireBytes(buffer, rpcStart, 1, "RPC hash");
            var packet = BasePacket(buffer, start, end, tick, bundleIndex, packetId, packetName);
            packet.ObjectId = header.ObjectId;
            packet.NetworkBehaviourIndex = header.ComponentIndex;
            var key = $"{header.ObjectId}:{header.ComponentIndex}";
            packet.NetworkBehaviourType = state.Components.GetValueOrDefault(key);

            var hash8 = buffer[rpcStart];
            int? hash16 = end - rpcStart >= 2 ? BitConverter.ToUInt16(buffer.Slice(rpcStart, 2)) : null;
            packet.RpcHash = hash8;
            packet.RpcHash16Candidate = hash16;

            if (packet.NetworkBehaviourType is null && options.RpcMap is not null)
            {
                var inferred = options.RpcMap.InferBehaviourType(packetName, hash8, hash16);
                if (inferred is not null)
                {
                    packet.NetworkBehaviourType = inferred;
                    state.Components[key] = inferred;
                }
            }

            ApplyRpcLookup(packet, options.RpcMap, packetName, hash8, hash16);
            var hashWidth = packet.RpcHash is int wh && wh > 0xff ? 2 : 1;
            packet.Payload = buffer.Slice(rpcStart + hashWidth, end - (rpcStart + hashWidth)).ToArray();
            return new ParsedMessage(packet, end, stop);
        }
        catch
        {
            return new ParsedMessage(Opaque(buffer, start, tick, bundleIndex, packetName), buffer.Length, true);
        }
    }

    private static void ApplyRpcLookup(DecodedFishNetPacket packet, FishNetRpcMap? map, string packetKind, int hash8, int? hash16)
    {
        if (map is null) return;
        var lookup = map.Lookup(packet.NetworkBehaviourType, packetKind, hash8, hash16);
        if (lookup is null) return;
        packet.RpcName = lookup.MethodName;
        packet.RpcHash = lookup.WireHash;
        packet.NetworkBehaviourType ??= lookup.BehaviourType;
    }

    private static DecodedFishNetPacket BasePacket(
        ReadOnlySpan<byte> buffer, int start, int end, uint tick, int bundleIndex, int packetId, string packetName)
    {
        var raw = buffer.Slice(start, end - start).ToArray();
        return new DecodedFishNetPacket
        {
            Tick = tick,
            PacketId = packetId,
            PacketName = packetName,
            BundleIndex = bundleIndex,
            Raw = raw,
            Payload = buffer.Slice(start + 2, end - (start + 2)).ToArray(),
        };
    }

    private static DecodedFishNetPacket Opaque(
        ReadOnlySpan<byte> buffer, int start, uint tick, int bundleIndex, string? packetName = null)
    {
        var packetId = BitConverter.ToUInt16(buffer.Slice(start, 2));
        return BasePacket(buffer, start, buffer.Length, tick, bundleIndex, packetId, packetName ?? FishNetPacketNames.Classify(packetId));
    }
}
