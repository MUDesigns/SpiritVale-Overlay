# SpiritVale Overlay + Plugin Manager

External Windows overlay for SpiritVale. It **passively** captures game UDP traffic with **Npcap**, decodes LiteNetLib / FishNet messages, and loads **DLL plugins** through a built-in manager (enable/disable, install, update from [spiritvalemods.com](https://www.spiritvalemods.com)).

This is a **separate process**. It does not inject into the game, does not use BepInEx, and never sends or modifies packets.

## Requirements

- Windows 10/11
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (prebuilt) or [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (from source)
- [Npcap](https://npcap.com/#download) with **WinPcap API-compatible mode** enabled
- SpiritVale running (`SpiritVale.exe`) for live data

## Prebuilt plugin manager

A Release `win-x64` publish of the overlay host (plugin manager) ships in [`dist/win-x64`](dist/win-x64), including the sample DPS plugin under `Plugins\`.

```powershell
cd dist\win-x64
.\SpiritVale.Overlay.Host.exe
```

## Build & run from source

```powershell
cd X:\projects\SpiritVale-Overlay
dotnet build SpiritVale.Overlay.slnx -c Release
dotnet run --project src\SpiritVale.Overlay.Host\SpiritVale.Overlay.Host.csproj -c Release
```

To refresh the committed publish output:

```powershell
dotnet publish src\SpiritVale.Overlay.Host\SpiritVale.Overlay.Host.csproj -c Release -r win-x64 --self-contained false -o dist\win-x64
```

### Hotkeys

| Key | Action |
|-----|--------|
| Insert | Toggle manager window |
| Home | Force click-through |

## Plugin manager

The host is the overlay **and** the plugin manager (same role as the old BepInEx mod manager, but for overlay DLLs).

| Path | Purpose |
|------|---------|
| `%AppData%\SpiritValeOverlay\plugins\` | Installed plugin folders |
| `%AppData%\SpiritValeOverlay\library\` | Cached catalog zips |
| `%AppData%\SpiritValeOverlay\registry.json` | Enabled state, catalog id/version/sha256 |

**Installed tab**

- Enable / disable (loads or unloads the DLL via `AssemblyLoadContext`)
- Check updates / update all (SHA-256 vs catalog, same as the Tauri manager)
- Uninstall

**Catalog tab**

- `GET https://www.spiritvalemods.com/api/catalog`
- Install / reinstall from tracked download URLs
- Verifies zip SHA-256 before extract

Catalog entries are the same public JSON shape the BepInEx manager uses. When the site hosts overlay plugin zips (instead of BepInEx mods), they install into the overlay plugin directory — not into the game folder.

### Overlay plugin zip layout

Any of these work:

```
MyPlugin.dll
MyPlugin/MyPlugin.dll
Plugins/MyPlugin/MyPlugin.dll
```

`BepInEx/` entries are ignored.

## Architecture

```
Npcap → SharpPcap → LiteNetLib → FishNet → Domain trackers → ISpiritValeApi → Plugin DLLs → ImGui overlay
spiritvalemods.com catalog → download zip → %AppData%\SpiritValeOverlay\plugins\
```

| Project | Role |
|---------|------|
| `SpiritVale.Overlay.Api` | **Only** assembly plugin authors reference |
| `SpiritVale.Overlay.Capture` | Npcap / SharpPcap + protocol decode |
| `SpiritVale.Overlay.Domain` | Combat / party / character / world trackers |
| `SpiritVale.Overlay.Host` | Manager UI + catalog client + plugin loader |
| `SpiritVale.Overlay.SamplePlugin` | Example DPS + party windows |

## Writing a plugin

See [docs/plugin-author.md](docs/plugin-author.md).

## License / Npcap

Npcap is licensed separately and is **not** redistributed with this project. Users must install it themselves.
