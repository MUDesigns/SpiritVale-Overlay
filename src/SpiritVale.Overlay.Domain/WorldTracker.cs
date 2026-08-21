using SpiritVale.Overlay.Api.World;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class WorldTracker : IWorldApi
{
    private readonly object _gate = new();
    private readonly Dictionary<int, WorldEntity> _entities = new();
    private readonly ActorDirectory _names;

    public WorldTracker(ActorDirectory names) => _names = names;

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
                MapName = packet.Fields.TryGetValue("displayName", out var map) ? map?.ToString()
                    : packet.Fields.TryGetValue("mapName", out var map2) ? map2?.ToString()
                    : MapName ?? "Unknown";
                if (MapName is not null && MapName.Length > 0 && !MapName.StartsWith("Actor"))
                    WorldChanged?.Invoke();
                break;
            case "ChannelChange_C":
                if (packet.Fields.TryGetValue("channel", out var ch) && int.TryParse(ch?.ToString(), out var channel))
                    Channel = channel;
                WorldChanged?.Invoke();
                break;
            case "MonsterSpawn_C":
            {
                var name = ReadDisplayName(packet) ?? "Monster";
                UpsertEntity(packet.ObjectId ?? 0, "Monster", name, registerName: true, markPlayer: false);
                break;
            }
        }

        switch (packet.PacketName)
        {
            case FishNetPacketNames.ObjectSpawn when packet.ObjectId is int oid:
            {
                var name = ReadDisplayName(packet);
                if (name is not null)
                    UpsertEntity(oid, GuessKind(packet), name, registerName: true, markPlayer: GuessKind(packet) == "Player");
                else
                    UpsertEntity(oid, "Entity", $"Object {oid}", registerName: false, markPlayer: false);
                break;
            }
            case FishNetPacketNames.SyncType when packet.ObjectId is int oid:
            {
                var name = ReadDisplayName(packet);
                if (name is not null)
                    UpsertEntity(oid, "Player", name, registerName: true, markPlayer: true);
                break;
            }
            case FishNetPacketNames.ObjectDespawn when packet.ObjectId is int oid:
                RemoveEntity(oid);
                break;
        }
    }

    public void NotifyLoot(LootDropEvent drop) => LootDropped?.Invoke(drop);

    private static string GuessKind(DecodedFishNetPacket packet)
    {
        var t = packet.NetworkBehaviourType ?? "";
        if (t.Contains("Monster", StringComparison.OrdinalIgnoreCase)) return "Monster";
        if (t.Contains("Player", StringComparison.OrdinalIgnoreCase)) return "Player";
        return "Entity";
    }

    private static string? ReadDisplayName(DecodedFishNetPacket packet)
    {
        if (packet.Fields.TryGetValue("displayName", out var n) && n is string s && s.Length >= 2)
            return s;
        if (packet.Fields.TryGetValue("nameCandidates", out var c) && c is IReadOnlyList<string> list)
            return list.FirstOrDefault();
        return null;
    }

    private void UpsertEntity(int actorId, string kind, string name, bool registerName, bool markPlayer)
    {
        if (actorId == 0) return;
        if (registerName)
            _names.SetName(actorId, name, markPlayer: markPlayer);

        var entity = new WorldEntity(actorId, _names.Resolve(actorId), kind, null, null, null);
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
