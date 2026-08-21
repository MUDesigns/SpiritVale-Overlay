using System.Numerics;
using ImGuiNET;

namespace SpiritVale.Overlay.Host;

/// <summary>Hold-Tab radial picker for showing/hiding loaded plugin HUDs.</summary>
internal sealed class PluginRadialMenu
{
    private bool _open;
    private bool _mouseWasDown;
    private float _openAnim;
    private int _hoveredIndex = -1;

    public bool IsOpen => _open;

    /// <returns>True while the menu is open (caller should allow mouse clicks).</returns>
    public bool UpdateAndDraw(PluginManager plugins, bool tabHeld)
    {
        var dt = ImGui.GetIO().DeltaTime;
        if (tabHeld)
        {
            _open = true;
            _openAnim = Math.Min(1f, _openAnim + dt * 8f);
        }
        else
        {
            _openAnim = Math.Max(0f, _openAnim - dt * 10f);
            if (_openAnim <= 0.01f)
            {
                _open = false;
                _hoveredIndex = -1;
                _mouseWasDown = false;
                return false;
            }
        }

        var entries = plugins.GetRadialEntries();
        Draw(entries, plugins, interactive: tabHeld && _openAnim > 0.5f);
        return _open;
    }

    private void Draw(IReadOnlyList<RadialPluginEntry> entries, PluginManager plugins, bool interactive)
    {
        var io = ImGui.GetIO();
        var display = io.DisplaySize;
        var center = display * 0.5f;
        var mouse = io.MousePos;
        var draw = ImGui.GetForegroundDrawList();

        var ease = EaseOut(_openAnim);
        var inner = 54f * ease;
        var outer = 168f * ease;
        var labelRadius = 118f * ease;

        var scrim = HudTheme.Color(7, 6, 14, (int)(160 * _openAnim));
        draw.AddRectFilled(Vector2.Zero, display, ImGui.ColorConvertFloat4ToU32(scrim));

        draw.AddCircleFilled(center, inner, ImGui.ColorConvertFloat4ToU32(HudTheme.Panel), 64);
        draw.AddCircle(center, inner, ImGui.ColorConvertFloat4ToU32(HudTheme.Ember), 64, 2f);
        draw.AddCircle(center, outer, ImGui.ColorConvertFloat4ToU32(HudTheme.Border), 64, 1.5f);

        var hubLabel = "PLUGINS";
        var hubSize = ImGui.CalcTextSize(hubLabel);
        draw.AddText(center - hubSize * 0.5f, ImGui.ColorConvertFloat4ToU32(HudTheme.Ember), hubLabel);

        if (entries.Count == 0)
        {
            var empty = "No loaded plugins";
            var es = ImGui.CalcTextSize(empty);
            draw.AddText(
                center + new Vector2(-es.X * 0.5f, outer + 12f),
                ImGui.ColorConvertFloat4ToU32(HudTheme.TextMuted),
                empty);
            return;
        }

        var count = entries.Count;
        var slice = MathF.Tau / count;
        const float startAngle = -MathF.PI / 2f;

        var delta = mouse - center;
        var dist = delta.Length();
        var angle = MathF.Atan2(delta.Y, delta.X);
        var rel = angle - startAngle;
        while (rel < 0) rel += MathF.Tau;
        while (rel >= MathF.Tau) rel -= MathF.Tau;

        _hoveredIndex = -1;
        if (interactive && dist >= inner && dist <= outer + 24f)
            _hoveredIndex = (int)(rel / slice) % count;

        for (var i = 0; i < count; i++)
        {
            var a0 = startAngle + i * slice;
            var a1 = a0 + slice;
            var mid = a0 + slice * 0.5f;
            var entry = entries[i];
            var hovered = i == _hoveredIndex;
            var visible = entry.HudVisible;

            var fill = visible
                ? (hovered ? HudTheme.Color(196, 168, 255, 90) : HudTheme.Color(42, 32, 72, 160))
                : (hovered ? HudTheme.Color(255, 90, 140, 70) : HudTheme.Color(24, 18, 40, 140));
            var edge = hovered ? HudTheme.Ember : (visible ? HudTheme.Cyan : HudTheme.Danger);
            var edgeThick = hovered ? 2.5f : 1.5f;

            AddWedge(draw, center, inner + 4f, outer - 2f, a0 + 0.02f, a1 - 0.02f, fill, edge, edgeThick);

            var labelPos = center + new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * labelRadius;
            var name = Truncate(entry.DisplayName, 14);
            var status = visible ? "ON" : "OFF";
            var nameSize = ImGui.CalcTextSize(name);
            var statusSize = ImGui.CalcTextSize(status);
            draw.AddText(
                labelPos - new Vector2(nameSize.X * 0.5f, nameSize.Y),
                ImGui.ColorConvertFloat4ToU32(HudTheme.Text),
                name);
            draw.AddText(
                labelPos + new Vector2(-statusSize.X * 0.5f, 2f),
                ImGui.ColorConvertFloat4ToU32(visible ? HudTheme.Live : HudTheme.Danger),
                status);
        }

        var hint = "Hold Tab  ·  Click wedge to show/hide";
        var hs = ImGui.CalcTextSize(hint);
        draw.AddText(
            new Vector2((display.X - hs.X) * 0.5f, center.Y + outer + 28f),
            ImGui.ColorConvertFloat4ToU32(HudTheme.TextMuted),
            hint);

        if (!interactive || _hoveredIndex < 0 || _hoveredIndex >= count)
        {
            _mouseWasDown = io.MouseDown[0];
            return;
        }

        var mouseDown = io.MouseDown[0];
        if (mouseDown && !_mouseWasDown)
            plugins.ToggleHudVisible(entries[_hoveredIndex].Id);
        _mouseWasDown = mouseDown;
    }

    private static void AddWedge(
        ImDrawListPtr draw,
        Vector2 center,
        float innerR,
        float outerR,
        float a0,
        float a1,
        Vector4 fill,
        Vector4 edge,
        float edgeThickness)
    {
        const int segments = 20;
        var fillU32 = ImGui.ColorConvertFloat4ToU32(fill);
        var edgeU32 = ImGui.ColorConvertFloat4ToU32(edge);

        for (var i = 0; i < segments; i++)
        {
            var t0 = a0 + (a1 - a0) * (i / (float)segments);
            var t1 = a0 + (a1 - a0) * ((i + 1) / (float)segments);
            var i0 = center + new Vector2(MathF.Cos(t0), MathF.Sin(t0)) * innerR;
            var i1 = center + new Vector2(MathF.Cos(t1), MathF.Sin(t1)) * innerR;
            var o0 = center + new Vector2(MathF.Cos(t0), MathF.Sin(t0)) * outerR;
            var o1 = center + new Vector2(MathF.Cos(t1), MathF.Sin(t1)) * outerR;
            draw.AddTriangleFilled(i0, o0, o1, fillU32);
            draw.AddTriangleFilled(i0, o1, i1, fillU32);
        }

        draw.PathClear();
        for (var i = 0; i <= segments; i++)
        {
            var t = a0 + (a1 - a0) * (i / (float)segments);
            draw.PathLineTo(center + new Vector2(MathF.Cos(t), MathF.Sin(t)) * outerR);
        }
        draw.PathStroke(edgeU32, ImDrawFlags.None, edgeThickness);

        draw.AddLine(
            center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * innerR,
            center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * outerR,
            edgeU32, edgeThickness);
        draw.AddLine(
            center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * innerR,
            center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * outerR,
            edgeU32, edgeThickness);
    }

    private static float EaseOut(float t) => 1f - MathF.Pow(1f - Math.Clamp(t, 0f, 1f), 3f);

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)] + "…";
}
