namespace SpiritVale.Overlay.Api.Combat;

public interface ICombatApi
{
    event Action<CombatDamageEvent>? Damage;
    event Action<CombatHealEvent>? Heal;
    event Action<CombatDeathEvent>? Death;
    event Action<CombatEncounterSnapshot>? EncounterChanged;

    CombatEncounterSnapshot? CurrentEncounter { get; }
    IReadOnlyList<DpsRow> DpsRows { get; }

    /// <summary>Local player + named combatants with current-encounter DPS (0 when idle).</summary>
    IReadOnlyList<DpsRow> Leaderboard { get; }

    /// <summary>Per-skill breakdown for one actor in the current encounter.</summary>
    IReadOnlyList<SkillDpsRow> GetSkillBreakdown(int actorId, string? displayName = null);

    /// <summary>Clears the current encounter totals (hotkey / UI reset).</summary>
    void ResetEncounter();
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
    double Share,
    int? ArchetypeId = null,
    string? ClassName = null,
    int? Level = null,
    int Hits = 0,
    int Crits = 0,
    int Deaths = 0,
    double CritRate = 0);

public sealed record SkillDpsRow(
    string SkillId,
    string DisplayName,
    string? SpriteId,
    long TotalDamage,
    double Dps,
    double Share,
    int Hits,
    int Crits,
    double CritRate);

public sealed record CombatEncounterSnapshot(
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    long TotalDamage,
    IReadOnlyList<DpsRow> Rows);
