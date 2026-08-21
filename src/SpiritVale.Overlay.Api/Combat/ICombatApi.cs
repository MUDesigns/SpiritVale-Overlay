namespace SpiritVale.Overlay.Api.Combat;

public interface ICombatApi
{
    event Action<CombatDamageEvent>? Damage;
    event Action<CombatHealEvent>? Heal;
    event Action<CombatDeathEvent>? Death;
    event Action<CombatEncounterSnapshot>? EncounterChanged;

    CombatEncounterSnapshot? CurrentEncounter { get; }
    IReadOnlyList<DpsRow> DpsRows { get; }
}

public sealed record CombatDamageEvent(
    int Tick,
    int SourceActorId,
    int TargetActorId,
    long Amount,
    string? SkillLabel,
    bool Critical,
    string? SourceName,
    string? TargetName);

public sealed record CombatHealEvent(
    int Tick,
    int SourceActorId,
    int TargetActorId,
    long Amount,
    string? SkillLabel,
    string? SourceName,
    string? TargetName);

public sealed record CombatDeathEvent(
    int Tick,
    int ActorId,
    int? KillerActorId,
    string? ActorName,
    string? KillerName);

public sealed record DpsRow(
    int ActorId,
    string DisplayName,
    long TotalDamage,
    double Dps,
    double Share);

public sealed record CombatEncounterSnapshot(
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    long TotalDamage,
    IReadOnlyList<DpsRow> Rows);
