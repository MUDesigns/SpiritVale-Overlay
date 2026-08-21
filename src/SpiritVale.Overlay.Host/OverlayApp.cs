using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using SpiritVale.Overlay.Api;
using SpiritVale.Overlay.Capture;
using SpiritVale.Overlay.Domain;

namespace SpiritVale.Overlay.Host;

internal sealed class OverlayApp : ClickableTransparentOverlay.Overlay
{
    private readonly SpiritValeApi _api;
    private readonly PluginManager _plugins;
    private readonly ImGuiOverlayUi _ui;
    private readonly Dictionary<string, IntPtr> _spriteTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recentPackets = new();
    private readonly object _packetGate = new();

    private bool _showHost = true;
    private bool _forceClickThrough;
    private bool _followGame = true;
    private bool _autoStartAttempted;
    private string _status = "Starting…";
    private int _selectedDevice;
    private string[] _devices = Array.Empty<string>();
    private string? _preferredDevice;
    private string _spriteDumpPath = "";
    private float _watchdogTimer;
    private bool _userPickedDevice;
    private int _tab;
    private string _catalogFilter = "";
    private string _managerMessage = "";
    private Task? _busyTask;
    private bool _f2Down, _f3Down, _f4Down;
    private readonly Dictionary<string, bool> _pluginHotkeyEdges = new(StringComparer.OrdinalIgnoreCase);
    private string? _settingsPluginId;
    private string _hotkeyCaptureKey = "";
    private bool _capturingHotkey;
    private readonly PluginRadialMenu _radial = new();
    private bool _radialClickThroughOverride;
    private bool _windowIconApplied;
    private bool _overlayHiddenForGame;

    public OverlayApp(SpiritValeApi api, PluginManager plugins)
        : base("SpiritVale Overlay", DPIAware: true, GetPrimaryScreenWidth(), GetPrimaryScreenHeight())
    {
        _api = api;
        _plugins = plugins;
        _ui = new ImGuiOverlayUi(ResolveSpriteTexture);
        _api.Protocol.Packet += OnPacket;
        _api.UdpSeen += OnUdpSeen;
        _api.CaptureStateChanged += running => _status = running ? "Capturing" : "Idle";
        FPSLimit = 60;
    }

    protected override Task PostInitialized()
    {
        HudTheme.Apply();
        HudTheme.TryLoadFonts(this);
        OverlayIcons.EnsureLoaded(this);
        if (window is not null)
            OverlayIcons.ApplyWindowIcon(window.Handle);

        var settings = PluginRegistryStore.Load();
        _preferredDevice = settings.PreferredCaptureDevice;
        _spriteDumpPath = settings.SpriteDumpPath
            ?? @"X:\projects\SpiritVale Development - Il2CPP dump\development\sprite dump";
        _api.ConfigureSpriteDump(_spriteDumpPath);
        if (!string.IsNullOrWhiteSpace(settings.LocalCharacterName))
            _api.SeedLocalPlayerName(settings.LocalCharacterName);

        _api.Character.CharacterChanged += PersistLocalCharacterName;

        _devices = PacketCaptureService.ListDevices().ToArray();
        _selectedDevice = ResolveDeviceIndex(_preferredDevice, _devices);

        if (!_autoStartAttempted && settings.AutoStartCapture)
        {
            _autoStartAttempted = true;
            // Do NOT force index 0 — use saved adapter name / auto-select.
            TryStartCapture(userSelected: false);
        }
        RunBusy("Refreshing catalog…", () => _plugins.RefreshCatalogAsync());
        return Task.CompletedTask;
    }

    protected override void Render()
    {
        // Re-apply each frame so late style resets can't wash the HUD out.
        HudTheme.Apply();
        OverlayIcons.EnsureLoaded(this);
        if (window is not null && !_windowIconApplied)
        {
            OverlayIcons.ApplyWindowIcon(window.Handle);
            _windowIconApplied = true;
        }

        // Pin overlay to SpiritVale's client area; hide when the game is gone/minimized.
        SyncOverlayToSpiritVale();

        HandleHotkeys();

        var tabHeld = (GetAsyncKeyState(0x09) & 0x8000) != 0; // VK_TAB
        // Don't let ImGui eat Tab / arrow nav while we use Tab for the radial.
        var io = ImGui.GetIO();
        io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;

        var radialOpen = _radial.UpdateAndDraw(_plugins, tabHeld);

        // Radial needs mouse hits without activating/focus-stealing the overlay
        // (focus steal causes SpiritVale to miss Tab key-up and "lock" input).
        if (window is not null)
        {
            if (radialOpen)
            {
                if (!_radialClickThroughOverride)
                {
                    _radialClickThroughOverride = true;
                    SetWindowClickThrough(window.Handle, clickThrough: false, noActivate: true);
                }
            }
            else if (_radialClickThroughOverride)
            {
                _radialClickThroughOverride = false;
                SetWindowClickThrough(window.Handle, _forceClickThrough, noActivate: true);
                FocusSpiritVale();
            }
            else if (_forceClickThrough)
            {
                SetWindowClickThrough(window.Handle, true, noActivate: true);
            }
            else
            {
                SetWindowClickThrough(window.Handle, false, noActivate: true);
            }
        }

        RunCaptureWatchdog(ImGui.GetIO().DeltaTime);

        if (_showHost)
            DrawHostWindow();

        _plugins.DrawEnabled(_ui);
    }

    private void DrawHostWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(560, 580), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.55f);
        if (!OverlayIcons.BeginBrandedWindow("SPIRITVALE  //  OVERLAY", ref _showHost))
        {
            ImGui.End();
            return;
        }

        HudTheme.AccentRail();
        DrawBrandHeader();

        if (ImGui.BeginTabBar("manager_tabs", ImGuiTabBarFlags.FittingPolicyResizeDown))
        {
            if (ImGui.BeginTabItem("CAPTURE"))
            {
                _tab = 0;
                DrawCaptureTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("PLUGINS"))
            {
                _tab = 1;
                DrawInstalledTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("CATALOG"))
            {
                _tab = 2;
                DrawCatalogTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.Spacing();
        DrawFooterStatus();
        ImGui.End();
        _ = _tab;
    }

    private void DrawBrandHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, HudTheme.Ember);
        ImGui.TextUnformatted("SPIRITVALE");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.TextColored(HudTheme.TextMuted, "OVERLAY MANAGER");

        ImGui.SameLine(ImGui.GetWindowWidth() - 280);
        if (_forceClickThrough)
        {
            HudTheme.StatusChip("CLICK-THRU", HudTheme.Warn);
            ImGui.SameLine();
        }
        if (_api.IsCapturing)
            HudTheme.StatusChip("LIVE", HudTheme.Live);
        else
            HudTheme.StatusChip("IDLE", HudTheme.TextMuted);

        ImGui.TextColored(HudTheme.TextMuted, _api.CaptureStatus ?? _status);
        ImGui.Spacing();
    }

    private void DrawFooterStatus()
    {
        ImGui.Separator();
        var msg = !string.IsNullOrEmpty(_plugins.StatusMessage) ? _plugins.StatusMessage : _managerMessage;
        if (_plugins.IsBusy || _busyTask is { IsCompleted: false })
        {
            HudTheme.StatusChip("SYNC", HudTheme.Warn);
            ImGui.SameLine();
        }
        if (!string.IsNullOrEmpty(msg))
            ImGui.TextColored(HudTheme.TextMuted, msg);
        ImGui.TextColored(HudTheme.TextMuted, "F2 manager   ·   F3 click-through   ·   F4 follow   ·   hold Tab plugin radial");
    }

    private void DrawCaptureTab()
    {
        HudTheme.SectionLabel("Network");

        var npcap = PacketCaptureService.GetNpcapStatus();
        if (!npcap.Available)
        {
            HudTheme.BeginCard();
            ImGui.TextColored(HudTheme.Danger, "NPCAP MISSING");
            ImGui.TextWrapped(npcap.Message ?? "Npcap not found.");
            ImGui.TextWrapped("Install with WinPcap API-compatible mode from npcap.com");
            HudTheme.EndCard();
        }
        else
        {
            HudTheme.BeginCard();
            ImGui.TextColored(HudTheme.Live, "NPCAP READY");
            ImGui.SameLine();
            ImGui.TextColored(HudTheme.TextMuted, $"{npcap.DeviceCount} adapters");
            if (npcap.AdminOnly && !npcap.Elevated)
            {
                ImGui.TextColored(HudTheme.Danger, "Npcap is Admin-only — restart this app as Administrator.");
            }
            else if (!npcap.Elevated)
            {
                ImGui.TextColored(HudTheme.Warn, "Not elevated. If you see 0 packets, run as Administrator.");
            }

            if (_devices.Length > 0)
            {
                if (ImGui.Combo("Adapter", ref _selectedDevice, _devices, _devices.Length))
                {
                    _userPickedDevice = true;
                    _preferredDevice = _devices[_selectedDevice];
                    SaveCaptureSettings();
                    TryStartCapture(userSelected: true);
                }
            }

            var stats = _api.CaptureStats;
            ImGui.TextColored(HudTheme.TextMuted,
                $"UDP {stats.UdpPackets:N0}  ·  LiteNet {stats.LiteNetLeaves:N0}  ·  FishNet {stats.FishNetPackets:N0}  ·  err {stats.DecodeErrors:N0}");
            if (stats.LastUdpSummary is not null)
                ImGui.TextColored(HudTheme.TextMuted, $"last: {stats.LastUdpSummary}");
            if (stats.ActiveDevice is not null)
                ImGui.TextColored(HudTheme.TextMuted, $"device: {stats.ActiveDevice}");
            if (stats.LastError is not null)
                ImGui.TextColored(HudTheme.Danger, stats.LastError);

            if (!_api.IsCapturing)
            {
                if (HudTheme.AccentButton("START CAPTURE", new Vector2(160, 0)))
                    TryStartCapture(userSelected: _userPickedDevice);
            }
            else if (ImGui.Button("STOP CAPTURE", new Vector2(160, 0)))
            {
                _api.StopCapture();
            }
            ImGui.SameLine();
            if (ImGui.Button("RESTART"))
                TryStartCapture(userSelected: _userPickedDevice);
            HudTheme.EndCard();
        }

        HudTheme.SectionLabel("Overlay");
        HudTheme.BeginCard();
        ImGui.Checkbox("Follow SpiritVale window", ref _followGame);
        ImGui.TextColored(HudTheme.TextMuted,
            "Matches the game window; hides when SpiritVale is minimized or not focused (alt-tab).");
        if (ImGui.Checkbox("Force click-through", ref _forceClickThrough) && window is not null)
            SetWindowClickThrough(window.Handle, _forceClickThrough, noActivate: true);
        if (_forceClickThrough)
            ImGui.TextColored(HudTheme.Warn, "Click-through ON — press F3 to disable (mouse clicks pass through).");
        HudTheme.EndCard();

        HudTheme.SectionLabel("Sprites");
        HudTheme.BeginCard();
        ImGui.InputText("Sprite dump folder", ref _spriteDumpPath, 512);
        ImGui.TextColored(HudTheme.TextMuted, "Class / skill icons from the Il2CPP sprite dump.");
        if (ImGui.Button("APPLY SPRITES"))
        {
            _api.ConfigureSpriteDump(_spriteDumpPath);
            _spriteTextures.Clear();
            SaveCaptureSettings();
            _managerMessage = Directory.Exists(_spriteDumpPath)
                ? "Sprite dump applied."
                : "Sprite dump path not found.";
        }
        HudTheme.EndCard();

        if (ImGui.CollapsingHeader("PACKET FEED", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.BeginChild("packet_feed", new Vector2(0, 200), ImGuiChildFlags.Borders);
            lock (_packetGate)
            {
                if (_recentPackets.Count == 0)
                    ImGui.TextColored(HudTheme.TextMuted, "No packets yet. Join a world in SpiritVale, then confirm UDP count above moves.");
                for (var i = _recentPackets.Count - 1; i >= 0 && i >= _recentPackets.Count - 60; i--)
                    ImGui.TextColored(HudTheme.TextMuted, _recentPackets[i]);
            }
            ImGui.EndChild();
        }
    }

    private void DrawInstalledTab()
    {
        var installed = _plugins.Installed;
        HudTheme.SectionLabel("Loadout");
        ImGui.TextColored(HudTheme.TextMuted,
            $"{installed.Count} installed  ·  {installed.Count(p => p.Enabled)} armed");
        ImGui.TextColored(HudTheme.TextMuted, OverlayPaths.PluginsDir);

        if (ImGui.Button("CHECK UPDATES"))
            RunBusy("Checking updates…", () => _plugins.CheckUpdatesAsync());
        ImGui.SameLine();
        if (HudTheme.AccentButton("UPDATE ALL") && !_plugins.IsBusy)
        {
            var due = installed.Where(p => p.UpdateAvailable).Select(p => p.Id).ToArray();
            RunBusy("Updating…", async () =>
            {
                foreach (var id in due)
                    await _plugins.UpdateFromCatalogAsync(id);
            });
        }

        ImGui.Spacing();
        foreach (var plugin in installed)
        {
            ImGui.PushID(plugin.Id);
            HudTheme.BeginCard();

            var enabled = plugin.Enabled;
            if (ImGui.Checkbox($"##en", ref enabled))
                _plugins.SetEnabled(plugin.Id, enabled);
            ImGui.SameLine();
            ImGui.TextUnformatted(plugin.DisplayName.ToUpperInvariant());
            if (plugin.UpdateAvailable)
            {
                ImGui.SameLine();
                HudTheme.StatusChip("UPDATE", HudTheme.Warn);
            }
            else if (plugin.Enabled)
            {
                ImGui.SameLine();
                HudTheme.StatusChip(plugin.HudVisible ? "HUD" : "HIDDEN", plugin.HudVisible ? HudTheme.Live : HudTheme.Warn);
            }

            var meta = plugin.CatalogVersion is not null ? $"v{plugin.CatalogVersion}" : "LOCAL BUILD";
            if (plugin.CatalogId is not null) meta += $"  ·  {plugin.CatalogId}";
            ImGui.TextColored(HudTheme.TextMuted, meta);

            if (plugin.Enabled)
            {
                var hud = plugin.HudVisible;
                if (ImGui.Checkbox("Show HUD", ref hud))
                    _plugins.SetHudVisible(plugin.Id, hud);
            }
            if (!string.IsNullOrEmpty(plugin.LastError))
                ImGui.TextColored(HudTheme.Danger, plugin.LastError);

            // Always show SETTINGS for enabled plugins (popup explains if none).
            if (plugin.Enabled)
            {
                if (ImGui.Button("SETTINGS"))
                {
                    _settingsPluginId = plugin.Id;
                    _capturingHotkey = false;
                    ImGui.OpenPopup("plugin_settings");
                }
                ImGui.SameLine();
            }
            if (plugin.UpdateAvailable)
            {
                if (HudTheme.AccentButton("UPDATE"))
                {
                    var id = plugin.Id;
                    RunBusy($"Updating {plugin.DisplayName}…", () => _plugins.UpdateFromCatalogAsync(id));
                }
                ImGui.SameLine();
            }
            if (ImGui.Button("UNINSTALL"))
                _plugins.Uninstall(plugin.Id);

            DrawPluginSettingsPopup(plugin);

            HudTheme.EndCard();
            ImGui.PopID();
        }

        if (installed.Count == 0)
        {
            HudTheme.BeginCard();
            ImGui.TextWrapped("No plugins armed. Pull one from CATALOG or drop a folder into the plugins directory.");
            HudTheme.EndCard();
        }
    }

    private void DrawPluginSettingsPopup(PluginRecord plugin)
    {
        var open = true;
        if (!ImGui.BeginPopupModal("plugin_settings", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_settingsPluginId is null || !string.Equals(_settingsPluginId, plugin.Id, StringComparison.OrdinalIgnoreCase))
        {
            ImGui.EndPopup();
            return;
        }

        ImGui.TextUnformatted($"{plugin.DisplayName} — settings");
        ImGui.Separator();

        var defs = _plugins.GetOptionDefinitions(plugin.Id);
        if (defs.Count == 0)
        {
            ImGui.TextColored(HudTheme.TextMuted,
                "This plugin exposes no settings (or an older build is installed). Restart the overlay after rebuilding to refresh.");
            if (HudTheme.AccentButton("DONE") || !open)
            {
                _settingsPluginId = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
            return;
        }

        var values = new Dictionary<string, string>(_plugins.GetOptions(plugin.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var def in defs)
        {
            ImGui.PushID(def.Key);
            values.TryGetValue(def.Key, out var current);
            current ??= def.DefaultValue ?? "";

            switch (def.Kind)
            {
                case PluginOptionKind.Bool:
                {
                    var on = PluginOptionDefaults.ParseBool(current, PluginOptionDefaults.ParseBool(def.DefaultValue, false));
                    if (ImGui.Checkbox(def.Label, ref on))
                        _plugins.SetOption(plugin.Id, def.Key, PluginOptionDefaults.Bool(on));
                    break;
                }
                case PluginOptionKind.String:
                {
                    var buf = current;
                    ImGui.SetNextItemWidth(220);
                    if (ImGui.InputText(def.Label, ref buf, 128) && buf != current)
                        _plugins.SetOption(plugin.Id, def.Key, buf);
                    break;
                }
                case PluginOptionKind.Hotkey:
                {
                    ImGui.TextUnformatted(def.Label);
                    ImGui.SameLine();
                    ImGui.TextColored(HudTheme.Ember, string.IsNullOrWhiteSpace(current) ? "(none)" : current);
                    ImGui.SameLine();
                    if (ImGui.Button(_capturingHotkey && _hotkeyCaptureKey == def.Key ? "Press keys…" : "Change"))
                    {
                        _capturingHotkey = true;
                        _hotkeyCaptureKey = def.Key;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear"))
                    {
                        _plugins.SetOption(plugin.Id, def.Key, "");
                        _capturingHotkey = false;
                    }
                    if (_capturingHotkey && _hotkeyCaptureKey == def.Key)
                    {
                        ImGui.TextColored(HudTheme.Warn, "Hold modifiers + key, then release…");
                        if (TryCaptureHotkey(out var chord))
                        {
                            _plugins.SetOption(plugin.Id, def.Key, chord);
                            _capturingHotkey = false;
                        }
                    }
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(def.Description))
                ImGui.TextColored(HudTheme.TextMuted, def.Description);

            ImGui.PopID();
            ImGui.Spacing();
        }

        if (HudTheme.AccentButton("DONE") || !open)
        {
            _capturingHotkey = false;
            _settingsPluginId = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static bool TryCaptureHotkey(out string chord)
    {
        chord = "";
        // Wait until a non-modifier key is down together with optional modifiers, then emit on first frame.
        var ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        var shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        var alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;

        for (var vk = 0x30; vk <= 0x5A; vk++) // 0-9 A-Z
        {
            if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (alt) parts.Add("Alt");
            parts.Add(vk <= 0x39 ? ((char)vk).ToString() : ((char)vk).ToString());
            chord = HotkeyChord.Normalize(string.Join("+", parts));
            return chord.Length > 0;
        }
        for (var f = 1; f <= 12; f++)
        {
            var vk = 0x70 + (f - 1);
            if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (alt) parts.Add("Alt");
            parts.Add($"F{f}");
            chord = HotkeyChord.Normalize(string.Join("+", parts));
            return chord.Length > 0;
        }
        return false;
    }

    private void DrawCatalogTab()
    {
        HudTheme.SectionLabel("spiritvalemods.com");
        ImGui.TextColored(HudTheme.TextMuted, _plugins.CatalogUrl);
        if (ImGui.Button("REFRESH"))
            RunBusy("Refreshing catalog…", () => _plugins.RefreshCatalogAsync());
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Filter plugins…", ref _catalogFilter, 128);

        var catalog = _plugins.LastCatalog;
        if (catalog is null)
        {
            HudTheme.BeginCard();
            ImGui.TextWrapped("Catalog not loaded yet.");
            HudTheme.EndCard();
            return;
        }

        if (catalog.Paused == true)
        {
            HudTheme.BeginCard();
            HudTheme.StatusChip("PAUSED", HudTheme.Warn);
            ImGui.Spacing();
            ImGui.TextWrapped("The public catalog is paused. Overlay plugin listings will show here once the site hosts them.");
            HudTheme.EndCard();
            return;
        }

        var installedIds = _plugins.Installed
            .Select(p => p.CatalogId ?? p.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mods = catalog.Mods
            .Where(m => string.IsNullOrWhiteSpace(_catalogFilter)
                || m.Name.Contains(_catalogFilter, StringComparison.OrdinalIgnoreCase)
                || m.Id.Contains(_catalogFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Name)
            .ToList();

        ImGui.TextColored(HudTheme.TextMuted, $"{mods.Count} LISTING(S)");
        foreach (var mod in mods)
        {
            ImGui.PushID(mod.Id);
            HudTheme.BeginCard();
            ImGui.TextUnformatted(mod.Name.ToUpperInvariant());
            ImGui.TextColored(HudTheme.TextMuted, $"{mod.Id}  ·  v{mod.LatestVersion}  ·  {FormatBytes(mod.SizeBytes)}");
            if (!string.IsNullOrWhiteSpace(mod.Description))
                ImGui.TextWrapped(mod.Description);
            else if (!string.IsNullOrWhiteSpace(mod.Changelog))
                ImGui.TextWrapped(mod.Changelog);

            var already = installedIds.Contains(mod.Id);
            if (already)
            {
                HudTheme.StatusChip("OWNED", HudTheme.Live);
                ImGui.SameLine();
                if (HudTheme.AccentButton("REINSTALL"))
                {
                    var id = mod.Id;
                    RunBusy($"Installing {mod.Name}…", () => _plugins.InstallFromCatalogAsync(id));
                }
            }
            else if (HudTheme.AccentButton("INSTALL"))
            {
                var id = mod.Id;
                RunBusy($"Installing {mod.Name}…", () => _plugins.InstallFromCatalogAsync(id));
            }

            HudTheme.EndCard();
            ImGui.PopID();
        }

        if (mods.Count == 0)
        {
            HudTheme.BeginCard();
            ImGui.TextWrapped("No catalog entries yet. When spiritvalemods.com lists overlay plugin zips, they appear here.");
            HudTheme.EndCard();
        }
    }

    private void RunBusy(string message, Func<Task> work)
    {
        if (_busyTask is { IsCompleted: false } || _plugins.IsBusy)
        {
            _managerMessage = "Already busy.";
            return;
        }

        _managerMessage = message;
        _busyTask = Task.Run(async () =>
        {
            try
            {
                await work();
                _managerMessage = _plugins.StatusMessage ?? "Done.";
            }
            catch (Exception ex)
            {
                _managerMessage = ex.Message;
            }
        });
    }

    private void TryStartCapture(bool userSelected)
    {
        try
        {
            if (userSelected && _selectedDevice >= 0 && _selectedDevice < _devices.Length)
            {
                _preferredDevice = _devices[_selectedDevice];
                SaveCaptureSettings();
                _api.StartCapture(_selectedDevice, _preferredDevice);
            }
            else
            {
                // Auto path: prefer saved device name, never force index 0.
                _api.StartCapture(deviceIndex: null, preferredDevice: _preferredDevice);
                var active = _api.ActiveCaptureDevice;
                if (active is not null)
                {
                    _preferredDevice = active;
                    _selectedDevice = ResolveDeviceIndex(active, _devices);
                    SaveCaptureSettings();
                }
            }
            _status = "Capturing";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            _managerMessage = ex.Message;
        }
    }

    private void RunCaptureWatchdog(float dt)
    {
        if (!_api.IsCapturing) return;
        _watchdogTimer += dt;
        if (_watchdogTimer < 4f) return;
        _watchdogTimer = 0f;

        // If we're "capturing" but saw zero UDP, cycle adapters (common after Npcap reinstall).
        if (_api.TryRecoverCaptureIfSilent(TimeSpan.FromSeconds(4)))
        {
            var active = _api.ActiveCaptureDevice;
            if (active is not null)
            {
                _preferredDevice = active;
                _selectedDevice = ResolveDeviceIndex(active, _devices);
                SaveCaptureSettings();
                _managerMessage = $"Switched capture adapter → {active}";
            }
        }
    }

    private void SaveCaptureSettings()
    {
        var settings = PluginRegistryStore.Load();
        settings.PreferredCaptureDevice = _preferredDevice;
        settings.SpriteDumpPath = string.IsNullOrWhiteSpace(_spriteDumpPath) ? null : _spriteDumpPath.Trim();
        PluginRegistryStore.Save(settings);
    }

    private IntPtr? ResolveSpriteTexture(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        string? path = null;
        if (key.StartsWith("class:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(6), out var archId))
        {
            path = _api.Sprites.ResolveClassIconPath(archId);
        }
        else if (key.Contains('\\') || key.Contains('/') || key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            path = File.Exists(key) ? key : null;
        }
        else
        {
            path = _api.Sprites.ResolveSpritePath(key);
        }

        if (path is null || !File.Exists(path)) return null;

        if (_spriteTextures.TryGetValue(path, out var cached) && cached != IntPtr.Zero)
            return cached;

        AddOrGetImagePointer(path, false, out var tex, out _, out _);
        _spriteTextures[path] = tex;
        return tex == IntPtr.Zero ? null : tex;
    }

    private static int ResolveDeviceIndex(string? preferred, string[] devices)
    {
        if (string.IsNullOrWhiteSpace(preferred) || devices.Length == 0) return 0;
        for (var i = 0; i < devices.Length; i++)
        {
            if (devices[i].Contains(preferred, StringComparison.OrdinalIgnoreCase)
                || preferred.Contains(devices[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    private void OnUdpSeen(string summary)
    {
        lock (_packetGate)
        {
            _recentPackets.Add($"udp  {summary}");
            if (_recentPackets.Count > 300)
                _recentPackets.RemoveAt(0);
        }
    }

    private void OnPacket(SpiritVale.Overlay.Api.Protocol.DecodedFishNetEvent evt)
    {
        var line = $"fn   t={evt.Tick} {evt.PacketName}"
            + (evt.RpcName is not null ? $" {evt.RpcName}" : "")
            + (evt.ObjectId is int oid ? $" obj={oid}" : "");
        lock (_packetGate)
        {
            _recentPackets.Add(line);
            if (_recentPackets.Count > 300)
                _recentPackets.RemoveAt(0);
        }
    }

    private void HandleHotkeys()
    {
        // Win32 key state so F2/F3/F4 still work while WS_EX_TRANSPARENT click-through is on.
        PollToggle(ImGuiKey.F2, ref _f2Down, () => _showHost = !_showHost);
        PollToggle(ImGuiKey.F3, ref _f3Down, () =>
        {
            _forceClickThrough = !_forceClickThrough;
            if (window is not null && !_radialClickThroughOverride)
                SetWindowClickThrough(window.Handle, _forceClickThrough, noActivate: true);
        });
        PollToggle(ImGuiKey.F4, ref _f4Down, () => _followGame = !_followGame);
        _plugins.PollOptionHotkeys(_pluginHotkeyEdges);
    }

    private static void PollToggle(ImGuiKey key, ref bool wasDown, Action onPress)
    {
        var vk = key switch
        {
            ImGuiKey.F2 => 0x71,
            ImGuiKey.F3 => 0x72,
            ImGuiKey.F4 => 0x73,
            _ => 0,
        };
        if (vk == 0) return;
        var down = (GetAsyncKeyState(vk) & 0x8000) != 0;
        if (down && !wasDown) onPress();
        wasDown = down;
    }

    private void SyncOverlayToSpiritVale()
    {
        if (window is null) return;

        var game = FindSpiritValeHwnd();
        if (game == IntPtr.Zero || IsIconic(game) || !IsWindowVisible(game)
            || !IsGameOrOverlayForeground(game))
        {
            SetOverlayVisible(false);
            return;
        }

        // Game is focused again — always unhide first. Positioning can fail for a frame
        // after alt-tab without leaving the overlay stuck invisible.
        SetOverlayVisible(true);

        if (!_followGame)
            return;

        if (!TryGetGameClientScreenRect(game, out var x, out var y, out var width, out var height))
            return;

        try
        {
            if (Size.Width != width || Size.Height != height)
                Size = new System.Drawing.Size(width, height);
            if (Position.X != x || Position.Y != y)
                Position = new System.Drawing.Point(x, y);

            // Stay above the game without covering other apps outside its rect.
            SetWindowPos(window.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
        catch
        {
            // Window may not be fully ready on first frames.
        }
    }

    /// <summary>
    /// True when SpiritVale (or this overlay) owns the foreground window.
    /// Alt-tab to another app must hide the overlay even though the game HWND stays visible.
    /// </summary>
    private bool IsGameOrOverlayForeground(IntPtr gameHwnd)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (window is not null && (fg == window.Handle || IsAncestorOf(window.Handle, fg))) return true;
        if (fg == gameHwnd || IsAncestorOf(gameHwnd, fg)) return true;

        // Child / owned windows of the game (e.g. dialogs).
        var root = GetAncestor(fg, GaRoot);
        if (root == gameHwnd) return true;

        GetWindowThreadProcessId(fg, out var fgPid);
        GetWindowThreadProcessId(gameHwnd, out var gamePid);
        return fgPid != 0 && fgPid == gamePid;
    }

    private static bool IsAncestorOf(IntPtr ancestor, IntPtr child)
    {
        if (ancestor == IntPtr.Zero || child == IntPtr.Zero) return false;
        var cur = child;
        for (var i = 0; i < 8 && cur != IntPtr.Zero; i++)
        {
            if (cur == ancestor) return true;
            cur = GetParent(cur);
        }
        return false;
    }

    private void SetOverlayVisible(bool visible)
    {
        if (window is null) return;
        var hwnd = window.Handle;
        if (hwnd == IntPtr.Zero) return;

        if (visible)
        {
            // Retry every frame while OS still reports hidden — ShowWindow can fail once
            // after alt-tab on layered/topmost windows, and the old flag short-circuit
            // left the overlay permanently invisible.
            var actuallyVisible = IsWindowVisible(hwnd);
            if (!_overlayHiddenForGame && actuallyVisible) return;

            _overlayHiddenForGame = false;
            ShowWindow(hwnd, SwShowNoActivate);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
        else
        {
            if (_overlayHiddenForGame && !IsWindowVisible(hwnd)) return;
            _overlayHiddenForGame = true;
            ShowWindow(hwnd, SwHide);
        }
    }

    private static bool TryGetGameClientScreenRect(IntPtr gameHwnd, out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        if (!GetClientRect(gameHwnd, out var client)) return false;
        width = client.Right - client.Left;
        height = client.Bottom - client.Top;
        if (width < 64 || height < 64) return false;

        var topLeft = new Point { X = client.Left, Y = client.Top };
        if (!ClientToScreen(gameHwnd, ref topLeft)) return false;
        x = topLeft.X;
        y = topLeft.Y;
        return true;
    }

    private static int GetPrimaryScreenWidth()
        => Math.Max(GetSystemMetrics(0 /* SM_CXSCREEN */), 1280);

    private static int GetPrimaryScreenHeight()
        => Math.Max(GetSystemMetrics(1 /* SM_CYSCREEN */), 720);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }

    private static IntPtr FindSpiritValeHwnd()
    {
        foreach (var process in Process.GetProcessesByName("SpiritVale"))
        {
            if (process.MainWindowHandle != IntPtr.Zero)
                return process.MainWindowHandle;
        }
        return IntPtr.Zero;
    }

    private void PersistLocalCharacterName()
    {
        var name = _api.LocalPlayerName ?? _api.Character.Local?.DisplayName;
        if (string.IsNullOrWhiteSpace(name) || name is "You" or "Local Player") return;
        var settings = PluginRegistryStore.Load();
        if (string.Equals(settings.LocalCharacterName, name, StringComparison.Ordinal)) return;
        settings.LocalCharacterName = name;
        PluginRegistryStore.Save(settings);
    }

    private void FocusSpiritVale()
    {
        var hwnd = FindSpiritValeHwnd();
        if (hwnd == IntPtr.Zero) return;
        SetForegroundWindow(hwnd);
    }

    private static void SetWindowClickThrough(IntPtr hwnd, bool clickThrough, bool noActivate = true)
    {
        const int GwlExstyle = -20;
        const int WsExTransparent = 0x20;
        const int WsExLayered = 0x80000;
        const int WsExNoActivate = 0x08000000;
        var style = GetWindowLong(hwnd, GwlExstyle);
        style |= WsExLayered;
        if (noActivate) style |= WsExNoActivate;
        else style &= ~WsExNoActivate;
        if (clickThrough) style |= WsExTransparent;
        else style &= ~WsExTransparent;
        SetWindowLong(hwnd, GwlExstyle, style);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint GaRoot = 2;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }
}
