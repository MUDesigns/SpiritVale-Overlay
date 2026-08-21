namespace SpiritVale.Overlay.Api.Character;

public interface ICharacterApi
{
    event Action? CharacterChanged;

    CharacterSnapshot? Local { get; }
}

public sealed record CharacterSnapshot(
    string? DisplayName,
    string? CharacterId,
    int? ActorId,
    int? Level,
    string? ClassName,
    IReadOnlyDictionary<string, double> Stats,
    IReadOnlyList<InventoryItem> Equipped,
    IReadOnlyList<InventoryItem> Bag);

public sealed record InventoryItem(
    string ItemId,
    string DisplayName,
    int Count,
    int Refine,
    string Location,
    IReadOnlyDictionary<string, double> Stats);
