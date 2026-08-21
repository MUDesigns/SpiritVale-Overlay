using System.Numerics;
using ImGuiNET;
using SpiritVale.Overlay.Api;

namespace SpiritVale.Overlay.Host;

internal sealed class ImGuiOverlayUi : IOverlayUi
{
    private readonly Func<string, IntPtr?> _resolveTexture;

    public ImGuiOverlayUi(Func<string, IntPtr?> resolveTexture)
        => _resolveTexture = resolveTexture;

    public bool BeginWindow(string title, ref bool open)
    {
        ImGui.SetNextWindowBgAlpha(0.55f);
        var began = OverlayIcons.BeginBrandedWindow(title, ref open);
        if (began) HudTheme.AccentRail();
        return began;
    }

    public void EndWindow() => ImGui.End();

    public void Text(string text)
    {
        // TextWrapped / Text treat '%' as printf — escape so DPS "45%" etc. render.
        ImGui.TextWrapped(EscapeImGui(text));
    }

    public void TextUnformatted(string text) => ImGui.TextUnformatted(text);

    public void TextColored(float r, float g, float b, float a, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(r, g, b, a));
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static string EscapeImGui(string text)
        => text.Replace("%", "%%", StringComparison.Ordinal);

    public bool Checkbox(string label, ref bool value) => ImGui.Checkbox(label, ref value);
    public bool Button(string label) => ImGui.Button(label);
    public bool SmallButton(string label) => ImGui.SmallButton(label);
    public bool AccentButton(string label) => HudTheme.AccentButton(label);
    public void Separator() => ImGui.Separator();
    public void SameLine(float spacing = -1f)
    {
        if (spacing < 0) ImGui.SameLine();
        else ImGui.SameLine(0, spacing);
    }
    public void Spacing() => ImGui.Spacing();
    public void Dummy(float width, float height) => ImGui.Dummy(new Vector2(width, height));
    public bool CollapsingHeader(string label, bool defaultOpen = false)
        => ImGui.CollapsingHeader(label, defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);

    public void SetNextWindowSize(float width, float height, bool once = true)
        => ImGui.SetNextWindowSize(new Vector2(width, height), once ? ImGuiCond.FirstUseEver : ImGuiCond.Always);

    public void SetNextWindowPos(float x, float y, bool once = true)
        => ImGui.SetNextWindowPos(new Vector2(x, y), once ? ImGuiCond.FirstUseEver : ImGuiCond.Always);

    public void ProgressBar(float fraction, float height = 12f, string? overlay = null)
        => HudTheme.MeterBar(fraction, HudTheme.BarFill, height, overlay);

    public void ProgressBar(float fraction, float r, float g, float b, float height = 12f, string? overlay = null)
        => HudTheme.MeterBar(fraction, new Vector4(r, g, b, 1f), height, overlay);

    public void SectionLabel(string text) => HudTheme.SectionLabel(text);

    public float GetContentWidth() => ImGui.GetContentRegionAvail().X;

    public void Sprite(string? spriteIdOrPath, float size = 16f)
    {
        if (string.IsNullOrWhiteSpace(spriteIdOrPath))
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }
        var tex = _resolveTexture(spriteIdOrPath);
        if (tex is null || tex == IntPtr.Zero)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }
        ImGui.Image(tex.Value, new Vector2(size, size));
    }

    public void ClassIcon(int? archetypeId, float size = 16f)
    {
        // Always reserve the icon slot so SameLine never pulls the next row up.
        if (archetypeId is null)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }
        Sprite($"class:{archetypeId}", size);
    }
}
