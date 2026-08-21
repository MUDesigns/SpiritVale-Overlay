namespace SpiritVale.Overlay.Api.World;

public interface IWorldApi
{
    event Action? WorldChanged;
    event Action<WorldEntityEvent>? EntityChanged;
    event Action<LootDropEvent>? LootDropped;

    string? MapName { get; }
    int? Channel { get; }
    IReadOnlyList<WorldEntity> NearbyEntities { get; }
}

public sealed record WorldEntity(
    int ActorId,
    string DisplayName,
    string Kind,
    float? X,
    float? Y,
    float? Z);

public sealed record WorldEntityEvent(
    string Operation,
    WorldEntity Entity);

public sealed record LootDropEvent(
    int Tick,
    string ItemId,
    string DisplayName,
    int Count,
    int? OwnerActorId);
