# Plugin author guide

## Quick start

```powershell
dotnet new classlib -n MySpiritValePlugin -f net8.0
cd MySpiritValePlugin
dotnet add reference X:\projects\SpiritVale-Overlay\src\SpiritVale.Overlay.Api\SpiritVale.Overlay.Api.csproj
```

In the csproj, keep the Api reference non-private so the host shares one copy:

```xml
<ProjectReference Include="...\SpiritVale.Overlay.Api.csproj">
  <Private>false</Private>
</ProjectReference>
```

Implement `ISpiritValePlugin`, build, then either:

1. **Dev drop** — put the folder under the host's build `Plugins\` (imported into AppData on launch), or directly into `%AppData%\SpiritValeOverlay\plugins\<id>\`
2. **Catalog** — publish a zip to spiritvalemods.com; users install from the host **Catalog** tab

## Manager behaviour

| Action | Effect |
|--------|--------|
| Enable | Load DLL in a collectible ALC, call `OnLoad` |
| Disable | Call `OnUnload`, unload ALC; files stay on disk |
| Install | Download zip → verify SHA-256 → extract → enable |
| Update | Same as install; keeps prior enabled state |
| Uninstall | Disable + delete plugin folder + registry row |

Update detection matches the BepInEx manager: local zip/plugin `sha256` ≠ catalog `sha256`.

## Events vs snapshots

```csharp
api.Combat.Damage += OnDamage;
var rows = api.Combat.DpsRows;
var members = api.Party.Members;
var me = api.Character.Local;
```

## Drawing

Use `IOverlayUi` only — do not reference ImGui.NET from plugins unless you ship those native dependencies yourself.

## Isolation

Each plugin loads in a collectible `AssemblyLoadContext`. If `Draw` throws, the host stops drawing that plugin and keeps running.
