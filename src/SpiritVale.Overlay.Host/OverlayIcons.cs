using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace SpiritVale.Overlay.Host;

/// <summary>Brand shop icon for exe/taskbar and ImGui window titles.</summary>
internal static class OverlayIcons
{
    public const string PngFileName = "shop-icon.png";
    public const string IcoFileName = "shop-icon.ico";

    private static IntPtr _texture;
    private static bool _loadAttempted;
    private static IntPtr _hIconBig;
    private static IntPtr _hIconSmall;

    public static string PngPath => Path.Combine(AppContext.BaseDirectory, "Assets", PngFileName);
    public static string IcoPath => Path.Combine(AppContext.BaseDirectory, "Assets", IcoFileName);

    public static void EnsureLoaded(ClickableTransparentOverlay.Overlay overlay)
    {
        if (_loadAttempted) return;
        _loadAttempted = true;
        var path = PngPath;
        if (!File.Exists(path))
        {
            // Fall back to project-relative copy next to exe root.
            path = Path.Combine(AppContext.BaseDirectory, PngFileName);
        }
        if (!File.Exists(path)) return;

        overlay.AddOrGetImagePointer(path, false, out _texture, out _, out _);
    }

    public static void ApplyWindowIcon(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var ico = IcoPath;
        if (!File.Exists(ico))
            ico = Path.Combine(AppContext.BaseDirectory, IcoFileName);
        if (!File.Exists(ico)) return;

        const int ImageIcon = 1;
        const uint LrLoadFromFile = 0x0010;
        const uint LrDefaultSize = 0x0040;
        const int IconSmall = 0;
        const int IconBig = 1;
        const uint WmSetIcon = 0x0080;

        _hIconBig = LoadImage(IntPtr.Zero, ico, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        _hIconSmall = LoadImage(IntPtr.Zero, ico, ImageIcon, 16, 16, LrLoadFromFile);
        if (_hIconBig != IntPtr.Zero)
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconBig, _hIconBig);
        if (_hIconSmall != IntPtr.Zero)
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, _hIconSmall);
        else if (_hIconBig != IntPtr.Zero)
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, _hIconBig);
    }

    /// <summary>
    /// Begins an ImGui window whose title bar shows the shop icon at font size, then the title text.
    /// </summary>
    public static bool BeginBrandedWindow(string title, ref bool open, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        // Leading spaces reserve room for the icon so native title text doesn't overlap it.
        var labeled = $"    {title}";
        var began = ImGui.Begin(labeled, ref open, flags);
        if (began)
            PaintTitleIcon();
        return began;
    }

    public static bool BeginBrandedWindow(string title, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        var labeled = $"    {title}";
        var began = ImGui.Begin(labeled, flags);
        if (began)
            PaintTitleIcon();
        return began;
    }

    public static void PaintTitleIcon()
    {
        if (_texture == IntPtr.Zero) return;

        var style = ImGui.GetStyle();
        var fontSize = ImGui.GetFontSize();
        var titleBarHeight = fontSize + style.FramePadding.Y * 2f;
        var iconSize = fontSize;
        var winPos = ImGui.GetWindowPos();
        var min = new Vector2(
            winPos.X + style.FramePadding.X,
            winPos.Y + (titleBarHeight - iconSize) * 0.5f);
        var max = min + new Vector2(iconSize, iconSize);
        ImGui.GetWindowDrawList().AddImage(_texture, min, max);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hInst, string name, int type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
