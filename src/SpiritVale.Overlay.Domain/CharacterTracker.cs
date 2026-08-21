using SpiritVale.Overlay.Api.Character;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class CharacterTracker : ICharacterApi
{
    public event Action? CharacterChanged;

    public CharacterSnapshot? Local { get; private set; }

    public void Consume(DecodedFishNetPacket packet)
    {
        if (packet.RpcName is not ("CharacterRecordSync_T" or "InventorySync_T")
            && packet.PacketName != FishNetPacketNames.Authenticated)
            return;

        if (packet.PacketName == FishNetPacketNames.Authenticated)
        {
            Local = new CharacterSnapshot(
                Local?.DisplayName ?? "Local Player",
                Local?.CharacterId,
                packet.ObjectId ?? Local?.ActorId,
                Local?.Level,
                Local?.ClassName,
                Local?.Stats ?? new Dictionary<string, double>(),
                Local?.Equipped ?? Array.Empty<InventoryItem>(),
                Local?.Bag ?? Array.Empty<InventoryItem>());
            CharacterChanged?.Invoke();
            return;
        }

        var name = packet.Fields.TryGetValue("displayName", out var n) ? n?.ToString() : Local?.DisplayName;
        Local = new CharacterSnapshot(
            name ?? "Local Player",
            Local?.CharacterId,
            packet.ObjectId ?? Local?.ActorId,
            Local?.Level ?? 1,
            Local?.ClassName,
            Local?.Stats ?? new Dictionary<string, double>(),
            Local?.Equipped ?? Array.Empty<InventoryItem>(),
            Local?.Bag ?? Array.Empty<InventoryItem>());
        CharacterChanged?.Invoke();
    }

    public void SetLocal(CharacterSnapshot snapshot)
    {
        Local = snapshot;
        CharacterChanged?.Invoke();
    }
}
