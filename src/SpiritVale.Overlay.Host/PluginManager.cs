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

    public void DrawEnabled(IOverlayUi ui)
    {
        RuntimePlugin[] snapshot;
        lock (_gate) snapshot = _runtime.Where(r => r.Record.Enabled && r.IsLoaded && !r.DrawFaulted).ToArray();

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
                if (_registry.Plugins.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Copy into managed plugins dir so enable/disable stays consistent.
                var dest = Path.Combine(OverlayPaths.PluginsDir, id);
                CopyDirectory(dir, dest);
                var destDll = FindPluginDll(dest, id)!;
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
                try
                {
                    LoadInto(runtime);
                }
                catch (Exception ex)
                {
                    record.Enabled = false;
                    record.LastError = ex.Message;
                }
            }
        }
        Save();
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
        runtime.Context = context;
        runtime.Instance = instance;
        if (!string.IsNullOrWhiteSpace(instance.Name))
            runtime.Record.DisplayName = instance.Name;
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
        CatalogId = r.CatalogId,
        CatalogVersion = r.CatalogVersion,
        Sha256 = r.Sha256,
        UpdateAvailable = r.UpdateAvailable,
        LastError = r.LastError,
        InstalledAt = r.InstalledAt,
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
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
