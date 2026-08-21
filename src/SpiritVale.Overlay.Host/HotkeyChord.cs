using System.Runtime.InteropServices;

namespace SpiritVale.Overlay.Host;

/// <summary>Parses chords like "Ctrl+R", "F5", "Ctrl+Shift+R" for GetAsyncKeyState polling.</summary>
internal static class HotkeyChord
{
    public static bool IsPressed(string? chord)
    {
        if (string.IsNullOrWhiteSpace(chord)) return false;
        var parts = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var needCtrl = false;
        var needShift = false;
        var needAlt = false;
        int? mainVk = null;

        foreach (var raw in parts)
        {
            var p = raw.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                needCtrl = true;
                continue;
            }
            if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                needShift = true;
                continue;
            }
            if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                needAlt = true;
                continue;
            }
            mainVk = ParseVk(p);
        }

        if (mainVk is null) return false;
        if (needCtrl && (GetAsyncKeyState(0x11) & 0x8000) == 0) return false;
        if (needShift && (GetAsyncKeyState(0x10) & 0x8000) == 0) return false;
        if (needAlt && (GetAsyncKeyState(0x12) & 0x8000) == 0) return false;
        return (GetAsyncKeyState(mainVk.Value) & 0x8000) != 0;
    }

    public static string Normalize(string? chord)
    {
        if (string.IsNullOrWhiteSpace(chord)) return "";
        var parts = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mods = new List<string>();
        string? key = null;
        foreach (var raw in parts)
        {
            var p = raw.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                mods.Add("Ctrl");
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                mods.Add("Shift");
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                mods.Add("Alt");
            else
                key = p.Length == 1 ? p.ToUpperInvariant() : p;
        }
        if (key is null) return "";
        mods.Sort(StringComparer.Ordinal);
        return mods.Count == 0 ? key : string.Join("+", mods) + "+" + key;
    }

    private static int? ParseVk(string token)
    {
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
        }
        if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(token[1..], out var fn)
            && fn is >= 1 and <= 24)
            return 0x70 + (fn - 1);
        return token.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "ENTER" or "RETURN" => 0x0D,
            _ => null,
        };
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
