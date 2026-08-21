using ImGuiNET;
using SpiritVale.Overlay.Api;

namespace SpiritVale.Overlay.Host;

internal sealed class ImGuiOverlayUi : IOverlayUi
{
    public bool BeginWindow(string title, ref bool open) => ImGui.Begin(title, ref open);
    public void EndWindow() => ImGui.End();
    public void Text(string text) => ImGui.TextWrapped(text);
    public void TextUnformatted(string text) => ImGui.TextUnformatted(text);
    public bool Checkbox(string label, ref bool value) => ImGui.Checkbox(label, ref value);
    public bool Button(string label) => ImGui.Button(label);
    public void Separator() => ImGui.Separator();
    public void SameLine() => ImGui.SameLine();
    public void Spacing() => ImGui.Spacing();
    public bool CollapsingHeader(string label) => ImGui.CollapsingHeader(label);

    public void SetNextWindowSize(float width, float height, bool once = true)
        => ImGui.SetNextWindowSize(new System.Numerics.Vector2(width, height), once ? ImGuiCond.FirstUseEver : ImGuiCond.Always);

    public void SetNextWindowPos(float x, float y, bool once = true)
        => ImGui.SetNextWindowPos(new System.Numerics.Vector2(x, y), once ? ImGuiCond.FirstUseEver : ImGuiCond.Always);
}
