# SpiritVale Plugin Manager

External Windows overlay for SpiritVale. It **passively** captures game UDP traffic with **Npcap**, decodes LiteNetLib / FishNet messages, and loads **DLL plugins** (listed as **mods** on [spiritvalemods.com](https://www.spiritvalemods.com)).

This is a **separate process**. It does not inject into the game, does not use BepInEx, and never sends or modifies packets.

**Plugins are not bundled.** A fresh install has no Cooldown Manager, Nameplate, or DPS Meter — install those from the site catalog or download the zip and use **Import zip**.

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- [Npcap](https://npcap.com/#download) with **WinPcap API-compatible mode** enabled
- SpiritVale running (`SpiritVale.exe`) for live data

## Install

Prefer the **Plugin Manager** installer from [spiritvalemods.com](https://www.spiritvalemods.com) (NSIS current-user setup). Portable zip and GitHub releases are also available.

```powershell
# Portable: extract anywhere, then
.\SpiritVale.Overlay.Host.exe
```

The published `dist\win-x64` folder is host + sprites only (no plugin DLLs).

## Build & run from source

```powershell
cd X:\projects\SpiritVale-Overlay
dotnet build SpiritVale.Overlay.slnx -c Release
dotnet run --project src\SpiritVale.Overlay.Host\SpiritVale.Overlay.Host.csproj -c Release
```

Debug builds may sync a local `Plugins\` folder into AppData for plugin authors. Release / publish never ships plugins.

To refresh the committed publish output:

```powershell
dotnet publish src\SpiritVale.Overlay.Host\SpiritVale.Overlay.Host.csproj -c Release -r win-x64 --self-contained false -o dist\win-x64
# then build the NSIS installer (see tools/plugin-manager.nsi)
```

### Hotkeys

| Key | Action |
|-----|--------|
| F2 | Toggle manager window |
| F3 | Force click-through |
| F4 | Toggle follow SpiritVale window |
| Hold Tab | Plugin radial menu |

## Plugin manager

The host is the overlay **and** the plugin manager.

| Path | Purpose |
|------|---------|
| `%AppData%\SpiritValeOverlay\plugins\` | Installed plugin folders |
| `%AppData%\SpiritValeOverlay\library\` | Cached catalog zips |
| `%AppData%\SpiritValeOverlay\registry.json` | Enabled state, catalog id/version/sha256 |

**Installed tab**

- Enable / disable (loads or unloads the DLL via `AssemblyLoadContext`)
- Import zip (manual download from the site)
- Check updates / update all (SHA-256 vs catalog)
- Uninstall

**Catalog tab**

- `GET https://www.spiritvalemods.com/api/catalog`
- Install / reinstall from tracked download URLs
- Verifies zip SHA-256 before extract

Deep link: `spiritvale://install/{mod-id}` opens the Plugin Manager and installs that catalog mod.

The manager also checks `GET /api/app` for Plugin Manager updates (NSIS installer preferred).

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
| `SpiritVale.Overlay.Host` | Plugin Manager UI + catalog client + plugin loader |
| `SpiritVale.Overlay.SamplePlugin` | DPS Meter (catalog mod; not shipped with the host) |

## Writing a plugin

See [docs/plugin-author.md](docs/plugin-author.md).

## License / Npcap

Npcap is licensed separately and is **not** redistributed with this project. Users must install it themselves.
