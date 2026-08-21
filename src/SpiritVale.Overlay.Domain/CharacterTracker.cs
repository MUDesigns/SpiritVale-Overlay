using SpiritVale.Overlay.Api.Character;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class CharacterTracker : ICharacterApi
{
    private readonly ActorDirectory _names;

    public CharacterTracker(ActorDirectory names) => _names = names;

    public event Action? CharacterChanged;

    public CharacterSnapshot? Local { get; private set; }

    public void Consume(DecodedFishNetPacket packet)
    {
        if (packet.RpcName is "LoadCharacter_T" or "CharacterCallback_T" or "CharacterRecordSync_T" or "InventorySync_T")
        {
            var meta = CharacterMetaCodec.TryDecode(packet.Payload);
            var identity = ActorIdentityCodec.TryDecodeCharacterData(packet.Payload);
            var name = packet.Fields.TryGetValue("displayName", out var n) ? n?.ToString() : null;
            name ??= meta?.DisplayName;
            name ??= identity?.DisplayName;
            name ??= Local?.DisplayName ?? _names.LocalDisplayName;
            var uid = packet.Fields.TryGetValue("uid", out var uidObj) ? uidObj?.ToString() : null;
            uid ??= identity?.Uid ?? Local?.CharacterId;

            if (packet.ObjectId is int actorId)
            {
                _names.SetLocalIdentity(actorId, name);
                if (meta is not null)
                    _names.SetMeta(actorId, meta.Value.ArchetypeId, meta.Value.Level, isLocal: true);
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                _names.SetLocalIdentity(_names.LocalActorId, name);
                if (meta is not null)
                    _names.SetMeta(_names.LocalActorId ?? 0, meta.Value.ArchetypeId, meta.Value.Level, isLocal: true);
            }

            var stats = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (Local?.Stats is { Count: > 0 } existing)
            {
                foreach (var kv in existing)
                    stats[kv.Key] = kv.Value;
            }
            if (meta?.Level is int metaLevel && metaLevel > 0)
                stats["level"] = metaLevel;
            if (meta?.JobLevel is int metaJob && metaJob > 0)
                stats["jobLevel"] = metaJob;

            Local = new CharacterSnapshot(
                name ?? "You",
                uid,
                packet.ObjectId ?? Local?.ActorId ?? _names.LocalActorId,
                meta?.Level ?? _names.LocalLevel ?? Local?.Level,
                meta?.ClassName ?? _names.LocalClassName ?? Local?.ClassName,
                stats.Count > 0 ? stats : Local?.Stats ?? new Dictionary<string, double>(),
                Local?.Equipped ?? Array.Empty<InventoryItem>(),
                Local?.Bag ?? Array.Empty<InventoryItem>());
            CharacterChanged?.Invoke();
            return;
        }

        if (packet.PacketName == FishNetPacketNames.Authenticated)
        {
            if (packet.ObjectId is int oid)
                _names.SetLocalIdentity(oid, Local?.DisplayName ?? _names.LocalDisplayName);

            Local = new CharacterSnapshot(
                Local?.DisplayName ?? _names.LocalDisplayName ?? "You",
                Local?.CharacterId,
                packet.ObjectId ?? Local?.ActorId,
                Local?.Level ?? _names.LocalLevel,
                Local?.ClassName ?? _names.LocalClassName,
                Local?.Stats ?? new Dictionary<string, double>(),
                Local?.Equipped ?? Array.Empty<InventoryItem>(),
                Local?.Bag ?? Array.Empty<InventoryItem>());
            CharacterChanged?.Invoke();
        }
    }

    public void SetLocal(CharacterSnapshot snapshot)
    {
        Local = snapshot;
        if (snapshot.ActorId is int id)
            _names.SetLocalIdentity(id, snapshot.DisplayName);
        CharacterChanged?.Invoke();
    }
}
