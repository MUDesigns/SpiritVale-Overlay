using System.Runtime.InteropServices;
using SpiritVale.Overlay.Capture;
using SpiritVale.Overlay.Domain;
using SpiritVale.Overlay.Host;

// Prevent two hosts from fighting over the same AppData plugin DLLs (common when
// elevating while a non-admin instance is still running).
using var singleInstance = new Mutex(true, @"Local\SpiritVale.Overlay.Host", out var createdNew);
if (!createdNew)
{
    NativeMessageBox(
        "SpiritVale Overlay is already running.\n\nClose the other instance first (check the tray/taskbar), then launch again — including when switching to Run as administrator.",
        "SpiritVale Overlay");
    return;
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    LogFatal(e.ExceptionObject?.ToString() ?? "unknown");

try
{
    OverlayPaths.Ensure();

    // Refresh AppData plugins from this build while DLLs are still unlocked.
    var buildPlugins = Path.Combine(AppContext.BaseDirectory, "Plugins");
    PluginManager.SyncDevBuildPlugins(buildPlugins);

    using var capture = new PacketCaptureService();
    using var api = new SpiritValeApi(capture);
    using var plugins = new PluginManager(api);
    plugins.ImportDevPluginsFrom(buildPlugins);

    using var overlay = new OverlayApp(api, plugins);
    await overlay.Run();
}
catch (Exception ex)
{
    LogFatal(ex.ToString());
    Environment.Exit(1);
}

static void LogFatal(string text)
{
    try
    {
        var path = Path.Combine(OverlayPaths.Root, "crash.log");
        Directory.CreateDirectory(OverlayPaths.Root);
        File.WriteAllText(path, $"{DateTimeOffset.Now:u}\n{text}\n");
    }
    catch { /* ignore */ }

    NativeMessageBox(text, "SpiritVale Overlay crashed");
}

static void NativeMessageBox(string text, string caption)
    => MessageBoxW(IntPtr.Zero, text, caption, 0x00000010 /* MB_ICONERROR */);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
