using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed record NamedPlayer(int ActorId, string DisplayName, bool IsLocal);
public readonly record struct ActorMeta(int? ArchetypeId, string? ClassName, int? Level);

/// <summary>
/// Maps FishNet object ids → display names. Mirrors spirit-vale-tools actor-directory:
/// names live on VisualData / CharacterData objects, while combat AttackerId may be a
/// sibling object that shares the same owner connection.
/// </summary>
public sealed class ActorDirectory
{
    private readonly object _gate = new();
    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<int, ActorMeta> _meta = new();
    private readonly Dictionary<int, int> _ownerByObject = new(); // objectId → ownerConnectionId
    private readonly Dictionary<int, HashSet<int>> _objectsByOwner = new();
    private readonly HashSet<int> _playerIds = new();
    private int? _localActorId;
    private string? _localDisplayName;
    private int? _localArchetypeId;
    private string? _localClassName;
    private int? _localLevel;

    public event Action<int, string>? NameChanged;
    public event Action? PlayersChanged;

    public int? LocalActorId
    {
        get { lock (_gate) return _localActorId; }
    }

    public string? LocalDisplayName
    {
        get { lock (_gate) return _localDisplayName; }
    }

    public int? LocalLevel
    {
        get { lock (_gate) return _localLevel; }
    }

    public string? LocalClassName
    {
        get { lock (_gate) return _localClassName; }
    }

    public ActorMeta GetMeta(int actorId)
    {
        lock (_gate)
        {
            if (_meta.TryGetValue(actorId, out var m)) return m;
            if (_localActorId == actorId)
                return new ActorMeta(_localArchetypeId, _localClassName, _localLevel);
            // Owner-sibling fallback
            if (_ownerByObject.TryGetValue(actorId, out var owner)
                && _objectsByOwner.TryGetValue(owner, out var siblings))
            {
                foreach (var sib in siblings)
                {
                    if (_meta.TryGetValue(sib, out var sm) && (sm.ArchetypeId is not null || sm.Level is not null))
                        return sm;
                }
            }
            return default;
        }
    }

    public void SetMeta(int actorId, int? archetypeId = null, int? level = null, bool isLocal = false)
    {
        if (actorId <= 0 && !isLocal) return;
        lock (_gate)
        {
            var className = ArchetypeNames.GetName(archetypeId);
            if (actorId > 0)
            {
                _meta.TryGetValue(actorId, out var cur);
                _meta[actorId] = new ActorMeta(
                    archetypeId ?? cur.ArchetypeId,
                    className ?? cur.ClassName ?? ArchetypeNames.GetName(cur.ArchetypeId),
                    level ?? cur.Level);
                _playerIds.Add(actorId);
                if (_ownerByObject.TryGetValue(actorId, out var owner)
                    && _objectsByOwner.TryGetValue(owner, out var siblings))
                {
                    foreach (var sib in siblings)
                    {
                        if (sib == actorId) continue;
                        _meta.TryGetValue(sib, out var sm);
                        _meta[sib] = new ActorMeta(
                            archetypeId ?? sm.ArchetypeId ?? _meta[actorId].ArchetypeId,
                            className ?? sm.ClassName ?? _meta[actorId].ClassName,
                            level ?? sm.Level ?? _meta[actorId].Level);
                    }
                }
            }

            if (isLocal || _localActorId == actorId)
            {
                if (archetypeId is not null)
                {
                    _localArchetypeId = archetypeId;
                    _localClassName = className ?? _localClassName;
                }
                if (level is not null)
                    _localLevel = level;
            }
        }
        PlayersChanged?.Invoke();
    }

    public void SetLocalIdentity(int? actorId, string? displayName)
    {
        List<(int Id, string Name)> notify = new();
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                _localDisplayName = displayName.Trim();

            if (actorId is int id && id > 0)
            {
                _localActorId = id;
                _playerIds.Add(id);
                if (_localDisplayName is not null)
                {
                    _names[id] = _localDisplayName;
                    notify.Add((id, _localDisplayName));
                    PropagateOwnerNameUnlocked(id, _localDisplayName, notify);
                }
            }
            else if (_localActorId is int existing && _localDisplayName is not null)
            {
                _names[existing] = _localDisplayName;
                notify.Add((existing, _localDisplayName));
            }
        }

        foreach (var (id, name) in notify)
            NameChanged?.Invoke(id, name);
        if (notify.Count > 0)
            PlayersChanged?.Invoke();
    }

    public void SetName(int actorId, string? name, bool markPlayer = false)
    {
        if (actorId <= 0 || string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!IsPlausiblePlayerName(name)) return;

        List<(int Id, string Name)> notify = new();
        lock (_gate)
        {
            if (markPlayer) _playerIds.Add(actorId);
            if (!_names.TryGetValue(actorId, out var cur) || cur != name)
            {
                _names[actorId] = name;
                notify.Add((actorId, name));
            }
            PropagateOwnerNameUnlocked(actorId, name, notify);
            if (_localActorId == actorId)
                _localDisplayName = name;
        }

        foreach (var (id, n) in notify)
            NameChanged?.Invoke(id, n);
        if (notify.Count > 0 || markPlayer)
            PlayersChanged?.Invoke();
    }

    public void ObservePlayerActor(int actorId)
    {
        if (actorId <= 0) return;
        var changed = false;
        lock (_gate)
        {
            changed = _playerIds.Add(actorId);
            // If this combat id shares an owner with a named object, inherit the name.
            if (_ownerByObject.TryGetValue(actorId, out var owner)
                && _objectsByOwner.TryGetValue(owner, out var siblings))
            {
                foreach (var sib in siblings)
                {
                    if (_names.TryGetValue(sib, out var n) && IsPlausiblePlayerName(n))
                    {
                        if (!_names.ContainsKey(actorId) || _names[actorId] != n)
                        {
                            _names[actorId] = n;
                            changed = true;
                        }
                        break;
                    }
                }
            }
        }
        if (changed) PlayersChanged?.Invoke();
    }

    public void NoteOwnership(int actorId, int ownerConnectionId)
    {
        if (actorId <= 0 || ownerConnectionId < 0) return;
        List<(int Id, string Name)> notify = new();
        lock (_gate)
        {
            if (_ownerByObject.TryGetValue(actorId, out var prev) && prev == ownerConnectionId)
                return;

            if (_ownerByObject.TryGetValue(actorId, out prev))
            {
                if (_objectsByOwner.TryGetValue(prev, out var oldSet))
                {
                    oldSet.Remove(actorId);
                    if (oldSet.Count == 0) _objectsByOwner.Remove(prev);
                }
            }

            _ownerByObject[actorId] = ownerConnectionId;
            if (!_objectsByOwner.TryGetValue(ownerConnectionId, out var set))
            {
                set = new HashSet<int>();
                _objectsByOwner[ownerConnectionId] = set;
            }
            set.Add(actorId);

            // Push best name across the owner group.
            string? best = null;
            foreach (var oid in set)
            {
                if (_names.TryGetValue(oid, out var n) && IsPlausiblePlayerName(n))
                {
                    best = n;
                    break;
                }
            }
            if (best is null && _localDisplayName is not null
                && _localActorId is int local && set.Contains(local))
                best = _localDisplayName;

            // Fill unnamed siblings only — never overwrite a different real name
            // (mis-shared owner ids would otherwise collapse the whole party to one name).
            if (best is not null)
            {
                foreach (var oid in set)
                {
                    if (_names.TryGetValue(oid, out var cur) && IsPlausiblePlayerName(cur) && cur != best)
                        continue;
                    if (!_names.TryGetValue(oid, out cur) || cur != best)
                    {
                        _names[oid] = best;
                        notify.Add((oid, best));
                    }
                }
            }
        }

        foreach (var (id, name) in notify)
            NameChanged?.Invoke(id, name);
        if (notify.Count > 0)
            PlayersChanged?.Invoke();
    }

    public void Remove(int actorId)
    {
        // Soft despawn: drop ownership edges only. Keeping sticky display names stops the
        // DPS meter from flashing players in/out as AOI objects recycle.
        lock (_gate)
        {
            if (_ownerByObject.Remove(actorId, out var owner)
                && _objectsByOwner.TryGetValue(owner, out var set))
            {
                set.Remove(actorId);
                if (set.Count == 0) _objectsByOwner.Remove(owner);
            }
        }
    }

    public string Resolve(int actorId)
    {
        lock (_gate)
            return ResolveUnlocked(actorId);
    }

    /// <summary>Resolve combat attacker including owner-sibling fallback.</summary>
    public string ResolveAttribution(int actorId)
    {
        lock (_gate)
        {
            var direct = ResolveUnlocked(actorId);
            if (!direct.StartsWith("Actor ", StringComparison.Ordinal))
                return direct;

            if (_ownerByObject.TryGetValue(actorId, out var owner)
                && _objectsByOwner.TryGetValue(owner, out var siblings))
            {
                foreach (var sib in siblings)
                {
                    if (_names.TryGetValue(sib, out var n) && IsPlausiblePlayerName(n))
                        return n;
                }
            }
            return direct;
        }
    }

    public bool IsTrackedPlayer(int actorId)
    {
        if (actorId <= 0) return false;
        lock (_gate)
        {
            if (_localActorId == actorId) return true;
            if (_playerIds.Contains(actorId)) return true;
            if (_names.TryGetValue(actorId, out var n) && IsPlausiblePlayerName(n)) return true;
            return false;
        }
    }

    public IReadOnlyList<NamedPlayer> NearbyPlayers()
    {
        lock (_gate)
        {
            var byName = new Dictionary<string, NamedPlayer>(StringComparer.OrdinalIgnoreCase);

            void Add(int id, string name, bool isLocal)
            {
                if (!IsPlausiblePlayerName(name) && !isLocal) return;
                if (name.StartsWith("Actor ", StringComparison.Ordinal) && !isLocal) return;
                if (byName.TryGetValue(name, out var existing))
                {
                    if (isLocal && !existing.IsLocal)
                        byName[name] = new NamedPlayer(id, name, true);
                    return;
                }
                byName[name] = new NamedPlayer(id, name, isLocal);
            }

            if (_localActorId is int local)
                Add(local, _localDisplayName ?? "You", true);
            else if (_localDisplayName is not null)
                Add(0, _localDisplayName, true);

            foreach (var id in _playerIds)
            {
                if (_localActorId == id) continue;
                Add(id, ResolveUnlocked(id), false);
            }

            foreach (var (id, name) in _names)
            {
                if (_localActorId == id) continue;
                if (!_playerIds.Contains(id)) continue;
                Add(id, name, false);
            }

            return byName.Values
                .OrderByDescending(p => p.IsLocal)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void ConsumeIdentityPacket(DecodedFishNetPacket packet)
    {
        if (packet.OwnerConnectionId is int owner && packet.ObjectId is int owned)
            NoteOwnership(owned, owner);

        if (packet.PacketName == FishNetPacketNames.OwnershipChange
            && packet.ObjectId is int ocOid
            && packet.OwnerConnectionId is int ocOwner)
        {
            NoteOwnership(ocOid, ocOwner);
        }

        switch (packet.RpcName)
        {
            case "LoadCharacter_T":
            case "CharacterCallback_T":
            {
                var meta = CharacterMetaCodec.TryDecode(packet.Payload);
                var name = packet.Fields.TryGetValue("displayName", out var n) ? n?.ToString() : null;
                name ??= meta?.DisplayName;
                if (string.IsNullOrWhiteSpace(name))
                    name = ActorIdentityCodec.TryDecodeCharacterData(packet.Payload)?.DisplayName;
                if (string.IsNullOrWhiteSpace(name))
                    name = ActorIdentityCodec.TryScanLooseDisplayName(packet.Payload);
                SetLocalIdentity(packet.ObjectId, name);
                if (meta is not null)
                    SetMeta(packet.ObjectId ?? _localActorId ?? 0, meta.Value.ArchetypeId, meta.Value.Level, isLocal: true);
                break;
            }
            case "MapChange_C":
                // New map → wipe object ids, keep remembered character name.
                ClearObjectsKeepLocalName();
                break;
        }

        // CharacterData without a named RPC: distinguish local PlayerSave vs Inspect stranger.
        if (packet.Payload.Length >= 16
            && packet.RpcName is not ("LoadCharacter_T" or "CharacterCallback_T")
            && ActorIdentityCodec.TryDecodeCharacterData(packet.Payload) is { } discovered)
        {
            packet.Fields["displayName"] = discovered.DisplayName;
            if (discovered.Uid is not null)
                packet.Fields["uid"] = discovered.Uid;

            if (packet.NetworkBehaviourType == "PlayerSave")
            {
                SetLocalIdentity(packet.ObjectId, discovered.DisplayName);
            }
            else if (packet.NetworkBehaviourType == "PlayerController" && packet.ObjectId is int inspectOid)
            {
                SetName(inspectOid, discovered.DisplayName, markPlayer: true);
            }
            else if (LocalDisplayName is null)
            {
                // Mid-session bootstrap: first valid CharacterData is almost always the local save.
                SetLocalIdentity(packet.ObjectId, discovered.DisplayName);
            }
            else if (packet.ObjectId is int otherOid
                     && !string.Equals(discovered.DisplayName, LocalDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                SetName(otherOid, discovered.DisplayName, markPlayer: true);
            }
        }

        // Any sync blob may carry VisualData — scan aggressively.
        if (packet.PacketName == FishNetPacketNames.SyncType && packet.ObjectId is int syncOid)
        {
            var payload = packet.SyncPayload ?? packet.Payload;
            string? name = packet.Fields.TryGetValue("displayName", out var dn) ? dn?.ToString() : null;
            var identity = ActorIdentityCodec.TryDecodeVisualData(payload)
                ?? ActorIdentityCodec.TryScanSpawnIdentity(payload);
            name ??= identity?.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
                SetName(syncOid, name, markPlayer: true);
            if (identity?.Archetype is int arch)
                SetMeta(syncOid, archetypeId: arch);
        }

        if (packet.PacketName == FishNetPacketNames.ObjectSpawn && packet.ObjectId is int spawnOid)
        {
            var name = packet.Fields.TryGetValue("displayName", out var sn) ? sn?.ToString() : null;
            var identity = ActorIdentityCodec.TryScanSpawnIdentity(packet.Raw);
            name ??= identity?.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
                SetName(spawnOid, name, markPlayer: true);
            if (identity?.Archetype is int arch)
                SetMeta(spawnOid, archetypeId: arch);
            if (packet.OwnerConnectionId is int spawnOwner)
                NoteOwnership(spawnOid, spawnOwner);
        }

        if (packet.PacketName == FishNetPacketNames.ObjectDespawn && packet.ObjectId is int despawnOid)
            Remove(despawnOid);

        // Local client's objects emit serverRpc — stamp remembered local name onto new object ids.
        if (packet.PacketName == FishNetPacketNames.ServerRpc
            && packet.ObjectId is int serverOid
            && _localDisplayName is not null)
        {
            SetLocalIdentity(serverOid, _localDisplayName);
        }

        if (packet.PacketName == FishNetPacketNames.Authenticated)
        {
            ClearObjectsKeepLocalName();
        }
    }

    public void ClearObjectsKeepLocalName()
    {
        lock (_gate)
        {
            var keepName = _localDisplayName;
            var keepArch = _localArchetypeId;
            var keepClass = _localClassName;
            var keepLevel = _localLevel;
            // Object ids are invalidated across map/auth — keep local identity only.
            _names.Clear();
            _meta.Clear();
            _playerIds.Clear();
            _ownerByObject.Clear();
            _objectsByOwner.Clear();
            _localDisplayName = keepName;
            _localArchetypeId = keepArch;
            _localClassName = keepClass;
            _localLevel = keepLevel;
            _localActorId = null;
        }
        PlayersChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _names.Clear();
            _meta.Clear();
            _playerIds.Clear();
            _ownerByObject.Clear();
            _objectsByOwner.Clear();
            _localActorId = null;
        }
        PlayersChanged?.Invoke();
    }

    private string ResolveUnlocked(int actorId)
    {
        if (actorId > 0 && _localActorId == actorId)
            return _localDisplayName ?? "You";
        if (_names.TryGetValue(actorId, out var name))
            return name;
        return actorId > 0 ? $"Actor {actorId}" : "Unknown";
    }

    private void PropagateOwnerNameUnlocked(int actorId, string name, List<(int Id, string Name)> notify)
    {
        if (!_ownerByObject.TryGetValue(actorId, out var owner)) return;
        if (!_objectsByOwner.TryGetValue(owner, out var siblings)) return;
        foreach (var sib in siblings)
        {
            if (sib == actorId) continue;
            // Never clobber another player's resolved nameplate.
            if (_names.TryGetValue(sib, out var cur) && IsPlausiblePlayerName(cur) && cur != name)
                continue;
            if (!_names.TryGetValue(sib, out cur) || cur.StartsWith("Actor ", StringComparison.Ordinal) || cur != name)
            {
                _names[sib] = name;
                _playerIds.Add(sib);
                notify.Add((sib, name));
            }
        }
    }

    private static bool IsPlausiblePlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.StartsWith("Actor ", StringComparison.Ordinal)) return false;
        if (name.StartsWith("Object ", StringComparison.Ordinal)) return false;
        if (name.Equals("Monster", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;
        return name.Length is >= 2 and <= 32;
    }
}
