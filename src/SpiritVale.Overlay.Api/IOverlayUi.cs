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
    bool Checkbox(string label, ref bool value);
    bool Button(string label);
    void Separator();
    void SameLine();
    void Spacing();
    bool CollapsingHeader(string label);
    void SetNextWindowSize(float width, float height, bool once = true);
    void SetNextWindowPos(float x, float y, bool once = true);
}
