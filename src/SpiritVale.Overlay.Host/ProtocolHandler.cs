using Microsoft.Win32;
using System.Diagnostics;

namespace SpiritVale.Overlay.Host;

/// <summary>
/// Registers and handles spiritvale://install/{id} deep links (same scheme as the old Mod Manager).
/// </summary>
internal static class ProtocolHandler
{
    private const string Protocol = "spiritvale";

    public static void RegisterCurrentUser()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Protocol}");
            key.SetValue("", "URL:SpiritVale Plugin Manager");
            key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch
        {
            // Non-fatal — NSIS installer also registers the protocol.
        }
    }

    public static string? ParseInstallId(string[] args)
    {
        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var arg = raw.Trim().Trim('"');
            if (!arg.StartsWith($"{Protocol}://", StringComparison.OrdinalIgnoreCase))
                continue;

            // spiritvale://install/{id}
            var rest = arg[(Protocol.Length + 3)..];
            var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && parts[0].Equals("install", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
            if (parts.Length >= 1)
                return Uri.UnescapeDataString(parts[0]);
        }
        return null;
    }

    public static void LaunchSteamGame(int appId = 3767850)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"steam://rungameid/{appId}",
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }

    public static void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }
}
