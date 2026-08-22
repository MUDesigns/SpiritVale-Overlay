using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using SpiritVale.Overlay.Api;

namespace SpiritVale.Overlay.Host;

internal sealed class RuntimePlugin : IDisposable
{
    public required PluginRecord Record { get; set; }
    public ISpiritValePlugin? Instance { get; set; }
    public PluginLoadContext? Context { get; set; }
    public bool IsLoaded => Instance is not null && Context is not null;
    public bool DrawFaulted { get; set; }

    public void Dispose()
    {
        try { Instance?.OnUnload(); } catch { /* ignore */ }
        Instance = null;
        try { Context?.Unload(); } catch { /* ignore */ }
        Context = null;
    }
}

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == typeof(ISpiritValePlugin).Assembly.GetName().Name)
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

/// <summary>
/// Local plugin library manager: enable/disable, catalog install/update, ALC load/unload.
/// </summary>
internal sealed class PluginManager : IDisposable
{
    private readonly ISpiritValeApi _api;
    private readonly object _gate = new();
    private readonly List<RuntimePlugin> _runtime = new();
    private PluginRegistryFile _registry;
    private CatalogClient _catalog;

    public PluginManager(ISpiritValeApi api)
    {
        _api = api;
        OverlayPaths.Ensure();
        _registry = PluginRegistryStore.Load();
        _catalog = new CatalogClient(_registry.CatalogUrl);
        DiscoverLocalPlugins();
        LoadEnabled();
        DeduplicateByPluginDll();
    }

    public string CatalogUrl
    {
        get => _registry.CatalogUrl;
        set
        {
            _registry.CatalogUrl = string.IsNullOrWhiteSpace(value)
                ? CatalogClient.DefaultBaseUrl
                : value.Trim().TrimEnd('/');
            _catalog.Dispose();
            _catalog = new CatalogClient(_registry.CatalogUrl);
            Save();
        }
    }

    public IReadOnlyList<PluginRecord> Installed
    {
        get { lock (_gate) return _registry.Plugins.Select(CloneRecord).ToList(); }
    }

    public string? StatusMessage { get; private set; }
    public bool IsBusy { get; private set; }
    public CatalogFile? LastCatalog => _catalog.Cached;

    /// <summary>
    /// Copy newer build-output plugins into AppData <b>before</b> any DLL is loaded.
    /// ImportDevPluginsFrom alone often fails because ALC unload does not release file locks promptly.
    /// </summary>
    public static void SyncDevBuildPlugins(string buildPluginsDirectory)
    {
        if (!Directory.Exists(buildPluginsDirectory)) return;
        OverlayPaths.Ensure();
        foreach (var dir in Directory.GetDirectories(buildPluginsDirectory))
        {
            var id = Path.GetFileName(dir);
            var srcDll = FindPluginDll(dir, id);
            if (srcDll is null) continue;
            var dllName = Path.GetFileName(srcDll);

            // Prefer overwriting an existing AppData install of the same DLL (e.g. catalog id folder)
            // instead of creating a second SpiritVale.Overlay.* directory.
            var destDll = Directory.GetDirectories(OverlayPaths.PluginsDir)
                .Select(d => FindPluginDll(d, Path.GetFileName(d)))
                .FirstOrDefault(p =>
                    p is not null
                    && Path.GetFileName(p).Equals(dllName, StringComparison.OrdinalIgnoreCase));

            if (destDll is null)
            {
                var destDir = Path.Combine(OverlayPaths.PluginsDir, id);
                destDll = Path.Combine(destDir, dllName);
                Directory.CreateDirectory(destDir);
            }

            if (File.Exists(destDll)
                && File.GetLastWriteTimeUtc(srcDll) <= File.GetLastWriteTimeUtc(destDll))
                continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destDll)!);
                File.Copy(srcDll, destDll, overwrite: true);
            }
            catch
            {
                // Locked / in use — user still has the previous copy.
            }
        }
    }


    public void DrawEnabled(IOverlayUi ui)
    {
        RuntimePlugin[] snapshot;
        lock (_gate)
        {
            snapshot = _runtime
                .Where(r => r.Record.Enabled && r.Record.HudVisible && r.IsLoaded && !r.DrawFaulted)
                .ToArray();
        }

        foreach (var plugin in snapshot)
        {
            try
            {
                plugin.Instance!.Draw(ui);
            }
            catch (Exception ex)
            {
                plugin.DrawFaulted = true;
                plugin.Record.LastError = ex.Message;
                Save();
                StatusMessage = $"Disabled drawing for {plugin.Record.DisplayName}: {ex.Message}";
            }
        }
    }

    public IReadOnlyList<RadialPluginEntry> GetRadialEntries()
    {
        lock (_gate)
        {
            return _runtime
                .Where(r => r.Record.Enabled && r.IsLoaded)
                .Select(r => new RadialPluginEntry(
                    r.Record.Id,
                    string.IsNullOrWhiteSpace(r.Record.DisplayName) ? r.Record.Id : r.Record.DisplayName,
                    r.Record.HudVisible))
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool SetHudVisible(string id, bool visible)
    {
        lock (_gate)
        {
            var runtime = FindRuntime(id);
            if (runtime is null) return false;
            if (runtime.Record.HudVisible == visible) return true;
            runtime.Record.HudVisible = visible;
            StatusMessage = visible
                ? $"Showing {runtime.Record.DisplayName}"
                : $"Hiding {runtime.Record.DisplayName}";
            Save();
            return true;
        }
    }

    public bool ToggleHudVisible(string id)
    {
        lock (_gate)
        {
            var runtime = FindRuntime(id);
            if (runtime is null) return false;
            var next = !runtime.Record.HudVisible;
            runtime.Record.HudVisible = next;
            StatusMessage = next
                ? $"Showing {runtime.Record.DisplayName}"
                : $"Hiding {runtime.Record.DisplayName}";
            Save();
            return true;
        }
    }

    public bool SetEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            var runtime = FindRuntime(id);
            if (runtime is null) return false;

            if (enabled == runtime.Record.Enabled && (!enabled || runtime.IsLoaded))
                return true;

            runtime.Record.Enabled = enabled;
            if (enabled)
            {
                try
                {
                    LoadInto(runtime);
                    runtime.Record.LastError = null;
                    StatusMessage = $"Enabled {runtime.Record.DisplayName}";
                }
                catch (Exception ex)
                {
                    runtime.Record.Enabled = false;
                    runtime.Record.LastError = ex.Message;
                    StatusMessage = $"Failed to enable {runtime.Record.DisplayName}: {ex.Message}";
                    Save();
                    return false;
                }
            }
            else
            {
                runtime.Dispose();
                StatusMessage = $"Disabled {runtime.Record.DisplayName}";
            }

            Save();
            return true;
        }
    }

    public async Task RefreshCatalogAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var catalog = await _catalog.FetchCatalogAsync(ct);
            if (catalog.Paused == true)
            {
                StatusMessage = "Catalog is paused on spiritvalemods.com (empty list).";
                return;
            }

            lock (_gate)
            {
                foreach (var record in _registry.Plugins)
                {
                    if (string.IsNullOrEmpty(record.CatalogId) && string.IsNullOrEmpty(record.Id))
                        continue;
                    var remote = catalog.Mods.FirstOrDefault(m =>
                        string.Equals(m.Id, record.CatalogId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.Id, record.Id, StringComparison.OrdinalIgnoreCase));
                    if (remote is null) continue;
                    record.CatalogId ??= remote.Id;
                    record.UpdateAvailable = !string.IsNullOrEmpty(record.Sha256)
                        && !string.Equals(record.Sha256, remote.Sha256, StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrEmpty(record.CatalogVersion))
                        record.CatalogVersion = remote.LatestVersion;
                }
                Save();
            }

            StatusMessage = $"Catalog: {catalog.Mods.Count} plugin(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Catalog refresh failed: {ex.Message}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CatalogCheckResult> CheckUpdatesAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var catalog = await _catalog.FetchCatalogAsync(ct);
            var checkedCount = 0;
            var updates = 0;
            lock (_gate)
            {
                foreach (var record in _registry.Plugins)
                {
                    var remote = FindRemote(catalog, record);
                    if (remote is null) continue;
                    checkedCount++;
                    record.CatalogId ??= remote.Id;
                    var hashDiff = string.IsNullOrEmpty(record.Sha256)
                        || !string.Equals(record.Sha256, remote.Sha256, StringComparison.OrdinalIgnoreCase);
                    record.UpdateAvailable = hashDiff;
                    if (hashDiff) updates++;
                }
                Save();
            }

            var result = new CatalogCheckResult
            {
                Checked = checkedCount,
                Updates = updates,
                Message = updates == 0
                    ? $"Checked {checkedCount} catalog plugin(s); all up to date."
                    : $"Checked {checkedCount}; {updates} update(s) available.",
            };
            StatusMessage = result.Message;
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallFromCatalogAsync(string catalogId, bool enable = true, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var catalog = _catalog.Cached ?? await _catalog.FetchCatalogAsync(ct);
            var remote = catalog.Mods.FirstOrDefault(m => string.Equals(m.Id, catalogId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Catalog plugin '{catalogId}' not found.");

            var zipPath = Path.Combine(OverlayPaths.LibraryDir, $"{remote.Id}.zip");
            var (_, hash) = await _catalog.DownloadModAsync(remote, zipPath, msg => StatusMessage = msg, ct: ct);

            // Keep a library copy and extract into plugins/{id}
            var installDir = Path.Combine(OverlayPaths.PluginsDir, remote.Id);
            ExtractPluginZip(zipPath, installDir);
            var dllPath = FindPluginDll(installDir, remote.Id)
                ?? throw new InvalidOperationException($"No plugin DLL found in zip for '{remote.Id}'.");

            RuntimePlugin runtime;
            lock (_gate)
            {
                var existing = FindRuntime(remote.Id);
                existing?.Dispose();
                if (existing is not null)
                    _runtime.Remove(existing);
                _registry.Plugins.RemoveAll(p => string.Equals(p.Id, remote.Id, StringComparison.OrdinalIgnoreCase));

                var record = new PluginRecord
                {
                    Id = remote.Id,
                    DisplayName = remote.Name,
                    InstallDir = installDir,
                    DllPath = dllPath,
                    Enabled = enable,
                    CatalogId = remote.Id,
                    CatalogVersion = remote.LatestVersion,
                    Sha256 = hash,
                    UpdateAvailable = false,
                    InstalledAt = DateTimeOffset.Now,
                };
                _registry.Plugins.Add(record);
                runtime = new RuntimePlugin { Record = record };
                _runtime.Add(runtime);
                Save();
            }

            if (enable)
                SetEnabled(remote.Id, true);

            StatusMessage = $"Installed {remote.Name} {remote.LatestVersion}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ImportZipFromPath(string zipPath, bool enable = true)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Zip not found.", zipPath);

        IsBusy = true;
        try
        {
            OverlayPaths.Ensure();
            var hash = CatalogClient.ComputeSha256(zipPath);
            var id = Path.GetFileNameWithoutExtension(zipPath);
            // Prefer a folder name from the zip if it has a single top-level directory.
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var tops = archive.Entries
                    .Select(e => e.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                    .Where(s => !string.IsNullOrEmpty(s) && !s!.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) && !s.Equals("Plugins", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (tops.Count == 1 && tops[0] is string folder && !folder.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    id = folder;
            }
            catch { /* keep filename id */ }

            id = SanitizeId(id);
            var libraryCopy = Path.Combine(OverlayPaths.LibraryDir, $"{id}.zip");
            Directory.CreateDirectory(OverlayPaths.LibraryDir);
            File.Copy(zipPath, libraryCopy, overwrite: true);

            var installDir = Path.Combine(OverlayPaths.PluginsDir, id);
            ExtractPluginZip(libraryCopy, installDir);
            var dllPath = FindPluginDll(installDir, id)
                ?? throw new InvalidOperationException($"No plugin DLL found in zip '{zipPath}'.");

            lock (_gate)
            {
                var existing = FindRuntime(id);
                existing?.Dispose();
                if (existing is not null)
                    _runtime.Remove(existing);
                _registry.Plugins.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

                var record = new PluginRecord
                {
                    Id = id,
                    DisplayName = id,
                    InstallDir = installDir,
                    DllPath = dllPath,
                    Enabled = enable,
                    Sha256 = hash,
                    UpdateAvailable = false,
                    InstalledAt = DateTimeOffset.Now,
                };
                _registry.Plugins.Add(record);
                _runtime.Add(new RuntimePlugin { Record = record });
                Save();
            }

            if (enable)
                SetEnabled(id, true);

            StatusMessage = $"Imported {id}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<AppUpdateInfo> CheckAppUpdateAsync(CancellationToken ct = default)
        => _catalog.CheckAppUpdateAsync(ct);

    public Task<string> ApplyAppUpdateAsync(AppUpdateInfo info, CancellationToken ct = default)
        => AppUpdater.ApplyUpdateAsync(_catalog, info, msg => StatusMessage = msg, ct);

    private static string SanitizeId(string id)
    {
        var cleaned = new string(id.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "imported-plugin" : cleaned;
    }

    public async Task UpdateFromCatalogAsync(string id, CancellationToken ct = default)
    {
        PluginRecord? record;
        lock (_gate) record = FindRuntime(id)?.Record;
        var catalogId = record?.CatalogId ?? id;
        var wasEnabled = record?.Enabled ?? true;
        if (wasEnabled)
            SetEnabled(id, false);
        await InstallFromCatalogAsync(catalogId, enable: wasEnabled, ct);
    }

    public void Uninstall(string id)
    {
        lock (_gate)
        {
            var runtime = FindRuntime(id);
            if (runtime is null) return;
            runtime.Dispose();
            _runtime.Remove(runtime);
            _registry.Plugins.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

            try
            {
                if (Directory.Exists(runtime.Record.InstallDir)
                    && IsUnderPluginsRoot(runtime.Record.InstallDir))
                    Directory.Delete(runtime.Record.InstallDir, recursive: true);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Uninstalled registry entry; folder cleanup failed: {ex.Message}";
                Save();
                return;
            }

            Save();
            StatusMessage = $"Uninstalled {runtime.Record.DisplayName}";
        }
    }

    public void ImportDevPluginsFrom(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var dir in Directory.GetDirectories(directory))
        {
            var id = Path.GetFileName(dir);
            var dll = FindPluginDll(dir, id);
            if (dll is null) continue;
            lock (_gate)
            {
                var dest = Path.Combine(OverlayPaths.PluginsDir, id);
                var existing = _registry.Plugins.FirstOrDefault(p =>
                    string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

                // Always refresh from build output when the source DLL is newer.
                var destDllExisting = existing is not null && File.Exists(existing.DllPath) ? existing.DllPath : null;
                var shouldCopy = existing is null
                    || destDllExisting is null
                    || File.GetLastWriteTimeUtc(dll) > File.GetLastWriteTimeUtc(destDllExisting);

                if (shouldCopy)
                {
                    // Unload first so AppData DLL isn't locked by this process.
                    var loaded = FindRuntime(id);
                    if (loaded is { IsLoaded: true })
                    {
                        try { loaded.Dispose(); }
                        catch { /* ignore */ }
                    }

                    if (!TryCopyDirectory(dir, dest, out var copyError))
                    {
                        StatusMessage = $"Skipped refreshing {id}: {copyError}";
                        // Reload previous bits if we had unloaded them.
                        if (loaded is not null && existing is { Enabled: true })
                        {
                            try { LoadInto(loaded); }
                            catch { /* keep disabled until next run */ }
                        }
                        continue;
                    }
                }

                var destDll = FindPluginDll(dest, id);
                if (destDll is null) continue;

                if (existing is null)
                {
                    var record = new PluginRecord
                    {
                        Id = id,
                        DisplayName = id,
                        InstallDir = dest,
                        DllPath = destDll,
                        Enabled = true,
                        InstalledAt = DateTimeOffset.Now,
                    };
                    _registry.Plugins.Add(record);
                    var runtime = new RuntimePlugin { Record = record };
                    _runtime.Add(runtime);
                    try { LoadInto(runtime); }
                    catch (Exception ex)
                    {
                        record.Enabled = false;
                        record.LastError = ex.Message;
                    }
                }
                else
                {
                    var wasEnabled = existing.Enabled;
                    existing.InstallDir = dest;
                    existing.DllPath = destDll;
                    var runtime = FindRuntime(id);
                    if (runtime is not null && wasEnabled && shouldCopy)
                    {
                        try
                        {
                            LoadInto(runtime);
                            existing.LastError = null;
                        }
                        catch (Exception ex)
                        {
                            existing.Enabled = false;
                            existing.LastError = ex.Message;
                        }
                    }
                    else if (runtime is not null && wasEnabled && !runtime.IsLoaded)
                    {
                        try { LoadInto(runtime); }
                        catch (Exception ex)
                        {
                            existing.Enabled = false;
                            existing.LastError = ex.Message;
                        }
                    }
                }
            }
        }
        Save();
        DeduplicateByPluginDll();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var runtime in _runtime)
                runtime.Dispose();
            _runtime.Clear();
        }
        _catalog.Dispose();
    }

    private void DiscoverLocalPlugins()
    {
        OverlayPaths.Ensure();
        foreach (var dir in Directory.GetDirectories(OverlayPaths.PluginsDir))
        {
            var id = Path.GetFileName(dir);
            var dll = FindPluginDll(dir, id);
            if (dll is null) continue;
            var dllName = Path.GetFileName(dll);

            lock (_gate)
            {
                var existing = _registry.Plugins.FirstOrDefault(p =>
                    string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    existing.InstallDir = dir;
                    existing.DllPath = dll;
                    if (string.IsNullOrWhiteSpace(existing.DisplayName))
                        existing.DisplayName = id;
                    continue;
                }

                // Same DLL already tracked under another folder id (catalog vs local name).
                var sameDll = _registry.Plugins.FirstOrDefault(p =>
                    Path.GetFileName(p.DllPath).Equals(dllName, StringComparison.OrdinalIgnoreCase));
                if (sameDll is not null)
                    continue;

                _registry.Plugins.Add(new PluginRecord
                {
                    Id = id,
                    DisplayName = id,
                    InstallDir = dir,
                    DllPath = dll,
                    Enabled = true,
                    InstalledAt = DateTimeOffset.Now,
                });
            }
        }
        Save();
    }

    /// <summary>
    /// Collapse duplicate installs of the same plugin DLL (catalog folder + local folder).
    /// Prefers catalog-linked entries.
    /// </summary>
    private void DeduplicateByPluginDll()
    {
        lock (_gate)
        {
            var groups = _runtime
                .GroupBy(
                    r => Path.GetFileName(r.Record.DllPath) ?? r.Record.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            if (groups.Count == 0) return;

            foreach (var group in groups)
            {
                var ordered = group
                    .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.Record.CatalogId))
                    .ThenByDescending(r =>
                        r.Record.Id.StartsWith("spiritvale-overlay-", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(r => r.Record.InstalledAt ?? DateTimeOffset.MinValue)
                    .ToList();
                var keep = ordered[0];
                foreach (var drop in ordered.Skip(1))
                {
                    drop.Dispose();
                    _runtime.Remove(drop);
                    _registry.Plugins.RemoveAll(p =>
                        string.Equals(p.Id, drop.Record.Id, StringComparison.OrdinalIgnoreCase));
                    try
                    {
                        if (Directory.Exists(drop.Record.InstallDir)
                            && IsUnderPluginsRoot(drop.Record.InstallDir)
                            && !string.Equals(
                                Path.GetFullPath(drop.Record.InstallDir),
                                Path.GetFullPath(keep.Record.InstallDir),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.Delete(drop.Record.InstallDir, recursive: true);
                        }
                    }
                    catch
                    {
                        // Folder may be locked; registry entry is already gone.
                    }
                }
            }

            Save();
            StatusMessage = "Removed duplicate plugin installs.";
        }
    }

    private void LoadEnabled()
    {
        lock (_gate)
        {
            _runtime.Clear();
            foreach (var record in _registry.Plugins)
            {
                var runtime = new RuntimePlugin { Record = record };
                _runtime.Add(runtime);
                if (!record.Enabled) continue;
                try
                {
                    LoadInto(runtime);
                    record.LastError = null;
                }
                catch (Exception ex)
                {
                    record.Enabled = false;
                    record.LastError = ex.Message;
                }
            }
            Save();
        }
    }

    private void LoadInto(RuntimePlugin runtime)
    {
        runtime.Dispose();
        runtime.DrawFaulted = false;
        var dllPath = Path.GetFullPath(runtime.Record.DllPath);
        if (!File.Exists(dllPath))
            throw new FileNotFoundException("Plugin DLL missing", dllPath);

        var context = new PluginLoadContext(dllPath);
        var assembly = context.LoadFromAssemblyPath(dllPath);
        var type = assembly.GetTypes()
            .FirstOrDefault(t => typeof(ISpiritValePlugin).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
            ?? throw new InvalidOperationException($"No ISpiritValePlugin type in {Path.GetFileName(dllPath)}");

        var instance = (ISpiritValePlugin)Activator.CreateInstance(type)!;
        instance.OnLoad(_api);
        ApplyStoredOptions(runtime.Record, instance);
        runtime.Context = context;
        runtime.Instance = instance;
        if (!string.IsNullOrWhiteSpace(instance.Name))
            runtime.Record.DisplayName = instance.Name;
    }

    private static void ApplyStoredOptions(PluginRecord record, ISpiritValePlugin instance)
    {
        var defs = instance.OptionDefinitions;
        if (defs.Count == 0) return;
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in defs)
            merged[def.Key] = def.DefaultValue ?? "";
        foreach (var (k, v) in record.Options)
            merged[k] = v;
        instance.ApplyOptions(merged);
        // Keep registry filled with current export (so defaults get persisted).
        foreach (var (k, v) in instance.ExportOptions())
            record.Options[k] = v;
    }

    public IReadOnlyList<PluginOptionDefinition> GetOptionDefinitions(string pluginId)
    {
        lock (_gate)
        {
            var runtime = FindRuntime(pluginId);
            return runtime?.Instance?.OptionDefinitions ?? Array.Empty<PluginOptionDefinition>();
        }
    }

    public IReadOnlyDictionary<string, string> GetOptions(string pluginId)
    {
        lock (_gate)
        {
            var runtime = FindRuntime(pluginId);
            if (runtime?.Instance is null)
                return new Dictionary<string, string>(runtime?.Record.Options ?? new(), StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string, string>(runtime.Instance.ExportOptions(), StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool SetOption(string pluginId, string key, string value)
    {
        lock (_gate)
        {
            var runtime = FindRuntime(pluginId);
            if (runtime is null) return false;
            runtime.Record.Options[key] = value;
            if (runtime.Instance is not null)
            {
                var merged = new Dictionary<string, string>(runtime.Record.Options, StringComparer.OrdinalIgnoreCase);
                foreach (var def in runtime.Instance.OptionDefinitions)
                    merged.TryAdd(def.Key, def.DefaultValue ?? "");
                runtime.Instance.ApplyOptions(merged);
                foreach (var (k, v) in runtime.Instance.ExportOptions())
                    runtime.Record.Options[k] = v;
            }
            Save();
            return true;
        }
    }

    public bool HasOptions(string pluginId)
    {
        lock (_gate)
        {
            var runtime = FindRuntime(pluginId);
            return runtime?.Instance is { OptionDefinitions.Count: > 0 };
        }
    }

    /// <summary>Poll plugin hotkey options; fire OnOptionHotkey on rising edge.</summary>
    public void PollOptionHotkeys(Dictionary<string, bool> edgeState)
    {
        List<(string PluginId, string Key, string Chord, ISpiritValePlugin Instance)> hotkeys;
        lock (_gate)
        {
            hotkeys = new();
            foreach (var runtime in _runtime)
            {
                if (!runtime.Record.Enabled || runtime.Instance is null) continue;
                foreach (var def in runtime.Instance.OptionDefinitions)
                {
                    if (def.Kind != PluginOptionKind.Hotkey) continue;
                    var values = runtime.Instance.ExportOptions();
                    if (!values.TryGetValue(def.Key, out var chord) || string.IsNullOrWhiteSpace(chord))
                        chord = def.DefaultValue;
                    if (string.IsNullOrWhiteSpace(chord)) continue;
                    hotkeys.Add((runtime.Record.Id, def.Key, chord!, runtime.Instance));
                }
            }
        }

        foreach (var (pluginId, key, chord, instance) in hotkeys)
        {
            var edgeKey = $"{pluginId}:{key}";
            var down = HotkeyChord.IsPressed(chord);
            edgeState.TryGetValue(edgeKey, out var wasDown);
            if (down && !wasDown)
            {
                try { instance.OnOptionHotkey(key); }
                catch { /* ignore plugin hotkey faults */ }
            }
            edgeState[edgeKey] = down;
        }
    }

    private RuntimePlugin? FindRuntime(string id)
        => _runtime.FirstOrDefault(r => string.Equals(r.Record.Id, id, StringComparison.OrdinalIgnoreCase));

    private static CatalogMod? FindRemote(CatalogFile catalog, PluginRecord record)
        => catalog.Mods.FirstOrDefault(m =>
            string.Equals(m.Id, record.CatalogId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.Id, record.Id, StringComparison.OrdinalIgnoreCase));

    private void Save() => PluginRegistryStore.Save(_registry);

    private static PluginRecord CloneRecord(PluginRecord r) => new()
    {
        Id = r.Id,
        DisplayName = r.DisplayName,
        InstallDir = r.InstallDir,
        DllPath = r.DllPath,
        Enabled = r.Enabled,
        HudVisible = r.HudVisible,
        CatalogId = r.CatalogId,
        CatalogVersion = r.CatalogVersion,
        Sha256 = r.Sha256,
        UpdateAvailable = r.UpdateAvailable,
        LastError = r.LastError,
        InstalledAt = r.InstalledAt,
        Options = new Dictionary<string, string>(r.Options ?? new(), StringComparer.OrdinalIgnoreCase),
    };

    private static bool IsUnderPluginsRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(OverlayPaths.PluginsDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? FindPluginDll(string directory, string id)
    {
        var preferred = Path.Combine(directory, $"{id}.dll");
        if (File.Exists(preferred)) return preferred;

        return Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return !name.StartsWith("SpiritVale.Overlay.Api", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("ImGui.NET.dll", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => Path.GetFileName(f).Equals($"{id}.dll", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f.Length)
            .FirstOrDefault();
    }

    internal static void ExtractPluginZip(string zipPath, string installDir)
    {
        if (Directory.Exists(installDir))
            Directory.Delete(installDir, recursive: true);
        Directory.CreateDirectory(installDir);

        using var archive = ZipFile.OpenRead(zipPath);
        // Detect whether the zip already contains a single top-level folder matching the plugin id.
        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (entries.Count == 0)
            throw new InvalidOperationException("Zip archive is empty.");

        // Support layouts:
        // 1) flat DLLs
        // 2) {id}/*.dll
        // 3) Plugins/{id}/*.dll
        // 4) BepInEx-style ignored — we only extract plugin-looking files into installDir
        foreach (var entry in entries)
        {
            var relative = entry.FullName.Replace('\\', '/');
            if (relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase))
                continue;

            // Strip optional Plugins/ or {id}/ prefix when present.
            var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Equals("Plugins", StringComparison.OrdinalIgnoreCase))
                parts = parts.Skip(1).ToArray();
            if (parts.Length >= 2 && parts[0].Equals(Path.GetFileName(installDir), StringComparison.OrdinalIgnoreCase))
                parts = parts.Skip(1).ToArray();

            var destRelative = string.Join(Path.DirectorySeparatorChar, parts);
            var destPath = Path.GetFullPath(Path.Combine(installDir, destRelative));
            if (!destPath.StartsWith(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Zip entry escapes install directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        if (!TryCopyDirectory(source, dest, out var error))
            throw new IOException(error);
    }

    private static bool TryCopyDirectory(string source, string dest, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                var target = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                try
                {
                    File.Copy(file, target, overwrite: true);
                }
                catch (IOException ex)
                {
                    // Another overlay instance (or AV) may still hold the DLL — skip refresh.
                    error = ex.Message;
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

internal sealed record RadialPluginEntry(string Id, string DisplayName, bool HudVisible);
