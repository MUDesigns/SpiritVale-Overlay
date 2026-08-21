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
}
