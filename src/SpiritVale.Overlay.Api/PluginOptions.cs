namespace SpiritVale.Overlay.Api;

public enum PluginOptionKind
{
    Bool,
    String,
    Hotkey,
}

/// <summary>Declared setting a plugin exposes in the host plugin list.</summary>
public sealed record PluginOptionDefinition(
    string Key,
    string Label,
    PluginOptionKind Kind,
    string? Description = null,
    string? DefaultValue = null);

/// <summary>Optional helpers for plugins that ship configurable options.</summary>
public static class PluginOptionDefaults
{
    public static string Bool(bool value) => value ? "true" : "false";
    public static bool ParseBool(string? value, bool fallback = false)
        => value is null ? fallback
            : value.Equals("true", StringComparison.OrdinalIgnoreCase)
              || value == "1"
              || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
}
