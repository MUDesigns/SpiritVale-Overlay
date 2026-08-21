using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using SpiritVale.Overlay.Capture;
using SpiritVale.Overlay.Domain;

namespace SpiritVale.Overlay.Host;

internal sealed class OverlayApp : ClickableTransparentOverlay.Overlay
{
    private readonly SpiritValeApi _api;
    private readonly PluginManager _plugins;
    private readonly ImGuiOverlayUi _ui = new();
    private readonly List<string> _recentPackets = new();
    private readonly object _packetGate = new();

    private bool _showHost = true;
    private bool _forceClickThrough;
    private bool _followGame = true;
    private bool _autoStartAttempted;
    private string _status = "Starting…";
    private int _selectedDevice;
    private string[] _devices = Array.Empty<string>();
    private int _tab; // 0 capture, 1 installed, 2 catalog
    private string _catalogFilter = "";
    private string _managerMessage = "";
    private Task? _busyTask;

    public OverlayApp(SpiritValeApi api, PluginManager plugins)
        : base("SpiritVale Overlay", 1920, 1080)
    {
        _api = api;
        _plugins = plugins;
        _api.Protocol.Packet += OnPacket;
        _api.CaptureStateChanged += running => _status = running ? "Capturing" : "Idle";
        FPSLimit = 60;
    }

    protected override Task PostInitialized()
    {
        _devices = PacketCaptureService.ListDevices().ToArray();
        if (!_autoStartAttempted)
        {
            _autoStartAttempted = true;
            TryStartCapture();
        }
        RunBusy("Refreshing catalog…", () => _plugins.RefreshCatalogAsync());
        return Task.CompletedTask;
    }

    protected override void Render()
    {
        if (_followGame)
            FollowSpiritValeWindow();

        if (_forceClickThrough && window is not null)
            SetWindowClickThrough(window.Handle, true);

        HandleHotkeys();

        if (_showHost)
            DrawHostWindow();

        _plugins.DrawEnabled(_ui);
    }

    private void DrawHostWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(520, 520), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("SpiritVale Overlay Manager", ref _showHost))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("manager_tabs"))
        {
            if (ImGui.BeginTabItem("Capture"))
            {
                _tab = 0;
                DrawCaptureTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Installed"))
            {
                _tab = 1;
                DrawInstalledTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Catalog"))
            {
                _tab = 2;
                DrawCatalogTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.Separator();
        var msg = !string.IsNullOrEmpty(_plugins.StatusMessage) ? _plugins.StatusMessage : _managerMessage;
        if (!string.IsNullOrEmpty(msg))
            ImGui.TextWrapped(msg);
        if (_plugins.IsBusy || _busyTask is { IsCompleted: false })
            ImGui.TextDisabled("Working…");

        ImGui.TextDisabled("Insert = manager, Home = click-through");
        ImGui.End();
        _ = _tab;
    }

    private void DrawCaptureTab()
    {
        ImGui.Text($"Status: {_api.CaptureStatus ?? _status}");
        ImGui.Text($"Capturing: {_api.IsCapturing}");
        ImGui.Separator();

        var npcap = PacketCaptureService.GetNpcapStatus();
        if (!npcap.Available)
        {
            ImGui.TextWrapped(npcap.Message ?? "Npcap not found.");
            ImGui.TextWrapped("Install Npcap with WinPcap API-compatible mode: https://npcap.com/");
        }
        else
        {
            ImGui.Text($"Npcap devices: {npcap.DeviceCount}");
            if (_devices.Length > 0)
                ImGui.Combo("Adapter", ref _selectedDevice, _devices, _devices.Length);

            if (!_api.IsCapturing && ImGui.Button("Start capture"))
                TryStartCapture();
            if (_api.IsCapturing)
            {
                ImGui.SameLine();
                if (ImGui.Button("Stop capture"))
                    _api.StopCapture();
            }
        }

        ImGui.Separator();
        ImGui.Checkbox("Follow SpiritVale window", ref _followGame);
        if (ImGui.Checkbox("Force click-through", ref _forceClickThrough) && window is not null)
            SetWindowClickThrough(window.Handle, _forceClickThrough);

        if (ImGui.CollapsingHeader("Recent FishNet packets"))
        {
            lock (_packetGate)
            {
                for (var i = _recentPackets.Count - 1; i >= 0 && i >= _recentPackets.Count - 40; i--)
                    ImGui.TextUnformatted(_recentPackets[i]);
            }
        }
    }

    private void DrawInstalledTab()
    {
        var installed = _plugins.Installed;
        ImGui.Text($"{installed.Count} installed · {installed.Count(p => p.Enabled)} enabled");
        ImGui.TextDisabled(OverlayPaths.PluginsDir);

        if (ImGui.Button("Check updates"))
            RunBusy("Checking updates…", () => _plugins.CheckUpdatesAsync());
        ImGui.SameLine();
        if (ImGui.Button("Update all") && !_plugins.IsBusy)
        {
            var due = installed.Where(p => p.UpdateAvailable).Select(p => p.Id).ToArray();
            RunBusy("Updating…", async () =>
            {
                foreach (var id in due)
                    await _plugins.UpdateFromCatalogAsync(id);
            });
        }

        ImGui.Separator();
        foreach (var plugin in installed)
        {
            ImGui.PushID(plugin.Id);
            var enabled = plugin.Enabled;
            if (ImGui.Checkbox("##en", ref enabled))
                _plugins.SetEnabled(plugin.Id, enabled);

            ImGui.SameLine();
            ImGui.TextUnformatted(plugin.DisplayName);
            if (plugin.UpdateAvailable)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1f, 0.75f, 0.2f, 1f), "update");
            }

            var meta = plugin.CatalogVersion is not null
                ? $"v{plugin.CatalogVersion}"
                : "local";
            if (plugin.CatalogId is not null)
                meta += $" · {plugin.CatalogId}";
            ImGui.TextDisabled(meta);
            if (!string.IsNullOrEmpty(plugin.LastError))
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), plugin.LastError);

            if (plugin.UpdateAvailable && ImGui.Button("Update"))
            {
                var id = plugin.Id;
                RunBusy($"Updating {plugin.DisplayName}…", () => _plugins.UpdateFromCatalogAsync(id));
            }
            ImGui.SameLine();
            if (ImGui.Button("Uninstall"))
                _plugins.Uninstall(plugin.Id);

            ImGui.Separator();
            ImGui.PopID();
        }

        if (installed.Count == 0)
            ImGui.TextWrapped("No plugins installed yet. Use the Catalog tab, or drop a folder into the plugins directory.");
    }

    private void DrawCatalogTab()
    {
        ImGui.Text($"Source: {_plugins.CatalogUrl}");
        if (ImGui.Button("Refresh catalog"))
            RunBusy("Refreshing catalog…", () => _plugins.RefreshCatalogAsync());

        ImGui.InputText("Filter", ref _catalogFilter, 128);
        ImGui.Separator();

        var catalog = _plugins.LastCatalog;
        if (catalog is null)
        {
            ImGui.TextWrapped("Catalog not loaded yet.");
            return;
        }

        if (catalog.Paused == true)
        {
            ImGui.TextWrapped("The public catalog is currently paused. Overlay plugins will use this same API once the site hosts them.");
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

        ImGui.Text($"{mods.Count} listing(s)");
        foreach (var mod in mods)
        {
            ImGui.PushID(mod.Id);
            ImGui.TextUnformatted(mod.Name);
            ImGui.TextDisabled($"{mod.Id} · v{mod.LatestVersion} · {FormatBytes(mod.SizeBytes)}");
            if (!string.IsNullOrWhiteSpace(mod.Description))
                ImGui.TextWrapped(mod.Description);
            else if (!string.IsNullOrWhiteSpace(mod.Changelog))
                ImGui.TextWrapped(mod.Changelog);

            var already = installedIds.Contains(mod.Id);
            if (already)
            {
                ImGui.TextDisabled("Installed");
                ImGui.SameLine();
                if (ImGui.Button("Reinstall / update"))
                {
                    var id = mod.Id;
                    RunBusy($"Installing {mod.Name}…", () => _plugins.InstallFromCatalogAsync(id));
                }
            }
            else if (ImGui.Button("Install"))
            {
                var id = mod.Id;
                RunBusy($"Installing {mod.Name}…", () => _plugins.InstallFromCatalogAsync(id));
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (mods.Count == 0)
            ImGui.TextWrapped("No catalog entries. When spiritvalemods.com lists overlay plugin zips, they will appear here.");
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

    private void TryStartCapture()
    {
        try
        {
            _api.StartCapture();
            _status = "Capturing";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
    }

    private void OnPacket(SpiritVale.Overlay.Api.Protocol.DecodedFishNetEvent evt)
    {
        var line = $"t={evt.Tick} {evt.PacketName}"
            + (evt.RpcName is not null ? $" {evt.RpcName}" : "")
            + (evt.ObjectId is int oid ? $" obj={oid}" : "");
        lock (_packetGate)
        {
            _recentPackets.Add(line);
            if (_recentPackets.Count > 200)
                _recentPackets.RemoveAt(0);
        }
    }

    private void HandleHotkeys()
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Insert))
            _showHost = !_showHost;
        if (ImGui.IsKeyPressed(ImGuiKey.Home))
        {
            _forceClickThrough = !_forceClickThrough;
            if (window is not null)
                SetWindowClickThrough(window.Handle, _forceClickThrough);
        }
    }

    private void FollowSpiritValeWindow()
    {
        if (window is null) return;
        var hwnd = FindSpiritValeHwnd();
        if (hwnd == IntPtr.Zero) return;
        if (!GetWindowRect(hwnd, out var rect)) return;
        var width = Math.Max(rect.Right - rect.Left, 800);
        var height = Math.Max(rect.Bottom - rect.Top, 600);
        try
        {
            Size = new System.Drawing.Size(width, height);
            Position = new System.Drawing.Point(rect.Left, rect.Top);
        }
        catch
        {
            // Window may not be fully ready on first frames.
        }
    }

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

    private static void SetWindowClickThrough(IntPtr hwnd, bool clickThrough)
    {
        const int GwlExstyle = -20;
        const int WsExTransparent = 0x20;
        const int WsExLayered = 0x80000;
        var style = GetWindowLong(hwnd, GwlExstyle);
        if (clickThrough)
            SetWindowLong(hwnd, GwlExstyle, style | WsExTransparent | WsExLayered);
        else
            SetWindowLong(hwnd, GwlExstyle, (style | WsExLayered) & ~WsExTransparent);
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
}
