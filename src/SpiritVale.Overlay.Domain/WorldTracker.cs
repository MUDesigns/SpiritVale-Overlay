using SpiritVale.Overlay.Api.World;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class WorldTracker : IWorldApi
{
    private readonly object _gate = new();
    private readonly Dictionary<int, WorldEntity> _entities = new();

    public event Action? WorldChanged;
    public event Action<WorldEntityEvent>? EntityChanged;
    public event Action<LootDropEvent>? LootDropped;

    public string? MapName { get; private set; }
    public int? Channel { get; private set; }

    public IReadOnlyList<WorldEntity> NearbyEntities
    {
        get { lock (_gate) return _entities.Values.ToList(); }
    }

    public void Consume(DecodedFishNetPacket packet)
    {
        switch (packet.RpcName)
        {
            case "MapChange_C":
                MapName = packet.Fields.TryGetValue("mapName", out var map) ? map?.ToString() : MapName ?? "Unknown";
                WorldChanged?.Invoke();
                break;
            case "ChannelChange_C":
                if (packet.Fields.TryGetValue("channel", out var ch) && int.TryParse(ch?.ToString(), out var channel))
                    Channel = channel;
                WorldChanged?.Invoke();
                break;
            case "MonsterSpawn_C":
                UpsertEntity(packet.ObjectId ?? 0, "Monster", packet.Fields.TryGetValue("displayName", out var mn) ? mn?.ToString() ?? "Monster" : "Monster");
                break;
        }

        switch (packet.PacketName)
        {
            case FishNetPacketNames.ObjectSpawn when packet.ObjectId is int oid:
                UpsertEntity(oid, "Entity", $"Object {oid}");
                break;
            case FishNetPacketNames.ObjectDespawn when packet.ObjectId is int oid:
                RemoveEntity(oid);
                break;
        }
    }

    public void NotifyLoot(LootDropEvent drop) => LootDropped?.Invoke(drop);

    private void UpsertEntity(int actorId, string kind, string name)
    {
        if (actorId == 0) return;
        var entity = new WorldEntity(actorId, name, kind, null, null, null);
        lock (_gate) _entities[actorId] = entity;
        EntityChanged?.Invoke(new WorldEntityEvent("upsert", entity));
        WorldChanged?.Invoke();
    }

    private void RemoveEntity(int actorId)
    {
        WorldEntity? removed;
        lock (_gate)
        {
            if (!_entities.Remove(actorId, out removed)) return;
        }
        EntityChanged?.Invoke(new WorldEntityEvent("remove", removed));
        WorldChanged?.Invoke();
    }
}
