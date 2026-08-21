namespace SpiritVale.Overlay.Api;

[Flags]
public enum OverlayWindowFlags
{
    None = 0,
    NoTitleBar = 1 << 0,
    NoResize = 1 << 1,
    NoMove = 1 << 2,
    NoScrollbar = 1 << 3,
    NoCollapse = 1 << 4,
    AutoResize = 1 << 5,
    NoBackground = 1 << 6,
    NoSavedSettings = 1 << 7,
}

public enum OverlayCol
{
    Text,
    Button,
    ButtonHovered,
    ButtonActive,
    ChildBg,
}

/// <summary>
/// Thin drawing surface for plugins. Keeps ImGui types out of the public plugin surface
/// so authors can stay on the Api assembly alone.
/// </summary>
public interface IOverlayUi
{
    bool BeginWindow(string title, ref bool open);
    bool BeginWindow(string title, ref bool open, OverlayWindowFlags flags);
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

    /// <summary>
    /// Skill icon with a WoW-style clockwise radial cooldown overlay (1 = full cover, 0 = ready).
    /// </summary>
    void CooldownSlot(string? spriteIdOrPath, float size, float fillAmount, string? overlayText);

    bool BeginWindowChild(string id, float width, float height, bool border = true);
    void EndWindowChild();
    bool SliderFloat(string label, ref float value, float min, float max);
    bool SliderInt(string label, ref int value, int min, int max);
    bool Button(string label, float width, float height);
    void PushStyleColor(OverlayCol col, float r, float g, float b, float a);
    void PopStyleColor(int count = 1);
    void PushAlpha(float alpha);
    void PopAlpha();
    void PushWindowPadding(float x, float y);
    void PopWindowPadding();
    void SetCursorPos(float x, float y);
    (float X, float Y) GetCursorPos();
    (float X, float Y) GetWindowPos();
    (float Width, float Height) GetDisplaySize();
    void SetNextWindowBgAlpha(float alpha);

    /// <summary>Call while a plugin window needs mouse hits (config / drag). Honored next frame.</summary>
    void CaptureMouse();
}
