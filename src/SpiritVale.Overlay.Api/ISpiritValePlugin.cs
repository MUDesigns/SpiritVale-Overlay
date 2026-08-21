namespace SpiritVale.Overlay.Api;

/// <summary>Entry point for a drop-in overlay plugin DLL.</summary>
public interface ISpiritValePlugin
{
    string Id { get; }
    string Name { get; }
    string? Author => null;
    string? Version => null;

    void OnLoad(ISpiritValeApi api);
    void OnUnload();
    void Draw(IOverlayUi ui);

    /// <summary>Settings shown in the host plugin list. Empty = no Settings button.</summary>
    IReadOnlyList<PluginOptionDefinition> OptionDefinitions => Array.Empty<PluginOptionDefinition>();

    /// <summary>Apply persisted option values (missing keys use definition defaults).</summary>
    void ApplyOptions(IReadOnlyDictionary<string, string> values) { }

    /// <summary>Current values for the settings UI (and persistence).</summary>
    IReadOnlyDictionary<string, string> ExportOptions() => new Dictionary<string, string>();

    /// <summary>Fired when a <see cref="PluginOptionKind.Hotkey"/> chord is pressed.</summary>
    void OnOptionHotkey(string key) { }
}
