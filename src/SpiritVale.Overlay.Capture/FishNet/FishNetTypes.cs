namespace SpiritVale.Overlay.Capture.FishNet;

public static class FishNetPacketNames
{
    public const string Unset = "unset";
    public const string Authenticated = "authenticated";
    public const string Split = "split";
    public const string ObjectSpawn = "objectSpawn";
    public const string ObjectDespawn = "objectDespawn";
    public const string PredictedSpawnResult = "predictedSpawnResult";
    public const string SyncType = "syncType";
    public const string ServerRpc = "serverRpc";
    public const string ObserversRpc = "observersRpc";
    public const string TargetRpc = "targetRpc";
    public const string OwnershipChange = "ownershipChange";
    public const string Broadcast = "broadcast";
    public const string BulkSpawnOrDespawn = "bulkSpawnOrDespawn";
    public const string PingPong = "pingPong";
    public const string Replicate = "replicate";
    public const string Reconcile = "reconcile";
    public const string Disconnect = "disconnect";
    public const string TimingUpdate = "timingUpdate";
    public const string StateUpdate = "stateUpdate";
    public const string Version = "version";
    public const string RpcLink = "rpcLink";
    public const string Unknown = "unknown";

    public const int StartingRpcLinkId = 22;

    private static readonly Dictionary<int, string> Names = new()
    {
        [0] = Unset,
        [1] = Authenticated,
        [2] = Split,
        [3] = ObjectSpawn,
        [4] = ObjectDespawn,
        [5] = PredictedSpawnResult,
        [7] = SyncType,
        [8] = ServerRpc,
        [9] = ObserversRpc,
        [10] = TargetRpc,
        [11] = OwnershipChange,
        [12] = Broadcast,
        [13] = BulkSpawnOrDespawn,
        [14] = PingPong,
        [15] = Replicate,
        [16] = Reconcile,
        [17] = Disconnect,
        [18] = TimingUpdate,
        [20] = StateUpdate,
        [21] = Version,
    };

    public static readonly HashSet<string> RpcPacketNames = new(StringComparer.Ordinal)
    {
        ServerRpc, ObserversRpc, TargetRpc,
    };

    public static string Classify(int packetId)
        => Names.TryGetValue(packetId, out var name)
            ? name
            : packetId >= StartingRpcLinkId ? RpcLink : Unknown;
}

public sealed class DecodedFishNetPacket
{
    public required uint Tick { get; init; }
    public required int PacketId { get; init; }
    public required string PacketName { get; init; }
    public int BundleIndex { get; init; }
    public required byte[] Raw { get; init; }
    public required byte[] Payload { get; set; }

    public int? ObjectId { get; set; }
    public int? NetworkBehaviourIndex { get; set; }
    public string? NetworkBehaviourType { get; set; }
    public int? RpcHash { get; set; }
    public int? RpcHash16Candidate { get; set; }
    public string? RpcName { get; set; }
    public string? SyncName { get; set; }
    public int? SyncIndex { get; set; }
    public byte[]? SyncPayload { get; set; }
    public int? BroadcastHash { get; set; }
    public string? BroadcastName { get; set; }
    public int? OwnerConnectionId { get; set; }
    public int? LinkId { get; set; }
    public bool? LinkResolved { get; set; }
    public string? LinkedPacketName { get; set; }
    public int? SpawnType { get; set; }
    public int? SpawnPrefabId { get; set; }
    public Dictionary<string, object?> Fields { get; set; } = new(StringComparer.Ordinal);
}

public sealed class FishNetDecodeOptions
{
    public bool Reliable { get; init; }
    public string? ConnectionId { get; init; }
    public string Direction { get; init; } = "unknown";
    public int? Channel { get; init; }
    public ushort? Sequence { get; init; }
    public FishNetRpcMap? RpcMap { get; init; }
}

public sealed class RpcLinkRegistration
{
    public required int ObjectId { get; init; }
    public required int ComponentIndex { get; init; }
    public required int RpcHash { get; init; }
    public required string PacketName { get; init; }
    public string? NetworkBehaviourType { get; init; }
}

public sealed record FishNetRpcDefinition
{
    public required int WireHash { get; init; }
    public required string PacketKind { get; init; }
    public required string MethodName { get; init; }
    public string? BehaviourType { get; init; }
}

public sealed class FishNetBehaviourDefinition
{
    public required string TypeName { get; init; }
    public required IReadOnlyList<FishNetRpcDefinition> Rpcs { get; init; }
}

public sealed class FishNetRpcMap
{
    public string BuildFingerprint { get; init; } = "unknown";
    public IReadOnlyList<FishNetBehaviourDefinition> Behaviours { get; init; } = Array.Empty<FishNetBehaviourDefinition>();

    private Dictionary<(string Behaviour, string Kind, int Hash), FishNetRpcDefinition>? _index;

    public FishNetRpcDefinition? Lookup(string? behaviourType, string packetKind, int hash8, int? hash16 = null)
    {
        EnsureIndex();
        if (behaviourType is not null)
        {
            if (_index!.TryGetValue((behaviourType, packetKind, hash8), out var by8))
                return by8;
            if (hash16 is int h16 && _index.TryGetValue((behaviourType, packetKind, h16), out var by16))
                return by16;
        }

        // Ambiguous global lookup by hash alone (first match).
        foreach (var behaviour in Behaviours)
        {
            foreach (var rpc in behaviour.Rpcs)
            {
                if (!string.Equals(rpc.PacketKind, packetKind, StringComparison.Ordinal))
                    continue;
                if (rpc.WireHash == hash8 || (hash16 is int h && rpc.WireHash == h))
                    return rpc with { BehaviourType = behaviour.TypeName };
            }
        }
        return null;
    }

    public string? InferBehaviourType(string packetKind, int hash8, int? hash16)
    {
        string? found = null;
        foreach (var behaviour in Behaviours)
        {
            foreach (var rpc in behaviour.Rpcs)
            {
                if (!string.Equals(rpc.PacketKind, packetKind, StringComparison.Ordinal))
                    continue;
                var matches = rpc.WireHash == hash8 || (hash16 is int hx && rpc.WireHash == hx);
                if (!matches) continue;
                if (found is not null && !string.Equals(found, behaviour.TypeName, StringComparison.Ordinal))
                    return null; // ambiguous
                found = behaviour.TypeName;
            }
        }
        return found;
    }

    private void EnsureIndex()
    {
        if (_index is not null) return;
        _index = new Dictionary<(string, string, int), FishNetRpcDefinition>();
        foreach (var behaviour in Behaviours)
        {
            foreach (var rpc in behaviour.Rpcs)
                _index[(behaviour.TypeName, rpc.PacketKind, rpc.WireHash)] = rpc with { BehaviourType = behaviour.TypeName };
        }
    }
}
