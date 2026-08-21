namespace SpiritVale.Overlay.Api;

/// <summary>
/// Thin drawing surface for plugins. Keeps ImGui types out of the public plugin surface
/// so authors can stay on the Api assembly alone.
/// </summary>
public interface IOverlayUi
{
    bool BeginWindow(string title, ref bool open);
    void EndWindow();
    void Text(string text);
    void TextUnformatted(string text);
    void TextColored(float r, float g, float b, float a, string text);
    bool Checkbox(string label, ref bool value);
    bool Button(string label);
    bool SmallButton(string label);
    bool AccentButton(string label);
    void Separator();
    void SameLine(float spacing = -1f);
    void Spacing();
    void Dummy(float width, float height);
    bool CollapsingHeader(string label, bool defaultOpen = false);
    void SetNextWindowSize(float width, float height, bool once = true);
    void SetNextWindowPos(float x, float y, bool once = true);
    void ProgressBar(float fraction, float height = 12f, string? overlay = null);
    void ProgressBar(float fraction, float r, float g, float b, float height = 12f, string? overlay = null);
    void SectionLabel(string text);
    float GetContentWidth();

    /// <summary>Draw a sprite from the configured dump / catalog (no-op if missing).</summary>
    void Sprite(string? spriteIdOrPath, float size = 16f);

    /// <summary>Class/archetype icon for a player row.</summary>
    void ClassIcon(int? archetypeId, float size = 16f);
}
