namespace SpiritVale.Overlay.Api.Protocol;

public interface IProtocolApi
{
    /// <summary>Decoded FishNet messages (no raw bytes by default).</summary>
    event Action<DecodedFishNetEvent>? Packet;

    /// <summary>
    /// Subscribe to raw UDP payload bytes. Opt-in only — casual plugins should use typed APIs.
    /// </summary>
    event Action<RawPacketEvent>? RawPacket;

    bool IncludeRawBytes { get; set; }
}

public sealed record DecodedFishNetEvent(
    int Tick,
    string PacketName,
    int PacketId,
    int? ObjectId,
    string? RpcName,
    string? NetworkBehaviourType,
    int? RpcHash,
    string? BroadcastName,
    string? SyncName,
    IReadOnlyDictionary<string, object?> Fields);

public sealed record RawPacketEvent(
    DateTimeOffset Timestamp,
    string Direction,
    ReadOnlyMemory<byte> Payload);
