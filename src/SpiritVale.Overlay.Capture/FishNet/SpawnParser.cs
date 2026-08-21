namespace SpiritVale.Overlay.Capture.FishNet;

internal sealed class SpawnCandidate
{
    public required int End { get; init; }
    public required int ObjectId { get; init; }
    public required int OwnerConnectionId { get; init; }
    public int? PrefabId { get; init; }
    public required List<(int LinkId, RpcLinkRegistration Registration)> Registrations { get; init; }
    public required List<(string Key, string TypeName)> ComponentBindings { get; init; }
}

/// <summary>ObjectSpawn parser that extracts RpcLink registrations (required for ApplyDamage_C naming).</summary>
internal static class SpawnParser
{
    private static readonly Dictionary<int, string> LinkedPacketNames = new()
    {
        [9] = FishNetPacketNames.ObserversRpc,
        [10] = FishNetPacketNames.TargetRpc,
        [16] = FishNetPacketNames.Reconcile,
    };

    public static SpawnCandidate? TryParse(ReadOnlySpan<byte> buffer, int start, FishNetRpcMap? map)
    {
        try
        {
            WireReader.RequireBytes(buffer, start, 1, "spawn flags");
            var flags = buffer[start];
            if ((flags & ~0x1f) != 0) return null;
            var kindBits = flags & 0x0e;
            if (kindBits is not (0x02 or 0x04 or 0x08)) return null;
            var nested = (flags & 0x01) != 0;
            var offset = start + 1;

            if (nested)
            {
                WireReader.RequireBytes(buffer, offset, 1, "nested object component");
                offset += 1;
                var parent = WireReader.ReadNetworkObjectReference(buffer, offset);
                offset = parent.NextOffset;
                WireReader.RequireBytes(buffer, offset, 1, "nested parent component");
                offset += 1;
            }

            var objectRef = WireReader.ReadSignedPackedWhole(buffer, offset);
            if (objectRef.Value < 0) return null;
            offset = objectRef.NextOffset;
            WireReader.RequireBytes(buffer, offset, 2, "spawn collection id");
            var collectionId = BitConverter.ToUInt16(buffer.Slice(offset, 2));
            offset += 2;
            offset = WireReader.ReadSignedPackedWhole(buffer, offset).NextOffset;
            var owner = WireReader.ReadSignedPackedWhole(buffer, offset);
            offset = owner.NextOffset;
            WireReader.RequireBytes(buffer, offset, 1, "transform flags");
            var transformFlags = buffer[offset];
            if ((transformFlags & ~0x07) != 0) return null;
            offset += 1;

            if ((transformFlags & 0x01) != 0)
                offset = WireReader.CheckedEnd(buffer, offset, 12); // Vector3

            var rotationWidths = (transformFlags & 0x02) != 0 ? new[] { 8, 4, 16 } : new[] { 0 };
            SpawnCandidate? best = null;
            foreach (var rotationBytes in rotationWidths)
            {
                try
                {
                    var candidateOffset = WireReader.CheckedEnd(buffer, offset, rotationBytes);
                    if ((transformFlags & 0x04) != 0)
                        candidateOffset = WireReader.CheckedEnd(buffer, candidateOffset, 12);

                    int? prefabId = null;
                    if ((flags & 0x02) != 0)
                    {
                        WireReader.RequireBytes(buffer, candidateOffset, 8, "spawn scene id");
                        candidateOffset += 8;
                    }
                    else
                    {
                        var prefab = WireReader.ReadSignedPackedWhole(buffer, candidateOffset);
                        if (prefab.Value < 0) continue;
                        prefabId = prefab.Value;
                        candidateOffset = prefab.NextOffset;
                    }

                    WireReader.RequireBytes(buffer, candidateOffset, 4, "spawn payload length");
                    var payloadLength = BitConverter.ToInt32(buffer.Slice(candidateOffset, 4));
                    candidateOffset = WireReader.CheckedEnd(buffer, candidateOffset + 4, payloadLength);

                    WireReader.RequireBytes(buffer, candidateOffset, 2, "RPC Link segment length");
                    var linksLength = BitConverter.ToUInt16(buffer.Slice(candidateOffset, 2));
                    var linksStart = candidateOffset + 2;
                    var linksEnd = WireReader.CheckedEnd(buffer, linksStart, linksLength);
                    var registrations = ParseLinkRegistrations(buffer, linksStart, linksEnd, objectRef.Value);
                    if (registrations is null) continue;
                    candidateOffset = linksEnd;

                    WireReader.RequireBytes(buffer, candidateOffset, 4, "spawn SyncType length");
                    var syncLength = BitConverter.ToInt32(buffer.Slice(candidateOffset, 4));
                    candidateOffset = WireReader.CheckedEnd(buffer, candidateOffset + 4, syncLength);
                    if (!IsPlausibleBoundary(buffer, candidateOffset)) continue;

                    var bindings = BindBehaviourTypes(registrations, map);

                    // Rebuild registrations with bound types.
                    var typed = new List<(int, RpcLinkRegistration)>(registrations.Count);
                    var bindingMap = bindings.ToDictionary(b => b.Key, b => b.TypeName, StringComparer.Ordinal);
                    foreach (var (linkId, reg) in registrations)
                    {
                        var key = $"{reg.ObjectId}:{reg.ComponentIndex}";
                        bindingMap.TryGetValue(key, out var typeName);
                        typed.Add((linkId, new RpcLinkRegistration
                        {
                            ObjectId = reg.ObjectId,
                            ComponentIndex = reg.ComponentIndex,
                            RpcHash = reg.RpcHash,
                            PacketName = reg.PacketName,
                            NetworkBehaviourType = typeName ?? reg.NetworkBehaviourType,
                        }));
                    }

                    var candidate = new SpawnCandidate
                    {
                        End = candidateOffset,
                        ObjectId = objectRef.Value,
                        OwnerConnectionId = owner.Value,
                        PrefabId = prefabId,
                        Registrations = typed,
                        ComponentBindings = bindings,
                    };
                    // Prefer the first plausible width that fully consumes (matches reference uniqueness).
                    best ??= candidate;
                    _ = collectionId; // reserved for prefab bind later
                }
                catch
                {
                    // try next rotation width
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static List<(int LinkId, RpcLinkRegistration Registration)>? ParseLinkRegistrations(
        ReadOnlySpan<byte> buffer, int start, int end, int objectId)
    {
        var registrations = new List<(int, RpcLinkRegistration)>();
        var seen = new HashSet<int>();
        var offset = start;
        while (offset < end)
        {
            if (end - offset < 3) return null;
            var componentIndex = buffer[offset];
            var count = BitConverter.ToUInt16(buffer.Slice(offset + 1, 2));
            if (count < 1 || count > (end - offset - 3) / 6) return null;
            offset += 3;
            for (var i = 0; i < count; i++)
            {
                if (end - offset < 6) return null;
                var linkId = BitConverter.ToUInt16(buffer.Slice(offset, 2));
                var rpcHash = BitConverter.ToUInt16(buffer.Slice(offset + 2, 2));
                var kind = BitConverter.ToUInt16(buffer.Slice(offset + 4, 2));
                if (linkId < FishNetPacketNames.StartingRpcLinkId
                    || !LinkedPacketNames.TryGetValue(kind, out var packetName)
                    || !seen.Add(linkId))
                    return null;
                registrations.Add((linkId, new RpcLinkRegistration
                {
                    ObjectId = objectId,
                    ComponentIndex = componentIndex,
                    RpcHash = rpcHash,
                    PacketName = packetName,
                }));
                offset += 6;
            }
        }
        return offset == end ? registrations : null;
    }

    private static List<(string Key, string TypeName)> BindBehaviourTypes(
        List<(int LinkId, RpcLinkRegistration Registration)> registrations,
        FishNetRpcMap? map)
    {
        var result = new List<(string, string)>();
        if (map is null) return result;

        var byComponent = new Dictionary<string, List<RpcLinkRegistration>>(StringComparer.Ordinal);
        foreach (var (_, reg) in registrations)
        {
            var key = $"{reg.ObjectId}:{reg.ComponentIndex}";
            if (!byComponent.TryGetValue(key, out var list))
            {
                list = new List<RpcLinkRegistration>();
                byComponent[key] = list;
            }
            list.Add(reg);
        }

        foreach (var (key, values) in byComponent)
        {
            var fingerprint = RpcFingerprint(values);
            var matches = map.Behaviours
                .Where(b => RpcFingerprint(b.Rpcs.Where(r => r.PacketKind != FishNetPacketNames.ServerRpc)
                    .Select(r => (r.WireHash, r.PacketKind))) == fingerprint)
                .ToList();
            if (matches.Count != 1) continue;
            result.Add((key, matches[0].TypeName));
        }
        return result;
    }

    private static string RpcFingerprint(IEnumerable<RpcLinkRegistration> rpcs)
        => RpcFingerprint(rpcs.Select(r => (r.RpcHash, r.PacketName)));

    private static string RpcFingerprint(IEnumerable<(int WireHash, string PacketKind)> rpcs)
        => string.Join("|", rpcs.OrderBy(r => r.WireHash).ThenBy(r => r.PacketKind, StringComparer.Ordinal)
            .Select(r => $"{r.PacketKind}:{r.WireHash}"));

    private static bool IsPlausibleBoundary(ReadOnlySpan<byte> buffer, int offset)
    {
        if (offset == buffer.Length) return true;
        if (buffer.Length - offset < 2) return false;
        return FishNetPacketNames.Classify(BitConverter.ToUInt16(buffer.Slice(offset, 2))) != FishNetPacketNames.Unknown;
    }
}
