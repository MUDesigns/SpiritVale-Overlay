using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using SpiritVale.Overlay.Capture.FishNet;
using SpiritVale.Overlay.Capture.LiteNetLib;

namespace SpiritVale.Overlay.Capture;

public sealed class CapturedUdpPacket
{
    public required DateTimeOffset Timestamp { get; init; }
    public required IPAddress SourceAddress { get; init; }
    public required IPAddress DestinationAddress { get; init; }
    public required ushort SourcePort { get; init; }
    public required ushort DestinationPort { get; init; }
    public required byte[] Payload { get; init; }
    public required string Direction { get; init; }
}

public sealed class PacketCaptureOptions
{
    public string ProcessName { get; init; } = "SpiritVale";
    /// <summary>Substring match against device description/name. Preferred over index.</summary>
    public string? PreferredDevice { get; set; }
    public int? DeviceIndex { get; set; }
    public bool DecodeFishNet { get; init; } = true;
    public FishNetRpcMap? RpcMap { get; init; }
}

public sealed class CaptureStats
{
    public long UdpPackets;
    public long LiteNetLeaves;
    public long FishNetPackets;
    public long DecodeErrors;
    public DateTimeOffset? StartedAt;
    public DateTimeOffset? LastUdpAt;
    public DateTimeOffset? LastFishNetAt;
    public string? LastUdpSummary;
    public string? ActiveDevice;
    public string? LastError;
}

public sealed class PacketCaptureService : IDisposable
{
    private readonly PacketCaptureOptions _options;
    private readonly FishNetSessionDecoder _session;
    private ILiveDevice? _device;
    private HashSet<IPAddress> _localAddresses = new();
    private int _watchdogDeviceCursor;

    public event Action<CapturedUdpPacket>? UdpPacket;
    public event Action<DecodedLiteNetLibPacket, CapturedUdpPacket>? LiteNetPacket;
    public event Action<DecodedFishNetPacket, CapturedUdpPacket>? FishNetPacket;
    public event Action<string>? Warning;
    public event Action<string>? StatusChanged;

    public bool IsRunning => _device?.Started == true;
    public string? Status { get; private set; }
    public CaptureStats Stats { get; } = new();
    public PacketCaptureOptions Options => _options;
    public string? ActiveDeviceKey { get; private set; }

    public PacketCaptureService(PacketCaptureOptions? options = null)
    {
        _options = options ?? new PacketCaptureOptions();
        _session = new FishNetSessionDecoder(_options.RpcMap ?? BuiltinRpcMap.Load());
    }

    public static NpcapStatus GetNpcapStatus(bool refreshDevices = false)
    {
        try
        {
            if (refreshDevices)
                RefreshDeviceList();
            var devices = CaptureDeviceList.Instance;
            var adminOnly = IsNpcapAdminOnly();
            var elevated = IsProcessElevated();
            string? message = null;
            if (devices.Count == 0)
                message = "No capture devices found. Install Npcap with WinPcap API-compatible mode from https://npcap.com/";
            else if (adminOnly && !elevated)
                message = "Npcap Admin-only is enabled. If capture fails, run as Administrator or reinstall Npcap with Admin-only unchecked.";

            return new NpcapStatus(devices.Count > 0, devices.Count, message, adminOnly, elevated);
        }
        catch (Exception ex)
        {
            return new NpcapStatus(false, 0, $"Npcap unavailable: {ex.Message}. Install from https://npcap.com/", false, IsProcessElevated());
        }
    }

    public static IReadOnlyList<string> ListDevices()
    {
        try
        {
            RefreshDeviceList();
            return CaptureDeviceList.Instance
                .Select(DeviceLabel)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <param name="deviceIndex">Explicit UI index. Pass null to use preferred name / auto-select.</param>
    public void Start(int? deviceIndex = null, string? preferredDevice = null)
    {
        if (IsRunning) Stop();

        if (preferredDevice is not null)
            _options.PreferredDevice = preferredDevice;
        // Only lock to an index when the caller explicitly passes one (>= 0).
        _options.DeviceIndex = deviceIndex;

        var status = GetNpcapStatus(refreshDevices: true);
        if (!status.Available)
        {
            Stats.LastError = status.Message ?? "Npcap unavailable";
            SetStatus(Stats.LastError);
            throw new InvalidOperationException(Stats.LastError);
        }

        // Warn but do not hard-fail — some installs report AdminOnly incorrectly after reinstall.
        if (status.AdminOnly && !status.Elevated)
            Warning?.Invoke(status.Message ?? "Npcap may require Administrator.");

        RefreshLocalAddresses();
        ResetStats();

        var device = SelectDevice();
        OpenDevice(device);

        try { device.Filter = "udp"; }
        catch (Exception ex) { Warning?.Invoke($"BPF filter not applied: {ex.Message}"); }

        device.OnPacketArrival += OnPacketArrival;
        device.StartCapture();
        _device = device;
        ActiveDeviceKey = DeviceLabel(device);
        Stats.ActiveDevice = ActiveDeviceKey;
        Stats.StartedAt = DateTimeOffset.Now;
        SetStatus($"Capturing on {ActiveDeviceKey}");
    }

    /// <summary>If capture is alive but silent, reopen on the next usable adapter.</summary>
    public bool TryRecoverIfSilent(TimeSpan silence)
    {
        if (!IsRunning || Stats.StartedAt is null) return false;
        if (Stats.UdpPackets > 0) return false;
        if (DateTimeOffset.Now - Stats.StartedAt.Value < silence) return false;

        var devices = CaptureDeviceList.Instance.OfType<ILiveDevice>().ToList();
        if (devices.Count == 0) return false;

        _watchdogDeviceCursor = (_watchdogDeviceCursor + 1) % devices.Count;
        // Skip obvious junk a few times.
        for (var i = 0; i < devices.Count; i++)
        {
            var idx = (_watchdogDeviceCursor + i) % devices.Count;
            var label = DeviceLabel(devices[idx]).ToLowerInvariant();
            if (label.Contains("loopback") || label.Contains("bluetooth") || label.Contains("wan miniport"))
                continue;
            Warning?.Invoke($"No UDP on {Stats.ActiveDevice}; switching to {DeviceLabel(devices[idx])}");
            Start(idx);
            return true;
        }
        return false;
    }

    public void Stop()
    {
        if (_device is null) return;
        try
        {
            _device.OnPacketArrival -= OnPacketArrival;
            if (_device.Started) _device.StopCapture();
            _device.Close();
        }
        catch
        {
            // ignore shutdown races
        }
        finally
        {
            try { _device.Dispose(); } catch { /* ignore */ }
            _device = null;
            SetStatus("Capture stopped");
        }
    }

    public void Dispose() => Stop();

    private void OpenDevice(ILiveDevice device)
    {
        // DataTransferUdp has been observed to capture nothing after Npcap restarts on some setups.
        // Prefer a simple open first.
        Exception? last = null;
        foreach (var mode in new[]
                 {
                     DeviceModes.MaxResponsiveness,
                     DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness,
                     DeviceModes.None,
                 })
        {
            try
            {
                device.Open(new DeviceConfiguration
                {
                    Mode = mode,
                    ReadTimeout = 1000,
                });
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                try { device.Close(); } catch { /* ignore */ }
            }
        }

        throw new InvalidOperationException($"Failed to open capture device: {last?.Message}");
    }

    private ILiveDevice SelectDevice()
    {
        RefreshDeviceList();
        var devices = CaptureDeviceList.Instance.OfType<ILiveDevice>().ToList();
        if (devices.Count == 0)
            throw new InvalidOperationException("No Npcap devices available.");

        // 1) Preferred name from settings (stable across restarts)
        if (!string.IsNullOrWhiteSpace(_options.PreferredDevice))
        {
            var match = devices.FirstOrDefault(d =>
                DeviceLabel(d).Contains(_options.PreferredDevice, StringComparison.OrdinalIgnoreCase)
                || (d.Name?.Contains(_options.PreferredDevice, StringComparison.OrdinalIgnoreCase) ?? false));
            if (match is not null) return match;
        }

        // 2) Explicit index from UI (only when set)
        if (_options.DeviceIndex is int index && index >= 0 && index < devices.Count)
            return devices[index];

        // 3) Default route interface
        var preferred = PreferDefaultRouteDevice(devices) ?? PreferUsableDevice(devices);
        return preferred ?? devices[0];
    }

    private static void RefreshDeviceList()
    {
        try
        {
            // SharpPcap singleton can go stale after Npcap reinstall/restart.
            CaptureDeviceList.Instance.Refresh();
        }
        catch
        {
            // older/newer SharpPcap — ignore if Refresh is unavailable at runtime
        }
    }

    private static string DeviceLabel(ICaptureDevice device)
        => device.Description ?? device.Name ?? "(unnamed)";

    private static ILiveDevice? PreferUsableDevice(IReadOnlyList<ILiveDevice> devices)
    {
        foreach (var device in devices)
        {
            var name = DeviceLabel(device).ToLowerInvariant();
            if (name.Contains("loopback") || name.Contains("bluetooth") || name.Contains("wan miniport")
                || name.Contains("vmware") || name.Contains("hyper-v") || name.Contains("virtualbox")
                || name.Contains("virtual"))
                continue;
            return device;
        }
        return null;
    }

    private static ILiveDevice? PreferDefaultRouteDevice(IReadOnlyList<ILiveDevice> devices)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "route.exe",
                Arguments = "PRINT 0.0.0.0",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            // Columns: Destination Netmask Gateway Interface Metric
            var routes = output.Split('\n')
                .Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(cols => cols.Length >= 5 && cols[0] == "0.0.0.0" && cols[1] == "0.0.0.0")
                .Select(cols =>
                {
                    _ = int.TryParse(cols[4], out var metric);
                    return (InterfaceIp: cols[3], Metric: metric);
                })
                .OrderBy(r => r.Metric)
                .ToList();

            foreach (var route in routes)
            {
                if (!IPAddress.TryParse(route.InterfaceIp, out var addr)) continue;
                foreach (var device in devices.OfType<LibPcapLiveDevice>())
                {
                    if (device.Addresses.Any(a => a.Addr?.ipAddress?.Equals(addr) == true))
                        return device;
                }
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private void ResetStats()
    {
        Stats.UdpPackets = 0;
        Stats.LiteNetLeaves = 0;
        Stats.FishNetPackets = 0;
        Stats.DecodeErrors = 0;
        Stats.LastUdpAt = null;
        Stats.LastFishNetAt = null;
        Stats.LastUdpSummary = null;
        Stats.LastError = null;
        Stats.StartedAt = null;
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var raw = e.GetPacket();
            var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            var udp = packet.Extract<UdpPacket>();
            if (udp is null) return;
            var ip = packet.Extract<IPPacket>();
            if (ip is null) return;

            var src = ip.SourceAddress;
            var dst = ip.DestinationAddress;
            var direction = _localAddresses.Contains(src) ? "outbound"
                : _localAddresses.Contains(dst) ? "inbound" : "unknown";

            var payload = udp.PayloadData ?? Array.Empty<byte>();
            if (payload.Length == 0) return;

            var captured = new CapturedUdpPacket
            {
                Timestamp = DateTimeOffset.Now,
                SourceAddress = src,
                DestinationAddress = dst,
                SourcePort = udp.SourcePort,
                DestinationPort = udp.DestinationPort,
                Payload = payload,
                Direction = direction,
            };

            Interlocked.Increment(ref Stats.UdpPackets);
            Stats.LastUdpAt = captured.Timestamp;
            Stats.LastUdpSummary = $"{direction} {src}:{udp.SourcePort} → {dst}:{udp.DestinationPort} ({payload.Length}b)";
            UdpPacket?.Invoke(captured);

            if (!_options.DecodeFishNet) return;

            IReadOnlyList<DecodedLiteNetLibPacket> leaves;
            try
            {
                leaves = LiteNetLibDecoder.Decode(payload);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref Stats.DecodeErrors);
                Warning?.Invoke($"LiteNetLib decode: {ex.Message}");
                return;
            }

            foreach (var leaf in leaves)
            {
                Interlocked.Increment(ref Stats.LiteNetLeaves);
                LiteNetPacket?.Invoke(leaf, captured);
                if (leaf.Property is not (LiteNetLibProperty.Unreliable or LiteNetLibProperty.Channeled))
                    continue;
                if (leaf.Payload.Length < 6) continue;

                try
                {
                    var reliable = leaf.Property == LiteNetLibProperty.Channeled;
                    var decoded = _session.Decode(leaf.Payload.Span, new FishNetDecodeOptions
                    {
                        Reliable = reliable,
                        Direction = direction,
                        Channel = leaf.Channel,
                        Sequence = leaf.Sequence,
                        ConnectionId = $"{src}:{udp.SourcePort}->{dst}:{udp.DestinationPort}",
                    });
                    foreach (var fish in decoded)
                    {
                        Interlocked.Increment(ref Stats.FishNetPackets);
                        Stats.LastFishNetAt = DateTimeOffset.Now;
                        FishNetPacket?.Invoke(fish, captured);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref Stats.DecodeErrors);
                    Warning?.Invoke($"FishNet decode: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref Stats.DecodeErrors);
            Warning?.Invoke($"Capture frame: {ex.Message}");
        }
    }

    private void RefreshLocalAddresses()
    {
        _localAddresses = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Select(ua => ua.Address)
            .Where(a => a.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                or System.Net.Sockets.AddressFamily.InterNetworkV6)
            .ToHashSet();
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private static bool IsNpcapAdminOnly()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\npcap\Parameters");
            var value = key?.GetValue("AdminOnly");
            return value is int i && i != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

public sealed record NpcapStatus(
    bool Available,
    int DeviceCount,
    string? Message,
    bool AdminOnly = false,
    bool Elevated = false);
