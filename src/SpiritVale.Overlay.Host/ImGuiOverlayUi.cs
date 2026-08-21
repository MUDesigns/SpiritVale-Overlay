using System.Numerics;
using ImGuiNET;
using SpiritVale.Overlay.Api;

namespace SpiritVale.Overlay.Host;

internal sealed class ImGuiOverlayUi : IOverlayUi
{
    private readonly Func<string, IntPtr?> _resolveTexture;
    private int _styleColorStack;
    private int _alphaStack;
    private int _paddingStack;

    public ImGuiOverlayUi(Func<string, IntPtr?> resolveTexture)
        => _resolveTexture = resolveTexture;

    /// <summary>Set during Draw; read at the start of the next frame for click-through.</summary>
    public bool WantsMouse { get; private set; }

    public void BeginFrame() => WantsMouse = false;

    public void CaptureMouse() => WantsMouse = true;

    public bool BeginWindow(string title, ref bool open)
    {
        ImGui.SetNextWindowBgAlpha(0.55f);
        var began = OverlayIcons.BeginBrandedWindow(title, ref open);
        if (began) HudTheme.AccentRail();
        return began;
    }

    public bool BeginWindow(string title, ref bool open, OverlayWindowFlags flags)
    {
        var imFlags = ToImGui(flags);
        if ((flags & OverlayWindowFlags.NoTitleBar) != 0)
            return ImGui.Begin(title, ref open, imFlags);

        var began = OverlayIcons.BeginBrandedWindow(title, ref open, imFlags);
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
    public bool Button(string label, float width, float height) => ImGui.Button(label, new Vector2(width, height));
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

    public void SetNextWindowBgAlpha(float alpha) => ImGui.SetNextWindowBgAlpha(alpha);

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

    public void CooldownSlot(string? spriteIdOrPath, float size, float fillAmount, string? overlayText)
    {
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + new Vector2(size, size);
        var draw = ImGui.GetWindowDrawList();
        var bg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.07f, 0.55f));
        draw.AddRectFilled(p0, p1, bg, 2f);

        var inset = new Vector2(2f, 2f);
        var tex = string.IsNullOrWhiteSpace(spriteIdOrPath) ? null : _resolveTexture(spriteIdOrPath);
        if (tex is IntPtr t && t != IntPtr.Zero)
            draw.AddImage(t, p0 + inset, p1 - inset);
        else
            draw.AddRectFilled(p0 + inset, p1 - inset, ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.38f, 0.45f, 0.9f)));

        fillAmount = Math.Clamp(fillAmount, 0f, 1f);
        if (fillAmount > 0.002f)
        {
            var center = (p0 + p1) * 0.5f;
            var radius = size * 0.5f - 1f;
            var overlay = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.72f));
            AddRadialFill(draw, center, radius, fillAmount, overlay);
        }

        if (!string.IsNullOrEmpty(overlayText))
        {
            var font = ImGui.GetFont();
            var fontSize = Math.Clamp(size * 0.38f, 11f, 28f);
            var ts = ImGui.CalcTextSize(overlayText);
            var pos = new Vector2(
                p0.X + (size - ts.X * (fontSize / ImGui.GetFontSize())) * 0.5f,
                p0.Y + (size - fontSize) * 0.5f);
            var shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.9f));
            var white = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
            draw.AddText(font, fontSize, pos + new Vector2(1f, 1f), shadow, overlayText);
            draw.AddText(font, fontSize, pos, white, overlayText);
        }

        ImGui.Dummy(new Vector2(size, size));
    }

    public bool BeginWindowChild(string id, float width, float height, bool border = true)
        => ImGui.BeginChild(id, new Vector2(width, height),
            border ? ImGuiChildFlags.Borders : ImGuiChildFlags.None);

    public void EndWindowChild() => ImGui.EndChild();

    public bool SliderFloat(string label, ref float value, float min, float max)
        => ImGui.SliderFloat(label, ref value, min, max);

    public bool SliderInt(string label, ref int value, int min, int max)
        => ImGui.SliderInt(label, ref value, min, max);

    public void PushStyleColor(OverlayCol col, float r, float g, float b, float a)
    {
        ImGui.PushStyleColor(ToImGui(col), new Vector4(r, g, b, a));
        _styleColorStack++;
    }

    public void PopStyleColor(int count = 1)
    {
        count = Math.Clamp(count, 0, _styleColorStack);
        if (count <= 0) return;
        ImGui.PopStyleColor(count);
        _styleColorStack -= count;
    }

    public void PushAlpha(float alpha)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(alpha, 0.1f, 1f));
        _alphaStack++;
    }

    public void PopAlpha()
    {
        if (_alphaStack <= 0) return;
        ImGui.PopStyleVar();
        _alphaStack--;
    }

    public void PushWindowPadding(float x, float y)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(x, y));
        _paddingStack++;
    }

    public void PopWindowPadding()
    {
        if (_paddingStack <= 0) return;
        ImGui.PopStyleVar();
        _paddingStack--;
    }

    public void SetCursorPos(float x, float y) => ImGui.SetCursorPos(new Vector2(x, y));

    public (float X, float Y) GetCursorPos()
    {
        var p = ImGui.GetCursorPos();
        return (p.X, p.Y);
    }

    public (float X, float Y) GetWindowPos()
    {
        var p = ImGui.GetWindowPos();
        return (p.X, p.Y);
    }

    public (float Width, float Height) GetDisplaySize()
    {
        var s = ImGui.GetIO().DisplaySize;
        return (s.X, s.Y);
    }

    private static void AddRadialFill(ImDrawListPtr draw, Vector2 center, float radius, float amount, uint col)
    {
        if (amount >= 0.997f)
        {
            draw.AddCircleFilled(center, radius, col, 32);
            return;
        }

        var segments = Math.Max(8, (int)MathF.Ceiling(48 * amount));
        draw.PathClear();
        draw.PathLineTo(center);
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (float)segments;
            // Clockwise from top (Unity Radial360 Origin360.Top).
            var ang = -MathF.PI / 2f - t * amount * MathF.PI * 2f;
            draw.PathLineTo(center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * radius);
        }
        draw.PathFillConvex(col);
    }

    private static ImGuiWindowFlags ToImGui(OverlayWindowFlags flags)
    {
        var result = ImGuiWindowFlags.None;
        if ((flags & OverlayWindowFlags.NoTitleBar) != 0) result |= ImGuiWindowFlags.NoTitleBar;
        if ((flags & OverlayWindowFlags.NoResize) != 0) result |= ImGuiWindowFlags.NoResize;
        if ((flags & OverlayWindowFlags.NoMove) != 0) result |= ImGuiWindowFlags.NoMove;
        if ((flags & OverlayWindowFlags.NoScrollbar) != 0) result |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if ((flags & OverlayWindowFlags.NoCollapse) != 0) result |= ImGuiWindowFlags.NoCollapse;
        if ((flags & OverlayWindowFlags.AutoResize) != 0) result |= ImGuiWindowFlags.AlwaysAutoResize;
        if ((flags & OverlayWindowFlags.NoBackground) != 0) result |= ImGuiWindowFlags.NoBackground;
        if ((flags & OverlayWindowFlags.NoSavedSettings) != 0) result |= ImGuiWindowFlags.NoSavedSettings;
        return result;
    }

    private static ImGuiCol ToImGui(OverlayCol col) => col switch
    {
        OverlayCol.Text => ImGuiCol.Text,
        OverlayCol.Button => ImGuiCol.Button,
        OverlayCol.ButtonHovered => ImGuiCol.ButtonHovered,
        OverlayCol.ButtonActive => ImGuiCol.ButtonActive,
        OverlayCol.ChildBg => ImGuiCol.ChildBg,
        _ => ImGuiCol.Text,
    };
}
