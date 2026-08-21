using SpiritVale.Overlay.Capture;
using SpiritVale.Overlay.Domain;
using SpiritVale.Overlay.Host;

OverlayPaths.Ensure();

using var capture = new PacketCaptureService();
using var api = new SpiritValeApi(capture);
using var plugins = new PluginManager(api);

// Dev convenience: seed from the build output Plugins folder once.
var buildPlugins = Path.Combine(AppContext.BaseDirectory, "Plugins");
plugins.ImportDevPluginsFrom(buildPlugins);

using var overlay = new OverlayApp(api, plugins);
await overlay.Run();
