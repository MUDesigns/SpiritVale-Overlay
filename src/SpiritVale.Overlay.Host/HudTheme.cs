using System.Numerics;
using ImGuiNET;

namespace SpiritVale.Overlay.Host;

/// <summary>
/// Cyberpunk HUD — night city ink, light violet neon, cyan live status.
/// </summary>
internal static class HudTheme
{
    // Keep Ember as the primary accent alias so call sites stay readable.
    public static readonly Vector4 Ink = Rgba(7, 6, 14, 150);
    public static readonly Vector4 Panel = Rgba(16, 12, 28, 130);
    public static readonly Vector4 PanelAlt = Rgba(24, 18, 40, 145);
    public static readonly Vector4 Border = Rgba(90, 70, 140, 200);
    public static readonly Vector4 Ember = Rgba(196, 168, 255, 255);       // light violet
    public static readonly Vector4 EmberDim = Rgba(196, 168, 255, 90);
    public static readonly Vector4 Cyan = Rgba(80, 230, 255, 255);
    public static readonly Vector4 Magenta = Rgba(255, 110, 210, 255);
    public static readonly Vector4 Live = Cyan;
    public static readonly Vector4 Warn = Rgba(255, 210, 100, 255);
    public static readonly Vector4 Danger = Rgba(255, 90, 140, 255);
    public static readonly Vector4 Text = Rgba(236, 230, 255, 255);
    public static readonly Vector4 TextMuted = Rgba(160, 148, 190, 255);
    public static readonly Vector4 BarTrack = Rgba(28, 22, 46, 255);
    public static readonly Vector4 BarFill = Ember;
    public static readonly Vector4 Hp = Rgba(90, 240, 190, 255);
    public static readonly Vector4 Mp = Cyan;

    public static void Apply()
    {
        var style = ImGui.GetStyle();
        style.WindowRounding = 3f;
        style.ChildRounding = 3f;
        style.FrameRounding = 3f;
        style.PopupRounding = 3f;
        style.ScrollbarRounding = 3f;
        style.GrabRounding = 3f;
        style.TabRounding = 3f;

        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;

        style.WindowPadding = new Vector2(14, 12);
        style.FramePadding = new Vector2(10, 6);
        style.ItemSpacing = new Vector2(10, 8);
        style.ItemInnerSpacing = new Vector2(8, 5);
        style.IndentSpacing = 16f;
        style.ScrollbarSize = 10f;
        style.GrabMinSize = 10f;

        style.WindowTitleAlign = new Vector2(0.02f, 0.5f);
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);

        var c = style.Colors;
        c[(int)ImGuiCol.Text] = Text;
        c[(int)ImGuiCol.TextDisabled] = TextMuted;
        c[(int)ImGuiCol.WindowBg] = Ink;
        c[(int)ImGuiCol.ChildBg] = Panel;
        c[(int)ImGuiCol.PopupBg] = Panel;
        c[(int)ImGuiCol.Border] = Border;
        c[(int)ImGuiCol.BorderShadow] = Vector4.Zero;
        c[(int)ImGuiCol.FrameBg] = PanelAlt;
        c[(int)ImGuiCol.FrameBgHovered] = Rgba(40, 30, 68, 255);
        c[(int)ImGuiCol.FrameBgActive] = Rgba(52, 38, 86, 255);
        c[(int)ImGuiCol.TitleBg] = Rgba(10, 8, 20, 255);
        c[(int)ImGuiCol.TitleBgActive] = Rgba(14, 10, 28, 255);
        c[(int)ImGuiCol.TitleBgCollapsed] = Rgba(10, 8, 20, 200);
        c[(int)ImGuiCol.MenuBarBg] = Panel;
        c[(int)ImGuiCol.ScrollbarBg] = Rgba(10, 8, 20, 180);
        c[(int)ImGuiCol.ScrollbarGrab] = Border;
        c[(int)ImGuiCol.ScrollbarGrabHovered] = EmberDim;
        c[(int)ImGuiCol.ScrollbarGrabActive] = Ember;
        c[(int)ImGuiCol.CheckMark] = Ember;
        c[(int)ImGuiCol.SliderGrab] = Ember;
        c[(int)ImGuiCol.SliderGrabActive] = Magenta;
        c[(int)ImGuiCol.Button] = Rgba(42, 32, 72, 255);
        c[(int)ImGuiCol.ButtonHovered] = Rgba(196, 168, 255, 70);
        c[(int)ImGuiCol.ButtonActive] = Rgba(196, 168, 255, 130);
        c[(int)ImGuiCol.Header] = Rgba(196, 168, 255, 45);
        c[(int)ImGuiCol.HeaderHovered] = Rgba(196, 168, 255, 80);
        c[(int)ImGuiCol.HeaderActive] = Rgba(196, 168, 255, 120);
        c[(int)ImGuiCol.Separator] = Border;
        c[(int)ImGuiCol.SeparatorHovered] = Ember;
        c[(int)ImGuiCol.SeparatorActive] = Magenta;
        c[(int)ImGuiCol.ResizeGrip] = EmberDim;
        c[(int)ImGuiCol.ResizeGripHovered] = Ember;
        c[(int)ImGuiCol.ResizeGripActive] = Magenta;
        c[(int)ImGuiCol.Tab] = Rgba(24, 18, 42, 255);
        c[(int)ImGuiCol.TabHovered] = Rgba(196, 168, 255, 90);
        c[(int)ImGuiCol.TabSelected] = Rgba(196, 168, 255, 60);
        c[(int)ImGuiCol.TabDimmed] = Rgba(16, 12, 28, 255);
        c[(int)ImGuiCol.TabDimmedSelected] = Rgba(196, 168, 255, 40);
        c[(int)ImGuiCol.PlotHistogram] = BarFill;
        c[(int)ImGuiCol.PlotHistogramHovered] = Magenta;
        c[(int)ImGuiCol.TableHeaderBg] = PanelAlt;
        c[(int)ImGuiCol.TableBorderStrong] = Border;
        c[(int)ImGuiCol.TableBorderLight] = Rgba(70, 55, 110, 255);
        c[(int)ImGuiCol.TableRowBg] = Vector4.Zero;
        c[(int)ImGuiCol.TableRowBgAlt] = Rgba(196, 168, 255, 10);
        c[(int)ImGuiCol.TextSelectedBg] = Rgba(196, 168, 255, 80);
        c[(int)ImGuiCol.NavCursor] = Cyan;
        c[(int)ImGuiCol.ModalWindowDimBg] = Rgba(4, 2, 12, 160);
    }

    public static bool TryLoadFonts(ClickableTransparentOverlay.Overlay overlay)
    {
        var fonts = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "bahnschrift.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "cascadia.code.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "CascadiaCode.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguisb.ttf"),
        };

        foreach (var path in fonts)
        {
            if (!File.Exists(path)) continue;
            if (overlay.ReplaceFont(path, 17, ClickableTransparentOverlay.FontGlyphRangeType.English))
                return true;
        }
        return false;
    }

    public static void AccentRail()
    {
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        // Dual neon rail: violet + cyan edge.
        draw.AddRectFilled(min, new Vector2(min.X + 3f, max.Y), ImGui.ColorConvertFloat4ToU32(Ember));
        draw.AddRectFilled(new Vector2(min.X + 3f, min.Y), new Vector2(min.X + 4f, max.Y), ImGui.ColorConvertFloat4ToU32(Cyan));
    }

    public static void SectionLabel(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Ember);
        ImGui.TextUnformatted(text.ToUpperInvariant());
        ImGui.PopStyleColor();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p, new Vector2(p.X + Math.Min(w, 56f), p.Y + 2f), ImGui.ColorConvertFloat4ToU32(Ember));
        draw.AddRectFilled(
            new Vector2(p.X + Math.Min(w, 56f), p.Y),
            new Vector2(p.X + Math.Min(w, 72f), p.Y + 2f),
            ImGui.ColorConvertFloat4ToU32(Cyan));
        ImGui.Dummy(new Vector2(1, 8));
    }

    public static void StatusChip(string label, Vector4 color)
    {
        var draw = ImGui.GetWindowDrawList();
        var textSize = ImGui.CalcTextSize(label);
        var pad = new Vector2(8, 3);
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + textSize + pad * 2;
        var bg = new Vector4(color.X, color.Y, color.Z, 0.16f);
        draw.AddRectFilled(p0, p1, ImGui.ColorConvertFloat4ToU32(bg), 2f);
        draw.AddRect(p0, p1, ImGui.ColorConvertFloat4ToU32(color), 2f, ImDrawFlags.None, 1f);
        draw.AddText(p0 + pad, ImGui.ColorConvertFloat4ToU32(color), label);
        ImGui.Dummy(p1 - p0);
    }

    public static bool AccentButton(string label, Vector2? size = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Rgba(196, 168, 255, 210));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Rgba(220, 200, 255, 240));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Rgba(255, 110, 210, 230));
        ImGui.PushStyleColor(ImGuiCol.Text, Rgba(16, 10, 28, 255));
        var clicked = size is Vector2 s ? ImGui.Button(label, s) : ImGui.Button(label);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    public static void MeterBar(float fraction, Vector4 fill, float height = 10f, string? overlay = null)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        var width = ImGui.GetContentRegionAvail().X;
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p0, p1, ImGui.ColorConvertFloat4ToU32(BarTrack), 1f);
        if (fraction > 0.001f)
        {
            draw.AddRectFilled(p0, new Vector2(p0.X + width * fraction, p1.Y), ImGui.ColorConvertFloat4ToU32(fill), 1f);
        }
        draw.AddRect(p0, p1, ImGui.ColorConvertFloat4ToU32(Border), 1f);
        if (!string.IsNullOrEmpty(overlay))
        {
            var ts = ImGui.CalcTextSize(overlay);
            draw.AddText(
                new Vector2(p0.X + (width - ts.X) * 0.5f, p0.Y + (height - ts.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(Text),
                overlay);
        }
        ImGui.Dummy(new Vector2(width, height));
    }

    public static void BeginCard()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelAlt);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10));
        ImGui.BeginChild($"card##{ImGui.GetID("card")}", new Vector2(0, 0), ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.Borders);
    }

    public static void EndCard()
    {
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static Vector4 Rgba(int r, int g, int b, int a)
        => new(r / 255f, g / 255f, b / 255f, a / 255f);

    public static Vector4 Color(int r, int g, int b, int a) => Rgba(r, g, b, a);
}
