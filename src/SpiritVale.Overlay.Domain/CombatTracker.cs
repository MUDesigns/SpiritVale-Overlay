using SpiritVale.Overlay.Api.Combat;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class CombatTracker : ICombatApi
{
    private readonly object _gate = new();
    private readonly Dictionary<int, ActorAgg> _actors = new();
    private DateTimeOffset? _encounterStart;
    private long _totalDamage;

    public event Action<CombatDamageEvent>? Damage;
    public event Action<CombatHealEvent>? Heal;
    public event Action<CombatDeathEvent>? Death;
    public event Action<CombatEncounterSnapshot>? EncounterChanged;

    public CombatEncounterSnapshot? CurrentEncounter { get; private set; }
    public IReadOnlyList<DpsRow> DpsRows { get; private set; } = Array.Empty<DpsRow>();

    public void Consume(DecodedFishNetPacket packet)
    {
        if (packet.RpcName is null && packet.PacketName is not (
            FishNetPacketNames.ObjectSpawn or FishNetPacketNames.ObjectDespawn or FishNetPacketNames.Authenticated))
            return;

        switch (packet.RpcName)
        {
            case "ApplyDamage_C":
                HandleDamage(packet);
                break;
            case "Recover_C":
                HandleHeal(packet);
                break;
            case "Death_C":
                HandleDeath(packet);
                break;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _actors.Clear();
            _encounterStart = null;
            _totalDamage = 0;
            DpsRows = Array.Empty<DpsRow>();
            CurrentEncounter = null;
        }
        EncounterChanged?.Invoke(new CombatEncounterSnapshot(DateTimeOffset.Now, TimeSpan.Zero, 0, Array.Empty<DpsRow>()));
    }

    private void HandleDamage(DecodedFishNetPacket packet)
    {
        var amount = ReadLongField(packet, "damage", "amount", "value") ?? GuessAmount(packet.Payload);
        if (amount <= 0) return;

        var sourceId = packet.ObjectId ?? 0;
        var targetId = ReadIntField(packet, "targetId", "target", "victimId") ?? 0;
        var critical = ReadBoolField(packet, "critical", "isCritical") ?? false;
        var label = packet.RpcName;

        EnsureEncounter();
        lock (_gate)
        {
            var agg = GetOrCreate(sourceId);
            agg.TotalDamage += amount;
            _totalDamage += amount;
            PublishSnapshot();
        }

        var name = NameOf(sourceId);
        var targetName = targetId != 0 ? NameOf(targetId) : null;
        Damage?.Invoke(new CombatDamageEvent(
            (int)packet.Tick, sourceId, targetId, amount, label, critical, name, targetName));
        EncounterChanged?.Invoke(CurrentEncounter!);
    }

    private void HandleHeal(DecodedFishNetPacket packet)
    {
        var amount = ReadLongField(packet, "amount", "heal", "value") ?? GuessAmount(packet.Payload);
        if (amount <= 0) return;
        var sourceId = packet.ObjectId ?? 0;
        var targetId = ReadIntField(packet, "targetId", "target") ?? sourceId;
        Heal?.Invoke(new CombatHealEvent(
            (int)packet.Tick, sourceId, targetId, amount, packet.RpcName, NameOf(sourceId), NameOf(targetId)));
    }

    private void HandleDeath(DecodedFishNetPacket packet)
    {
        var actorId = packet.ObjectId ?? 0;
        var killer = ReadIntField(packet, "killerId", "sourceId");
        Death?.Invoke(new CombatDeathEvent(
            (int)packet.Tick, actorId, killer, NameOf(actorId), killer is int k ? NameOf(k) : null));
    }

    private void EnsureEncounter()
    {
        lock (_gate)
        {
            _encounterStart ??= DateTimeOffset.Now;
        }
    }

    private ActorAgg GetOrCreate(int actorId)
    {
        if (!_actors.TryGetValue(actorId, out var agg))
        {
            agg = new ActorAgg { ActorId = actorId, DisplayName = $"Actor {actorId}" };
            _actors[actorId] = agg;
        }
        return agg;
    }

    private void PublishSnapshot()
    {
        var started = _encounterStart ?? DateTimeOffset.Now;
        var duration = DateTimeOffset.Now - started;
        var seconds = Math.Max(duration.TotalSeconds, 1);
        var rows = _actors.Values
            .OrderByDescending(a => a.TotalDamage)
            .Select(a => new DpsRow(
                a.ActorId,
                a.DisplayName,
                a.TotalDamage,
                a.TotalDamage / seconds,
                _totalDamage <= 0 ? 0 : (double)a.TotalDamage / _totalDamage))
            .ToList();
        DpsRows = rows;
        CurrentEncounter = new CombatEncounterSnapshot(started, duration, _totalDamage, rows);
    }

    public void SetActorName(int actorId, string name)
    {
        lock (_gate)
        {
            GetOrCreate(actorId).DisplayName = name;
            if (_encounterStart is not null) PublishSnapshot();
        }
    }

    private string NameOf(int actorId)
    {
        lock (_gate)
        {
            return _actors.TryGetValue(actorId, out var a) ? a.DisplayName : $"Actor {actorId}";
        }
    }

    private static long? ReadLongField(DecodedFishNetPacket packet, params string[] names)
    {
        foreach (var name in names)
        {
            if (packet.Fields.TryGetValue(name, out var value) && value is not null)
            {
                if (value is long l) return l;
                if (value is int i) return i;
                if (long.TryParse(value.ToString(), out var parsed)) return parsed;
            }
        }
        return null;
    }

    private static int? ReadIntField(DecodedFishNetPacket packet, params string[] names)
    {
        var v = ReadLongField(packet, names);
        return v is long l ? (int)l : null;
    }

    private static bool? ReadBoolField(DecodedFishNetPacket packet, params string[] names)
    {
        foreach (var name in names)
        {
            if (packet.Fields.TryGetValue(name, out var value) && value is bool b)
                return b;
        }
        return null;
    }

    private static long GuessAmount(byte[] payload)
    {
        if (payload.Length < 4) return 0;
        // Heuristic: first int32 LE often carries damage in SpiritVale combat RPCs.
        var value = BitConverter.ToInt32(payload, 0);
        return value is > 0 and < 50_000_000 ? value : 0;
    }

    private sealed class ActorAgg
    {
        public int ActorId { get; init; }
        public string DisplayName { get; set; } = "";
        public long TotalDamage { get; set; }
    }
}
