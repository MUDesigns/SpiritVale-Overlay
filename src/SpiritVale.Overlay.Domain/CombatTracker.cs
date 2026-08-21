using SpiritVale.Overlay.Api.Combat;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class CombatTracker : ICombatApi
{
    private readonly object _gate = new();
    private readonly Dictionary<int, ActorAgg> _byActorId = new();
    private readonly ActorDirectory _names;
    private readonly SpriteCatalog _sprites;
    private DateTimeOffset? _encounterStart;
    private DateTimeOffset _lastDamageAt;
    private long _totalDamage;

    public CombatTracker(ActorDirectory names, SpriteCatalog sprites)
    {
        _names = names;
        _sprites = sprites;
        _names.NameChanged += (_, _) => PublishAndNotify();
        _names.PlayersChanged += PublishAndNotify;
    }

    public event Action<CombatDamageEvent>? Damage;
    public event Action<CombatHealEvent>? Heal;
    public event Action<CombatDeathEvent>? Death;
    public event Action<CombatEncounterSnapshot>? EncounterChanged;

    public CombatEncounterSnapshot? CurrentEncounter { get; private set; }
    public IReadOnlyList<DpsRow> DpsRows { get; private set; } = Array.Empty<DpsRow>();
    public IReadOnlyList<DpsRow> Leaderboard { get; private set; } = Array.Empty<DpsRow>();

    public void Consume(DecodedFishNetPacket packet)
    {
        switch (packet.RpcName)
        {
            case "ApplyDamage_C":
                HandleDamage(packet, death: false);
                break;
            case "Recover_C":
                HandleHeal(packet);
                break;
            case "Death_C":
                HandleDamage(packet, death: true);
                break;
        }
    }

    public IReadOnlyList<SkillDpsRow> GetSkillBreakdown(int actorId, string? displayName = null)
    {
        lock (_gate)
        {
            var seconds = _encounterStart is DateTimeOffset start
                ? Math.Max((DateTimeOffset.Now - start).TotalSeconds, 1)
                : 1;

            // Prefer sticky nameplates so respawn / new object ids still resolve after death.
            string? name = null;
            if (!string.IsNullOrWhiteSpace(displayName)
                && !displayName.StartsWith("Actor ", StringComparison.Ordinal))
                name = displayName.Trim();

            if (name is null && _byActorId.TryGetValue(actorId, out var direct)
                && !string.IsNullOrWhiteSpace(direct.StickyName))
                name = direct.StickyName;

            name ??= _names.ResolveAttribution(actorId);

            var merged = new Dictionary<string, SkillAgg>(StringComparer.OrdinalIgnoreCase);
            foreach (var other in _byActorId.Values)
            {
                var otherName = !string.IsNullOrWhiteSpace(other.StickyName)
                    ? other.StickyName!
                    : _names.ResolveAttribution(other.ActorId);

                var samePerson =
                    other.ActorId == actorId
                    || (!name.StartsWith("Actor ", StringComparison.Ordinal)
                        && string.Equals(otherName, name, StringComparison.OrdinalIgnoreCase));

                if (!samePerson) continue;

                foreach (var (sid, s) in other.Skills)
                {
                    if (!merged.TryGetValue(sid, out var m))
                    {
                        m = new SkillAgg { SkillId = sid };
                        merged[sid] = m;
                    }
                    m.TotalDamage += s.TotalDamage;
                    m.Hits += s.Hits;
                    m.Crits += s.Crits;
                }
            }

            return BuildSkillRows(merged.Values, seconds);
        }
    }

    public void Reset() => ResetEncounter();

    public void ResetEncounter()
    {
        lock (_gate)
        {
            _byActorId.Clear();
            _encounterStart = null;
            _totalDamage = 0;
            DpsRows = Array.Empty<DpsRow>();
            CurrentEncounter = null;
            RebuildLeaderboardUnlocked();
        }
        EncounterChanged?.Invoke(new CombatEncounterSnapshot(
            DateTimeOffset.Now, TimeSpan.Zero, 0, Array.Empty<DpsRow>()));
    }

    private void HandleDamage(DecodedFishNetPacket packet, bool death)
    {
        DamageFields? decoded = null;
        if (DamageCodec.TryDecode(packet.Payload, out var dmg))
        {
            decoded = dmg;
            DamageCodec.WriteToFields(packet, dmg);
        }

        int? team = decoded?.Team ?? ReadInt(packet, "dmg.Team");
        var value = decoded?.Value ?? ReadInt(packet, "dmg.Value", "damage", "amount") ?? 0;
        var attackerId = decoded?.AttackerId ?? ReadInt(packet, "dmg.AttackerId", "sourceId", "attackerId") ?? 0;
        var targetId = packet.ObjectId ?? 0;
        var critical = decoded?.Hit == 1 || (ReadBool(packet, "critical") ?? false);
        string? skill = decoded?.DamageSourceId;
        if (skill is null && packet.Fields.TryGetValue("dmg.DamageSourceId", out var src))
            skill = src?.ToString();

        if (death && targetId > 0)
        {
            lock (_gate)
            {
                // Only count deaths for actors already on the meter / known players —
                // don't invent rows for every mob death.
                if (_byActorId.ContainsKey(targetId) || _names.IsTrackedPlayer(targetId))
                    GetOrCreate(targetId).Deaths++;
            }
        }

        if ((team is int t && t != 0) || value <= 0 || attackerId <= 0 || attackerId == targetId)
        {
            if (death)
            {
                Death?.Invoke(new CombatDeathEvent(
                    (int)packet.Tick, targetId, attackerId > 0 ? attackerId : null,
                    _names.ResolveAttribution(targetId),
                    attackerId > 0 ? _names.ResolveAttribution(attackerId) : null));
                PublishAndNotify();
            }
            return;
        }

        _names.ObservePlayerActor(attackerId);

        MaybeRotateEncounter();

        var credited = false;
        lock (_gate)
        {
            // Encounter only opens on the local player's damage; others can't start a fight timer.
            if (_encounterStart is null && !IsLocalAttacker(attackerId))
            {
                // leave credited false
            }
            else
            {
                _encounterStart ??= DateTimeOffset.Now;

                var agg = GetOrCreate(attackerId);
                agg.TotalDamage += value;
                agg.Hits++;
                if (critical) agg.Crits++;
                var resolved = _names.ResolveAttribution(attackerId);
                if (!resolved.StartsWith("Actor ", StringComparison.Ordinal))
                    agg.StickyName = resolved;
                var skillId = string.IsNullOrWhiteSpace(skill) ? "Unknown" : skill.Trim();
                if (!agg.Skills.TryGetValue(skillId, out var skillAgg))
                {
                    skillAgg = new SkillAgg { SkillId = skillId };
                    agg.Skills[skillId] = skillAgg;
                }
                skillAgg.TotalDamage += value;
                skillAgg.Hits++;
                if (critical) skillAgg.Crits++;
                _totalDamage += value;
                _lastDamageAt = DateTimeOffset.Now;
                PublishSnapshotUnlocked();
                credited = true;
            }
        }

        var sourceName = _names.ResolveAttribution(attackerId);
        var targetName = targetId != 0 ? _names.ResolveAttribution(targetId) : null;

        Damage?.Invoke(new CombatDamageEvent(
            (int)packet.Tick, attackerId, targetId, value, skill, critical, sourceName, targetName));

        if (death)
        {
            Death?.Invoke(new CombatDeathEvent(
                (int)packet.Tick, targetId, attackerId, targetName, sourceName));
        }

        if (credited && CurrentEncounter is not null)
            EncounterChanged?.Invoke(CurrentEncounter);
    }

    private void HandleHeal(DecodedFishNetPacket packet)
    {
        var amount = ReadInt(packet, "amount", "damage") ?? 0;
        if (amount <= 0) return;
        var targetId = packet.ObjectId ?? 0;
        var sourceId = targetId;
        Heal?.Invoke(new CombatHealEvent(
            (int)packet.Tick, sourceId, targetId, amount, packet.RpcName,
            _names.ResolveAttribution(sourceId), _names.ResolveAttribution(targetId)));
    }

    private bool IsLocalAttacker(int attackerId)
    {
        if (attackerId <= 0) return false;
        var localId = _names.LocalActorId;
        if (localId is int id && id == attackerId) return true;

        var localName = _names.LocalDisplayName;
        if (string.IsNullOrWhiteSpace(localName)) return false;

        var resolved = _names.ResolveAttribution(attackerId);
        return !resolved.StartsWith("Actor ", StringComparison.Ordinal)
            && string.Equals(resolved, localName, StringComparison.OrdinalIgnoreCase);
    }

    private void MaybeRotateEncounter()
    {
        lock (_gate)
        {
            if (_encounterStart is null) return;
            if (DateTimeOffset.Now - _lastDamageAt < TimeSpan.FromSeconds(15)) return;
            _byActorId.Clear();
            _encounterStart = null;
            _totalDamage = 0;
            DpsRows = Array.Empty<DpsRow>();
            CurrentEncounter = null;
            RebuildLeaderboardUnlocked();
        }
    }

    private ActorAgg GetOrCreate(int actorId)
    {
        if (!_byActorId.TryGetValue(actorId, out var agg))
        {
            agg = new ActorAgg { ActorId = actorId };
            _byActorId[actorId] = agg;
        }
        return agg;
    }

    private void PublishAndNotify()
    {
        lock (_gate) PublishSnapshotUnlocked();
        if (CurrentEncounter is not null)
            EncounterChanged?.Invoke(CurrentEncounter);
    }

    private void PublishSnapshotUnlocked()
    {
        var started = _encounterStart ?? DateTimeOffset.Now;
        var duration = DateTimeOffset.Now - started;
        var seconds = Math.Max(duration.TotalSeconds, 1);

        var merged = new Dictionary<string, ActorAgg>(StringComparer.OrdinalIgnoreCase);
        foreach (var agg in _byActorId.Values)
        {
            var name = !string.IsNullOrWhiteSpace(agg.StickyName)
                ? agg.StickyName!
                : _names.ResolveAttribution(agg.ActorId);
            if (!name.StartsWith("Actor ", StringComparison.Ordinal))
                agg.StickyName = name;

            var key = name.StartsWith("Actor ", StringComparison.Ordinal)
                ? $"id:{agg.ActorId}"
                : $"name:{name}";
            if (merged.TryGetValue(key, out var cur))
            {
                cur.TotalDamage += agg.TotalDamage;
                cur.Hits += agg.Hits;
                cur.Crits += agg.Crits;
                cur.Deaths += agg.Deaths;
                cur.StickyName ??= agg.StickyName;
                cur.StickyArchetypeId ??= agg.StickyArchetypeId;
                foreach (var (sid, s) in agg.Skills)
                {
                    if (!cur.Skills.TryGetValue(sid, out var ms))
                    {
                        ms = new SkillAgg { SkillId = sid };
                        cur.Skills[sid] = ms;
                    }
                    ms.TotalDamage += s.TotalDamage;
                    ms.Hits += s.Hits;
                    ms.Crits += s.Crits;
                }
            }
            else
            {
                var clone = CloneAgg(agg);
                if (!name.StartsWith("Actor ", StringComparison.Ordinal))
                    clone.StickyName = name;
                merged[key] = clone;
            }
        }

        var rows = merged.Values
            .OrderByDescending(a => a.TotalDamage)
            .Select(a => ToRow(a, seconds))
            .ToList();

        DpsRows = rows;
        CurrentEncounter = _encounterStart is null
            ? null
            : new CombatEncounterSnapshot(started, duration, _totalDamage, rows);
        RebuildLeaderboardUnlocked();
    }

    private DpsRow ToRow(ActorAgg a, double seconds)
    {
        var name = !string.IsNullOrWhiteSpace(a.StickyName)
            ? a.StickyName!
            : _names.ResolveAttribution(a.ActorId);
        var meta = _names.GetMeta(a.ActorId);
        var inferred = _sprites.InferArchetype(
            a.Skills.Select(kv => (kv.Key, kv.Value.TotalDamage)),
            meta.ArchetypeId ?? a.StickyArchetypeId);
        if (inferred is not null)
            a.StickyArchetypeId = inferred;
        var arch = meta.ArchetypeId ?? a.StickyArchetypeId ?? inferred;
        return new DpsRow(
            a.ActorId,
            name,
            a.TotalDamage,
            a.TotalDamage / seconds,
            _totalDamage <= 0 ? 0 : (double)a.TotalDamage / _totalDamage,
            arch,
            meta.ClassName ?? _sprites.ResolveClassName(arch),
            meta.Level,
            a.Hits,
            a.Crits,
            a.Deaths,
            a.Hits <= 0 ? 0 : (double)a.Crits / a.Hits);
    }

    private void RebuildLeaderboardUnlocked()
    {
        var seconds = _encounterStart is DateTimeOffset start
            ? Math.Max((DateTimeOffset.Now - start).TotalSeconds, 1)
            : 1;

        var list = new List<DpsRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localId = _names.LocalActorId;
        var localName = _names.LocalDisplayName;

        void AddRow(DpsRow row)
        {
            var key = row.DisplayName.StartsWith("Actor ", StringComparison.Ordinal)
                ? $"id:{row.ActorId}"
                : row.DisplayName;
            if (!seen.Add(key)) return;
            list.Add(row);
        }

        // Combatants with damage only — AOI nameplate churn was flashing zero-DPS rows.
        foreach (var row in DpsRows.Where(r => r.TotalDamage > 0))
            AddRow(row);

        if (!string.IsNullOrWhiteSpace(localName) && !seen.Contains(localName!))
        {
            var localDmg = DpsRows.FirstOrDefault(r =>
                localId == r.ActorId
                || string.Equals(r.DisplayName, localName, StringComparison.OrdinalIgnoreCase));
            if (localDmg is not null)
                AddRow(localDmg);
            else
            {
                var meta = localId is int id ? _names.GetMeta(id) : default;
                AddRow(new DpsRow(
                    localId ?? 0, localName!, 0, 0, 0,
                    meta.ArchetypeId, meta.ClassName ?? _names.LocalClassName,
                    meta.Level ?? _names.LocalLevel));
            }
        }

        // Rank by damage only — local player is highlighted in the HUD, not pinned first.
        Leaderboard = list
            .OrderByDescending(r => r.TotalDamage)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _ = seconds;
    }

    private IReadOnlyList<SkillDpsRow> BuildSkillRows(IEnumerable<SkillAgg> skills, double seconds)
    {
        var list = skills.OrderByDescending(s => s.TotalDamage).ToList();
        var total = list.Sum(s => s.TotalDamage);
        return list.Select(s =>
        {
            var (display, sprite) = _sprites.ResolveSkill(s.SkillId);
            return new SkillDpsRow(
                s.SkillId,
                display,
                sprite,
                s.TotalDamage,
                s.TotalDamage / seconds,
                total <= 0 ? 0 : (double)s.TotalDamage / total,
                s.Hits,
                s.Crits,
                s.Hits <= 0 ? 0 : (double)s.Crits / s.Hits);
        }).ToList();
    }

    private static ActorAgg CloneAgg(ActorAgg src)
    {
        var clone = new ActorAgg
        {
            ActorId = src.ActorId,
            TotalDamage = src.TotalDamage,
            Hits = src.Hits,
            Crits = src.Crits,
            Deaths = src.Deaths,
            StickyName = src.StickyName,
            StickyArchetypeId = src.StickyArchetypeId,
        };
        foreach (var (k, v) in src.Skills)
        {
            clone.Skills[k] = new SkillAgg
            {
                SkillId = v.SkillId,
                TotalDamage = v.TotalDamage,
                Hits = v.Hits,
                Crits = v.Crits,
            };
        }
        return clone;
    }

    private static int? ReadInt(DecodedFishNetPacket packet, params string[] names)
    {
        foreach (var name in names)
        {
            if (!packet.Fields.TryGetValue(name, out var value) || value is null) continue;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (int.TryParse(value.ToString(), out var p)) return p;
        }
        return null;
    }

    private static bool? ReadBool(DecodedFishNetPacket packet, params string[] names)
    {
        foreach (var name in names)
        {
            if (packet.Fields.TryGetValue(name, out var value) && value is bool b)
                return b;
        }
        return null;
    }

    private sealed class ActorAgg
    {
        public int ActorId { get; init; }
        public long TotalDamage { get; set; }
        public int Hits { get; set; }
        public int Crits { get; set; }
        public int Deaths { get; set; }
        /// <summary>Once resolved, keep the nameplate for the encounter (stops AOI flash).</summary>
        public string? StickyName { get; set; }
        /// <summary>From VisualData or skill inference.</summary>
        public int? StickyArchetypeId { get; set; }
        public Dictionary<string, SkillAgg> Skills { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SkillAgg
    {
        public string SkillId { get; init; } = "";
        public long TotalDamage { get; set; }
        public int Hits { get; set; }
        public int Crits { get; set; }
    }
}
