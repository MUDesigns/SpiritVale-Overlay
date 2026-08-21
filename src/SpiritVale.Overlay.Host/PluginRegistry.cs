using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpiritVale.Overlay.Host;

internal static class OverlayPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpiritValeOverlay");

    public static string PluginsDir => Path.Combine(Root, "plugins");
    public static string CacheDir => Path.Combine(Root, "cache");
    public static string LibraryDir => Path.Combine(Root, "library");
    public static string RegistryPath => Path.Combine(Root, "registry.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(PluginsDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(LibraryDir);
    }
}

internal sealed class PluginRegistryFile
{
    public string CatalogUrl { get; set; } = "https://www.spiritvalemods.com";
    public List<PluginRecord> Plugins { get; set; } = new();
}

internal sealed class PluginRecord
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string InstallDir { get; set; } = "";
    public string DllPath { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? CatalogId { get; set; }
    public string? CatalogVersion { get; set; }
    public string? Sha256 { get; set; }
    public bool UpdateAvailable { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }
}

internal static class PluginRegistryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static PluginRegistryFile Load()
    {
        OverlayPaths.Ensure();
        if (!File.Exists(OverlayPaths.RegistryPath))
            return new PluginRegistryFile();

        try
        {
            var json = File.ReadAllText(OverlayPaths.RegistryPath);
            return JsonSerializer.Deserialize<PluginRegistryFile>(json, JsonOptions) ?? new PluginRegistryFile();
        }
        catch
        {
            return new PluginRegistryFile();
        }
    }

    public static void Save(PluginRegistryFile file)
    {
        OverlayPaths.Ensure();
        var json = JsonSerializer.Serialize(file, JsonOptions);
        File.WriteAllText(OverlayPaths.RegistryPath, json);
    }
}
