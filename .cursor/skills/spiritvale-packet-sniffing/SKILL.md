---
name: spiritvale-packet-sniffing
description: >-
  SpiritVale passive Npcap capture and FishNet/LiteNetLib decode for the external
  C# overlay. Use when fixing capture, ApplyDamage_C/DPS, RpcLink resolution,
  ObjectSpawn, actor names, rpc-map codecs, or anything involving SpiritVale UDP
  packet sniffing. Canonical reference:
  X:\projects\spirit-vale-tools--kar-mi-spirit-vale-tools-capture-1.7.0
---

# SpiritVale packet sniffing (overlay)

Passive decode-only. Never inject, modify, or send packets. Never BepInEx.

## Reference tree

Primary: `X:\projects\spirit-vale-tools--kar-mi-spirit-vale-tools-capture-1.7.0`

| Path | Use |
|------|-----|
| `docs/packet/packet-capture-workflow.md` | Npcap open mode, adapters, troubleshooting |
| `docs/packet/packet-decoding.md` | LiteNetLib → FishNet pipeline |
| `docs/combat/combat-packets.md` | Damage/heal/death/status dispatch |
| `packages/capture/src/fishnet/*` | Wire parsers to port |
| `packages/combat/src/actor-directory.ts` | Nameplate → AttackerId attribution |

Overlay live code: `SpiritVale-Overlay/src/SpiritVale.Overlay.Capture` + `Domain`.

## Pipeline (must stay aligned)

```
Npcap UDP → LiteNetLib leaves → FishNet session (tick + messages)
  → RpcLink resolve via ObjectSpawn registrations
  → field codecs from rpc-map
  → Domain trackers (combat / character / world)
```

- Channeled LiteNetLib = reliable FishNet; unreliable = no packed length.
- One UDP datagram can hold multiple FishNet messages; never set `stop=true` unless the boundary is unknown.
- Malformed leaf → warning, continue. Do not tear down capture.

## Capture rules (Npcap)

- Prefer **non-promiscuous** when possible (reference default). Overlay may fall back if open fails.
- Auto-select adapter by default route (`route PRINT 0.0.0.0` interface IP), not index 0.
- Persist preferred adapter by description name across restarts.
- Admin-only Npcap: warn; try open anyway.
- Never hard-crash on locked plugin DLL copy during import.

## ObjectSpawn → RpcLink (P0 for combat)

Most combat arrives as `rpcLink` (id ≥ 22). Without spawn registrations, `LinkResolved=false` and `RpcName` stays null → **zero DPS**.

Spawn layout (after packet id): flags → optional nested → packed objectId → u16 collectionId → packed → packed owner → transform flags → pos/rot/scale → sceneId|prefabId → u32 customPayloadLen → custom → **u16 linksLen → link bytes** → u32 syncLen → sync.

Link segment (repeat until end):

```
u8 componentIndex
u16 count
repeat count:
  u16 linkId (≥ 22)
  u16 rpcHash
  u16 packetKind  (9=observersRpc, 10=targetRpc, 16=reconcile)
```

Register into `ConnectionState.Links[linkId]`. Bind behaviour type via rpc-map fingerprint when unambiguous.

**Do not** clear all links on every `authenticated` if that wipes mid-session state without respawn; prefer disconnect-only clear, or quarantine.

## ApplyDamage_C / Death_C

Behaviour: `HealthComponent`. Hash 0 / 2, `observersRpc`.

- `objectId` = **victim**
- `dmg.AttackerId` = **dealer** (may be −1 unsourced)

`Damage` field order (all packedInt32 unless noted):

1. Team 2. Value 3. Type 4. Hit 5. Hits  
6. DamageSourceId (`stringUtf8Packed`, length −1 = null)  
7. AttackerId  
8. IsClone (bool) 9. IsSummon (bool)  
10. Element 11. WeaponType 12. Range  

`ApplyDamage_C` then: `position` vector3, `origin` vector3.  
`Death_C`: Damage only (lethal blow also on ApplyDamage — avoid double-count).

### DPS filter (critical)

```csharp
// WRONG — invents enemy team when decode fails → drops ALL damage
var team = decoded?.Team ?? -1;
if (team != 0) return;

// RIGHT — only exclude when team was decoded and is non-player
int? team = decoded?.Team;
if (team is int t && t != 0) return;
if (value <= 0 || attackerId <= 0 || attackerId == targetId) return;
```

Player team is `0`. Missing team ≠ reject.

## RPC map / lookup

- Embed codecs in `rpc-map.json` (`parameters` / `fields` / `codec`). Bare method names are not enough for map-driven decode.
- Shared hash collisions (e.g. observersRpc hash 0 = `ApplyDamage_C` vs `Attack_C`): disambiguate by **payload shape** (exact codec consume), never first-match.
- Refuse naming an RPC that declares **empty parameters** but carries bytes (FullHeal_C false positives).
- Do not bind `NetworkBehaviourType` from a refused match.

## Actor names

- Local name: `LoadCharacter_T` / `CharacterCallback_T` CharacterData (GUID + display name). **Not resent on every map change** — local spawn omits VisualData.
- Nearby: PlayerController VisualData sync index **5** (`Appearance.DisplayName` + Archetype) on spawn/sync after zoning.
- After map/auth: clear object ids but **keep** remembered local display name; re-bind via outbound `serverRpc` object ids.
- CharacterData string caps are **byte** counts (80/64), not char counts — tight caps reject CJK/Hangul names.
- Persist local name in `registry.json` (`LocalCharacterName`) once learned; seed on next launch.
- Combat `AttackerId` is often a sibling object; propagate names by **ownerConnectionId**.

## Overlay safety / product rules

- External process only (ClickableTransparentOverlay + SharpPcap).
- Plugin API stays on `SpiritVale.Overlay.Api`; decode details stay in Capture/Domain.
- Tab radial: `WS_EX_NOACTIVATE`, restore game focus on release — never steal keyboard focus from SpiritVale.

## When debugging “no damage”

1. CAPTURE tab: UDP / LiteNet / FishNet counters moving?
2. Packet feed: any `ApplyDamage_C` or unresolved `rpcLink`?
3. If only unresolved rpcLinks → spawn link registration broken.
4. If named ApplyDamage but meter empty → team filter / AttackerId ≤ 0.
5. Compare against reference tests in `builtin-maps.test.ts` (Damage writer layout).

## Do not

- Bundle or redistribute Npcap.
- Guess ObjectSpawn boundaries when parse fails (opaque remainder, stop).
- Credit victim `objectId` as the damager.
- Show raw `Actor {id}` in the DPS UI when a real name exists elsewhere on the same owner.
