---
name: spiritvale-overlay-plugins
description: >-
  Author and extend SpiritVale Overlay plugins (ISpiritValePlugin, IOverlayUi,
  combat/party HUD, options, sprites). Use when creating overlay plugins, DPS
  meters, party HUDs, SamplePlugin changes, or referencing SpiritVale sprite /
  Il2CPP dumps for icons and class/skill metadata. Repo:
  X:\projects\SpiritVale-Overlay
---

# SpiritVale Overlay plugins

External C# overlay plugins — **not** BepInEx. Passive against `SpiritVale.Overlay.Api` only. Host draws via thin `IOverlayUi` (ImGui stays out of the plugin surface).

## Paths

| What | Path |
|------|------|
| Overlay repo | `X:\projects\SpiritVale-Overlay` |
| Public API | `src/SpiritVale.Overlay.Api/` |
| Sample plugin | `src/SpiritVale.Overlay.SamplePlugin/` |
| Host / load | `src/SpiritVale.Overlay.Host/` |
| Run (Release) | `src/SpiritVale.Overlay.Host/bin/Release/net8.0-windows/SpiritVale.Overlay.Host.exe` |
| AppData plugins | `%AppData%\SpiritValeOverlay\plugins\` |
| Registry | `%AppData%\SpiritValeOverlay\registry.json` |
| Packet sniff skill | `spiritvale-packet-sniffing` (capture/codecs — not needed for pure UI plugins) |

### Dumps (reference / sprites)

| What | Path |
|------|------|
| Il2CPP / asset dump root | `X:\projects\SpiritVale Development - Il2CPP dump\development` |
| Sprite PNGs (~3056 flat) | `…\development\sprite dump` |
| Naming | `{spriteId}-sharedassets0.assets-{pathId}.png` |
| Class icons | stems `class-warrior`, `class-mage`, … or compact job name (`dragonknight`) |
| Skill icons | catalog `spriteId` → stem (e.g. `Barbarian20`, `Skills_096`) — **not** skill `id` |
| Capture / combat docs | `X:\projects\spirit-vale-tools--kar-mi-spirit-vale-tools-capture-1.7.0` |
| Skill catalog (tools) | `packages/skills/src/definitions/skills.ts` |
| Overlay skill JSON | `src/SpiritVale.Overlay.Domain/Resources/skill-catalog.json` |

Host CAPTURE tab → **Sprites** sets `SpriteDumpPath` (persisted in `registry.json`). Default often points at the sprite dump folder above.

## Plugin contract

Implement `ISpiritValePlugin`:

```csharp
string Id { get; }           // stable, e.g. "my.hud"
string Name { get; }
string? Author => null;
string? Version => null;

void OnLoad(ISpiritValeApi api);
void OnUnload();
void Draw(IOverlayUi ui);

// Optional settings (host SETTINGS modal)
IReadOnlyList<PluginOptionDefinition> OptionDefinitions => …;
void ApplyOptions(IReadOnlyDictionary<string, string> values);
IReadOnlyDictionary<string, string> ExportOptions();
void OnOptionHotkey(string key);  // PluginOptionKind.Hotkey
```

**csproj**: `net8.0`, reference Api with `<Private>false</Private>` so the host’s Api assembly is used.

```xml
<ProjectReference Include="..\SpiritVale.Overlay.Api\SpiritVale.Overlay.Api.csproj">
  <Private>false</Private>
</ProjectReference>
```

Copy output next to host `Plugins/{FolderId}/YourPlugin.dll`. Dev sync: host copies from build `Plugins/` into AppData **before** load (`PluginManager.SyncDevBuildPlugins`). Stale AppData DLL = common “my changes didn’t apply” bug — rebuild Release and relaunch host.

## API surface (`ISpiritValeApi`)

| Member | Use |
|--------|-----|
| `Combat` | `Leaderboard`, `CurrentEncounter`, `GetSkillBreakdown(actorId, displayName?)`, `ResetEncounter()`, Damage/Heal/Death events |
| `Character` | `Local` snapshot (name, level, class when known) |
| `Party` | Members / HP (best-effort) |
| `World` | Nearby entities |
| `Protocol` | Decoded FishNet events (advanced) |
| `Sprites` | `ResolveSkill`, `ResolveClassIconPath`, class names |

### Combat / encounter rules (host Domain)

- Encounter **starts only when local player deals damage**.
- Idle **15s** with no damage → encounter clears; needs your hit again.
- `DpsRow`: damage, dps, share, archetype, level, hits/crits/deaths.
- Skill breakdown: pass **sticky display name** as well as actor id (ids recycle on death/respawn).
- Class icons: VisualData archetype when present; else damage-weighted skill → class inference from skill catalog.

### Passive (`IOverlayUi`)

- Always `EndWindow()` after `BeginWindow`.
- Prefer `TextUnformatted` for strings with `%` — colored text is safe (style + unformatted); raw ImGui printf would eat `%`.
- `ClassIcon` / `Sprite`: always reserve space (host uses `Dummy` when missing) before `SameLine`, or rows merge on one line.
- `ProgressBar(fraction, r,g,b, height)` for share/HP bars.
- Hotkeys: declare `PluginOptionKind.Hotkey`; host calls `OnOptionHotkey(key)`.

## Scaffold checklist

1. New class library → reference Api (`Private=false`).
2. Implement `ISpiritValePlugin` (unique `Id`).
3. Subscribe in `OnLoad`, unsubscribe in `OnUnload`.
4. Draw compact windows; use options for toggles.
5. Build Release → ensure DLL under host `Plugins/` and AppData after relaunch.
6. Test with capture running and SpiritVale focused (overlay hides on alt-tab).

## Do / don’t

| Do | Don’t |
|----|-------|
| Api assembly only | Reference Host / ImGuiNET from plugins |
| Passive-encoded options | Invent packet inject / send |
| Sticky names for skill panels | Key UI solely by FishNet object id |
| Sprite dump + catalog `spriteId` | Assume skill id == PNG stem |
| Follow SampleHud patterns | BepInEx / game process injection |

## Related skills

- `spiritvale-packet-sniffing` — when changing capture, Damage codecs, RpcLink, names.
- `spiritvale-mod-creation` — BepInEx IL2CPP mods (different stack; not this overlay).

## Canonical sample

Read `src/SpiritVale.Overlay.SamplePlugin/SampleHudPlugin.cs` for DPS meter, options, skill panel, class icons, share bars, and local-player highlight.
